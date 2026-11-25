using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [P, Physics] The tyre force model (Unity-FREE, allocation-free): AC tyres.ini VERSION 10 numbers in a brush-like combined-slip model
    /// (research/ac_physics_sound_rules.md 1.10 step 4).
    ///   peak mu        D = D_REF * (Fz/FZ0)^(LS_EXP-1)                              (load sensitivity, per direction)
    ///                  * surface FRICTION * thermal PERFORMANCE peak * WEAR_CURVE(vkm)/100
    ///                  * (1 - (|camber|*DCAMBER_0 + camber^2*DCAMBER_1))             ([READ] formula_k comment)
    ///                  / (1 + SPEED_SENSITIVITY * sliding speed)                     (// ours reading: grip falls with SLIDING speed)
    ///                  Dx * (1 + BRAKE_DX_MOD) while braking                          (// ours reading)
    ///   normalised slip sx = slipRatio*CX_MULT/tan(FRICTION_LIMIT_ANGLE), sy = (tan(alpha) - camber*CAMBER_GAIN)/tan(FRICTION_LIMIT_ANGLE)
    ///   shape          f(s) = 2s - s^2 up to the peak (s = 1), then 1 - (1-FALLOFF_LEVEL)*(1-exp(-(s-1)*FALLOFF_SPEED))   // ours shape
    ///   forces         Fx = Dx*Fz*f*sx/s,  Fy = -Dy*Fz*f*sy/s  (friction ellipse)
    /// Pressure (AcCarCore): AC loses grip by |p_hot - PRESSURE_IDEAL| * PRESSURE_D_GAIN (formula_k: IDEAL "ideal pressure for grip"). With no
    /// thermal model (// ours) the car's default cold PRESSURE_STATIC is taken to reach IDEAL when hot (p_hot = p_set + IDEAL - STATIC), so
    /// the loss is |p_set - PRESSURE_STATIC| * D_GAIN: a garage pressure off the car's default costs grip in either direction, the default none.
    /// Sign convention: slip ratio > 0 = wheel faster than the road (traction); tan(alpha) > 0 = patch sliding to +lateral (wheel right),
    /// the force then points to -lateral. Camber > 0 = top of the tyre leaning to +lateral (camber thrust to +lateral).
    /// </summary>
    public static class AcTyreModel
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);

        /// <summary>Per-tyre constants derived once from the spec (avoids pow/tan per step for the fixed parts).</summary>
        public struct Derived
        {
            public float TanPeak, InvTanPeak, CxMult, DxRef, DyRef, Fz0, LsX1, LsY1, FalloffLevel, FalloffSpeed, SpeedSens, CamberGain, DC0, DC1, BrakeDxMod;
            public float Relax, Radius, Inertia, RR0, RR1, RRSlip, RadiusK, PressureIdeal, PressureDGain, Perf;

            public static Derived From(AcTyreSpec t)
            {
                float peakDeg = Math.Max(1f, Math.Min(30f, t.FrictionLimitAngle));
                float tp = (float)Math.Tan(peakDeg * Deg2Rad);
                return new Derived
                {
                    TanPeak = tp, InvTanPeak = 1f / tp, CxMult = t.CxMult > 0f ? t.CxMult : 1f,
                    DxRef = t.DxRef, DyRef = t.DyRef, Fz0 = Math.Max(100f, t.Fz0), LsX1 = t.LsExpX - 1f, LsY1 = t.LsExpY - 1f,
                    FalloffLevel = Math.Max(0.2f, Math.Min(1f, t.FalloffLevel)), FalloffSpeed = Math.Max(0.1f, t.FalloffSpeed),
                    SpeedSens = Math.Max(0f, t.SpeedSensitivity), CamberGain = t.CamberGain, DC0 = t.DCamber0, DC1 = t.DCamber1, BrakeDxMod = t.BrakeDxMod,
                    Relax = Math.Max(0.01f, t.RelaxationLength), Radius = t.Radius, Inertia = Math.Max(0.1f, t.AngularInertia),
                    RR0 = t.RollingResistance0, RR1 = t.RollingResistance1, RRSlip = t.RollingResistanceSlip, RadiusK = t.RadiusAngularK,
                    PressureIdeal = t.PressureIdeal, PressureDGain = t.PressureDGain, Perf = t.PerformancePeak
                };
            }
        }

        /// <summary>The force-shape f(s) (0..1 up to the peak at s = 1, then falling to FALLOFF_LEVEL).</summary>
        public static float Shape(float s, float falloffLevel, float falloffSpeed)
        {
            if (s <= 1f) return s * (2f - s);
            return 1f - (1f - falloffLevel) * (1f - (float)Math.Exp(-(s - 1f) * falloffSpeed));
        }

        /// <summary>
        /// Tyre forces at a load and slip state. <paramref name="grip"/> = surface FRICTION * wear * pressure factor (the caller's
        /// slow-changing multipliers); <paramref name="camberRad"/> signed (see class comment); <paramref name="slideSpeed"/> m/s at the patch.
        /// Outputs: Fx, Fy (N), <paramref name="kx"/> = secant stiffness dFx/dslipRatio (>= 0, for the implicit wheel step),
        /// <paramref name="ky"/> the same for tan(alpha), <paramref name="combined"/> = the normalised combined slip s (1 = peak).
        /// </summary>
        public static void Forces(ref Derived t, float load, float slipRatio, float tanAlpha, float grip, float camberRad, float slideSpeed,
            out float fx, out float fy, out float kx, out float ky, out float combined)
        {
            fx = 0f; fy = 0f; kx = 0f; ky = 0f; combined = 0f;
            if (load <= 0f || grip <= 0f) return;
            float lr = load / t.Fz0;
            if (lr < 0.05f) lr = 0.05f; // ours guard: no infinite mu at tiny loads
            float dx = t.DxRef * (float)Math.Pow(lr, t.LsX1);
            float dy = t.DyRef * (float)Math.Pow(lr, t.LsY1);
            float c = camberRad < 0f ? -camberRad : camberRad;
            float cam = 1f - (c * t.DC0 + c * c * t.DC1);
            if (cam < 0.5f) cam = 0.5f; else if (cam > 1.2f) cam = 1.2f; // ours guard
            float common = grip * t.Perf * cam / (1f + t.SpeedSens * slideSpeed);
            dx *= common * (slipRatio < 0f ? 1f + t.BrakeDxMod : 1f);
            dy *= common;
            float sx = slipRatio * t.CxMult * t.InvTanPeak;
            float sy = (tanAlpha - camberRad * t.CamberGain) * t.InvTanPeak;
            float s = (float)Math.Sqrt(sx * sx + sy * sy);
            combined = s;
            float fOverS;
            if (s < 1e-5f) fOverS = 2f;
            else fOverS = Shape(s, t.FalloffLevel, t.FalloffSpeed) / s;
            fx = dx * load * fOverS * sx;
            fy = -dy * load * fOverS * sy;
            kx = dx * load * fOverS * t.CxMult * t.InvTanPeak;
            ky = dy * load * fOverS * t.InvTanPeak;
        }

        /// <summary>Rolling resistance force (N, >= 0) at a load: Fz * (RR0 + RR1*v^2 + RR_SLIP*alpha^2) / 1000 (// ours reading of the units:
        /// RR0 = 12 gives the textbook 0.012 coefficient), alpha clamped to the peak angle.</summary>
        public static float RollingResistance(ref Derived t, float load, float speed, float tanAlpha)
        {
            if (load <= 0f) return 0f;
            float a = tanAlpha < 0f ? -tanAlpha : tanAlpha;
            if (a > t.TanPeak) a = t.TanPeak;
            return load * (t.RR0 + t.RR1 * speed * speed + t.RRSlip * a * a) * 0.001f;
        }
    }
}
