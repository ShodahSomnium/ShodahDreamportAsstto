using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [S, Seat] Iteration 2 (DAMAGE_DESIGN 8, optional): a counted damage hit on the car I sit in pulses both VR controllers once,
    /// amplitude min(1, impact km/h / 60) // ours (60 = damage_displayer.ini MAX_DAMAGE_KMH's full scale, read at run time). No camera shake
    /// (VR comfort). AcXr.Pulse already respects the "haptics" setting and does nothing without a headset. CarDamaged only fires with damage
    /// on, so DAMAGE 0 changes nothing.
    /// </summary>
    internal static class AcSeatDamagePulse
    {
        /// <summary>Pulse length (s). // ours</summary>
        public const float Seconds = 0.12f;
        private static bool _hooked;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            AcEvents.CarDamaged += OnCarDamaged;
        }

        public static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            AcEvents.CarDamaged -= OnCarDamaged;
        }

        private static void OnCarDamaged(AcCarRig car, AcDamageChange what, float impactKmh)
        {
            if (car == null || !AcLocal.Seated || AcLocal.SeatCar != car) return;
            if ((what & (AcDamageChange.Zones | AcDamageChange.Suspension)) == 0 || impactKmh <= 0f) return;
            float full = AcDamageRules.Current.MaxDamageKmh;
            float amp = Mathf.Clamp01(impactKmh / (full > 1f ? full : 60f));
            if (amp < 0.05f) return;      // ours: below 3 km/h at the 60 km/h scale nothing worth feeling
            AcXr.Pulse(false, amp, Seconds);
            AcXr.Pulse(true, amp, Seconds);
        }
    }
}
