using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Cross-module notifications that are not session phases (those are on <see cref="AcSession"/>). Main thread only. Every handler is
    /// called on its own inside try/catch: one that throws is logged and the others still run. Raise with the Raise* methods (never
    /// invoke the event fields directly). Owner: lead; any module may raise the event that belongs to it (listed per event).
    /// </summary>
    // AcDamageChange (the CarDamaged flags, DAMAGE_DESIGN 9 "lead") is declared in Data/Physics/AcCarDamage.cs: the Unity-free damage state
    // returns it from TakeChanges and Data/ must compile without Game/ (the harness). Same namespace, same members.

    public static class AcEvents
    {
        /// <summary>A short message for the player (text, seconds). Raised by anyone (R: "FALSE START", "LAP INVALID"; S: "Stop the car to
        /// get out"); H shows it on the HUD, F may toast it on the tablet.</summary>
        public static event Action<string, float> Message;

        /// <summary>The local player's own car was spawned / replaced (new rig) or removed (null). Raised by R.</summary>
        public static event Action<AcCarRig> LocalCarChanged;

        /// <summary>The local player sat in (car, seat) or got out (null, seat). Raised by S.</summary>
        public static event Action<AcCarRig, AcSeatKind> SeatChanged;

        /// <summary>A car hit something (car, impact speed km/h, world point). Raised by P (local / AI cars); A plays the collision sound,
        /// S may pulse the controllers.</summary>
        public static event Action<AcCarRig, float, Vector3> CarImpact;

        /// <summary>A gear change happened on a car (car, new gear). Raised by P; A plays the gear sound, C animates the paddle / shifter.</summary>
        public static event Action<AcCarRig, int> GearChanged;

        /// <summary>A car's damage changed (car, what changed since the last raise, the largest impact km/h counted since then - 0 for engine
        /// wear / repair / remote updates). Raised once per change set from Update (never inside the physics callbacks): by P for cars this
        /// client simulates (local, AI on the master), by N for remote cars when the wire record changed. Read the state from
        /// <c>rig.Physics.Damage</c>. At DAMAGE 0 (multiplier off) only <see cref="AcDamageChange.Repaired"/> can be raised.</summary>
        public static event Action<AcCarRig, AcDamageChange, float> CarDamaged;

        /// <summary>A contact with details (car, km/h, world point, contact normal (world), the other car or null, withCar = the other
        /// collider belongs to a car (also a car without a rig)). Raised by P right after <see cref="CarImpact"/> with the same gating and
        /// km/h (hits and the 10 Hz scrape repeats); A may use the angle, C the sparks.</summary>
        public static event Action<AcCarRig, float, Vector3, Vector3, AcCarRig, bool> CarCollision;

        /// <summary>Someone listens to <see cref="CarCollision"/> (integration 2026-11-02: P skips the other-car lookup when nobody does, so the
        /// iteration-1 contact path does no extra work - A2 angle and C's sparks are not built yet).</summary>
        public static bool CarCollisionWanted { get { return CarCollision != null; } }

        public static void RaiseMessage(string text, float seconds)
        {
            var h = Message; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<string, float>)d)(text, seconds); }
                catch (Exception e) { AcLog.Error("Events: a Message handler failed: " + e); }
            }
        }

        public static void RaiseLocalCarChanged(AcCarRig car)
        {
            var h = LocalCarChanged; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcCarRig>)d)(car); }
                catch (Exception e) { AcLog.Error("Events: a LocalCarChanged handler failed: " + e); }
            }
        }

        public static void RaiseSeatChanged(AcCarRig car, AcSeatKind seat)
        {
            var h = SeatChanged; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcCarRig, AcSeatKind>)d)(car, seat); }
                catch (Exception e) { AcLog.Error("Events: a SeatChanged handler failed: " + e); }
            }
        }

        public static void RaiseCarImpact(AcCarRig car, float impactKmh, Vector3 point)
        {
            var h = CarImpact; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcCarRig, float, Vector3>)d)(car, impactKmh, point); }
                catch (Exception e) { AcLog.Error("Events: a CarImpact handler failed: " + e); }
            }
        }

        public static void RaiseCarDamaged(AcCarRig car, AcDamageChange what, float impactKmh)
        {
            var h = CarDamaged; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcCarRig, AcDamageChange, float>)d)(car, what, impactKmh); }
                catch (Exception e) { AcLog.Error("Events: a CarDamaged handler failed: " + e); }
            }
        }

        public static void RaiseCarCollision(AcCarRig car, float kmh, Vector3 point, Vector3 normal, AcCarRig other, bool withCar)
        {
            var h = CarCollision; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcCarRig, float, Vector3, Vector3, AcCarRig, bool>)d)(car, kmh, point, normal, other, withCar); }
                catch (Exception e) { AcLog.Error("Events: a CarCollision handler failed: " + e); }
            }
        }

        public static void RaiseGearChanged(AcCarRig car, int gear)
        {
            var h = GearChanged; if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcCarRig, int>)d)(car, gear); }
                catch (Exception e) { AcLog.Error("Events: a GearChanged handler failed: " + e); }
            }
        }
    }
}
