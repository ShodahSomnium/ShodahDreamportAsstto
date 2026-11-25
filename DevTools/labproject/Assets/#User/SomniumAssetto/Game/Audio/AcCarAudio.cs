using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [A, Audio] A car's sound, rebuilt from its own FMOD bank (content/cars/&lt;id&gt;/sfx/&lt;id&gt;.bank, module A's Data/Audio parse of the
    /// FMOD Studio 1.08 metadata + D's FSB5 PCM16 reader) exactly as the bank lays it out:
    ///  - engine_int (listener in this car's cockpit) / engine_ext (everyone else): every rpm layer with its region, crossfades, load /
    ///    coast bus curves on throttle, auto-pitch, the bank's own spatialiser (engine_int 3D at the engine, 7/50 m; engine_ext 6/350 m
    ///    inverse tapered) and bus effects (engine_ext lowpass on the event cone angle + distance filter, engine_int distortion on rpm)
    ///    at the engine emitter (sounds.ini [ENGINE] POSITION);
    ///  - gear_int / gear_ext on every gear change (AcEvents.GearChanged or a telemetry gear change), gear_grind, limiter (decay since the
    ///    last cut), tractioncontrol_int/ext, turbo (boost in bar), transmission (drivetrain speed), backfire_int/ext on lift-off by
    ///    sounds.ini [BACKFIRE] (MAXGAS / TRIGGERGAS / MINRPM-MAXRPM / VOLUME_IN / VOLUME_OUT), wind and wheel (speed, brake), bodywork
    ///    (suspension travel speed, [BODY_WORK] MIN / GAIN), door (S calls <see cref="Door"/>), horn;
    ///  - skids per wheel: skid_int / skid_ext timeline loops scaled by the tyre's slide, its load against [SKIDS] ENTRY_POINT and
    ///    MIX_VOLUME; surfaces per wheel from common.bank event:/surfaces/* named by the track's surfaces.ini WAV (pitch WAV_PITCH);
    ///    collisions from common.bank event:/collisions/car|track/hit (impact speed) on a new contact and car|track/scrape while it stays.
    /// Remote / AI cars (<see cref="Local"/> false): engine_ext, skids, surfaces, collisions, gear, backfire, limiter, horn, door - built
    /// when first audible, silent beyond 350 m and only the nearest <see cref="AcAudioMix.MaxRemoteCars"/> (AcAudioMix budget); a
    /// passenger riding in one gets the full cockpit set. A car not fed for <see cref="FeedTimeout"/> s fades out (its owner left).
    /// A MonoBehaviour on the car root (AcCarRig adds it via Attach). Every AudioSource is made when the sound set is built, never mid-drive.
    /// </summary>
    public sealed class AcCarAudio : MonoBehaviour
    {
        /// <summary>No Feed for this long = the car's owner stopped sending (left, or the timeline cannot sample): fade out. // ours</summary>
        public const float FeedTimeout = 0.5f;
        /// <summary>A collision call after a gap longer than this is a new contact (a hit); P repeats every >= 0.1 s while a contact
        /// stays, which drives the scrape. // ours</summary>
        public const float NewContactGap = 0.3f;
        /// <summary>The scrape's "decay" stays 0 while P's contact repeats keep coming (P repeats every 0.1 s); it then counts the seconds since
        /// the contact ended (the banks' scrape curves fade it out from decay 0 to 0.3 / 1). // ours (P's repeat interval + one frame margin)</summary>
        public const float ScrapeHold = 0.15f;

        public AcCarEntry Car { get; private set; }
        /// <summary>The local player's own car (interior sounds possible) vs a remote / AI car (exterior only unless someone rides in it).</summary>
        public bool Local { get; private set; }
        /// <summary>The bank is decoded and the sources play.</summary>
        public bool Ready { get; private set; }
        /// <summary>The physics spec given to Attach (may be null).</summary>
        public AcCarSpec Spec { get; private set; }
        /// <summary>The listener is in this car's cockpit: the _int events play, 2D bank events stay 2D, no doppler.</summary>
        public bool Interior { get; private set; }
        /// <summary>The budget lets this car sound (always true for the local car).</summary>
        internal bool Audible = true;

        AcSoundBank _bank, _common;
        AcCarSounds _snd;
        bool _inited, _built, _commonBuilt, _disposed, _seatInside, _forcedInside, _readyLogged, _interiorSet;
        AcTelemetry _t;
        bool _fed;
        float _fedAt = -99f, _fade = 1f;
        float _rpmS, _thrS, _speedS;
        int _lastGear = int.MinValue, _cfgVersion = -1;
        float _sinceLimiter = 99f, _sinceTc = 99f, _lastContact = -99f, _scrapeDecay = 1f;
        bool _armed, _horn, _hornLights, _configuredAboard;
        readonly float[] _feed = new float[AcFeed.Count];
        readonly float[] _travelPrev = new float[4];
        readonly float[] _skidS = new float[4];
        Vector3 _eye = new Vector3(0f, 0.6f, 0f);
        float _halfWidth = 0.9f;

        Transform _eEngine, _eExhaust, _eBody, _eDoor, _eHit;
        readonly Transform[] _eWheel = new Transform[4];

        AcEventPlayer _engInt, _engExt, _gearInt, _gearExt, _grind, _bfInt, _bfExt, _tcInt, _tcExt, _limiter, _turbo, _transmission, _wind, _wheel, _body, _hornP, _door;
        readonly AcEventPlayer[] _skidInt = new AcEventPlayer[4], _skidExt = new AcEventPlayer[4];
        AcEventPlayer _hitCar, _hitTrack, _scrapeCar, _scrapeTrack, _scrape;
        // damage (DAMAGE_DESIGN 6): common.bank screw / unscrew for the pit repair, the car's damage state for suspension_damage
        AcEventPlayer _screw, _unscrew;
        AcCarPhysics _phys;
        bool _physLooked;
        /// <summary>Lab (audio-damage): treat damage as on when players are built, without a session.</summary>
        public static bool LabDamageOn;
        /// <summary>Lab: suspension_damage to feed instead of the car's damage state (-1 = the car's).</summary>
        public float LabSuspDamage = -1f;
        readonly List<AcEventPlayer> _all = new List<AcEventPlayer>();

        // common.bank surfaces: one player per wheel x event:/surfaces/* event, all built with the common set
        AcSndEvent[] _surfEv = new AcSndEvent[0];
        AcEventPlayer[] _surfP = new AcEventPlayer[0];
        float[] _surfDecay = new float[0];
        static readonly Dictionary<AcSurface, AcSndEvent> SurfaceEvents = new Dictionary<AcSurface, AcSndEvent>();
        static readonly Collider[] Overlap = new Collider[8];

        /// <summary>Adds the audio to a car root and starts loading its bank on a worker. <paramref name="spec"/> may be null (remote cars:
        /// limiter rpm etc. then come from the telemetry).</summary>
        public static AcCarAudio Attach(GameObject carRoot, AcCarEntry car, AcCarSpec spec, bool local)
        {
            if (carRoot == null) return null;
            AcAudioHost.Ensure();
            AcCarAudio a = carRoot.GetComponent<AcCarAudio>();
            if (a == null) a = carRoot.AddComponent<AcCarAudio>();
            else if (a.Car != car || a.Local != local) a.Dispose();
            a.Init(car, spec, local);
            return a;
        }

        void Init(AcCarEntry car, AcCarSpec spec, bool local)
        {
            if (_inited && !_disposed && Car == car && Local == local) { Spec = spec ?? Spec; return; }
            _inited = true;
            _disposed = false;
            Car = car; Spec = spec; Local = local;
            Audible = local;   // a remote / AI car waits for the budget's first decision (nearest 10 within 350 m) before it builds its sound set
            _bank = AcSoundBank.ForCar(car);
            if (_bank != null) _bank.AddUser();
            else if (car != null) AcLog.WarnOnce("Audio: " + car.Id + " has no sound bank (sfx/*.bank) - only common.bank sounds (surfaces, collisions) for it");
            else AcLog.InfoOnce("Audio: a car that is not installed here gets common.bank sounds only (surfaces, collisions)");
            _common = AcSoundBank.Common();
            if (_common != null) _common.AddUser();
            _eEngine = Emitter("Ac Snd Engine"); _eExhaust = Emitter("Ac Snd Exhaust"); _eBody = Emitter("Ac Snd Body");
            _eDoor = Emitter("Ac Snd Door"); _eHit = Emitter("Ac Snd Hit");
            for (int i = 0; i < 4; i++) _eWheel[i] = Emitter("Ac Snd Wheel " + i);
            if (!AcAudioMix.Cars.Contains(this)) AcAudioMix.Cars.Add(this);
            AcEvents.SeatChanged -= OnSeatChanged; AcEvents.SeatChanged += OnSeatChanged;
            AcEvents.GearChanged -= OnGearChanged; AcEvents.GearChanged += OnGearChanged;
            AcEvents.CarImpact -= OnCarImpact; AcEvents.CarImpact += OnCarImpact;
            if (AcRace.RepairSound == null) AcRace.RepairSound = RepairSoundHook;   // R calls it at the start / end of a pit-box repair
        }

        Transform Emitter(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        /// <summary>Feeds this frame's car state (rpm, throttle, gear, speed, wheel skid + surfaces, limiter, boost, lights). Call once a frame;
        /// a car not fed for <see cref="FeedTimeout"/> s fades out.</summary>
        public void Feed(AcTelemetry telemetry)
        {
            float now = Time.time;
            if (!_fed || now - _fedAt > FeedTimeout)
            {
                // first feed, or feeding resumed after a gap: start from these values (no sweep from stale ones, no shift sound)
                _rpmS = telemetry.Rpm; _thrS = Mathf.Clamp01(telemetry.Throttle); _speedS = Mathf.Abs(telemetry.SpeedKmh);
                if (_fed) _lastGear = telemetry.Gear;
                _armed = false;
            }
            _t = telemetry;
            _fed = true;
            _fedAt = now;
        }

        /// <summary>Interior (listener in this car's cockpit: _int events) or exterior. S calls it when seating the local player (and a
        /// passenger); the car also checks the listener itself (a chase camera hears the exterior, as in AC).</summary>
        public void SetInterior(bool inside) { _forcedInside = inside; _interiorSet = true; }

        /// <summary>Door opening / closing one-shot (event door).</summary>
        public void Door(AcSide side, bool opening)
        {
            if (_door == null) { if (_built) AcLog.InfoOnce("Audio: missing event door in " + Label); return; }
            if (_eDoor != null) _eDoor.localPosition = new Vector3(side == AcSide.Left ? -_halfWidth : _halfWidth, _eye.y - 0.3f, _eye.z + 0.4f); // // ours: door handle beside the driver
            _door.Set(AcFeed.State, opening ? 1f : 0f);
            _door.Trigger();
        }

        /// <summary>Gear change one-shot (gear_int / gear_ext; grind when <paramref name="grind"/>).</summary>
        public void Gear(int newGear, bool grind)
        {
            if (grind)
            {
                if (_grind != null) _grind.PlayFor(GrindSeconds());
                else if (_built) AcLog.InfoOnce("Audio: missing event gear_grind in " + Label);
                return;
            }
            if (newGear == _lastGear) return;
            bool up = _lastGear == int.MinValue || newGear > _lastGear;
            _lastGear = newGear;
            var p = Interior && _gearInt != null && (_gearInt.Ready || _gearExt == null) ? _gearInt : _gearExt;
            if (p == null) return;
            p.Set(AcFeed.State, up ? 1f : 0f);
            p.Trigger();
        }

        float GrindSeconds()
        {
            float s = _grind.TimelineSeconds;
            if (s <= 0.05f && _grind.Ev.Instruments.Length > 0)
            {
                var clip = _bank.Clip(_grind.Ev.Instruments[0].Samples[0]);
                if (clip != null) s = clip.length;
            }
            return s > 0.05f ? s : 1f; // // ours fallback
        }

        /// <summary>Collision: a new contact (gap &gt; <see cref="NewContactGap"/>) plays common.bank collisions/car|track/hit by impact
        /// speed; the repeats P sends while the contact stays (sliding speed) drive collisions/car|track/scrape instead.</summary>
        public void Collision(float impactKmh, Vector3 point)
        {
            if (_disposed) return;
            if (!_commonBuilt) BuildCommon();
            if (_common == null || _common.Meta == null) return;
            float now = Time.time, kmh = Mathf.Abs(impactKmh);
            bool car = HitsCar(point);
            if (_eHit != null) _eHit.position = point;
            bool fresh = now - _lastContact > NewContactGap;
            _lastContact = now;
            if (fresh)
            {
                var p = car ? _hitCar : _hitTrack;
                if (p == null) return;
                p.Set(AcFeed.ImpactSpeed, kmh);
                p.Set(AcFeed.ImpactAngle, 90f); // // ours: the angle is not in AcEvents.CarImpact
                p.Trigger();
                return;
            }
            var sc = car && _scrapeCar != null ? _scrapeCar : _scrapeTrack;
            if (sc == null) return;
            if (_scrape != null && _scrape != sc && _scrape.Playing) _scrape.Stop();
            _scrape = sc;
            sc.Set(AcFeed.Speed, kmh);
            _scrapeDecay = 0f;
            sc.Set(AcFeed.Decay, 0f);
            sc.Start();
        }

        bool HitsCar(Vector3 point)
        {
            int n;
            try { n = Physics.OverlapSphereNonAlloc(point, 0.6f, Overlap, 1 << AcLayers.Car, QueryTriggerInteraction.Ignore); }
            catch (Exception) { return false; }
            bool hit = false;
            for (int i = 0; i < n; i++)
            {
                var c = Overlap[i];
                Overlap[i] = null;
                if (!hit && c != null && !c.transform.IsChildOf(transform)) hit = true;
            }
            return hit;
        }

        /// <summary>Horn on/off.</summary>
        public void Horn(bool on)
        {
            _horn = on;
            if (_hornP == null) { if (on && _built) AcLog.InfoOnce("Audio: missing event horn in " + Label); return; }
            if (on) _hornP.Start(); else _hornP.Stop();
        }

        /// <summary>
        /// Pit-box repair (R calls it, DAMAGE_DESIGN 4 / 6): <paramref name="starting"/> true plays common.bank event:/common/unscrew at the
        /// car, false stops it and plays event:/common/screw (both exist in common.bank [V harness fev]). The two players are made with the
        /// common set when damage was on at that time, else here on first use (the car stands in its box, never mid-drive).
        /// </summary>
        public void Repair(bool starting)
        {
            if (_disposed) return;
            if (!_commonBuilt) BuildCommon();
            if (!_commonBuilt || _common == null || _common.Meta == null) { AcLog.InfoOnce("Audio: repair sound skipped (common.bank not ready)"); return; }
            if (_screw == null && _unscrew == null) MakeRepairPlayers();
            if (_eHit != null) _eHit.localPosition = new Vector3(0f, 0.4f, 0f);   // ours: at the car's middle, a little up (the mechanics work all round)
            if (starting)
            {
                if (_screw != null && _screw.Playing) _screw.Stop();
                if (_unscrew != null) _unscrew.Trigger();
            }
            else
            {
                if (_unscrew != null && _unscrew.Playing) _unscrew.Stop();
                if (_screw != null) _screw.Trigger();
            }
            AcLog.InfoOnce("Audio: pit repair sounds " + (_unscrew != null ? "event:/common/unscrew" : "(no unscrew)") + " / " + (_screw != null ? "event:/common/screw" : "(no screw)"));
        }

        /// <summary>R's AcRace.RepairSound: the repaired car's own audio plays the sounds.</summary>
        static void RepairSoundHook(AcCarRig rig, bool starting)
        {
            AcCarAudio a = rig != null ? rig.Audio : null;
            if (a != null) a.Repair(starting);
        }

        void MakeRepairPlayers()
        {
            var m = _common.Meta;
            AcSndEvent us = m.Event("event:/common/unscrew"), sc = m.Event("event:/common/screw");
            _unscrew = MakeCommon(us, _eHit);
            _screw = MakeCommon(sc, _eHit);
            if (_unscrew == null && _screw == null) AcLog.InfoOnce("Audio: common.bank has no event:/common/screw|unscrew - the pit repair is silent");
        }

        /// <summary>suspension_damage for the wheel event's flutter layer: the worst steer rod of this car, RodOffset / MAX_DAMAGE 0..1 (P's
        /// AcCarDamage; remote cars from N's decoded levels). 0 without a damage state - and at DAMAGE 0 the players pin it at 0 anyway.</summary>
        float SuspDamage()
        {
            if (LabSuspDamage >= 0f) return Mathf.Clamp01(LabSuspDamage);
            if (!_physLooked) { _physLooked = true; _phys = GetComponent<AcCarPhysics>(); }
            if (_phys == null) return 0f;
            AcCarDamage d = _phys.Damage;
            return d != null ? Mathf.Clamp01(d.MaxSuspDamage01) : 0f;
        }

        string Label { get { return Car != null ? Car.Id : "?"; } }

        // ------------------------------------------------------------------ build

        void TryBuild()
        {
            if (_bank != null && !_bank.MetaReady) return;
            // a remote / AI car builds its sound set (sources, sample decode) only when it first becomes audible
            if (!Local && !Audible && !Interior) return;
            _built = true;
            _snd = (_bank != null ? _bank.Sounds : null) ?? new AcCarSounds { CarId = Label };
            PlaceEmitters();
            if (_bank == null || _bank.Meta == null) return;
            var m = _bank.Meta;
            _engExt = Make("engine_ext", _eEngine, AcAudioGroup.Engine, true);
            _gearExt = Make("gear_ext", _eEngine, AcAudioGroup.Effects, false);
            _bfExt = _snd.HasBackfire ? Make("backfire_ext", _eExhaust, AcAudioGroup.Engine, false) : null;
            _limiter = Make("limiter", _eEngine, AcAudioGroup.Engine, false);
            _hornP = Make("horn", _eBody, AcAudioGroup.Effects, false);
            _door = Make("door", _eDoor, AcAudioGroup.Effects, false);
            var skidExt = m.Event("skid_ext");
            for (int i = 0; i < 4; i++) _skidExt[i] = MakeEv(skidExt, _eWheel[i], AcAudioGroup.Effects, false);
            if (Local || Interior) BuildInterior();
            int layers = (m.Event("engine_int") != null ? m.Event("engine_int").Instruments.Length : 0) + (m.Event("engine_ext") != null ? m.Event("engine_ext").Instruments.Length : 0);
            if (_door == null) AcLog.InfoOnce("Audio: missing event door in " + Label);
            if (_limiter == null) AcLog.InfoOnce("Audio: missing event limiter in " + Label);
            if (_snd.HasBackfire && _bfExt == null) AcLog.InfoOnce("Audio: missing event backfire_ext in " + Label + " (sounds.ini has [BACKFIRE])");
            if (Local && _snd.MaxBoostBar > 0f && _turbo == null) AcLog.InfoOnce("Audio: missing event turbo in " + Label + " (engine.ini has a turbo)");
            if (_engExt == null) AcLog.WarnOnce("Audio: missing event engine_ext in " + Label);
            AcLog.InfoOnce("Audio: " + Label + " sound set up as " + (Local ? "local" : "remote/AI") + " car: " + _all.Count + " event players, " + Sources + " sources, " + layers + " engine layers, engine at "
                           + (_snd.EnginePosition.Length > 0 ? _snd.EnginePosition : "CoG") + ", engine_int " + (_engInt != null ? (_engInt.Spatial ? "3D" : "2D") : "-"));
        }

        void BuildInterior()
        {
            if (_bank == null || _bank.Meta == null) return;
            if (_engInt == null) _engInt = Make("engine_int", _eEngine, AcAudioGroup.Engine, true);
            if (_gearInt == null) _gearInt = Make("gear_int", _eEngine, AcAudioGroup.Effects, false);
            if (_bfInt == null && _snd.HasBackfire) _bfInt = Make("backfire_int", _eExhaust, AcAudioGroup.Engine, false);
            if (_tcInt == null) _tcInt = Make("tractioncontrol_int", _eEngine, AcAudioGroup.Engine, false);
            if (_skidInt[0] == null)
            {
                var skidInt = _bank.Meta.Event("skid_int");
                for (int i = 0; i < 4; i++) _skidInt[i] = MakeEv(skidInt, _eWheel[i], AcAudioGroup.Effects, false);
            }
            // the rest of the cockpit set (also for a passenger riding in a remote car: DESIGN passengers)
            if (_tcExt == null) _tcExt = Make("tractioncontrol_ext", _eEngine, AcAudioGroup.Engine, false);
            if (_turbo == null) _turbo = Make("turbo", _eEngine, AcAudioGroup.Engine, false);
            if (_transmission == null) _transmission = Make("transmission", _eEngine, AcAudioGroup.Engine, false);
            if (_grind == null) _grind = Make("gear_grind", _eEngine, AcAudioGroup.Effects, false);
            if (_wind == null) _wind = Make("wind", _eBody, AcAudioGroup.Effects, false);
            if (_wheel == null) _wheel = Make("wheel", _eBody, AcAudioGroup.Effects, false);
            if (_body == null) _body = Make("bodywork", _eBody, AcAudioGroup.Effects, false);
        }

        AcEventPlayer Make(string ev, Transform at, AcAudioGroup g, bool high)
        {
            return MakeEv(_bank.Meta.Event(ev), at, g, high);
        }

        AcEventPlayer MakeEv(AcSndEvent ev, Transform at, AcAudioGroup g, bool high)
        {
            if (ev == null || ev.Instruments.Length == 0) return null;
            var p = new AcEventPlayer(ev, _bank, at, g, SpatialFor(ev), Interior);
            p.Require(high);
            _all.Add(p);
            return p;
        }

        /// <summary>3D or 2D for an event: the bank's spatialiser decides (engine_int is 3D at the engine in every AC bank - setting
        /// audio.interior3d off makes it 2D); an event the bank leaves 2D (gear_int, some limiters / backfires) is 2D for the listener in
        /// this car and 3D with the SDK fallback distances for everyone else. // ours (a 2D sound of a car 300 m away would be wrong)</summary>
        bool SpatialFor(AcSndEvent ev)
        {
            if (ev.Name == "engine_int") return ev.Spatialiser != null && AcAudioMix.Interior3D;
            if (ev.Spatialiser != null) return true;
            return !Interior;
        }

        void BuildCommon()
        {
            if (_commonBuilt || _common == null || !_common.MetaReady) return;
            if (!Local && !Audible && !Interior) return;
            _commonBuilt = true;
            if (_common.Meta == null) return;
            var m = _common.Meta;
            _hitCar = MakeCommon(m.Event("event:/collisions/car/hit"), _eHit);
            _hitTrack = MakeCommon(m.Event("event:/collisions/track/hit"), _eHit);
            _scrapeCar = MakeCommon(m.Event("event:/collisions/car/scrape"), _eHit);
            _scrapeTrack = MakeCommon(m.Event("event:/collisions/track/scrape"), _eHit);
            if (_hitCar == null || _hitTrack == null) AcLog.InfoOnce("Audio: common.bank has no event:/collisions/car|track/hit - collisions are silent");
            if (AcFeed.DamageOn()) MakeRepairPlayers();   // damage on: the repair one-shots are decoded with the common set (else on first Repair)
            // every surface event of common.bank on every wheel, now (no player / source is made while driving)
            var list = new List<AcSndEvent>();
            for (int i = 0; i < m.Events.Count; i++)
                if (m.Events[i].Instruments.Length > 0 && m.Events[i].Path.StartsWith("event:/surfaces/", StringComparison.OrdinalIgnoreCase)) list.Add(m.Events[i]);
            _surfEv = list.ToArray();
            _surfP = new AcEventPlayer[4 * _surfEv.Length];
            _surfDecay = new float[_surfP.Length];
            for (int w = 0; w < 4; w++)
                for (int k = 0; k < _surfEv.Length; k++)
                {
                    _surfP[w * _surfEv.Length + k] = MakeCommon(_surfEv[k], _eWheel[w]);
                    _surfDecay[w * _surfEv.Length + k] = 1f;
                }
        }

        AcEventPlayer MakeCommon(AcSndEvent ev, Transform at)
        {
            if (ev == null || ev.Instruments.Length == 0 || at == null) return null;
            var p = new AcEventPlayer(ev, _common, at, AcAudioGroup.Effects, SpatialFor(ev), Interior);
            p.Require(false);
            _all.Add(p);
            return p;
        }

        void PlaceEmitters()
        {
            float x, y, z;
            _snd.EngineEmitter(out x, out y, out z); _eEngine.localPosition = new Vector3(x, y, z);
            _snd.BackfireEmitter(out x, out y, out z); _eExhaust.localPosition = new Vector3(x, y, z);
            for (int i = 0; i < 4; i++) { _snd.WheelEmitter(i, out x, out y, out z); _eWheel[i].localPosition = new Vector3(x, y, z); }
            _eBody.localPosition = Vector3.zero;
            _halfWidth = Mathf.Max(_snd.TrackFrontM, _snd.TrackRearM) * 0.5f + 0.15f; // // ours: door skin just outside the track
            if (_snd.HasEye) _eye = new Vector3(_snd.EyeX, _snd.EyeY, _snd.EyeZ);
            _eDoor.localPosition = new Vector3(-_halfWidth, _eye.y - 0.3f, _eye.z + 0.4f);
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (_disposed) return;
            var host = AcAudioHost.Instance != null ? AcAudioHost.Instance : AcAudioHost.Ensure();
            if (host != null) host.TickOnce();
            try { Step(Time.deltaTime); }
            catch (Exception e)
            {
                AcLog.Error("Audio: " + Label + " failed, its sound is stopped: " + e);
                try { Dispose(); } catch (Exception) { _disposed = true; }
            }
        }

        void Step(float dt)
        {
            UpdateInterior();
            if (!_built) { TryBuild(); if (!_built) return; }
            if (!_commonBuilt) BuildCommon();
            if (!Ready)
            {
                bool ok = (_engExt == null || _engExt.Ready) && (_engInt == null || _engInt.Ready);
                if (ok && (_engExt != null || _engInt != null))
                {
                    Ready = true;
                    if (!_readyLogged) { _readyLogged = true; AcLog.InfoOnce("Audio: " + Label + " engine sound ready (" + (Local ? "local" : "remote/AI") + ")"); }
                }
            }
            if (_cfgVersion != AcAudioMix.ConfigVersion || _configuredAboard != Interior) Reconfigure();

            // the owner stopped feeding (left / no timeline sample): fade the whole car out over 0.3 s, then stop it // ours
            bool live = _fed && Time.time - _fedAt <= FeedTimeout;
            _fade = live ? 1f : Mathf.Max(0f, _fade - dt / 0.3f);
            bool audible = (Local || Audible || Interior) && _fade > 0f;
            for (int i = 0; i < _all.Count; i++) { _all[i].Muted = !audible; _all[i].CarGain = _fade; }
            if (!audible)
            {
                if (_fed) _lastGear = _t.Gear;   // no stale gear / backfire edge when the car becomes audible again
                _armed = false;
                if (_fade <= 0f) { _horn = false; _hornLights = false; }
                for (int i = 0; i < _all.Count; i++) { if (_fade <= 0f && _all[i].Playing) _all[i].Stop(); _all[i].Drive(_feed, dt); }
                return;
            }

            float k40 = 1f - Mathf.Exp(-dt / 0.04f), k70 = 1f - Mathf.Exp(-dt / 0.07f), k100 = 1f - Mathf.Exp(-dt / 0.1f); // ETS smoothing 40 / 70 / 100 ms
            _rpmS += (_t.Rpm - _rpmS) * k40;
            _thrS += (Mathf.Clamp01(_t.Throttle) - _thrS) * k70;
            _speedS += (Mathf.Abs(_t.SpeedKmh) - _speedS) * k100;
            bool running = _fed && _rpmS > 50f; // // ours: no engine below 50 rpm (stalled / not fed)
            bool rolling = _fed && _speedS > 0.5f;

            _feed[AcFeed.Rpm] = _rpmS;
            _feed[AcFeed.Throttle] = _thrS;
            _feed[AcFeed.Speed] = _speedS;
            _feed[AcFeed.Brake] = Mathf.Clamp01(_t.Brake);
            _feed[AcFeed.Boost] = Mathf.Max(0f, _t.BoostBar);           // pipeline PDF: "boost (in bar)"
            _feed[AcFeed.DrivetrainSpeed] = _t.Gear != 0 ? _speedS : 0f;  // // ours: drivetrain speed ~ road speed in gear
            _feed[AcFeed.TransmissionLoad] = _thrS * 50f;                // // ours: transmission_load 0..50 from throttle
            _feed[AcFeed.ImpactAngle] = 90f;
            _feed[AcFeed.SuspDamage] = SuspDamage();

            // engine: one of the two events plays
            AcEventPlayer engOn = Interior && _engInt != null && (_engInt.Ready || _engExt == null) ? _engInt : _engExt, engOff = engOn == _engInt ? _engExt : _engInt;
            if (engOff != null && engOff.Playing) engOff.Stop();
            if (engOn != null) { if (running) engOn.Start(); else if (engOn.Playing) engOn.Stop(); }

            // gear change seen in the telemetry (remote / AI cars, or P without GearChanged)
            if (live && _t.Gear != _lastGear)
            {
                if (_lastGear == int.MinValue) _lastGear = _t.Gear;
                else Gear(_t.Gear, false);
            }

            // horn of a remote / AI car from its lights bits (N copies the owner's AcLightState; P sets Horn) - the local car's comes by Horn()
            if (!Local && live)
            {
                bool hl = (_t.Lights & AcLightState.Horn) != 0;
                if (hl != _hornLights) { _hornLights = hl; Horn(hl); }
            }

            // limiter / traction control: "decay" = seconds since the last cut (bus 0 -> 0 dB, ~0.14 s -> silent)
            _sinceLimiter = live && _t.Limiter ? 0f : _sinceLimiter + dt;
            _sinceTc = live && _t.TcActive ? 0f : _sinceTc + dt;
            Hold(_limiter, running && _sinceLimiter < 0.5f, AcFeed.Decay, _sinceLimiter);
            AcEventPlayer tcOn = Interior && _tcInt != null ? _tcInt : _tcExt, tcOff = tcOn == _tcInt ? _tcExt : _tcInt;
            if (tcOff != null && tcOff.Playing) tcOff.Stop();
            Hold(tcOn, running && _sinceTc < 0.5f, AcFeed.Decay, _sinceTc);

            Hold(_turbo, running, -1, 0f);
            Hold(_transmission, running && rolling, -1, 0f);
            Hold(_wind, rolling, -1, 0f);
            Hold(_wheel, rolling, -1, 0f);
            if (_body != null)
            {
                float v = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float tr = _t.Wheel(i).TravelM;
                    if (dt > 1e-4f) v = Mathf.Max(v, Mathf.Abs(tr - _travelPrev[i]) / dt);
                    _travelPrev[i] = tr;
                }
                float s = v * _snd.BodyworkGain;
                _feed[AcFeed.SuspTravelSpeed] = s < _snd.BodyworkMin ? 0f : Mathf.Clamp01(s); // // ours reading of [BODY_WORK] MIN / GAIN
                Hold(_body, rolling, -1, 0f);
            }

            if (live) Backfire();
            Skids(dt, live);
            Surfaces(dt, live);
            if (_scrape != null)
            {
                float since = Time.time - _lastContact;   // decay = seconds since the contact ended (as the limiter's), held at 0 between P's repeats
                _scrapeDecay = since <= ScrapeHold ? 0f : Mathf.Min(1f, since - ScrapeHold);
                _scrape.Set(AcFeed.Decay, _scrapeDecay);
                if (_scrapeDecay >= 1f && _scrape.Playing) _scrape.Stop();
            }
            if (_hornP != null && _horn && !_hornP.Playing) _hornP.Start();

            for (int i = 0; i < _all.Count; i++) _all[i].Drive(_feed, dt);
        }

        static void Hold(AcEventPlayer p, bool on, int feed, float value)
        {
            if (p == null) return;
            if (feed >= 0) p.Set(feed, value);
            if (on) p.Start(); else if (p.Playing) p.Stop();
        }

        /// <summary>Re-applies 2D/3D + doppler to every player (a setting changed, or the listener got in / out of this car).</summary>
        void Reconfigure()
        {
            _cfgVersion = AcAudioMix.ConfigVersion;
            _configuredAboard = Interior;
            for (int i = 0; i < _all.Count; i++) _all[i].Reconfigure(SpatialFor(_all[i].Ev), Interior);
        }

        void UpdateInterior()
        {
            bool seatedHere = _seatInside;
            try { if (AcLocal.Seated && AcLocal.SeatCar != null && AcLocal.SeatCar.Root == gameObject) seatedHere = true; } catch (Exception) { }
            // S tells the view: SetInterior (AcCarCamera) / AcLocal.ViewInside (cockpit or dash camera, or VR); else the camera mode
            bool cockpitCam = _interiorSet ? _forcedInside : (AcLocal.ViewInside || AcLocal.CameraMode == AcCameraMode.Cockpit || AcLocal.CameraMode == AcCameraMode.Dash);
            try { if (!_interiorSet && AcPlayer.XrPresent) cockpitCam = true; } catch (Exception) { }
            if (_interiorSet && _forcedInside) seatedHere = true;
            float d = AcAudioMix.HasListener ? Vector3.Distance(AcAudioMix.ListenerPos, transform.TransformPoint(_eye)) : 99f;
            // seated + cockpit view + the listener near the driver eye; or (nobody said otherwise) the listener right at the eye // ours radii
            bool inside = (seatedHere && cockpitCam && d < 1.5f) || (!_interiorSet && d < 0.45f);
            if (inside && _built && _engInt == null && _bank != null && _bank.Meta != null) BuildInterior();
            if (inside != Interior)
            {
                Interior = inside;
                AcLog.InfoOnce("Audio: " + Label + " switched to " + (inside ? "interior (engine_int)" : "exterior (engine_ext)") + " sounds" + (Local ? "" : " (riding in a remote car)"));
            }
        }

        void Backfire()
        {
            if (!_snd.HasBackfire || (_bfExt == null && _bfInt == null)) return;
            float thr = Mathf.Clamp01(_t.Throttle), rpm = _t.Rpm;
            // sounds.ini [BACKFIRE] (pipeline PDF p.8): armed above TRIGGERGAS; fires when the gas falls below MAXGAS inside MINRPM..MAXRPM
            if (thr >= _snd.BackfireTriggerGas) _armed = true;
            if (_armed && thr <= _snd.BackfireMaxGas)
            {
                _armed = false;
                if (rpm >= _snd.BackfireMinRpm && rpm <= _snd.BackfireMaxRpm)
                {
                    bool inside = Interior && _bfInt != null;
                    var p = inside ? _bfInt : _bfExt;
                    if (p != null)
                    {
                        p.Gain = inside ? _snd.BackfireVolumeIn : _snd.BackfireVolumeOut;
                        p.Trigger();   // the burst's throttle bus follows the live (smoothed) throttle
                    }
                }
            }
        }

        void Skids(float dt, bool live)
        {
            float staticLoad = _snd.TotalMassKg > 0f ? _snd.TotalMassKg * 9.81f / 4f : 0f;
            float k = 1f - Mathf.Exp(-dt / 0.05f); // // ours: 50 ms smoothing of the slide level
            for (int i = 0; i < 4; i++)
            {
                var w = _t.Wheel(i);
                float load = 1f;
                if (staticLoad > 0f && w.LoadN > 0f) load = Mathf.Clamp01(w.LoadN / staticLoad / Mathf.Max(0.05f, _snd.SkidEntryPoint)); // [SKIDS] ENTRY_POINT: "related to the tyre load" // ours shape
                float target = !live || (Local && !w.Contact && w.LoadN <= 0f) ? 0f : Mathf.Clamp01(w.Skid) * load;
                _skidS[i] += (target - _skidS[i]) * k;
                AcEventPlayer on = Interior && _skidInt[i] != null && (_skidInt[i].Ready || _skidExt[i] == null) ? _skidInt[i] : _skidExt[i], off = on == _skidInt[i] ? _skidExt[i] : _skidInt[i];
                if (off != null && off.Playing) off.Stop();
                if (on == null) continue;
                on.Gain = _snd.SkidMixVolume * _skidS[i];
                if (_skidS[i] > 0.02f) on.Start();
                else if (_skidS[i] < 0.005f && on.Playing) on.Stop();
            }
        }

        void Surfaces(float dt, bool live)
        {
            int n = _surfEv.Length;
            if (n == 0 || _common == null || _common.Meta == null) return;
            for (int s = 0; s < _surfDecay.Length; s++) _surfDecay[s] = Mathf.Min(1f, _surfDecay[s] + dt * 2f); // // ours: decay 0 -> 1 over 0.5 s after leaving
            if (live)
                for (int i = 0; i < 4; i++)
                {
                    var w = _t.Wheel(i);
                    if (!w.Contact || w.Surface == null) continue;
                    AcSndEvent ev = SurfaceEvent(w.Surface);
                    if (ev == null) continue;
                    int k = -1;
                    for (int e = 0; e < n; e++) if (ReferenceEquals(_surfEv[e], ev)) { k = e; break; }
                    if (k < 0) continue;
                    int slot = i * n + k;
                    _surfDecay[slot] = 0f;
                    var sp = _surfP[slot];
                    if (sp != null) sp.PitchMul = w.Surface.WavPitch > 0f ? w.Surface.WavPitch : 1f;   // surfaces.ini WAV_PITCH (0 = as recorded)
                }
            for (int s = 0; s < _surfP.Length; s++)
            {
                var p = _surfP[s];
                if (p == null) continue;
                p.Set(AcFeed.Decay, _surfDecay[s]);
                if (_surfDecay[s] < 1f) p.Start(); else if (p.Playing) p.Stop();
            }
        }

        /// <summary>common.bank event for a track surface (surfaces.ini WAV=grass.wav -> event:/surfaces/grass); null for silent surfaces.
        /// Cached per surface object (no allocation after the first time; cleared when the track unloads).</summary>
        AcSndEvent SurfaceEvent(AcSurface s)
        {
            AcSndEvent ev;
            if (SurfaceEvents.TryGetValue(s, out ev)) return ev;
            string path = s.SoundEvent;
            ev = path != null ? _common.Meta.Event(path) : null;
            if (ev != null && ev.Instruments.Length == 0) ev = null;
            if (path != null && ev == null) AcLog.InfoOnce("Audio: surface " + s.Key + " names " + path + " which common.bank does not play");
            SurfaceEvents[s] = ev;
            return ev;
        }

        /// <summary>Forgets the track's surfaces (AcSession.Unloading / the host closing).</summary>
        internal static void ClearSurfaceCache() { SurfaceEvents.Clear(); }

        // ------------------------------------------------------------------ events

        void OnSeatChanged(AcCarRig rig, AcSeatKind seat)
        {
            _seatInside = rig != null && rig.Root == gameObject;
            if (!_seatInside) _interiorSet = false; // got out (or sat in another car): S's last SetInterior no longer applies
        }

        void OnGearChanged(AcCarRig rig, int gear)
        {
            if (rig != null && rig.Root == gameObject) Gear(gear, false);
        }

        void OnCarImpact(AcCarRig rig, float kmh, Vector3 point)
        {
            if (rig != null && rig.Root == gameObject) Collision(kmh, point);
        }

        // ------------------------------------------------------------------ diagnostics

        /// <summary>Snapshot of every playing source of this car (lab renders, diagnostics). Adds to <paramref name="into"/>.</summary>
        public void Tap(List<AcAudioVoice> into)
        {
            if (into == null) return;
            for (int i = 0; i < _all.Count; i++) _all[i].Tap(into);
        }

        /// <summary>Playing sources of this car.</summary>
        public int Voices
        {
            get { int n = 0; for (int i = 0; i < _all.Count; i++) n += _all[i].Voices; return n; }
        }

        /// <summary>AudioSources this car owns (all made when its sound set was built).</summary>
        public int Sources
        {
            get { int n = 0; for (int i = 0; i < _all.Count; i++) n += _all[i].Sources; return n; }
        }

        /// <summary>The sound set is built (a remote car builds when first audible).</summary>
        public bool Built { get { return _built; } }

        /// <summary>The collision players' state (lab diagnostics; allocates).</summary>
        public string DescribeCollisions()
        {
            return "hit track " + PlayerState(_hitTrack) + ", scrape track " + PlayerState(_scrapeTrack) + ", scrape car " + PlayerState(_scrapeCar)
                   + ", current scrape " + (_scrape == null ? "none" : _scrape == _scrapeTrack ? "track" : "car") + " decay " + _scrapeDecay.ToString("0.00")
                   + ", common built " + _commonBuilt;
        }

        static string PlayerState(AcEventPlayer p)
        {
            return p == null ? "none" : "(playing " + p.Playing + ", ready " + p.Ready + ", required " + p.Required + ", voices " + p.Voices + ")";
        }

        /// <summary>One line of state (lab / diagnostics).</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Label).Append(Local ? " local" : " remote").Append(Ready ? " ready" : _built ? " loading" : " not built").Append(Interior ? " interior" : " exterior")
              .Append(Audible ? "" : " culled").Append(" rpm ").Append(_rpmS.ToString("0")).Append(" thr ").Append(_thrS.ToString("0.00")).Append(" players ").Append(_all.Count)
              .Append(" sources ").Append(Sources).Append(" voices ").Append(Voices).Append(_fade < 1f ? " fade " + _fade.ToString("0.00") : "");
            return sb.ToString();
        }

        /// <summary>Stops and frees the clips.</summary>
        public void Dispose()
        {
            if (_disposed && !_inited) return;
            _disposed = true;
            _inited = false;
            Ready = false;
            AcEvents.SeatChanged -= OnSeatChanged;
            AcEvents.GearChanged -= OnGearChanged;
            AcEvents.CarImpact -= OnCarImpact;
            for (int i = 0; i < _all.Count; i++) { try { _all[i].Stop(); _all[i].Destroy(); } catch (Exception) { } }
            _all.Clear();
            _engInt = _engExt = _gearInt = _gearExt = _grind = _bfInt = _bfExt = _tcInt = _tcExt = _limiter = _turbo = _transmission = _wind = _wheel = _body = _hornP = _door = null;
            _hitCar = _hitTrack = _scrapeCar = _scrapeTrack = _scrape = null;
            _screw = _unscrew = null; _phys = null; _physLooked = false;
            for (int i = 0; i < 4; i++) { _skidInt[i] = null; _skidExt[i] = null; }
            _surfEv = new AcSndEvent[0]; _surfP = new AcEventPlayer[0]; _surfDecay = new float[0];
            if (_bank != null) _bank.RemoveUser();
            if (_common != null) _common.RemoveUser();
            _bank = null; _common = null;
            _built = false; _commonBuilt = false; _fed = false; _readyLogged = false; _lastGear = int.MinValue; _fade = 1f; _horn = false; _hornLights = false;
            Interior = false; _cfgVersion = -1;
            DestroyEmitter(ref _eEngine); DestroyEmitter(ref _eExhaust); DestroyEmitter(ref _eBody); DestroyEmitter(ref _eDoor); DestroyEmitter(ref _eHit);
            for (int i = 0; i < 4; i++) DestroyEmitter(ref _eWheel[i]);
            AcAudioMix.Cars.Remove(this);
        }

        static void DestroyEmitter(ref Transform t)
        {
            if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            t = null;
        }

        void OnDestroy() { Dispose(); }
    }
}
