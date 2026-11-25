using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [I, AI drivers] Who drives what and where they start: car / skin / name choice like AC's quick race, the build coroutine (worker job for
    /// the car's ai.ini + physics digest + speed plans, then P's AcCarRig.Spawn of Kind Ai, one car at a time), the start place (a free pit
    /// box from the far end, else the pit lane, the back of the grid, the line behind the start), and the hand-over snapshot of the previous
    /// master's AI cars. Main thread except the worker job (pure Data/Ai code on immutable inputs).
    /// </summary>
    internal static class AcAiField
    {
        // ------------------------------------------------------------------ choice

        private static List<string> _names;
        private static string _namesRoot;

        /// <summary>Makes <paramref name="count"/> drivers (only the first <paramref name="build"/> get a car built).</summary>
        internal static List<AcAiDriver> Choose(AcContent content, int count, int build, AcRules rules, int level)
        {
            var list = new List<AcAiDriver>(count);
            var rng = new System.Random(Environment.TickCount);
            List<AcCarEntry> pool = CarPool(content, rules);
            List<string> names = ShuffledNames(rng);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(AcLocal.Nick)) usedNames.Add(AcLocal.Nick);
            var skinUse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int skinStart = rng.Next(1000);
            int namePos = 0;
            for (int i = 0; i < count; i++)
            {
                var d = new AcAiDriver { Index = i, Level = level, State = i < build ? AcAiState.Building : AcAiState.Failed };
                AcCarEntry car = null;
                AcSkinEntry skin = null;
                // a new master continues the previous master's car i where it is
                if (Adopt[i].Present && Fresh(i))
                {
                    car = content.Car(Adopt[i].CarId);
                    if (car != null && car.Owned && !car.IsDlcStub) { skin = car.Skin(Adopt[i].SkinId); if (Adopt[i].Level > 0) d.Level = Adopt[i].Level; }
                    else car = null;
                }
                if (car == null && pool.Count > 0)
                {
                    car = pool[i % pool.Count];
                    skin = PickSkin(car, skinUse, skinStart);
                }
                if (car == null) { d.State = AcAiState.Failed; d.Name = "AI " + (i + 1); list.Add(d); continue; }
                d.CarId = car.Id;
                d.SkinId = skin != null ? skin.Id : "";
                string name = car != null && Adopt[i].Present && Fresh(i) && !string.IsNullOrEmpty(Adopt[i].Name) ? Adopt[i].Name : null;
                if (name == null && skin != null && !string.IsNullOrEmpty(skin.Driver) && !usedNames.Contains(skin.Driver.Trim())) name = skin.Driver.Trim();
                while (name == null && namePos < names.Count) { string n = names[namePos++]; if (!usedNames.Contains(n)) name = n; }
                if (name == null) name = "AI " + (i + 1);
                usedNames.Add(name);
                d.Name = name;
                list.Add(d);
            }
            return list;
        }

        /// <summary>The cars the AI may drive: the master's AllowedCars (owned ones, in order), else the local player's car (AC's quick race
        /// puts every opponent in the player's car), else the first owned car.</summary>
        private static List<AcCarEntry> CarPool(AcContent content, AcRules rules)
        {
            var pool = new List<AcCarEntry>();
            if (rules.AllowedCars != null)
                for (int i = 0; i < rules.AllowedCars.Count; i++)
                {
                    AcCarEntry c = content.Car(rules.AllowedCars[i]);
                    if (c != null && c.Owned && !c.IsDlcStub && !pool.Contains(c)) pool.Add(c);
                }
            if (pool.Count == 0)
            {
                AcCarEntry mine = string.IsNullOrEmpty(AcLocal.CarId) ? null : content.Car(AcLocal.CarId);
                if (mine != null && mine.Owned && !mine.IsDlcStub) pool.Add(mine);
            }
            if (pool.Count == 0)
                for (int i = 0; i < content.Cars.Count; i++)
                    if (content.Cars[i].Owned && !content.Cars[i].IsDlcStub) { pool.Add(content.Cars[i]); break; }
            return pool;
        }

        private static AcSkinEntry PickSkin(AcCarEntry car, Dictionary<string, int> use, int start)
        {
            int n = car.Skins != null ? car.Skins.Count : 0;
            if (n == 0) return null;
            int k; use.TryGetValue(car.Id, out k);
            use[car.Id] = k + 1;
            bool mine = string.Equals(car.Id, AcLocal.CarId, StringComparison.OrdinalIgnoreCase);
            for (int t = 0; t < n; t++)
            {
                AcSkinEntry s = car.Skins[(start + k + t) % n];
                if (mine && n > 1 && string.Equals(s.Id, AcLocal.SkinId, StringComparison.OrdinalIgnoreCase)) continue; // not the player's own livery
                return s;
            }
            return car.Skins[(start + k) % n];
        }

        /// <summary>content/career/names.txt (39 AC driver names, BOM + CRLF) shuffled; empty when missing.</summary>
        private static List<string> ShuffledNames(System.Random rng)
        {
            AcInstall inst = AcGame.Current != null ? AcGame.Current.Install : null;
            string root = inst != null ? inst.Root : null;
            if (_names == null || _namesRoot != root)
            {
                _namesRoot = root;
                _names = new List<string>();
                try
                {
                    string path = root != null ? Path.Combine(Path.Combine(Path.Combine(root, "content"), "career"), "names.txt") : null;
                    if (path != null && File.Exists(path))
                    {
                        string[] lines = AcText.ReadFile(path).Split('\n');
                        for (int i = 0; i < lines.Length; i++) { string s = lines[i].Trim().Trim('﻿'); if (s.Length > 0) _names.Add(s); }
                    }
                    AcLog.InfoOnce("Ai: " + _names.Count + " driver names from content/career/names.txt");
                }
                catch (Exception e) { AcLog.Warn("Ai: names.txt: " + e.Message); }
            }
            var copy = new List<string>(_names);
            for (int i = copy.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); string t = copy[i]; copy[i] = copy[j]; copy[j] = t; }
            return copy;
        }

        // ------------------------------------------------------------------ build

        private sealed class Job
        {
            public string Dir, Id;
            public int Level;
            public float Grip, PitCap;
            public AcAiTrack Fast, Pit;
            public AcAiHints Hints;
            public float StopS = -1f, EndSpeed;
            public Fuel Fuel;
            public AcAiCar Car;
            public AcAiPlan FastPlan, PitPlan;
            public float FuelLoad;
            public string Error;
            public volatile bool Done;
        }

        /// <summary>What the fuel load depends on (AcRules at build / grid time).</summary>
        private struct Fuel
        {
            public bool Race;
            public int Laps, Minutes;
            public float Rate;
            public string Key { get { return Race ? "race " + Laps + "/" + Minutes + "/" + Rate.ToString("0.00") : "practice " + Rate.ToString("0.00"); } }
        }

        /// <summary>The fuel case now: a race session from the grid on = race distance, else practice / qualify.</summary>
        private static Fuel CurrentFuel()
        {
            AcRules r = AcSession.Rules;
            AcPhase p = AcSession.Phase;
            bool race = r != null && r.Session == AcSessionType.Race && p >= AcPhase.Grid && p <= AcPhase.Finished;
            return FuelOf(r, race);
        }

        private static Fuel FuelOf(AcRules r, bool race)
        {
            var f = new Fuel { Race = race, Rate = 1f };
            if (r == null) return f;
            f.Laps = r.Laps; f.Minutes = r.TimeMinutes; f.Rate = Mathf.Max(0f, r.FuelRate / 100f);
            return f;
        }

        private sealed class Planned { public AcAiCar Car; public AcAiPlan Fast, Pit; public float FuelLoad; }

        private sealed class RigHolder
        {
            public AcCarRig Rig;
            public bool Called;
            public void Set(AcCarRig r) { Rig = r; Called = true; }
        }

        private static readonly Dictionary<string, Planned> Plans = new Dictionary<string, Planned>(StringComparer.OrdinalIgnoreCase);

        internal static void ForgetPlans() { Plans.Clear(); }

        private static void RunJob(object o)
        {
            var j = (Job)o;
            try
            {
                j.Car = AcAiCar.Load(j.Dir, j.Id);
                if (j.Fast != null) j.FastPlan = new AcAiPlan(j.Fast, j.Car, j.Level, j.Grip, float.PositiveInfinity, j.Hints, j.StopS, j.EndSpeed);
                // the fuel this car starts with, and the plans re-made for that mass when it differs from car.ini FUEL by > 1 l // ours
                j.FuelLoad = j.Car.FuelLitres;
                if (j.FastPlan != null)
                {
                    j.FuelLoad = AcAiPlan.FuelLoad(j.Car, j.FastPlan.FuelPerLap, j.FastPlan.LapTime, j.Fuel.Race, j.Fuel.Laps, j.Fuel.Minutes, j.Fuel.Rate);
                    if (Math.Abs(j.FuelLoad - j.Car.FuelLitres) > 1f)
                    {
                        j.Car.SetFuel(j.FuelLoad);
                        j.FastPlan = new AcAiPlan(j.Fast, j.Car, j.Level, j.Grip, float.PositiveInfinity, j.Hints, j.StopS, j.EndSpeed);
                    }
                }
                if (j.Pit != null) j.PitPlan = new AcAiPlan(j.Pit, j.Car, j.Level, j.Grip, j.PitCap);
            }
            catch (Exception e) { j.Error = e.Message; }
            j.Done = true;
        }

        private sealed class PlanSlot { public Planned Result; }

        /// <summary>The digest + plans of <paramref name="car"/> for this level and fuel case: from the cache, else a worker job (one log line).</summary>
        private static IEnumerator Plan(AcCarEntry car, int level, Fuel fuel, float grip, float pitCap, PlanSlot slot)
        {
            string key = car.Id + "|" + level + "|" + fuel.Key;
            Planned pl;
            if (Plans.TryGetValue(key, out pl)) { slot.Result = pl; yield break; }
            var job = new Job { Dir = car.Dir, Id = car.Id, Level = level, Grip = grip, PitCap = pitCap, Fast = AcAi.FastTrack, Pit = AcAi.PitTrack, Hints = AcAi.Hints, Fuel = fuel, StopS = AcAi.OpenStopS, EndSpeed = AcAi.OpenRunOut ? AcAiPlan.RunOutSpeed : 0f };
            ThreadPool.QueueUserWorkItem(RunJob, job);
            float t0 = Time.realtimeSinceStartup;
            while (!job.Done && Time.realtimeSinceStartup - t0 < 30f) yield return null; // ours: 30 s guard
            if (!job.Done || job.Car == null || job.FastPlan == null)
            {
                AcLog.Warn("Ai: " + car.Id + ": could not plan (" + (job.Error ?? (job.Done ? "no plan" : "timed out")) + ")");
                yield break;
            }
            pl = new Planned { Car = job.Car, Fast = job.FastPlan, Pit = job.PitPlan, FuelLoad = job.FuelLoad };
            Plans[key] = pl;
            AcLog.Info("Ai: " + car.Id + " ai.ini " + pl.Car.Ai + " | " + pl.Car + " | planned lap " + LapText((int)(pl.Fast.LapTime * 1000f)) + " at level " + level
                       + " (grip use " + pl.Fast.GripUse.ToString("0.00") + ", road " + grip.ToString("0.00")
                       + (pl.Fast.HintedPoints > 0 ? ", ai_hints on " + pl.Fast.HintedPoints + " points" : "") + (pl.Fast.CrestPoints > 0 ? ", slower on " + pl.Fast.CrestPoints + " crest points" : "")
                       + ") | fuel " + pl.FuelLoad.ToString("0.0") + " l (" + fuel.Key + ", " + pl.Fast.FuelPerLap.ToString("0.00") + " l/lap planned, tank " + pl.Car.FuelMax.ToString("0") + " l)");
            slot.Result = pl;
        }

        /// <summary>
        /// Grid of a race (AcAi's PhaseChanged): the AI cars that drove the practice get the race fuel (FUEL_RATE x distance, AcAiPlan.FuelLoad)
        /// and the plans for that mass; the controller is re-made (the car stands frozen on the grid, R releases it at the green).
        /// </summary>
        internal static IEnumerator Refuel(int gen)
        {
            Fuel fuel = FuelOf(AcSession.Rules, AcSession.Rules != null && AcSession.Rules.Session == AcSessionType.Race);
            if (!fuel.Race) yield break;
            AcGame game = AcGame.Current;
            AcContent content = game != null ? game.Content : null;
            if (content == null) yield break;
            float grip = RoadGrip();
            float pitCap = Mathf.Max(5f, (AcSession.Rules != null ? AcSession.Rules.PitLimitKmh : 80f) / 3.6f);
            IReadOnlyList<AcAiDriver> drivers = AcAi.Drivers;
            int n = 0;
            for (int i = 0; i < drivers.Count; i++)
            {
                if (!AcAi.StillBuilding(gen)) yield break;
                AcAiDriver d = drivers[i];
                if (d == null || d.Rig == null || d.Rig.Physics == null || d.CarData == null) continue;
                AcCarEntry car = content.Car(d.CarId);
                if (car == null) continue;
                var slot = new PlanSlot();
                IEnumerator pe = Plan(car, d.Level, fuel, grip, pitCap, slot);
                while (pe.MoveNext()) { yield return pe.Current; if (!AcAi.StillBuilding(gen)) yield break; }
                Planned pl = slot.Result;
                if (pl == null || d.Rig == null || d.Rig.Physics == null) continue;
                AcCarPhysics ph = d.Rig.Physics;
                d.CarData = pl.Car; d.FastPlan = pl.Fast; d.PitPlan = pl.Pit; d.PlannedLapS = pl.Fast.LapTime; d.FuelLoad = pl.FuelLoad;
                float hw = d.Brain != null ? d.Brain.HalfWidth : 0.95f; // ours default
                d.Brain = new AcAiBrain(d.CarData, d.FastTrack, d.FastPlan) { HalfWidth = hw };
                d.OnPitLine = false;
                try { d.ChooseLine(ph.Body != null ? ph.Body.position : d.Rig.Root.transform.position); } catch (Exception) { }
                ApplyFuel(d, ph);
                n++;
            }
            if (n > 0) AcLog.Info("Ai: race fuel for " + n + " AI car(s) (" + fuel.Key + ")");
        }

        /// <summary>Puts the driver's planned fuel load in its car (P's setup item FUEL) and the server FUEL_RATE.</summary>
        internal static void ApplyFuel(AcAiDriver d, AcCarPhysics ph)
        {
            if (ph == null) return;
            try { ph.FuelRate = AcSession.Rules != null ? Mathf.Max(0f, AcSession.Rules.FuelRate / 100f) : 1f; } catch (Exception) { }
            // iteration 2: DAMAGE_MULTIPLIER (0 = off; AcCarRig sets it at spawn too, R keeps it current in ApplyRates)
            try { ph.DamageRate = AcSession.Rules != null ? Mathf.Clamp01(Mathf.Max(0, AcSession.Rules.DamageMultiplier) / 100f) : 0f; } catch (Exception) { }
            if (d.FuelLoad <= 0f) return;
            try { if (!ph.ApplySetup("FUEL", d.FuelLoad)) AcLog.WarnOnce("Ai: P's ApplySetup(FUEL) refused - AI cars keep car.ini FUEL"); }
            catch (Exception e) { AcLog.WarnOnce("Ai: ApplySetup(FUEL) failed: " + e.Message); }
        }

        /// <summary>Builds every driver's car, one after another (run on AcGame).</summary>
        internal static IEnumerator BuildAll(int gen, int level)
        {
            AcGame game = AcGame.Current;
            AcContent content = game != null ? game.Content : null;
            IReadOnlyList<AcAiDriver> drivers = AcAi.Drivers;
            var usedBoxes = new HashSet<int>();
            var summary = new StringBuilder();
            int built = 0, extra = 0;
            float grip = RoadGrip();
            float pitCap = Mathf.Max(5f, (AcSession.Rules != null ? AcSession.Rules.PitLimitKmh : 80f) / 3.6f);
            for (int i = 0; i < drivers.Count; i++)
            {
                Fuel fuel = CurrentFuel();
                if (!AcAi.StillBuilding(gen)) yield break;
                AcAiDriver d = drivers[i];
                if (d == null || d.State == AcAiState.Failed) continue;
                AcCarEntry car = content != null ? content.Car(d.CarId) : null;
                if (car == null) { d.State = AcAiState.Failed; continue; }

                // 1) ai.ini + physics digest + fuel + plans (worker; cached per car, level and fuel case)
                var slot = new PlanSlot();
                IEnumerator pe = Plan(car, d.Level, fuel, grip, pitCap, slot);
                while (pe.MoveNext()) { yield return pe.Current; if (!AcAi.StillBuilding(gen)) yield break; }
                if (!AcAi.StillBuilding(gen)) yield break;
                Planned pl = slot.Result;
                if (pl == null) { d.State = AcAiState.Failed; continue; }
                d.CarData = pl.Car; d.FastPlan = pl.Fast; d.PitPlan = pl.Pit; d.FastTrack = AcAi.FastTrack; d.PitTrack = AcAi.PitTrack;
                d.PlannedLapS = pl.Fast.LapTime; d.FuelLoad = pl.FuelLoad;

                // 2) where
                Vector3 at; float yaw, speed; bool inPits;
                Place(d, usedBoxes, ref extra, out at, out yaw, out inPits, out speed);

                // 3) the car (P's factory), stepped here so a failure inside is one log line
                var holder = new RigHolder();
                Transform parent = AcSession.WorldRoot != null ? AcSession.WorldRoot : AcTrackWorld.Root;
                var opts = new AcCarRigOptions { Kind = AcCarKind.Ai, Audio = true, Parent = parent, Handle = new AcCarSpawnHandle() };
                IEnumerator sp = null;
                try { sp = AcCarRig.Spawn(car, d.SkinId, opts, holder.Set); }
                catch (Exception e) { AcLog.Error("Ai: AcCarRig.Spawn(" + car.Id + ") failed: " + e); }
                while (sp != null)
                {
                    object cur;
                    try { if (!sp.MoveNext()) break; cur = sp.Current; }
                    catch (Exception e) { AcLog.Error("Ai: building " + car.Id + " failed: " + e); break; }
                    yield return cur;
                    // the field was cleared / respawned: abandon this car the safe way - Spawn destroys its root and keeps stepping C's builder
                    // until the half-built model is disposed (never stop stepping it: that leaks the meshes / texture scope and leaves it drawn)
                    if (!AcAi.StillBuilding(gen)) opts.Handle.Cancel();
                }
                if (!AcAi.StillBuilding(gen)) { if (holder.Rig != null) holder.Rig.Dispose(); yield break; }
                if (holder.Rig == null || holder.Rig.Root == null) { AcLog.Warn("Ai: no car built for " + d.Name + " (" + car.Id + ")"); d.State = AcAiState.Failed; continue; }
                try { Setup(d, holder.Rig, at, yaw, inPits, speed); built++; }
                catch (Exception e) { AcLog.Error("Ai: setting up " + d.Name + " failed: " + e); d.State = AcAiState.Failed; holder.Rig.Dispose(); d.Rig = null; continue; }
                if (summary.Length > 0) summary.Append(", ");
                summary.Append(d.Name).Append(" (").Append(car.Id).Append(string.IsNullOrEmpty(d.SkinId) ? "" : "/" + d.SkinId).Append(")");
                yield return null;
            }
            AcLog.Info("Ai: spawned " + built + " AI cars (level " + level + "): " + summary);
            ClearAdopt();
            AcAi.BuildDone(gen);
        }

        private static void Setup(AcAiDriver d, AcCarRig rig, Vector3 at, float yaw, bool inPits, float speed)
        {
            rig.OwnerId = -(100 + d.Index);
            rig.Root.name = "Ac Ai " + (d.Index + 1) + " " + d.Name + " (" + d.CarId + ")";
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            AcCarPhysics ph = rig.Physics;
            float hw = 0.95f, hl = 2.2f; // ours defaults
            if (rig.Model != null)
            {
                Bounds b = rig.Model.Bounds;
                if (b.extents.x > 0.4f && b.extents.x < 1.6f) hw = b.extents.x;
                if (b.extents.z > 1f && b.extents.z < 4f) hl = b.extents.z;
            }
            d.HalfLength = hl;
            if (ph != null)
            {
                // the AI's aids: the car's factory ABS and TC (assists.ini value 1 - a car without ABS in its data gets none; the AI eases
                // its brake pedal on a locking wheel itself, AcAiDriver.Write), its own gear changes (ai.ini UP/DOWN) [BELIEF: AC's AI aids]
                ph.Assists = new AcAssists { Abs = 1, Tc = 1, Stability = 0, AutoShift = false, AutoClutch = true, AutoBlip = true };
                ph.ResetTo(at, rot);
                ApplyFuel(d, ph);
                AcPhase phase = AcSession.Phase;
                if (phase != AcPhase.Grid && phase != AcPhase.Countdown) ph.SetDriven(true);
                if (speed > 1f && ph.Body != null && !ph.Body.isKinematic) ph.Body.linearVelocity = rot * Vector3.forward * speed;
            }
            else rig.Root.transform.SetPositionAndRotation(at, rot);
            bool pit = inPits && d.PitTrack != null && d.PitPlan != null;
            d.Brain = new AcAiBrain(d.CarData, pit ? d.PitTrack : d.FastTrack, pit ? d.PitPlan : d.FastPlan) { HalfWidth = hw };
            d.OnPitLine = pit;
            d.State = pit ? AcAiState.Pits : AcAiState.Driving;
            d.RailsPos = Vector3.zero; d.RailsSpeed = speed;
            // the master-loss watcher: hands the AI over when this client stops being the master even if nobody ticks AcAi any more
            try { if (rig.Root.GetComponent<AcAiWatch>() == null) rig.Root.AddComponent<AcAiWatch>(); }
            catch (Exception e) { AcLog.WarnOnce("Ai: master-loss watcher not added: " + e.Message); }
            d.Rig = rig; // last: Produce runs from now on
        }

        // ------------------------------------------------------------------ start places

        private static void Place(AcAiDriver d, HashSet<int> usedBoxes, ref int extra, out Vector3 at, out float yaw, out bool inPits, out float speed)
        {
            speed = 0f;
            // a new master: where the previous master's car is now
            if (Adopt[d.Index].Present && Fresh(d.Index) && string.Equals(Adopt[d.Index].CarId, d.CarId, StringComparison.OrdinalIgnoreCase))
            {
                at = Adopt[d.Index].Pos; yaw = Adopt[d.Index].Rot.eulerAngles.y; speed = Adopt[d.Index].Speed;
                inPits = false;
                return;
            }
            // a free pit box from the far end (players take theirs from box 0 up - R's PickPitBox)
            AcSpawnPoint[] boxes = AcTrackWorld.PitBoxes;
            int mine = -1;
            try { mine = AcRace.PitBox; } catch (Exception) { }
            for (int k = boxes.Length - 1; k >= 0; k--)
            {
                if (k == mine || usedBoxes.Contains(k)) continue;
                Vector3 g = Ground(boxes[k].Marker);
                if (Taken(g)) continue;
                usedBoxes.Add(k);
                at = g; yaw = boxes[k].HeadingDeg; inPits = true;
                return;
            }
            // the pit lane itself (30..80 % of it, 12 m apart) // ours
            AcAiTrack pit = AcAi.PitTrack;
            if (pit != null)
            {
                float s = pit.Length * 0.3f + extra * 12f;
                if (s < pit.Length * 0.8f)
                {
                    extra++;
                    AcAiDriver.LinePose(pit, s, out at, out yaw);
                    at = Ground(at);
                    inPits = true;
                    return;
                }
            }
            // the back of the grid
            AcSpawnPoint[] grid = AcTrackWorld.StartGrid;
            for (int k = grid.Length - 1; k >= 0; k--)
            {
                Vector3 g = Ground(grid[k].Marker);
                if (Taken(g)) continue;
                at = g; yaw = grid[k].HeadingDeg; inPits = false;
                return;
            }
            // the line behind the start
            AcAiTrack fast = AcAi.FastTrack;
            AcAiDriver.LinePose(fast, -40f - 12f * extra++, out at, out yaw); // ours
            at = Ground(at);
            inPits = false;
        }

        private static Vector3 Ground(Vector3 p)
        {
            Vector3 g, n;
            try { if (AcTrackWorld.TryGround(p, out g, out n)) return g; } catch (Exception) { }
            return p;
        }

        private static bool Taken(Vector3 g)
        {
            try { if (AcSpawner.Occupied(g, null)) return true; } catch (Exception) { }
            AcCarRig mine = AcLocal.Car;
            if (mine != null && mine.Root != null)
            {
                Vector3 m = mine.Root.transform.position;
                float dx = m.x - g.x, dz = m.z - g.z;
                if (dx * dx + dz * dz < 4f * 4f) return true; // ours: 4 m
            }
            return false;
        }

        private static float RoadGrip()
        {
            try
            {
                AcSurfaces s = AcTrackWorld.Surfaces;
                if (s == null) return 1f;
                float best = 0f;
                for (int i = 0; i < s.Surfaces.Count; i++)
                {
                    AcSurface x = s.Surfaces[i];
                    if (x.IsValidTrack && !x.IsPitlane && x.Friction > best && x.Friction < 1.3f) best = x.Friction;
                }
                return best > 0.5f ? best : 1f;
            }
            catch (Exception) { return 1f; }
        }

        // ------------------------------------------------------------------ hand-over snapshot

        private struct AdoptEntry
        {
            public bool Present;
            public string CarId, SkinId, Name;
            public int Level;
            public Vector3 Pos;
            public Quaternion Rot;
            public float Speed, Seen;
        }

        private static readonly AdoptEntry[] Adopt = new AdoptEntry[AcAi.MaxDrivers + 2];

        /// <summary>Non-master: remembers the master's AI cars (N's AiPlayers) once a frame.</summary>
        internal static void Snapshot()
        {
            IReadOnlyList<AcNetPlayer> ai;
            try { ai = AcNetGame.AiPlayers; } catch (Exception) { return; }
            float now = Time.unscaledTime;
            for (int i = 0; i < ai.Count; i++)
            {
                AcNetPlayer p = ai[i];
                if (p == null || p.AiIndex < 0 || p.AiIndex >= Adopt.Length || !p.OnTrack) continue;
                AcCarRig r = p.Car != null ? p.Car.Rig : null;
                if (r == null || r.Root == null) continue;
                ref AdoptEntry a = ref Adopt[p.AiIndex];
                a.Present = true; a.CarId = p.CarId; a.SkinId = p.SkinId; a.Name = p.Name; a.Level = p.AiLevel;
                a.Pos = r.Root.transform.position; a.Rot = r.Root.transform.rotation; a.Speed = p.SpeedKmh / 3.6f; a.Seen = now;
            }
        }

        /// <summary>
        /// Master (just took over): the previous master's AI cars N still keeps in the "Ac Net Ai" blocks (it holds them up to 10 s until this
        /// AcAi drives - N's note in REQUESTS.md): car / skin by name hash from my catalog, name, level, pose and velocity, on my layout.
        /// </summary>
        internal static void FromNet(AcContent content)
        {
            AcNet net;
            int layout;
            try { net = AcNetGame.Net; layout = AcNetGame.LayoutHash; } catch (Exception) { return; }
            if (net == null || layout == 0 || content == null) return;
            float now = Time.unscaledTime;
            int n = 0;
            for (int i = 0; i < AcAi.MaxDrivers; i++)
            {
                if (Fresh(i)) { n++; continue; } // Observe already saw it drawn here (interpolated pose: better)
                AcNet.AiCar a;
                try { a = net.Ai(i); } catch (Exception) { continue; }
                if (a == null || !a.Present || a.Car.LayoutHash != layout) continue;
                AcCarEntry car = null;
                for (int k = 0; k < content.Cars.Count; k++)
                    if (content.Cars[k].Owned && AcNetWire.NameHash(content.Cars[k].Id) == a.Car.CarHash) { car = content.Cars[k]; break; }
                if (car == null) continue;
                AcSkinEntry skin = null;
                for (int k = 0; car.Skins != null && k < car.Skins.Count; k++)
                    if (AcNetWire.NameHash(car.Skins[k].Id) == a.Car.SkinHash) { skin = car.Skins[k]; break; }
                ref AdoptEntry e = ref Adopt[i];
                e.Present = true; e.CarId = car.Id; e.SkinId = skin != null ? skin.Id : "";
                e.Name = !string.IsNullOrEmpty(a.Name) ? a.Name : (skin != null ? skin.Driver : "");
                e.Level = a.Level;
                e.Pos = new Vector3(a.Car.Pos.X, a.Car.Pos.Y, a.Car.Pos.Z);
                e.Rot = new Quaternion(a.Car.Rot.X, a.Car.Rot.Y, a.Car.Rot.Z, a.Car.Rot.W);
                e.Speed = new Vector3(a.Car.Vel.X, a.Car.Vel.Y, a.Car.Vel.Z).magnitude;
                e.Seen = now;
                n++;
            }
            if (n > 0) AcLog.Info("Ai: " + n + " AI car(s) of the previous master found - continuing them where they are");
        }

        private static bool Fresh(int i) { return i >= 0 && i < Adopt.Length && Adopt[i].Present && Time.unscaledTime - Adopt[i].Seen < 5f; } // ours: 5 s

        internal static int AdoptCount()
        {
            int n = 0;
            for (int i = 0; i < Adopt.Length; i++) if (Fresh(i)) n++;
            return n;
        }

        private static void ClearAdopt() { for (int i = 0; i < Adopt.Length; i++) Adopt[i].Present = false; }

        internal static string LapText(int ms)
        {
            if (ms <= 0) return "-";
            return (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00") + "." + (ms % 1000).ToString("000");
        }
    }
}
