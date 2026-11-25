using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// What a car's sound needs from its data.acd besides the bank: sounds.ini ([SKIDS], [BACKFIRE], [BODY_WORK], [ENGINE] POSITION -
    /// semantics READ in the SDK "AC Audio Pipeline 1.9.pdf" p.8) and the emitter geometry (pipeline PDF p.4-5: engine at the front
    /// axle for POSITION=front, 0.5 m ahead of the rear axle for rear, the CoG when missing; backfire 0.5 m behind the rear axle;
    /// skid / surface at each wheel; horn, wind, bodywork at the CoG) from suspensions.ini / car.ini / tyres.ini, plus engine.ini
    /// limiter / idle / turbo max boost. Positions are in the car BODY frame (Unity axes, origin at the CG = the rig root): x right,
    /// y up, z forward. Unity-free; read on a worker.
    /// </summary>
    public sealed class AcCarSounds
    {
        public string CarId = "";
        public bool FromArchive;
        public readonly List<string> Problems = new List<string>();

        // ---- sounds.ini
        public float SkidEntryPoint = 0.5f;       // [SKIDS] ENTRY_POINT (fallback = the 458's value)
        public float SkidMixVolume = 0.65f;       // [SKIDS] MIX_VOLUME
        public bool HasBackfire;
        public float BackfireMaxGas = 0.4f, BackfireMinRpm = 6500f, BackfireMaxRpm = 12000f, BackfireTriggerGas = 0.8f; // [BACKFIRE]
        public float BackfireVolumeIn = 1f, BackfireVolumeOut = 0.8f;
        /// <summary>[BACKFIRE] VOLUME_SCALE_OUT and [GEAR_EXT] DISTANCE_SCALE (0 = absent). Not in the pipeline PDF; by the PDF's DISTANCE_SCALE
        /// rule these are track-camera (F3/F7) multipliers, which never apply to a listener present in the world (car-camera rule: 1).</summary>
        public float BackfireVolumeScaleOut, GearExtDistanceScale;
        public float BodyworkMin = 0.2f, BodyworkGain = 1f;   // [BODY_WORK] MIN, GAIN
        /// <summary>[ENGINE] POSITION: "front", "rear" or "" (CoG).</summary>
        public string EnginePosition = "";

        // ---- engine.ini
        public float LimiterRpm, IdleRpm;
        /// <summary>Largest [TURBO_n] MAX_BOOST (bar); 0 = no turbo.</summary>
        public float MaxBoostBar;

        // ---- geometry (body frame, Unity axes)
        public float WheelbaseM = 2.6f, CgLocation = 0.5f, TrackFrontM = 1.6f, TrackRearM = 1.6f; // // ours defaults when the inis are missing
        public float RadiusFront = 0.33f, RadiusRear = 0.33f;                                     // // ours defaults
        public float CgHeightM = 0.45f;                                                           // // ours default
        public float BaseYFront = float.NaN, BaseYRear = float.NaN;
        public float TotalMassKg;
        /// <summary>Driver eye (car.ini [GRAPHICS] DRIVEREYES) in the body frame; <see cref="HasEye"/> false when missing.</summary>
        public float EyeX, EyeY = 0.6f, EyeZ; // ours fallback height
        public bool HasEye;

        /// <summary>Front / rear axle z from the CG (ac_physics_sound_rules Critic addendum, VERIFIED 86/86).</summary>
        public float FrontAxleZ { get { return WheelbaseM * (1f - CgLocation); } }
        public float RearAxleZ { get { return -WheelbaseM * CgLocation; } }

        /// <summary>Engine emitter (body frame): front axle / 0.5 m ahead of the rear axle / CoG [READ pipeline PDF].</summary>
        public void EngineEmitter(out float x, out float y, out float z)
        {
            x = 0f; y = 0f;
            if (string.Equals(EnginePosition, "front", StringComparison.OrdinalIgnoreCase)) z = FrontAxleZ;
            else if (string.Equals(EnginePosition, "rear", StringComparison.OrdinalIgnoreCase)) z = RearAxleZ + 0.5f;
            else z = 0f;
        }

        /// <summary>Backfire emitter: 0.5 m behind the rear axle [READ pipeline PDF], at the CG height. // ours height</summary>
        public void BackfireEmitter(out float x, out float y, out float z) { x = 0f; y = 0f; z = RearAxleZ - 0.5f; }

        /// <summary>Wheel centre (0 LF, 1 RF, 2 LR, 3 RR) in the body frame: x = +-TRACK/2 (Unity left = -x), z = axle, y = BASEY
        /// (wheel centre relative to the CG; Critic addendum) or RADIUS - CG height.</summary>
        public void WheelEmitter(int i, out float x, out float y, out float z)
        {
            bool front = i < 2;
            float track = front ? TrackFrontM : TrackRearM;
            x = (i == 0 || i == 2) ? -track * 0.5f : track * 0.5f;
            z = front ? FrontAxleZ : RearAxleZ;
            float by = front ? BaseYFront : BaseYRear;
            y = !float.IsNaN(by) ? by : (front ? RadiusFront : RadiusRear) - CgHeightM;
        }

        /// <summary>Reads everything from the car's data.acd (or data/ folder). Never throws; missing values keep their defaults
        /// (listed in <see cref="Problems"/>).</summary>
        public static AcCarSounds Load(string carDir, string carId)
        {
            var s = new AcCarSounds { CarId = carId ?? "" };
            try
            {
                var acd = AcdArchive.Open(carDir);
                if (acd == null) { s.Problems.Add("no data.acd / data folder"); return s; }
                s.FromArchive = true;
                s.Read(acd);
            }
            catch (Exception e) { s.Problems.Add("data.acd: " + e.Message); }
            return s;
        }

        public void Read(AcdArchive acd)
        {
            AcIni snd = acd.Has("sounds.ini") ? acd.ReadIni("sounds.ini") : null;
            if (snd == null) Problems.Add("sounds.ini missing");
            else
            {
                SkidEntryPoint = snd.GetFloat("SKIDS", "ENTRY_POINT", SkidEntryPoint);
                SkidMixVolume = snd.GetFloat("SKIDS", "MIX_VOLUME", SkidMixVolume);
                HasBackfire = snd.Has("BACKFIRE");
                BackfireMaxGas = snd.GetFloat("BACKFIRE", "MAXGAS", BackfireMaxGas);
                BackfireMinRpm = snd.GetFloat("BACKFIRE", "MINRPM", BackfireMinRpm);
                BackfireMaxRpm = snd.GetFloat("BACKFIRE", "MAXRPM", BackfireMaxRpm);
                BackfireTriggerGas = snd.GetFloat("BACKFIRE", "TRIGGERGAS", BackfireTriggerGas);
                BackfireVolumeIn = snd.GetFloat("BACKFIRE", "VOLUME_IN", BackfireVolumeIn);
                BackfireVolumeOut = snd.GetFloat("BACKFIRE", "VOLUME_OUT", BackfireVolumeOut);
                BackfireVolumeScaleOut = snd.GetFloat("BACKFIRE", "VOLUME_SCALE_OUT", 0f);
                GearExtDistanceScale = snd.GetFloat("GEAR_EXT", "DISTANCE_SCALE", 0f);
                BodyworkMin = snd.GetFloat("BODY_WORK", "MIN", BodyworkMin);
                BodyworkGain = snd.GetFloat("BODY_WORK", "GAIN", BodyworkGain);
                EnginePosition = (snd.Get("ENGINE", "POSITION", "") ?? "").Trim();
            }
            AcIni eng = acd.Has("engine.ini") ? acd.ReadIni("engine.ini") : null;
            if (eng != null)
            {
                LimiterRpm = eng.GetFloat("ENGINE_DATA", "LIMITER", 0f);
                IdleRpm = eng.GetFloat("ENGINE_DATA", "MINIMUM", 0f);
                var turbos = eng.Numbered("TURBO_");
                for (int i = 0; i < turbos.Count; i++) MaxBoostBar = Math.Max(MaxBoostBar, turbos[i].GetFloat("MAX_BOOST", 0f));
            }
            else Problems.Add("engine.ini missing");
            AcIni sus = acd.Has("suspensions.ini") ? acd.ReadIni("suspensions.ini") : null;
            if (sus != null)
            {
                WheelbaseM = sus.GetFloat("BASIC", "WHEELBASE", WheelbaseM);
                CgLocation = sus.GetFloat("BASIC", "CG_LOCATION", CgLocation);
                TrackFrontM = sus.GetFloat("FRONT", "TRACK", TrackFrontM);
                TrackRearM = sus.GetFloat("REAR", "TRACK", TrackRearM);
                if (sus.Has("FRONT", "BASEY")) BaseYFront = sus.GetFloat("FRONT", "BASEY", 0f);
                if (sus.Has("REAR", "BASEY")) BaseYRear = sus.GetFloat("REAR", "BASEY", 0f);
            }
            else Problems.Add("suspensions.ini missing");
            AcIni car = acd.Has("car.ini") ? acd.ReadIni("car.ini") : null;
            if (car != null)
            {
                float[] off = car.GetFloats("BASIC", "GRAPHICS_OFFSET", null);
                if (off != null && off.Length >= 2) CgHeightM = -off[1];
                float[] eye = car.GetFloats("GRAPHICS", "DRIVEREYES", null);
                if (eye != null && eye.Length >= 3)
                {
                    float ox = off != null && off.Length >= 3 ? off[0] : 0f, oy = off != null && off.Length >= 3 ? off[1] : 0f, oz = off != null && off.Length >= 3 ? off[2] : 0f;
                    // DRIVEREYES is MODEL space; body (CG) frame in Unity axes = (-(x+off.x), y+off.y, z+off.z) (Critic addendum, 86/86)
                    EyeX = -(eye[0] + ox); EyeY = eye[1] + oy; EyeZ = eye[2] + oz; HasEye = true;
                }
                TotalMassKg = car.GetFloat("BASIC", "TOTALMASS", 0f);
            }
            AcIni ty = acd.Has("tyres.ini") ? acd.ReadIni("tyres.ini") : null;
            if (ty != null)
            {
                RadiusFront = ty.GetFloat("FRONT", "RADIUS", RadiusFront);
                RadiusRear = ty.GetFloat("REAR", "RADIUS", RadiusRear);
            }
        }

        public override string ToString()
        {
            return CarId + ": engine " + (EnginePosition.Length > 0 ? EnginePosition : "CoG") + ", skids entry " + SkidEntryPoint + " mix " + SkidMixVolume
                + (HasBackfire ? ", backfire gas<" + BackfireMaxGas + " after >" + BackfireTriggerGas + " " + BackfireMinRpm + "-" + BackfireMaxRpm + " rpm" : "")
                + (MaxBoostBar > 0f ? ", turbo " + MaxBoostBar + " bar" : "") + ", limiter " + LimiterRpm
                + (GearExtDistanceScale > 0f || BackfireVolumeScaleOut > 0f ? ", track-camera scales gear_ext " + GearExtDistanceScale + " backfire out " + BackfireVolumeScaleOut + " (not used: car-camera rule)" : "");
        }
    }
}
