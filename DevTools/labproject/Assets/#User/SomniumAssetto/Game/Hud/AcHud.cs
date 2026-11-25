using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] AC-like essentials while driving: speed (km/h | mph), gear, rpm bar with limiter, lap / best / last / delta / sector split,
    /// position, lap count, start lights, flags, pit limiter, wrong way, messages, standings tower. Monitor = screen overlay; VR = a panel
    /// anchored to the car under the dash line (never head-locked while driving). Reads AcLocal (Telemetry, Race fields, PedalsArmed),
    /// AcSession, AcRace (Standings, QualifyActive, SessionTimeLeft) and AcNetGame (player joined / left); art from content/gui +
    /// content/texture, fonts from content/fonts (Data/Hud). Started by R.
    /// <para>Flow: <see cref="Start"/> (R's first tick) registers the HUD settings, subscribes AcEvents.Message / SeatChanged /
    /// LocalCarChanged (+ AcNetGame.PlayerJoined / PlayerLeft) and adds <see cref="AcHudDriver"/> (LateUpdate, order 10010 - after the seat
    /// carry at 10000). The driver loads the art once the install is known (<see cref="AcHudArt"/>), then each frame gathers an
    /// <see cref="AcHudFeed"/> and draws it on the monitor overlay (<see cref="AcHudDesk"/>) or, with a headset, on the panel of the seated car
    /// (<see cref="AcHudVr"/>). Messages that arrive before either view exists wait in a small queue.</para>
    /// </summary>
    public static class AcHud
    {
        /// <summary>The HUD is up.</summary>
        public static bool Shown { get; private set; }

        private static bool _started;
        private static AcGame _game;
        private static GameObject _home;
        private static AcHudDesk _desk;
        private static AcHudVr _vr;
        private static AcCarRig _rig;
        private static bool _passenger;
        private static AcHudFeed _feed;
        private static bool _hasFake;
        private static AcHudFeed _fake;
        private static IReadOnlyList<AcStanding> _fakeStandings;
        private static readonly List<AcStanding> NoStandings = new List<AcStanding>();
        private static int _lightReds; private static bool _lightGreen, _lightsOn;
        private static bool _settingsDone, _vrMode, _vrModeKnown;
        private static float _nextVrCheck;
        private static bool _wasValid = true;
        private static int _lastPenalty;
        private static float _penaltyUntil;
        private static float _maxBoost; private static AcCarRig _boostFor;
        private static string _pendingShowLog;
        private static bool _failedOnce, _netHooked;
        // messages waiting for a view (desk / VR panel not built yet) // ours: 4
        private static readonly string[] _queueText = new string[4];
        private static readonly float[] _queueUntil = new float[4];
        // sector splits (from AcLocal.Sector / Laps / LapMs / LastSectorMs / BestMs)
        private static int _trackLaps = -1, _trackSector, _secStartMs;
        private static int[] _bestSec = new int[0];
        private static bool _bestSecValid;
        private static int _splitSector, _splitMs, _splitDelta;
        private static float _splitUntil;
        // damage displayer (iteration 2): shown until _damageUntil after a CarDamaged on my car; lab fake values
        private static float _damageUntil = -1f;
        private static bool _blownShown, _hasFakeDamage;
        private static readonly float[] _fakeZones = new float[5], _fakeSusp = new float[4];
        private static float _fakeEnginePct;
        /// <summary>Lab: the DAMAGE multiplier the displayer assumes (-1 = the session's AcRules.DamageMultiplier).</summary>
        public static int FakeDamageMultiplier = -1;

        /// <summary>VR: the hint while driving with the pedals not armed yet (S: armed after both triggers were released). // ours text</summary>
        public const string PedalHint = "RELEASE BOTH TRIGGERS TO DRIVE";

        /// <summary>Lab: force the VR panel without a headset (Editor/AcLabHud).</summary>
        public static bool ForceVr;
        /// <summary>Lab: with a fake feed, behave as if S's pedals were not armed (the VR hint).</summary>
        public static bool LabPedalsDisarmed;
        public static AcHudDesk Desk { get { return _desk; } }
        public static AcHudVr Vr { get { return _vr; } }
        /// <summary>The art and fonts are loaded and a host can draw.</summary>
        public static bool Ready { get { return AcHudArt.Ready; } }
        /// <summary>The feed drawn last (lab checks of the live path).</summary>
        public static AcHudFeed LastFeed { get { return _feed; } }

        /// <summary>Starts the module (adds its driver to <paramref name="game"/>, subscribes AcEvents.Message). Idempotent.</summary>
        public static void Start(AcGame game)
        {
            if (_started) return;
            _started = true;
            _game = game;
            RegisterSettings(false);
            AcEvents.Message += OnMessage;
            AcEvents.SeatChanged += OnSeatChanged;
            AcEvents.LocalCarChanged += OnLocalCarChanged;
            AcEvents.CarDamaged += OnCarDamaged;
            AcSettings.Changed += OnSettingChanged;
            try { AcNetGame.PlayerJoined += OnPlayerJoined; AcNetGame.PlayerLeft += OnPlayerLeft; _netHooked = true; }
            catch (Exception e) { AcLog.Warn("Hud: no player joined / left messages (" + e.Message + ")"); }
            if (game != null) game.AddOnce<AcHudDriver>();
            AcLog.Info("Hud: started (waits for the install to load the HUD art)");
        }

        /// <summary>Shows the driving HUD for <paramref name="car"/> (the seated car; VR panel anchors to it).</summary>
        public static void Show(AcCarRig car)
        {
            bool passenger = AcLocal.Seated && AcLocal.Seat == AcSeatKind.Passenger;
            if (Shown && car == _rig && passenger == _passenger) return;
            _rig = car;
            _passenger = passenger;
            Shown = true;
            _pendingShowLog = car != null && car.Car != null ? car.Car.Id : "no car";
        }

        /// <summary>Hides the driving HUD (messages still show).</summary>
        public static void Hide()
        {
            Shown = false;
            _pendingShowLog = null;
            if (_vr != null && _home != null) _vr.Detach(_home.transform);
            _rig = null;
        }

        /// <summary>Shows a message for <paramref name="seconds"/> (also fed from AcEvents.Message; F toasts those on the tablet itself).
        /// Kept until a view exists when none does yet.</summary>
        public static void Message(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            Deliver(text, Time.unscaledTime + Mathf.Clamp(seconds, 0.5f, 30f));
        }

        private static void Deliver(string text, float until)
        {
            bool any = false;
            if (_desk != null) { _desk.View.PushMessage(text, until); any = true; }
            if (_vr != null) { _vr.View.PushMessage(text, until); any = true; }
            if (any) return;
            int slot = -1;
            for (int i = 0; i < _queueText.Length; i++) if (_queueText[i] == null || _queueText[i] == text) { slot = i; break; }
            if (slot < 0)
            {
                for (int i = 0; i + 1 < _queueText.Length; i++) { _queueText[i] = _queueText[i + 1]; _queueUntil[i] = _queueUntil[i + 1]; }
                slot = _queueText.Length - 1;
            }
            _queueText[slot] = text; _queueUntil[slot] = until;
        }

        /// <summary>A view was just built: hand it the waiting messages that have not run out.</summary>
        private static void Replay(AcHudView view)
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _queueText.Length; i++)
            {
                if (_queueText[i] != null && _queueUntil[i] > now) view.PushMessage(_queueText[i], _queueUntil[i]);
                _queueText[i] = null;
            }
        }

        /// <summary>H's own messages (cut, players joining / leaving): on the HUD, and in a headset without a panel (on foot) also on the
        /// tablet - AcEvents messages are toasted by F already.</summary>
        private static void Own(string text, float seconds)
        {
            Message(text, seconds);
            if (IsVr() && (_vr == null || _vr.Go == null || !_vr.Go.activeSelf))
            {
                try { AcFront.Toast(text); }
                catch (Exception e) { AcLog.WarnOnce("Hud: tablet toast failed: " + e.Message); }
            }
        }

        /// <summary>The start-light panel: <paramref name="redCount"/> lit reds, <paramref name="green"/> go; <paramref name="visible"/> false hides it.</summary>
        public static void StartLights(int redCount, bool green, bool visible)
        {
            _lightReds = Mathf.Clamp(redCount, 0, AcHudView.LightCount);
            _lightGreen = green;
            _lightsOn = visible;
            if (_desk != null) _desk.View.SetLights(_lightReds, green, visible);
            if (_vr != null) _vr.View.SetLights(_lightReds, green, visible);
        }

        /// <summary>Stops the module (world closing).</summary>
        public static void Stop()
        {
            Shown = false;
            if (!_started) return;
            _started = false;
            AcEvents.Message -= OnMessage;
            AcEvents.SeatChanged -= OnSeatChanged;
            AcEvents.LocalCarChanged -= OnLocalCarChanged;
            AcEvents.CarDamaged -= OnCarDamaged;
            AcSettings.Changed -= OnSettingChanged;
            if (_netHooked)
            {
                try { AcNetGame.PlayerJoined -= OnPlayerJoined; AcNetGame.PlayerLeft -= OnPlayerLeft; } catch (Exception) { }
                _netHooked = false;
            }
            try
            {
                if (_desk != null) _desk.Destroy();
                if (_vr != null) _vr.Destroy();
                if (_home != null) UnityEngine.Object.Destroy(_home);
                AcHudView.ReleaseMaterials();
                AcHudArt.Release();
            }
            catch (Exception e) { AcLog.Warn("Hud: stop: " + e.Message); }
            _desk = null; _vr = null; _home = null; _rig = null; _settingsDone = false; _vrModeKnown = false; _pendingShowLog = null;
            for (int i = 0; i < _queueText.Length; i++) _queueText[i] = null;
        }

        /// <summary>Lab / tests: draw this feed (and standings) instead of the live AcLocal / AcSession / AcRace state.</summary>
        public static void SetFake(AcHudFeed feed, IReadOnlyList<AcStanding> standings) { _fake = feed; _fakeStandings = standings; _hasFake = true; }
        public static void ClearFake() { _hasFake = false; _fakeStandings = null; LabPedalsDisarmed = false; _hasFakeDamage = false; FakeDamageMultiplier = -1; }

        /// <summary>Lab (DAMAGE_DESIGN 9 H): draw these damage values instead of the seated car's - <paramref name="zones"/> = 5 zone km/h
        /// (front, rear, left, right, center; ZoneByte scale), <paramref name="susp"/> = 4 x SuspDamage01 (FL, FR, RL, RR), engine damage %.
        /// Starts the TIME-second display as a hit would.</summary>
        public static void SetFakeDamage(float[] zones, float[] susp, float enginePct)
        {
            for (int i = 0; i < 5; i++) _fakeZones[i] = zones != null && i < zones.Length ? zones[i] : 0f;
            for (int i = 0; i < 4; i++) _fakeSusp[i] = susp != null && i < susp.Length ? susp[i] : 0f;
            _fakeEnginePct = enginePct;
            _hasFakeDamage = true;
            _damageUntil = Time.unscaledTime + Mathf.Max(0.5f, AcDamageRules.Current.DisplayTimeS);
        }
        public static void ClearFakeDamage() { _hasFakeDamage = false; _damageUntil = -1f; }
        /// <summary>Lab: seconds the displayer still shows (0 = hidden by time).</summary>
        public static float DamageShowLeft { get { return Mathf.Max(0f, _damageUntil - Time.unscaledTime); } }

        /// <summary>P / N raised CarDamaged: a hit on the car I sit in (or my own car) shows the displayer for damage_displayer.ini TIME s; the
        /// engine dying says "ENGINE BLOWN" once (R says "ENGINE BLOWN - RETIRED" in a race instead). Repairs alone do not pop it up.</summary>
        private static void OnCarDamaged(AcCarRig car, AcDamageChange what, float impactKmh)
        {
            if (car == null || (car != AcLocal.Car && car != _rig)) return;
            if ((what & AcDamageChange.Repaired) != 0) _blownShown = false;
            if ((what & ~AcDamageChange.Repaired) == 0) return;
            _damageUntil = Time.unscaledTime + Mathf.Max(0.5f, AcDamageRules.Current.DisplayTimeS);
            if ((what & AcDamageChange.EngineDead) != 0 && !_blownShown && car == AcLocal.Car)
            {
                _blownShown = true;
                AcPhase ph = AcSession.Phase;
                if (ph != AcPhase.Racing && ph != AcPhase.Finished) Own("ENGINE BLOWN", 5f);     // ours text and 5 s
                AcLog.Info("Hud: engine blown on my car (damage displayer shown)");
            }
        }

        /// <summary>The damage displayer part of a feed (DAMAGE_DESIGN 5): hidden at DAMAGE 0 and with hud.damage off; else shown within TIME s
        /// of a hit and while R's pit-box repair runs. Body layers = ZoneByte / MAX_DAMAGE_KMH, engine = EnginePct / 100, suspension =
        /// SuspDamage01 per corner. No allocation.</summary>
        /// <summary>DAMAGE_DESIGN 3.3 (fixer 2026-11-02): the shift-light red line = min(LIMITER, RPM_THRESHOLD) while DAMAGE &gt; 0 and the car
        /// has an RPM_THRESHOLD; 0 (= the limiter, iteration 1) otherwise. No allocation.</summary>
        private static float RedLine(AcCarSpec spec, float limiter)
        {
            int m = FakeDamageMultiplier;
            if (m < 0) { AcRules r = AcSession.Rules; m = r != null ? r.DamageMultiplier : 0; }
            if (m <= 0 || spec == null || spec.Engine == null) return 0f;
            float th = spec.Engine.DamageRpmThreshold;
            if (!(th > 0f)) return 0f;
            return limiter > 0f && limiter < th ? limiter : th;
        }

        private static void Damage(ref AcHudFeed f, float now)
        {
            f.Damage = false;
            AcDamageRules dr = AcDamageRules.Current;
            int m = FakeDamageMultiplier;
            if (m < 0) { AcRules r = AcSession.Rules; m = r != null ? r.DamageMultiplier : 0; }
            if (m <= 0 || !f.Car || !AcSettings.Flag("hud.damage", dr.DisplayerDefaultOn)) return;
            bool rep = false; float left = 0f, total = 0f;
            try { rep = AcRace.RepairActive && (_rig == null || _rig == AcLocal.Car); if (rep) { left = AcRace.RepairSecondsLeft; total = AcRace.RepairTotalSeconds; } }
            catch (Exception) { rep = false; }
            if (!rep && now >= _damageUntil) return;
            float max = dr.MaxDamageKmh > 1f ? dr.MaxDamageKmh : 60f;
            if (_hasFakeDamage)
            {
                f.DmgFront = Mathf.Floor(_fakeZones[0]) / max; f.DmgRear = Mathf.Floor(_fakeZones[1]) / max;
                f.DmgLeft = Mathf.Floor(_fakeZones[2]) / max; f.DmgRight = Mathf.Floor(_fakeZones[3]) / max;
                f.DmgSusFl = _fakeSusp[0]; f.DmgSusFr = _fakeSusp[1]; f.DmgSusRl = _fakeSusp[2]; f.DmgSusRr = _fakeSusp[3];
                f.DmgEngine = _fakeEnginePct / 100f;
            }
            else
            {
                AcCarRig rig = _rig != null ? _rig : AcLocal.Car;
                AcCarDamage d = rig != null && rig.Physics != null ? rig.Physics.Damage : null;
                if (d == null) return;
                f.DmgFront = d.ZoneByte(0) / max; f.DmgRear = d.ZoneByte(1) / max; f.DmgLeft = d.ZoneByte(2) / max; f.DmgRight = d.ZoneByte(3) / max;
                f.DmgSusFl = d.SuspDamage01(0); f.DmgSusFr = d.SuspDamage01(1); f.DmgSusRl = d.SuspDamage01(2); f.DmgSusRr = d.SuspDamage01(3);
                f.DmgEngine = d.EnginePct / 100f;
            }
            f.Damage = true;
            f.DmgPrint = dr.PrintValues;
            f.RepairLeft = rep ? left : 0f; f.RepairTotal = rep ? total : 0f;
        }

        // ------------------------------------------------------------------ events

        private static void OnMessage(string text, float seconds) { Message(text, seconds); }

        private static void OnSeatChanged(AcCarRig car, AcSeatKind seat)
        {
            if (car != null) Show(car); else Hide();
        }

        private static void OnLocalCarChanged(AcCarRig car)
        {
            // our car was replaced / removed while we sat in it: follow the seat state (S raises SeatChanged on a re-seat)
            if (Shown && !AcLocal.Seated) Hide();
        }

        private static void OnSettingChanged(string key)
        {
            if (key == "hud.units") AcLog.Info("Hud: units " + (UseMph() ? "mph" : "km/h"));
        }

        /// <summary>AC's "player joined" system message (messages.ini SERVER_PLAYER_JOINED). acs.exe words it by car: "... is now driven by
        /// &lt;name&gt;"; without a known car: "&lt;name&gt; joined" (ours).</summary>
        private static void OnPlayerJoined(AcNetPlayer p)
        {
            if (p == null || p.IsAi || string.IsNullOrEmpty(p.Name)) return;
            var cfg = AcHudArt.Config;
            if (cfg != null && !cfg.MsgPlayerJoined) return;
            string car = CarName(p.CarId);
            Own(car != null ? car + " is now driven by " + p.Name : p.Name + " joined", 5f);    // ours: 5 s
        }

        /// <summary>AC's "player disconnected" system message (messages.ini SERVER_PLAYER_DISCONNECTED; acs.exe " disconnected from the server").</summary>
        private static void OnPlayerLeft(AcNetPlayer p)
        {
            if (p == null || p.IsAi || string.IsNullOrEmpty(p.Name)) return;
            var cfg = AcHudArt.Config;
            if (cfg != null && !cfg.MsgPlayerLeft) return;
            Own(p.Name + " disconnected from the server", 5f);
        }

        private static string CarName(string carId)
        {
            if (string.IsNullOrEmpty(carId)) return null;
            try
            {
                AcGame g = _game != null ? _game : AcGame.Current;
                AcCarEntry c = g != null && g.Content != null ? g.Content.Car(carId) : null;
                return c != null && !string.IsNullOrEmpty(c.Name) ? c.Name : carId;
            }
            catch (Exception) { return carId; }
        }

        // ------------------------------------------------------------------ settings

        /// <summary>Tablet HUD settings (F draws them). Re-registered once the config is read so the units default follows AC's own
        /// gameplay.ini USE_MPH, the time difference [TIME_DIFFERENCE] IS_ACTIVE and the standings [OVERLAY_LEADERBOARD] ACTIVE.</summary>
        private static void RegisterSettings(bool fromConfig)
        {
            var cfg = AcHudArt.Config;
            bool mph = fromConfig && cfg != null && cfg.UseMph;
            bool delta = !fromConfig || cfg == null || cfg.TimeDifference;
            bool tower = !fromConfig || cfg == null || cfg.LeaderboardDefault;
            try { Register(mph, delta, tower); }
            catch (Exception e) { AcLog.Warn("Hud: settings rows: " + e.Message); }
        }

        private static void Register(bool mph, bool delta, bool tower)
        {
            AcSettingsUi.Toggle("HUD", "hud.show", "Show the HUD", true);
            AcSettingsUi.Choice("HUD", "hud.units", "Speed units", new[] { "km/h", "mph" }, mph ? 1 : 0);
            AcSettingsUi.Choice("HUD", "hud.gauge", "Gauge", new[] { "AC dial", "Essentials" }, 0);
            AcSettingsUi.Toggle("HUD", "hud.tower", "Standings", tower);
            AcSettingsUi.Toggle("HUD", "hud.delta", "Time difference", delta);
            AcSettingsUi.Stepper("HUD", "hud.scale", "Monitor HUD size", 0.6f, 1.6f, 0.1f, 1f);
            AcSettingsUi.Stepper("HUD", "hud.vr.distance", "VR panel distance (cm)", 40f, 100f, 2f, 62f);
            AcSettingsUi.Stepper("HUD", "hud.vr.raise", "VR panel up / down (cm)", -20f, 10f, 1f, 0f);
            AcSettingsUi.Choice("HUD", "hud.vr.place", "VR panel place", new[] { "Beside the wheel", "Ahead, under the dash line" }, 0);
            AcSettingsUi.Stepper("HUD", "hud.vr.size", "VR panel width (m)", 0.3f, 0.7f, 0.01f, AcHudVr.DefaultSize);
            // iteration 2: AC's damage displayer, default = Documents gameplay.ini [DAMAGE_DISPLAYER] IS_ACTIVE (F registers the same key early)
            AcSettingsUi.Toggle("HUD", "hud.damage", "DAMAGE DISPLAY", AcDamageRules.Current.DisplayerDefaultOn);
        }

        /// <summary>hud.units when the player chose, else AC's own gameplay.ini USE_MPH.</summary>
        public static bool UseMph()
        {
            string v = AcSettings.Text("hud.units", null);
            if (v == null) { var c = AcHudArt.Config; return c != null && c.UseMph; }
            return AcSettings.Int("hud.units", 0) == 1;
        }

        private static bool IsVr()
        {
            if (ForceVr) return true;
            float now = Time.unscaledTime;
            if (!_vrModeKnown || now >= _nextVrCheck)
            {
                _nextVrCheck = now + 1f;
                _vrModeKnown = true;
                _vrMode = AcPlayer.XrPresent;
            }
            return _vrMode;
        }

        // ------------------------------------------------------------------ per frame (AcHudDriver.LateUpdate)

        internal static void Late()
        {
            if (!_started) return;
            try { LateBody(); }
            catch (Exception e)
            {
                if (!_failedOnce) { _failedOnce = true; AcLog.Error("Hud: frame failed (logged once): " + e); }
            }
        }

        private static void LateBody()
        {
            float now = Time.unscaledTime;
            if (!AcHudArt.Started)
            {
                AcGame g = _game != null ? _game : AcGame.Current;
                if (g != null && g.Install != null) AcHudArt.Begin(g.Install);
                return;
            }
            if (!AcHudArt.Pump(8)) return;     // ours: 8 pictures a frame
            var cfg = AcHudArt.Config;
            if (!_settingsDone)
            {
                _settingsDone = true;
                RegisterSettings(true);
                AcLog.Info("Hud: units " + (UseMph() ? "mph" : "km/h") + (AcSettings.Text("hud.units", null) == null ? " (AC gameplay.ini USE_MPH)" : " (setting)")
                    + ", standings default " + (cfg.LeaderboardDefault ? "on" : "off") + " ([OVERLAY_LEADERBOARD] ACTIVE " + cfg.OverlayLeaderboard + ")"
                    + ", session box practice/qualify/race " + (cfg.PracticeVisible ? 1 : 0) + "/" + (cfg.QualifyVisible ? 1 : 0) + "/" + (cfg.RaceVisible ? 1 : 0)
                    + ", join/leave messages " + (cfg.MsgPlayerJoined ? 1 : 0) + "/" + (cfg.MsgPlayerLeft ? 1 : 0));
            }
            if (_home == null)
            {
                _home = new GameObject("Ac HUD");
                if (_game != null) _home.transform.SetParent(_game.transform, false);
            }

            bool vr = IsVr();
            if (_rig != null && _rig.Root == null) { _rig = null; }
            // the VR panel hangs under the car: a car destroyed without Hide() takes it along - build a new one
            if (_vr != null && _vr.Go == null) _vr = null;

            Gather(now);
            IReadOnlyList<AcStanding> st = _hasFake ? (_fakeStandings ?? NoStandings) : Standings();
            if (!_hasFake) Flags(st, now);

            bool show = AcSettings.Flag("hud.show", true);
            bool driving = Shown && show;
            bool tower = show && AcSettings.Flag("hud.tower", cfg == null || cfg.LeaderboardDefault);
            bool delta = AcSettings.Flag("hud.delta", cfg == null || cfg.TimeDifference);
            int style = AcSettings.Int("hud.gauge", 0);

            if (vr)
            {
                if (_desk != null) _desk.Show(false);
                // seated: the panel stays up with hud.show off too (then only lights + messages draw on it)
                if (Shown && (_rig != null || _hasFake))
                {
                    if (_vr == null) { _vr = AcHudVr.Create(_home.transform); _vr.View.SetLights(_lightReds, _lightGreen, _lightsOn); Replay(_vr.View); }
                    if (_rig != null) _vr.Attach(_rig, _passenger);
                    else if (!_vr.Go.activeSelf) _vr.Go.SetActive(true);
                    _vr.Tick(now);
                    bool disarmed = _hasFake ? LabPedalsDisarmed : !AcLocal.PedalsArmed;
                    if (_feed.Driver && disarmed) _vr.View.PushMessage(PedalHint, now + 0.6f);      // ours: held while not armed, fades 0.4 s after
                    _vr.View.Tick(ref _feed, st, now, driving, tower, delta, style);
                    if (_pendingShowLog != null) LogShow("VR panel, eye " + _vr.EyeSource);
                }
                else if (_vr != null) _vr.Hide();
            }
            else
            {
                if (_vr != null) _vr.Hide();
                if (_desk == null) { _desk = AcHudDesk.Create(_home.transform); _desk.View.SetLights(_lightReds, _lightGreen, _lightsOn); Replay(_desk.View); }
                _desk.Show(true);
                _desk.SetScale(AcSettings.Num("hud.scale", 1f));
                _desk.View.Tick(ref _feed, st, now, driving, tower, delta, style);
                if (driving && _pendingShowLog != null) LogShow("monitor overlay");
            }
        }

        private static void LogShow(string where)
        {
            if (_pendingShowLog == null) return;
            AcLog.Info("Hud: shown for " + _pendingShowLog + " (" + where + (_passenger ? ", passenger" : "") + ")");
            _pendingShowLog = null;
        }

        private static IReadOnlyList<AcStanding> Standings()
        {
            try { return AcRace.Standings ?? NoStandings; }
            catch (Exception) { return NoStandings; }
        }

        /// <summary>The session the box names: R runs qualify and hotlap in the Practice PHASE with Rules.Session set (AcRace.Flow), so
        /// Qualify while AcRace.QualifyActive, Hotlap while a hotlap session runs (StartUtc set), the master's session from the grid on, else
        /// Practice (free driving, also after a race until the next START).</summary>
        private static AcSessionType ShownSession(AcPhase phase, AcRules rules)
        {
            if (rules == null) return AcSessionType.Practice;
            if (phase >= AcPhase.Grid) return rules.Session;
            bool qualify = false;
            try { qualify = AcRace.QualifyActive; } catch (Exception) { qualify = false; }
            if (qualify) return AcSessionType.Qualify;
            if (rules.Session == AcSessionType.Hotlap && AcSession.StartUtc > 0) return AcSessionType.Hotlap;
            return AcSessionType.Practice;
        }

        /// <summary>Fills <see cref="_feed"/> from the live state (or the lab's fake).</summary>
        private static void Gather(float now)
        {
            if (_hasFake) { _feed = _fake; Damage(ref _feed, now); return; }
            var f = new AcHudFeed();
            AcCarRig rig = _rig;
            f.Car = Shown && rig != null;
            f.Driver = f.Car && !_passenger;
            AcTelemetry t;
            if (rig != null && rig != AcLocal.Car && rig.Physics != null) t = rig.Physics.Telemetry;   // riding another player's car
            else t = AcLocal.Telemetry;
            f.Rpm = t.Rpm; f.LimiterRpm = t.LimiterRpm; f.SpeedKmh = t.SpeedKmh; f.Gear = t.Gear; f.Limiter = t.Limiter;
            f.Abs = t.AbsActive; f.Tc = t.TcActive; f.Fuel = t.FuelLitres; f.Boost = t.BoostBar;
            if (rig != null && rig.Spec != null)
            {
                f.MaxFuel = rig.Spec.MaxFuelLitres;
                if (f.LimiterRpm <= 0f) f.LimiterRpm = rig.Spec.LimiterRpm;
                f.MaxBoost = MaxBoost(rig);
                f.RedLineRpm = RedLine(rig.Spec, f.LimiterRpm);
            }
            f.Phase = AcSession.Phase;
            AcRules rules = AcSession.Rules;
            f.ShownSession = ShownSession(f.Phase, rules);
            try { f.OpenCourse = AcRace.OpenCourse; } catch (Exception) { f.OpenCourse = false; }
            if (rules != null)
            {
                f.Session = rules.Session;
                f.TotalLaps = f.ShownSession == AcSessionType.Race && !f.OpenCourse ? rules.Laps : 0;     // R: an open-course race is ONE run
                f.PitLimitKmh = rules.PitLimitKmh;
                f.SecondsLeft = SessionLeft();
                if (rules.FuelRate <= 0) f.MaxFuel = 0f;      // no consumption: no fuel gauge // ours
            }
            else f.SecondsLeft = -1;
            f.Laps = AcLocal.Laps; f.LapMs = AcLocal.LapMs; f.LastMs = AcLocal.LastMs; f.BestMs = AcLocal.BestMs; f.DeltaMs = AcLocal.DeltaMs;
            f.Sector = AcLocal.Sector; f.LapValid = AcLocal.LapValid; f.InPitlane = AcLocal.InPitlane && f.Driver; f.Finished = AcLocal.Finished;
            f.FinishedMs = AcLocal.FinishedMs;
            f.OutLap = AcLocal.OutLap && f.ShownSession != AcSessionType.Race;
            f.Position = AcLocal.Position;
            f.Mph = UseMph();
            f.WrongWay = f.Driver && AcLocal.WrongWay;          // R detects it (against fast_lane.ai)
            if (AcLocal.PenaltyMs > _lastPenalty) _penaltyUntil = now + 6f;      // ours: the penalty flag shows 6 s after a new penalty
            _lastPenalty = AcLocal.PenaltyMs;
            f.PenaltyFlag = now < _penaltyUntil;
            Splits(ref f, now);
            Damage(ref f, now);
            _feed = f;

            // cut warning: AC's own text (acs.exe UTF-16 "CUT DETECTED") once when the lap turns invalid
            if (f.Driver && f.Phase >= AcPhase.Practice)
            {
                if (_wasValid && !f.LapValid && f.LapMs > 0) Own("CUT DETECTED", 3f);   // ours: 3 s
                _wasValid = f.LapValid;
            }
            else _wasValid = true;
        }

        /// <summary>
        /// Sector splits from R's timing (REQUESTS "R -> H"): when AcLocal.Sector steps up within a lap, the split is the lap time since the
        /// previous gate (frame-accurate); when a lap completes, the last sector is R's own AcLocal.LastSectorMs. The delta is to the same
        /// sector of my best valid lap (kept here from LastSectorMs whenever the completed lap became the best). Out laps show none.
        /// </summary>
        private static void Splits(ref AcHudFeed f, float now)
        {
            if (!f.Driver || f.Phase < AcPhase.Practice) { _trackLaps = -1; _splitUntil = 0f; return; }
            int laps = AcLocal.Laps, sec = AcLocal.Sector, lapMs = AcLocal.LapMs;
            if (AcLocal.BestMs <= 0) _bestSecValid = false;
            if (_trackLaps < 0 || laps < _trackLaps) { _trackLaps = laps; _trackSector = sec; _secStartMs = 0; }
            else if (laps == _trackLaps + 1)
            {
                int[] ls = AcLocal.LastSectorMs;
                int n = ls != null ? ls.Length : 0;
                if (n > 1 && ls[n - 1] > 0 && !f.OutLap) ShowSplit(n, ls[n - 1], n - 1, now);
                if (n > 0 && AcLocal.LastMs > 0 && AcLocal.LastMs == AcLocal.BestMs)
                {
                    if (_bestSec.Length != n) _bestSec = new int[n];
                    Array.Copy(ls, _bestSec, n);
                    _bestSecValid = true;
                }
                _trackLaps = laps; _trackSector = sec; _secStartMs = 0;
            }
            else if (laps != _trackLaps) { _trackLaps = laps; _trackSector = sec; _secStartMs = 0; }
            else if (sec > _trackSector)
            {
                if (!AcLocal.OutLap && lapMs > _secStartMs) ShowSplit(sec, lapMs - _secStartMs, sec - 1, now);
                _trackSector = sec; _secStartMs = lapMs;
            }
            else if (sec < _trackSector) { _trackSector = sec; _secStartMs = 0; }
            if (now < _splitUntil) { f.SplitSector = _splitSector; f.SplitMs = _splitMs; f.SplitDeltaMs = _splitDelta; }
        }

        private static void ShowSplit(int sectorNumber, int ms, int index, float now)
        {
            _splitSector = sectorNumber; _splitMs = ms;
            _splitDelta = _bestSecValid && index >= 0 && index < _bestSec.Length && _bestSec[index] > 0 ? ms - _bestSec[index] : int.MinValue;
            _splitUntil = now + AcHudView.SplitSeconds;
        }

        /// <summary>Car count, race position, leader finished and the blue flag from the standings - racers only: R marks late joiners
        /// <c>Spectating</c> (Position 0). No allocation.</summary>
        private static void Flags(IReadOnlyList<AcStanding> st, float now) { ApplyStandings(ref _feed, st, AcSession.Phase); }

        /// <summary>The standings part of a feed (public for the lab: spectators, blue flag, leader finished on fake standings).</summary>
        public static void ApplyStandings(ref AcHudFeed feed, IReadOnlyList<AcStanding> st, AcPhase phase)
        {
            bool inRace = phase >= AcPhase.Grid && phase <= AcPhase.Finished;
            int n = st != null ? st.Count : 0;
            int racers = 0; bool leaderFinished = false; AcStanding me = null;
            for (int i = 0; i < n; i++)
            {
                var s = st[i];
                if (s == null) continue;
                if (s.IsLocal) me = s;
                if (s.Spectating) continue;
                racers++;
                if (s.Finished) leaderFinished = true;
            }
            feed.Cars = racers;
            feed.LeaderFinished = leaderFinished && phase >= AcPhase.Racing && phase <= AcPhase.Results;
            if (racers == 0 || !feed.Driver || me == null || me.Spectating) return;
            if (feed.Position <= 0 && inRace) feed.Position = me.Position;
            // blue flag: a racing car a lap up on us and within 3 % of a lap behind (it is about to lap us) // ours thresholds
            if (phase == AcPhase.Racing)
            {
                for (int i = 0; i < n; i++)
                {
                    var s = st[i];
                    if (s == null || s == me || s.Finished || s.Spectating) continue;
                    float d = s.RaceDistance - me.RaceDistance;
                    if (d > 0.97f && d < 1.0f) { feed.BlueFlag = true; break; }
                }
            }
        }

        /// <summary>engine.ini [TURBO_n] DISPLAY_MAX_BOOST (the gauge maximum AC's own dashboards use), else MAX_BOOST; 0 = no turbo.</summary>
        private static float MaxBoost(AcCarRig rig)
        {
            if (rig == _boostFor) return _maxBoost;
            _boostFor = rig; _maxBoost = 0f;
            try
            {
                AcIni eng;
                if (rig.Spec.Inis.TryGetValue("engine.ini", out eng) && eng != null)
                {
                    foreach (AcIniSection s in eng.Numbered("TURBO_"))
                    {
                        float m = s.GetFloat("DISPLAY_MAX_BOOST", 0f);
                        if (m <= 0f) m = s.GetFloat("MAX_BOOST", 0f);
                        if (m > _maxBoost) _maxBoost = m;
                    }
                }
            }
            catch (Exception) { _maxBoost = 0f; }
            return _maxBoost;
        }

        /// <summary>R's session clock (AcRace.SessionTimeLeft: qualify / timed race, -1 untimed), whole seconds rounded up.</summary>
        private static int SessionLeft()
        {
            try { float s = AcRace.SessionTimeLeft; return s < 0f ? -1 : Mathf.CeilToInt(s); }
            catch (Exception) { return -1; }
        }
    }
}
