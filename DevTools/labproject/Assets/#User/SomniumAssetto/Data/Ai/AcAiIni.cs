using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// A car's <c>data/ai.ini</c> (inside data.acd) - the numbers Kunos tuned for its AI driver. Census of the 86 owned cars [VERIFIED,
    /// harness <c>aiini</c>]: every car has [GEARS] UP DOWN SLIP_THRESHOLD GAS_CUTOFF_TIME, [PEDALS] GASGAIN BRAKE_HINT TRAIL_HINT,
    /// [STEER] STEER_GAIN, [LOOKAHEAD] BASE GAS_BRAKE_LOOKAHEAD; 46/86 [LOOKAHEAD] SPEED_GAIN, 85 [ULTRA_GRIP] VALUE, 84
    /// [PHYSICS_HINTS] AERO_HINT, 7 [TYRES] HINT. VERSION 3 has DOWN in rpm; VERSION 4 cars have DOWN = 79..85 (a percentage).
    ///
    /// Meanings: formula_k's comments [READ, sdk/dev/content/cars/formula_k/data/ai.ini]: UP/DOWN = "Upchange / Downshift gear at value
    /// RPM (also used for automatic gearbox assist)", SLIP_THRESHOLD = "Slipratio permitted for AI driving", GAS_CUTOFF_TIME = "Gearchange
    /// time needed for AI control". The rest has no comment anywhere; how WE use each value is written on the field [BELIEF].
    /// Unity-free (harness compiles it).
    /// </summary>
    public sealed class AcAiIni
    {
        public bool HasFile;
        public int Version = 3;
        /// <summary>[GEARS] UP: upshift rpm.</summary>
        public float UpRpm = 7000f; // ours default (file missing)
        /// <summary>[GEARS] DOWN raw: rpm (VERSION 3) or percent (VERSION 4, value &lt;= 100). Use <see cref="DownshiftRpm"/>.</summary>
        public float DownRaw = 4000f; // ours default (file missing)
        /// <summary>[GEARS] SLIP_THRESHOLD "slip ratio permitted for AI driving" - we read it as a multiplier on our traction limit
        /// (<see cref="AcAiBrain.TractionSlip"/>) [BELIEF].</summary>
        public float SlipThreshold = 0.95f; // ours default
        /// <summary>[GEARS] GAS_CUTOFF_TIME (s): the AI lifts for this long on an upshift.</summary>
        public float GasCutoffTime = 0.28f; // ours default
        /// <summary>[PEDALS] GASGAIN: proportional gain of the throttle controller (0.2 formula cars .. 4 most road cars) [BELIEF].</summary>
        public float GasGain = 2f; // ours default
        /// <summary>[PEDALS] BRAKE_HINT: fraction of the car's braking capability the AI plans with (0.75 .. 1.1) [BELIEF].</summary>
        public float BrakeHint = 0.95f; // ours default
        /// <summary>[PEDALS] TRAIL_HINT: how much braking the AI keeps while already cornering (0.3 .. 1) [BELIEF].</summary>
        public float TrailHint = 1f; // ours default
        /// <summary>[STEER] STEER_GAIN (1.0 .. 2.78): we scale our steering feedback by STEER_GAIN / 1.7 (1.7 = ferrari_458) [BELIEF].</summary>
        public float SteerGain = 1.7f; // ours default
        /// <summary>[LOOKAHEAD] BASE (m, 13.8 .. 26.6): the look-ahead of the steering preview at speed [BELIEF].</summary>
        public float LookaheadBase = 20f; // ours default
        /// <summary>[LOOKAHEAD] SPEED_GAIN (0.10 .. 0.8, missing on 40/86 cars): extra look-ahead metres per m/s [BELIEF].</summary>
        public float SpeedGain = 0.1f; // ours default when missing
        /// <summary>[LOOKAHEAD] GAS_BRAKE_LOOKAHEAD (0, 3 or 10): extra metres the speed controller looks ahead [BELIEF].</summary>
        public float GasBrakeLookahead = 0f; // ours default
        /// <summary>[ULTRA_GRIP] VALUE (1.15 .. 1.5) - kept for reference, not used (no grip cheats).</summary>
        public float UltraGrip = 1.2f;
        /// <summary>[PHYSICS_HINTS] AERO_HINT (0.75 .. 1.04): multiplies the downforce the speed plan counts on [BELIEF].</summary>
        public float AeroHint = 1f;
        /// <summary>[TYRES] HINT (0.98 .. 1, 7 cars): multiplies the tyre grip the speed plan counts on [BELIEF].</summary>
        public float TyresHint = 1f;

        /// <summary>Downshift rpm: VERSION 4 (DOWN &lt;= 100) = DOWN % of UP [BELIEF: v4 values 79-85 cannot be rpm]; else DOWN rpm.</summary>
        public float DownshiftRpm { get { return DownRaw <= 100f ? UpRpm * DownRaw / 100f : DownRaw; } }

        /// <summary>Downshift rule kind: true = "the lower gear would run below DownshiftRpm" (v4 percent), false = "rpm below DOWN" (v3).</summary>
        public bool DownIsPercent { get { return DownRaw <= 100f; } }

        public static AcAiIni FromIni(AcIni ini)
        {
            var a = new AcAiIni();
            if (ini == null) return a;
            a.HasFile = ini.Order.Count > 0;
            a.Version = ini.GetInt("HEADER", "VERSION", a.Version);
            a.UpRpm = ini.GetFloat("GEARS", "UP", a.UpRpm);
            a.DownRaw = ini.GetFloat("GEARS", "DOWN", a.DownRaw);
            a.SlipThreshold = ini.GetFloat("GEARS", "SLIP_THRESHOLD", a.SlipThreshold);
            a.GasCutoffTime = ini.GetFloat("GEARS", "GAS_CUTOFF_TIME", a.GasCutoffTime);
            a.GasGain = ini.GetFloat("PEDALS", "GASGAIN", a.GasGain);
            a.BrakeHint = ini.GetFloat("PEDALS", "BRAKE_HINT", a.BrakeHint);
            a.TrailHint = ini.GetFloat("PEDALS", "TRAIL_HINT", a.TrailHint);
            a.SteerGain = ini.GetFloat("STEER", "STEER_GAIN", a.SteerGain);
            a.LookaheadBase = ini.GetFloat("LOOKAHEAD", "BASE", a.LookaheadBase);
            a.SpeedGain = ini.GetFloat("LOOKAHEAD", "SPEED_GAIN", a.SpeedGain);
            // two cars put GAS_BRAKE_LOOKAHEAD under [STEER] (census): accept both
            a.GasBrakeLookahead = ini.GetFloat("LOOKAHEAD", "GAS_BRAKE_LOOKAHEAD", ini.GetFloat("STEER", "GAS_BRAKE_LOOKAHEAD", a.GasBrakeLookahead));
            a.UltraGrip = ini.GetFloat("ULTRA_GRIP", "VALUE", a.UltraGrip);
            a.AeroHint = ini.GetFloat("PHYSICS_HINTS", "AERO_HINT", a.AeroHint);
            a.TyresHint = ini.GetFloat("TYRES", "HINT", a.TyresHint);
            // sanity (a broken mod value must not make the AI insane)
            if (a.UpRpm < 1000f) a.UpRpm = 7000f; // ours
            if (a.BrakeHint < 0.3f || a.BrakeHint > 1.5f) a.BrakeHint = 0.95f; // ours
            if (a.TrailHint < 0f || a.TrailHint > 1.5f) a.TrailHint = 1f; // ours
            if (a.GasGain <= 0f) a.GasGain = 2f; // ours
            if (a.SteerGain <= 0f) a.SteerGain = 1.7f; // ours
            if (a.LookaheadBase < 3f) a.LookaheadBase = 20f; // ours
            if (a.AeroHint <= 0f) a.AeroHint = 1f;
            if (a.TyresHint <= 0f) a.TyresHint = 1f;
            return a;
        }

        public override string ToString()
        {
            return "v" + Version + " up " + UpRpm + " down " + DownshiftRpm.ToString("0") + (DownIsPercent ? " (" + DownRaw + "%)" : "")
                + " gas " + GasGain + " brake " + BrakeHint + " trail " + TrailHint + " steer " + SteerGain + " look " + LookaheadBase
                + "+" + SpeedGain + "/mps gb " + GasBrakeLookahead + " aero " + AeroHint;
        }
    }
}
