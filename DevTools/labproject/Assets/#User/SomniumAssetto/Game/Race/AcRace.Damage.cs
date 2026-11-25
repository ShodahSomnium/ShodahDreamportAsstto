using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] Iteration 2 - car damage rules (DAMAGE_DESIGN.md 1.4, 1.6, 4): the session's DAMAGE_MULTIPLIER into every own car's physics
    /// (local + the master's AI; &gt;0 -> 0 repairs them), the timed pit-box repair (AcRace.Repair), where damage is repaired (back to pits,
    /// grid, new session; never in a reset / PlaceInPits) and the blown engine in a race (retired = DNF). Damage 0 = nothing here changes
    /// anything: rates stay 0, no repair has anything to clear, no car retires (an engine only dies with damage on).
    /// </summary>
    public static partial class AcRace
    {
        /// <summary>A pit-box repair needs the car below this speed (km/h). // ours (the garage's "stands still" rule)</summary>
        public const float RepairStopKmh = 1f; // ours
        /// <summary>A retired car (blown engine, race) is held once it rolls below this speed (km/h). // ours</summary>
        public const float RetireStopKmh = 1f; // ours

        /// <summary>A pit-box repair is running (the car is frozen in its box). F / H read it.</summary>
        public static bool RepairActive { get; private set; }
        /// <summary>Seconds left of the running repair (0 when none).</summary>
        public static float RepairSecondsLeft { get { return RepairActive ? Mathf.Max(0f, _repairEnd - Time.unscaledTime) : 0f; } }
        /// <summary>Length of the running (or last) repair, s.</summary>
        public static float RepairTotalSeconds { get; private set; }
        /// <summary>Parts of the running (or last) repair (Suspension includes Body).</summary>
        public static AcRepair RepairWhat { get; private set; }
        /// <summary>My car retired from the running race (engine blown): DNF, no more timing until the next session.</summary>
        public static bool LocalRetired { get { return AcTiming.Local.Retired; } }
        /// <summary>Repair sounds (A: common unscrew at the start, screw at the end - DAMAGE_DESIGN 6). A plugs <c>AcCarAudio.Repair(bool)</c>
        /// here (null = silent): R cannot call a member A has not published yet. Called guarded.</summary>
        public static Action<AcCarRig, bool> RepairSound;

        private static float _repairEnd;
        private static AcCarRig _repairRig;
        private static string _repairSession;
        private static bool _rateZeroLogged;

        /// <summary>My car stands (&lt; 1 km/h) in MY pit box (within AcSpawner.OccupiedMetres of the AC_PIT_n marker, horizontally).</summary>
        public static bool InPitBoxStopped
        {
            get
            {
                AcCarRig rig = LocalCar;
                if (rig == null || rig.Physics == null || !AcSession.OnTrack) return false;
                AcSpawnPoint[] boxes = AcTrackWorld.PitBoxes;
                if (boxes == null || PitBox < 0 || PitBox >= boxes.Length) return false;
                if (rig.Physics.SpeedKmh >= RepairStopKmh) return false;
                Vector3 d = AcSpawner.CarPosition(rig) - boxes[PitBox].Marker; d.y = 0f;
                return d.sqrMagnitude <= AcSpawner.OccupiedMetres * AcSpawner.OccupiedMetres;
            }
        }

        /// <summary>Estimated seconds for a repair of my car (car.ini [PIT_STOP], DAMAGE_DESIGN 1.5); 0 when nothing to repair / no car.</summary>
        public static float RepairEstimate(AcRepair what)
        {
            AcCarDamage d = LocalDamage();
            return d != null ? d.RepairSeconds(Normalize(what)) : 0f;
        }

        /// <summary>The selected part of my car is damaged (F greys a row otherwise).</summary>
        public static bool NeedsRepair(AcRepair what)
        {
            AcCarDamage d = LocalDamage();
            if (d == null) return false;
            what = Normalize(what);
            if ((what & AcRepair.Body) != 0) for (int i = 0; i < 5; i++) if (d.Zone[i] > 0f) return true;
            if ((what & AcRepair.Suspension) != 0) for (int i = 0; i < 4; i++) if (d.RodOffset[i] > 0f) return true;
            if ((what & AcRepair.Engine) != 0 && (d.EngineDead || d.EngineLife < AcCarDamage.LifeNew)) return true;
            return false;
        }

        /// <summary>Whether <see cref="Repair"/> would start now; <paramref name="why"/> = the refusal text (empty when allowed).</summary>
        public static bool CanRepair(AcRepair what, out string why)
        {
            why = "";
            if (!AcSession.OnTrack || LocalCar == null) { why = "No car on the track."; return false; }
            if (RepairActive) { why = "A repair is already running."; return false; }
            if (Spectating) { why = "Not while spectating."; return false; }
            if (AcSession.Phase == AcPhase.Grid || AcSession.Phase == AcPhase.Countdown) { why = "No repair on the grid."; return false; }
            if (AcSession.InRace && LocalRetired) { why = "You are retired from this race."; return false; }
            if (!NeedsRepair(what)) { why = "Nothing to repair."; return false; }
            if (!InPitBoxStopped) { why = "Stop in your pit box to repair."; return false; }
            return true;
        }

        /// <summary>Pit-box repair (any session incl. the race, DAMAGE_DESIGN 4): my car stopped in its own box -> frozen for
        /// RepairSeconds(what), unscrew / screw sounds, then the parts are repaired and the car released. A session change, BACK TO PITS or a
        /// car change cancels it. False + toast when refused. Added by R (iteration 2).</summary>
        public static bool Repair(AcRepair what)
        {
            what = Normalize(what);
            string why;
            if (!CanRepair(what, out why)) { Toast(why); return false; }
            AcCarRig rig = LocalCar;
            float secs = Mathf.Max(0f, rig.Physics.Damage.RepairSeconds(what));
            RepairWhat = what;
            RepairTotalSeconds = secs;
            _repairEnd = Time.unscaledTime + secs;
            _repairRig = rig;
            _repairSession = _sessionKey;
            RepairActive = true;
            try { rig.Physics.Freeze(true); } catch (Exception e) { AcLog.Warn("Race: Freeze for the repair failed: " + e.Message); }
            PlayRepairSound(rig, true);
            AcLog.Info("Race: pit-box repair " + what + " started (" + secs.ToString("0.0") + " s; body " + rig.Physics.Damage.BodyPct.ToString("0") + " %, suspension "
                       + rig.Physics.Damage.SuspPct.ToString("0") + " %, engine " + rig.Physics.Damage.EnginePct.ToString("0") + " %)");
            Message("REPAIRING - " + Mathf.CeilToInt(secs) + " S", Mathf.Max(2f, Mathf.Min(secs, 6f)));
            return true;
        }

        private static AcRepair Normalize(AcRepair what)
        {
            what &= AcRepair.All;
            if ((what & AcRepair.Suspension) != 0) what |= AcRepair.Body;   // "Suspension repair will automatically fix body too" (changelog 1.12)
            return what;
        }

        private static AcCarDamage LocalDamage()
        {
            AcCarRig rig = LocalCar;
            try { return rig != null && rig.Physics != null ? rig.Physics.Damage : null; } catch (Exception) { return null; }
        }

        /// <summary>Once a frame on a track: the running repair (freeze held, countdown, finish) and the retire watch.</summary>
        private static void DamageTick()
        {
            if (RepairActive)
            {
                AcCarRig rig = LocalCar;
                if (rig == null || rig != _repairRig || rig.Physics == null) CancelRepair("car changed");
                else if (!string.Equals(_repairSession, _sessionKey, StringComparison.Ordinal)) CancelRepair("the session changed");
                else
                {
                    AcCarPhysics ph = rig.Physics;
                    try { if (!ph.Frozen) ph.Freeze(true); } catch (Exception) { }
                    if (Time.unscaledTime >= _repairEnd)
                    {
                        RepairActive = false;
                        _repairRig = null;
                        try { ph.Repair(RepairWhat); } catch (Exception e) { AcLog.Error("Race: repair failed: " + e); }
                        PlayRepairSound(rig, false);
                        try { ph.Freeze(false); } catch (Exception) { }
                        AcLog.Info("Race: pit-box repair " + RepairWhat + " done (" + RepairTotalSeconds.ToString("0.0") + " s)");
                        Message("REPAIR DONE", 2f);
                    }
                }
            }
            if (AcSession.Phase == AcPhase.Racing || AcSession.Phase == AcPhase.Finished) RetireTick();
        }

        /// <summary>Stops a running repair (nothing repaired; the freeze is left to whoever changed the state: the session / PlaceInPits).</summary>
        private static void CancelRepair(string why)
        {
            if (!RepairActive) return;
            RepairActive = false;
            _repairRig = null;
            AcLog.Info("Race: pit-box repair " + RepairWhat + " cancelled (" + why + ")");
        }

        private static void PlayRepairSound(AcCarRig rig, bool starting)
        {
            Action<AcCarRig, bool> s = RepairSound;
            if (s == null) { AcLog.InfoOnce("Race: no repair sound plugged (A: AcRace.RepairSound)"); return; }
            try { s(rig, starting); } catch (Exception e) { AcLog.WarnOnce("Race: repair sound failed: " + e.Message); }
        }

        /// <summary>Full repair of a car where AC starts it clean (back to pits, grid, new session; DAMAGE_DESIGN 1.6) and its retired mark
        /// cleared. Never called from ResetTo / PlaceInPits. Cheap no-op on a pristine car.</summary>
        private static void RepairCar(AcCarRig rig, string why)
        {
            if (rig == null) return;
            try
            {
                AcCarPhysics ph = rig.Physics;
                if (ph == null) return;
                AcCarDamage d = ph.Damage;
                if (d == null) return;
                d.Retired = false;
                if (d.Pristine) return;
                ph.Repair(AcRepair.All);
                AcLog.Info("Race: " + (rig == LocalCar ? "my car" : (rig.Car != null ? rig.Car.Id : "a car")) + " repaired (" + why + ")");
            }
            catch (Exception e) { AcLog.WarnOnce("Race: repair (" + why + ") failed: " + e.Message); }
        }

        /// <summary>The master's AI cars repaired (new session / back to practice; their grid repairs in GridPlace).</summary>
        private static void RepairAiCars(string why)
        {
            for (int i = 0; i < AcTiming.Ai.Count; i++)
            {
                AcCarTiming c = AcTiming.Ai[i];
                c.Retired = false;
                if (c.Rig != null) RepairCar(c.Rig, why);
            }
        }

        /// <summary>DAMAGE_MULTIPLIER -> P's DamageRate (0..1); a car going from damage on to off is repaired (// ours: "off" means no damage
        /// on the track, DAMAGE_DESIGN 1.4). Two field reads per car and frame when nothing changes.</summary>
        private static void ApplyDamageRate(AcCarPhysics ph, AcRules r)
        {
            float rate = Mathf.Clamp01(Mathf.Max(0, r.DamageMultiplier) / 100f);
            float old = ph.DamageRate;
            if (old == rate) return;
            ph.DamageRate = rate;
            if (old > 0f && rate <= 0f)
            {
                AcCarDamage d = ph.Damage;
                if (d != null) { d.Retired = false; if (!d.Pristine) ph.Repair(AcRepair.All); }
                if (!_rateZeroLogged) { _rateZeroLogged = true; AcLog.Info("Race: DAMAGE_MULTIPLIER set to 0 mid-session - damage off, own cars repaired"); }
            }
            else if (old <= 0f && rate > 0f) _rateZeroLogged = false;
        }

        /// <summary>Race: a blown engine retires the car (DAMAGE_DESIGN 4, changelog 0.21.6): DNF, the retired bit on the wire (d1 bit 31), my
        /// car held where it stops, "ENGINE BLOWN" once. The master's AI likewise (I parks it - AcAiDriver Retired).</summary>
        private static void RetireTick()
        {
            AcCarTiming me = AcTiming.Local;
            AcCarRig rig = LocalCar;
            if (!Spectating && rig != null && rig.Physics != null && me.Begun)
            {
                AcCarPhysics ph = rig.Physics;
                AcCarDamage d = ph.Damage;
                if (!me.Retired && !me.Timer.Finished && d != null && d.EngineDead)
                {
                    me.Retired = true;
                    d.Retired = true;
                    AcLog.Info("Race: my engine blew up - retired (DNF, lap " + me.Timer.Laps + ")");
                    Message("ENGINE BLOWN - RETIRED", 6f);
                }
                if (me.Retired && !ph.Frozen && ph.SpeedKmh < RetireStopKmh && !RepairActive) { try { ph.Freeze(true); } catch (Exception) { } }
            }
            for (int i = 0; i < AcTiming.Ai.Count; i++)
            {
                AcCarTiming c = AcTiming.Ai[i];
                if (c.Retired || c.Rig == null || !c.Begun || c.Benched || c.Timer.Finished) continue;
                AcCarDamage d;
                try { d = c.Rig.Physics != null ? c.Rig.Physics.Damage : null; } catch (Exception) { continue; }
                if (d == null || !d.EngineDead) continue;
                c.Retired = true;
                d.Retired = true;
                AcLog.Info("Race: AI " + c.Name + " engine blew up - retired (DNF, lap " + c.Timer.Laps + ")");
            }
        }

        /// <summary>A new race / session: nobody is retired any more (local + the master's AI; the cars are repaired by the caller).</summary>
        private static void ClearRetired()
        {
            AcTiming.Local.Retired = false;
            for (int i = 0; i < AcTiming.Ai.Count; i++) AcTiming.Ai[i].Retired = false;
        }
    }
}
