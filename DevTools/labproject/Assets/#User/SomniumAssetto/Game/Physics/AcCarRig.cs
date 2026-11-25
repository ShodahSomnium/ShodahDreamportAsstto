using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>How <see cref="AcCarRig.Spawn"/> assembles a car.</summary>
    public sealed class AcCarRigOptions
    {
        public AcCarKind Kind = AcCarKind.Local;
        /// <summary>Model options (LOD, interior...). Null = C's defaults for the kind (AcCarModelOptions.ForKind).</summary>
        public AcCarModelOptions Model;
        /// <summary>Attach A's car audio (engine etc.).</summary>
        public bool Audio = true;
        /// <summary>Parent transform (null = the scene root; R uses AcSession.WorldRoot).</summary>
        public Transform Parent;
        /// <summary>Optional: lets the caller abandon the build (<see cref="AcCarSpawnHandle.Cancel"/>) - never StopCoroutine a running Spawn.</summary>
        public AcCarSpawnHandle Handle;
    }

    /// <summary>
    /// [P] Cancels a car build in progress the safe way: the root under construction is destroyed and Spawn keeps stepping C's builder until it
    /// has disposed its half-built model (meshes, the texture scope, AcCarModel.LiveCount), then calls done(null). Stopping the coroutine instead
    /// leaks all of that for the session and leaves the half-built car drawn in the world.
    /// </summary>
    public sealed class AcCarSpawnHandle
    {
        /// <summary>The root being built (set by Spawn once it exists).</summary>
        public GameObject Root { get; internal set; }
        public bool Cancelled { get; private set; }
        /// <summary>Spawn has called done (built, failed or cancelled).</summary>
        public bool Finished { get; internal set; }

        public void Cancel()
        {
            if (Cancelled) return;
            Cancelled = true;
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
    }

    /// <summary>
    /// [P, Physics] One assembled car = root GameObject (Rigidbody at the CG, layer Car) + C's <see cref="AcCarModel"/> under it at
    /// (-GRAPHICS_OFFSET.x, GRAPHICS_OFFSET.y, GRAPHICS_OFFSET.z) with Euler(GRAPHICS_PITCH_ROTATION, 0, 0) (ac_car_formats.md C1) + P's
    /// <see cref="AcCarPhysics"/> + A's <see cref="AcCarAudio"/>. The single factory for every car: R spawns the local car, I the AI cars,
    /// N the remote cars (Kind Remote = puppet), F the showroom car (Kind Showroom = no physics). Kind Ai = like Local (I calls SetDriven /
    /// ResetTo itself). Kind Local starts PARKED (kinematic, parking brake) until S seats the player (SetOccupied + SetDriven). Kind Showroom has
    /// no physics and its root is the model's ground origin (put it on a floor); every other kind's root is the CG (place it with
    /// <see cref="AcCarPhysics.ResetTo"/>, which casts down and lifts the CG to -GRAPHICS_OFFSET.y).
    /// The spec is parsed on a worker thread (cached per car id), the model is built time-sliced by C.
    /// </summary>
    public sealed class AcCarRig
    {
        public AcCarKind Kind { get; private set; }
        public AcCarEntry Car { get; private set; }
        public string SkinId { get; private set; }
        public GameObject Root { get; private set; }
        public AcCarModel Model { get; private set; }
        public AcCarPhysics Physics { get; private set; }
        public AcCarAudio Audio { get; private set; }
        public AcCarSpec Spec { get; private set; }
        /// <summary>Photon id of the driver (remote cars), -(100 + index) for AI, local id for the own car. // ours convention</summary>
        public int OwnerId = -1;

        // one parsed spec per car id (parsing is ~1 ms but the archive decrypt is not free; specs are read-only after load, except the
        // per-car setup that AcCarPhysics.ApplySetup writes into its own copy - see SpecFor)
        private static readonly System.Collections.Generic.Dictionary<string, AcCarSpec> SpecCache = new System.Collections.Generic.Dictionary<string, AcCarSpec>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        /// <summary>Builds a car (spec on a worker, model time-sliced) and calls <paramref name="done"/> with the rig (null on failure, logged).
        /// Run with StartCoroutine.</summary>
        public static IEnumerator Spawn(AcCarEntry car, string skinId, AcCarRigOptions options, Action<AcCarRig> done)
        {
            if (options == null) options = new AcCarRigOptions();
            if (car == null) { AcLog.Warn("Physics: AcCarRig.Spawn without a car"); if (options.Handle != null) options.Handle.Finished = true; Done(done, null); yield break; }
            float t0 = Time.realtimeSinceStartup;

            // 1. the spec (worker thread; not for the showroom, which needs only GRAPHICS_OFFSET / PITCH - parsed too, it is cheap)
            var job = new SpecJob { Car = car, Fresh = options.Kind == AcCarKind.Local || options.Kind == AcCarKind.Ai };
            ThreadPool.QueueUserWorkItem(SpecJob.Run, job);
            while (!job.Done) yield return null;
            AcCarSpec spec = job.Spec;
            AcCarSpawnHandle handle = options.Handle;
            if (handle != null && handle.Cancelled) { handle.Finished = true; Done(done, null); yield break; }
            if (job.Error != null) AcLog.Warn("Physics: reading " + car.Id + "'s physics failed: " + job.Error);
            if (spec == null && car.Owned) AcLog.Warn("Physics: " + car.Id + " has no physics data (data.acd) - built without physics");

            // 2. the root at the CG and C's model under it
            var go = new GameObject("Ac Car " + car.Id);
            if (options.Parent != null) go.transform.SetParent(options.Parent, false);
            go.layer = AcLayers.Car;
            if (handle != null) handle.Root = go;
            var rig = new AcCarRig { Kind = options.Kind, Car = car, SkinId = skinId ?? "", Root = go, Spec = spec };
            AcCarModelOptions mo = options.Model;
            if (mo == null) { try { mo = AcCarModelOptions.ForKind(options.Kind); } catch (Exception) { mo = new AcCarModelOptions(); } }
            AcCarModel model = null;
            bool modelDone = false;
            IEnumerator build = null;
            try { build = AcCarModel.BuildAsync(car, skinId, mo, go.transform, m => { model = m; modelDone = true; }); }
            catch (Exception e) { AcLog.Error("Physics: the car model of " + car.Id + " could not start building: " + e); }
            while (build != null)
            {
                bool more;
                object cur = null;
                try { more = build.MoveNext(); if (more) cur = build.Current; }
                catch (Exception e) { AcLog.Error("Physics: building the car model of " + car.Id + " threw: " + e); more = false; }
                if (!more) break;
                yield return cur;
                if (go == null || (handle != null && handle.Cancelled))
                {
                    // destroyed while building (track unloaded) or cancelled: KEEP STEPPING the builder - it notices its root is gone and disposes its
                    // half-built model (meshes, texture scope, LiveCount); a model that still comes back is disposed here
                    if (go != null) UnityEngine.Object.Destroy(go);
                    while (true)
                    {
                        object c2 = null;
                        try { if (!build.MoveNext()) break; c2 = build.Current; }
                        catch (Exception e) { AcLog.Warn("Physics: draining the abandoned build of " + car.Id + " threw: " + e.Message); break; }
                        yield return c2;
                    }
                    if (model != null) { try { model.Dispose(); } catch (Exception) { } }
                    if (handle != null) handle.Finished = true;
                    Done(done, null);
                    yield break;
                }
            }
            if (go == null || (handle != null && handle.Cancelled))
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                if (model != null) { try { model.Dispose(); } catch (Exception) { } }
                if (handle != null) handle.Finished = true;
                Done(done, null);
                yield break;
            }
            if (!modelDone && model == null) AcLog.Warn("Physics: " + car.Id + " has no model (C's builder returned nothing) - physics only");
            rig.Model = model;
            if (model != null && model.Root != null)
            {
                Transform mr = model.Root;
                if (mr.parent != go.transform) mr.SetParent(go.transform, false);
                if (options.Kind == AcCarKind.Showroom || spec == null)
                {
                    // no physics: the root IS the model's ground origin (F / H put it on a floor)
                    mr.localPosition = Vector3.zero;
                    mr.localRotation = Quaternion.identity;
                }
                else
                {
                    mr.localPosition = new Vector3(-spec.GraphicsOffset.X, spec.GraphicsOffset.Y, spec.GraphicsOffset.Z);
                    mr.localRotation = Quaternion.Euler(spec.GraphicsPitchDeg, 0f, 0f);
                }
            }

            // 3. physics (not for the showroom)
            if (options.Kind != AcCarKind.Showroom && spec != null)
            {
                try
                {
                    AcCarPhysics ph = go.AddComponent<AcCarPhysics>();
                    ph.Rig = rig;
                    rig.Physics = ph;
                    ph.Init(spec, model, SurfacesNow());
                    if (options.Kind == AcCarKind.Remote) ph.SetPuppet(true);
                    else
                    {
                        ApplyServerRates(ph);
                        if (options.Kind == AcCarKind.Local) LocalSetup(car, ph);
                    }
                }
                catch (Exception e) { AcLog.Error("Physics: setting up " + car.Id + "'s physics failed: " + e); }
            }

            // 4. audio (A)
            if (options.Audio && options.Kind != AcCarKind.Showroom)
            {
                try { rig.Audio = AcCarAudio.Attach(go, car, spec, options.Kind == AcCarKind.Local); }
                catch (Exception e) { AcLog.Warn("Physics: attaching " + car.Id + "'s audio failed: " + e.GetType().Name + " " + e.Message); }
            }

            AcLog.InfoOnce("Physics: rig " + car.Id + " (" + options.Kind + ") built in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms: model " + (model != null) +
                ", hull colliders " + (model != null && model.Hull != null ? model.Hull.Length : 0) + ", physics " + (rig.Physics != null) + ", audio " + (rig.Audio != null));
            if (handle != null) handle.Finished = true;
            Done(done, rig);
        }

        /// <summary>The local car starts with the player's garage setup (F: AcFront.SetupValue per setup.ini key - PRESSURE_xx, FUEL,
        /// FRONT_BIAS, BRAKE_POWER_MULT) and the aids F computes from the rules (AcFront.Assists). Guarded: F missing = the car's defaults.</summary>
        private static void LocalSetup(AcCarEntry car, AcCarPhysics ph)
        {
            int applied = ApplyLocalSetup(ph);
            AcAssists s = ph.Assists;
            AcLog.InfoOnce("Physics: " + car.Id + " local setup: " + applied + " garage values applied; aids ABS " + s.Abs + " TC " + s.Tc + " auto-shift " + s.AutoShift + " auto-clutch " + s.AutoClutch + " auto-blip " + s.AutoBlip +
                "; rates fuel " + (ph.FuelRate * 100f).ToString("0") + " % tyre wear " + (ph.TyreWearRate * 100f).ToString("0") + " %");
        }

        /// <summary>Applies the player's garage setup (F: AcFront.SetupValue per setup.ini key: the player's value, else the car's default) and
        /// the aids (AcFront.Assists(AcSession.Rules)) to a car's physics; only values that differ from the last applied one (first time: the
        /// car's own default) are applied, so a later call (AcCarPhysics.ReapplySetup, when F's garage row changes) never refills the tank by
        /// accident. Guarded: F missing = the car's defaults. Returns the number of setup values applied.</summary>
        public static int ApplyLocalSetup(AcCarPhysics ph)
        {
            if (ph == null || ph.Spec == null || ph.Core == null) return 0;
            int applied = 0;
            string carId = ph.Spec.CarId;
            try
            {
                for (int i = 0; i < ph.Spec.Setup.Count; i++)
                {
                    AcSetupItem item = ph.Spec.Setup[i];
                    float v;
                    // false = the player did not set this row: P's own default (F's row default is rounded / clamped for display - FRONT_BIAS
                    // Math.Round(FRONT_SHARE*100) - and must not become a phantom override that also switches ctrl_ebb off); applied when it
                    // differs from the last applied value (a reset row)
                    if (!AcFront.SetupValue(carId, item.Key, out v)) v = item.Default;
                    if (float.IsNaN(v)) continue;
                    float last;
                    if (!ph.AppliedSetup.TryGetValue(item.Key, out last)) last = item.Default;
                    if (!float.IsNaN(last) && Math.Abs(v - last) < 1e-4f) continue;
                    if (ph.ApplySetup(item.Key, v)) applied++;
                }
            }
            catch (Exception e) { AcLog.WarnOnce("Physics: reading the garage setup failed (" + e.GetType().Name + ": " + e.Message + ") - the car's defaults"); }
            try { AcAssists a = AcFront.Assists(AcSession.Rules); if (a != null) ph.Assists = a; }
            catch (Exception e) { AcLog.WarnOnce("Physics: reading the assists failed (" + e.GetType().Name + ") - factory aids"); }
            return applied;
        }

        /// <summary>Server-style rates from the session rules (AcRules FuelRate / TyreWearRate, server_cfg FUEL_RATE / TYRE_WEAR_RATE in %)
        /// for the local and AI cars. Guarded (R missing = 100 %).</summary>
        private static void ApplyServerRates(AcCarPhysics ph)
        {
            try
            {
                AcRules r = AcSession.Rules;
                if (r == null) return;
                ph.FuelRate = Mathf.Max(0f, r.FuelRate) / 100f;
                ph.TyreWearRate = Mathf.Max(0f, r.TyreWearRate) / 100f;
                ph.DamageRate = Mathf.Clamp01(r.DamageMultiplier / 100f); // iteration 2 (R keeps it current in ApplyRates; 0 = damage off)
            }
            catch (Exception e) { AcLog.WarnOnce("Physics: reading the session's fuel / tyre wear rates failed (" + e.GetType().Name + ") - 100 %"); }
        }

        private static void Done(Action<AcCarRig> done, AcCarRig rig)
        {
            if (done == null) return;
            try { done(rig); }
            catch (Exception e) { AcLog.Error("Physics: the spawn callback failed: " + e); }
        }

        private static AcSurfaces SurfacesNow()
        {
            try { return AcTrackWorld.Loaded ? AcTrackWorld.Surfaces : null; }
            catch (Exception) { return null; }
        }

        /// <summary>The parsed spec of a car (worker-safe). <paramref name="fresh"/> = a private copy (a driven car's setup may change its
        /// numbers); otherwise the shared cached one (remote / showroom: read-only use).</summary>
        public static AcCarSpec SpecFor(AcCarEntry car, bool fresh)
        {
            if (car == null || !car.Owned) return null;
            if (fresh) return AcCarSpec.Load(car);
            lock (CacheLock)
            {
                AcCarSpec s;
                if (SpecCache.TryGetValue(car.Id, out s)) return s;
            }
            AcCarSpec loaded = AcCarSpec.Load(car);
            lock (CacheLock) { SpecCache[car.Id] = loaded; }
            return loaded;
        }

        private sealed class SpecJob
        {
            public AcCarEntry Car;
            public bool Fresh;
            public volatile bool Done;
            public AcCarSpec Spec;
            public string Error;

            public static void Run(object o)
            {
                var j = (SpecJob)o;
                try { j.Spec = SpecFor(j.Car, j.Fresh); }
                catch (Exception e) { j.Error = e.GetType().Name + ": " + e.Message; }
                finally { j.Done = true; }
            }
        }

        /// <summary>Destroys the car (model, audio, physics) and frees its resources.</summary>
        public void Dispose()
        {
            if (ReferenceEquals(AcLocal.Car, this)) AcLocal.Telemetry = default(AcTelemetry);
            try { if (Audio != null) Audio.Dispose(); } catch (Exception e) { AcLog.WarnOnce("Physics: audio dispose failed: " + e.Message); }
            try { if (Model != null) Model.Dispose(); } catch (Exception e) { AcLog.WarnOnce("Physics: model dispose failed: " + e.Message); }
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null; Model = null; Physics = null; Audio = null;
        }
    }
}
