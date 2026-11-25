using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [P, Physics] aero.ini wing forces (Unity-FREE, allocation-free): per [WING_n]
    ///   F = 0.5 * rho * v^2 * CHORD * SPAN * C(aoa) * GAIN * GH(height)       (research 1.7)
    ///   aoa = ANGLE (or the DYNAMIC_CONTROLLER chain's angle) + the airflow pitch angle; positive CL = downforce;
    ///   lift: C = LUT_AOA_CL * CL_GAIN * LUT_GH_CL(height of the wing POSITION above the ground, <see cref="GroundHeight"/>) * (1 + YAW_CL_GAIN * |yaw deg|) (// ours yaw form);
    ///   drag: C = LUT_AOA_CD * CD_GAIN * LUT_GH_CD(height). Applied at POSITION (physics space).
    /// </summary>
    public static class AcAeroModel
    {
        /// <summary>Air density (kg/m^3). // ours (AC's ambient model not read)</summary>
        public const float Rho = 1.2f;

        /// <summary>Forces of one wing: <paramref name="downforce"/> (N, along -body up when positive), <paramref name="drag"/> (N, along the
        /// airflow). <paramref name="airSpeed"/> m/s, <paramref name="flowPitchDeg"/> extra angle of attack from the body pitch / vertical
        /// airflow, <paramref name="heightM"/> the wing's height above the ground, <paramref name="yawDeg"/> the body slip angle,
        /// <paramref name="angleDeg"/> the wing angle (ANGLE or the controller's).</summary>
        public static void Wing(AcWingSpec w, float airSpeed, float flowPitchDeg, float heightM, float yawDeg, float angleDeg, out float downforce, out float drag)
        {
            Wing(w, airSpeed, flowPitchDeg, heightM, yawDeg, angleDeg, 1f, 1f, out downforce, out drag);
        }

        /// <summary>As above with this wing's damage factors (iteration 2, the aero.ini formula; AcCarDamage.AeroFactors): <c>cl /= clDiv</c>
        /// (sign kept - changelog 1.11.3 fixed negative-CL wings), <c>cd *= cdMul</c>, before the forces. 1 / 1 = the undamaged wing exactly.</summary>
        public static void Wing(AcWingSpec w, float airSpeed, float flowPitchDeg, float heightM, float yawDeg, float angleDeg, float clDiv, float cdMul, out float downforce, out float drag)
        {
            downforce = 0f; drag = 0f;
            if (w == null || airSpeed <= 0.1f) return;
            float q = 0.5f * Rho * airSpeed * airSpeed * w.Chord * w.Span;
            float aoa = angleDeg + flowPitchDeg;
            if (w.ClGain != 0f && w.AoaCl != null)
            {
                float cl = w.AoaCl.Eval(aoa) * w.ClGain;
                if (w.GhCl != null) cl *= w.GhCl.Eval(heightM);
                if (w.YawClGain != 0f) cl *= Math.Max(0f, 1f + w.YawClGain * Math.Abs(yawDeg));
                if (clDiv != 1f && clDiv > 0f) cl /= clDiv;
                downforce = q * cl;
            }
            if (w.CdGain != 0f && w.AoaCd != null)
            {
                float cd = w.AoaCd.Eval(aoa) * w.CdGain;
                if (w.GhCd != null) cd *= w.GhCd.Eval(heightM);
                if (cdMul != 1f) cd *= cdMul;
                drag = q * Math.Max(0f, cd);
            }
        }

        /// <summary>The height a wing's LUT_GH_CL / LUT_GH_CD is read at: the wing's POSITION above the ground = the CG height above the
        /// ground at the wing's z (blended between the CG heights over the front and rear tyre patches, clamped to the axles) + POSITION.y.
        /// Second pass (critic: "use [RIDE] PICKUP_*_HEIGHT"): checked on all 45 cars with ground luts (DevTools/agents/P/pride_all.txt). For
        /// the underbody wings the two agree (458_gt2 splitter y -0.26 vs pickup -0.264); the body wings decide it: ferrari_458 / _s3 WING_0
        /// height_body_CD.lut spans 0.30..0.47 m (1.0 at 0.45) and mclaren_mp412c WING_0 peaks at 0.52 m - read at the pickup ride height
        /// (~0.1 m) they give 0.82 / 0.25 (wrong top speed / lift), at the wing height 1.00 / 0.95. So the pickups stay parsed (AcCarSpec, ride
        /// height for telemetry) and the luts use the wing height. The splitter loss the critic saw (0.107 m -> 0.4) came from the static CG
        /// height ("design point = static" put the car 2-4 cm too high); with AcCarSpec.PhysicsCgHeightM's suspension reading the 458_gt2
        /// splitter / diffuser read 0.76 / 0.90 at rest (were 0.37 / 0.47).</summary>
        public static float GroundHeight(AcCarSpec s, AcWingSpec w, float cgAboveFront, float cgAboveRear)
        {
            float zf = s.Front.AxleZ, zr = s.Rear.AxleZ;
            float t = (w.Position.Z - zr) / Math.Max(0.5f, zf - zr);
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            float h = cgAboveRear + (cgAboveFront - cgAboveRear) * t + w.Position.Y;
            return h > 0f ? h : 0f;
        }

        /// <summary>Ride height (m) at the car.ini [RIDE] pickup points (front / rear) from the CG heights above the front / rear patches.</summary>
        public static void RideHeights(AcCarSpec s, float cgAboveFront, float cgAboveRear, out float front, out float rear)
        {
            front = cgAboveFront + s.PickupFrontHeight;
            rear = cgAboveRear + s.PickupRearHeight;
        }
    }
}
