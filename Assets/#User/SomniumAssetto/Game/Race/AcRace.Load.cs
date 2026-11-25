using System;
using System.Collections;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[R, Race] The way onto a track (W builds it, P spawns my car in my pit box) and back out to the hub (Halo HaloMatchControl.Load port).</summary>
    public static partial class AcRace
    {
        /// <summary>A car build that takes longer than this is given up (s). // ours</summary>
        public const float SpawnTimeoutSeconds = 180f; // ours

        private static int _gen;                     // bumps on every load / teardown: work of an older generation stops
        private static Coroutine _loading, _spawning, _leaving;
        private static float _loadStarted;
        private static string _wantCar, _wantSkin;
        // the local car build in flight: run in its OWN coroutine (StepSpawn) that is never stopped - a superseded (_spawnGen) or torn-down
        // (_gen) build is cancelled through its AcCarSpawnHandle and drained, so C's builder disposes the half-built car (StopCoroutine would
        // leave it drawn in the world and leak its meshes / texture scope); the parent routine only waits for it
        private static int _spawnGen;
        private static LocalSpawn _pending;

        private sealed class LocalSpawn
        {
            public int Gen, SpawnGen;
            public readonly AcCarSpawnHandle Handle = new AcCarSpawnHandle();
            public AcCarRig Rig;
            public bool Finished, Taken, GaveUp;
            public bool Stale { get { return Gen != _gen || SpawnGen != _spawnGen || GaveUp; } }
            public void Set(AcCarRig rig) { Rig = rig; }
        }
        private static Action<float, string> _onTrackProgress;

        // ------------------------------------------------------------------ load

        private static void StartLoad(AcLayoutEntry layout, AcRaceCommand c, string by)
        {
            AcGame game = _game ?? AcGame.Current;
            if (game == null) { AcLog.Warn("Race: load before the Dreamport started - ignored"); return; }
            if (AcSession.Phase != AcPhase.Hub || LocalCar != null || AcTrackWorld.Loaded) Teardown("another track", true);
            int gen = ++_gen;
            AcTrackEntry track = game.Content != null ? game.Content.Track(layout.TrackId) : null;
            AcLog.Info("Race: load " + layout.Key + " (" + c.Rules + ") by " + by);
            AcSession.Begin(track, layout, c.Rules.Clone());
            AcSession.StartUtc = c.StartUtc;
            AcSession.SetPhase(AcPhase.Loading);
            game.SetStatus("Loading " + (layout.Name ?? layout.Key) + "...", false);
            if (_onTrackProgress == null) _onTrackProgress = OnTrackProgress;
            _loading = game.StartCoroutine(LoadRoutine(gen, layout));
        }

        private static void OnTrackProgress(float f, string stage)
        {
            AcSession.ReportLoading(0.02f + 0.9f * Mathf.Clamp01(f), stage ?? "");
        }

        private static IEnumerator LoadRoutine(int gen, AcLayoutEntry layout)
        {
            _loadStarted = Time.realtimeSinceStartup;
            AcSession.ReportLoading(0.01f, "Loading " + (layout.Name ?? layout.Key));
            yield return null;

            IEnumerator build = null;
            string error = null;
            try { build = AcTrackWorld.Load(layout, _onTrackProgress); }
            catch (Exception e) { error = e.Message; AcLog.Error("Race: starting the track build failed: " + e); }
            while (build != null)
            {
                if (gen != _gen) yield break;
                bool more;
                object step = null;
                try { more = build.MoveNext(); if (more) step = build.Current; }
                catch (Exception e) { error = e.Message; AcLog.Error("Race: the track build failed: " + e); break; }
                if (!more) break;
                yield return step;
            }
            if (gen != _gen) yield break;
            bool loaded = false;
            try { loaded = AcTrackWorld.Loaded; } catch (Exception) { }
            if (!loaded) { LoadFailed(layout, error ?? "the track did not build (see the Track lines above)"); yield break; }

            // an exception past this point would kill the coroutine and leave Phase = Loading (loading card up) forever: fail cleanly instead
            try { AfterTrackBuilt(layout); }
            catch (Exception e)
            {
                AcLog.Error("Race: preparing the built track failed: " + e);
                LoadFailed(layout, "preparing the track failed (" + e.Message + ")");
                yield break;
            }

            AcSession.ReportLoading(0.93f, "Your car");
            IEnumerator spawn = SpawnRoutine(gen, true);
            while (true)
            {
                if (gen != _gen) yield break;
                bool more;
                try { more = spawn.MoveNext(); }
                catch (Exception e) { AcLog.Error("Race: spawning my car failed: " + e); break; }
                if (!more) break;
                yield return spawn.Current;
            }
            if (gen != _gen) yield break;
            _loading = null;
            try
            {
                if (LocalCar == null) PlayerToPits();     // no car: the player still leaves the hub for the pit lane
                AcSession.ReportLoading(1f, "Ready");
                if (_game != null) _game.SetStatus("On track: " + (layout.Name ?? layout.Key), false);
                EnterCommandPhase(null);
            }
            catch (Exception e)
            {
                AcLog.Error("Race: entering the session after the load failed: " + e);
                if (AcSession.Phase == AcPhase.Loading) AcSession.SetPhase(AcPhase.Practice);   // never stuck behind the loading card
            }
        }

        private static void AfterTrackBuilt(AcLayoutEntry layout)
        {
            Physics.SyncTransforms();
            AcSession.NotifyTrackBuilt(AcTrackWorld.Root);
            AcSpawner.ReadLayout(layout);
            AcTiming.SetupTrack();
            PitBox = PickPitBox();
            AcAiSpline line = AcTrackWorld.AiLine;
            float pole = AcTrackWorld.StartGrid.Length > 0 ? AcTrackWorld.StartGrid[0].Progress : float.NaN;
            AcLog.Info("Race: track built in " + (Time.realtimeSinceStartup - _loadStarted).ToString("0.0") + " s, my pit box " + PitBox + " (of " + AcTrackWorld.PitBoxes.Length
                       + "), grid " + AcTrackWorld.StartGrid.Length + " slots (pole " + (float.IsNaN(pole) ? "?" : pole > 0.5f ? "before the line" : "after the line") + "), hotlap start "
                       + (AcTrackWorld.HotlapStart.Index >= 0 ? "yes" : "no") + ", start lights " + AcTrackWorld.StartLights.Count + ", crew side " + AcSpawner.CrewSide
                       + (line == null || line.IsEmpty ? ", NO fast lane" : "") + (AcTiming.OpenCourse ? ", open course" : ""));
        }

        private static void PlayerToPits()
        {
            AcSpawnPoint[] boxes = AcTrackWorld.PitBoxes;
            if (boxes.Length == 0) return;
            AcSpawnPoint b = boxes[Math.Max(0, Math.Min(PitBox, boxes.Length - 1))];
            bool hit;
            Vector3 g = AcSpawner.GroundUnder(b.Marker, out hit);
            string how;
            bool ok = false;
            try { ok = AcPlayer.Teleport(g + Vector3.up * 0.05f, b.HeadingDeg, out how); } catch (Exception e) { how = e.Message; }
            AcLog.Info("Race: no car - player " + (ok ? "moved" : "NOT moved") + " to pit box " + PitBox + " (" + how + ")");
        }

        private static void LoadFailed(AcLayoutEntry layout, string why)
        {
            AcLog.Warn("Race: loading " + layout.Key + " failed: " + why);
            Toast("Loading " + (layout.Name ?? layout.Key) + " failed: " + why);
            Teardown("load failed", true);
            if (_game != null) _game.SetStatus("Loading " + (layout.Name ?? layout.Key) + " failed: " + why, true);
        }

        // ------------------------------------------------------------------ my car


        /// <summary>Builds my car through P's AcCarRig and puts it where the session wants it (pit box / grid / hotlap start).</summary>
        private static IEnumerator SpawnRoutine(int gen, bool movePlayer)
        {
            AcCarEntry car = PickCar();
            if (car == null)
            {
                AcLog.Warn("Race: no owned car to spawn (choose one on the tablet's Drive page)");
                Toast("Choose a car on the Drive page.");
                yield break;
            }
            string skin = PickSkin(car);
            float t0 = Time.realtimeSinceStartup;
            AcGame host = _game ?? AcGame.Current;
            if (host == null) yield break;
            AbandonPending();
            var s = new LocalSpawn { Gen = gen, SpawnGen = _spawnGen };
            _pending = s;
            AcCarDamageGate.Refresh(AcSession.Rules);        // iteration 2: W / C read it while the model builds
            var opts = new AcCarRigOptions { Kind = AcCarKind.Local, Audio = true, Parent = AcSession.WorldRoot, Handle = s.Handle };
            IEnumerator build = null;
            try { build = AcCarRig.Spawn(car, skin, opts, s.Set); }
            catch (Exception e) { AcLog.Error("Race: AcCarRig.Spawn threw: " + e); }
            if (build == null) { s.Finished = true; }
            else host.StartCoroutine(StepSpawn(build, s, car.Id));
            while (!s.Finished)
            {
                if (s.Stale) yield break;                 // StepSpawn cancels, drains and disposes it
                if (Time.realtimeSinceStartup - t0 > SpawnTimeoutSeconds)
                {
                    AcLog.Warn("Race: my car " + car.Id + " was not built in " + SpawnTimeoutSeconds + " s - giving up");
                    s.GaveUp = true;
                    yield break;
                }
                yield return null;
            }
            if (s.Stale) { DisposeSpawn(s); yield break; }
            s.Taken = true;
            if (ReferenceEquals(_pending, s)) _pending = null;
            AcCarRig rig = s.Rig;
            s.Rig = null;
            if (rig == null || rig.Root == null)
            {
                AcLog.Warn("Race: my car " + car.Id + " could not be built");
                Toast("Your car (" + car.Name + ") could not be built.");
                yield break;
            }

            LocalCar = rig;
            rig.OwnerId = AcLocal.PhotonId;
            AcLocal.Car = rig;              // P's field; its contract: "R assigns Car through P's AcCarRig spawn"
            AcTiming.Local.Rig = rig;
            AcTiming.Local.Id = AcLocal.PhotonId;
            AcTiming.Local.Name = string.IsNullOrEmpty(AcLocal.Nick) ? "You" : AcLocal.Nick;
            AcTiming.Local.Begun = false; AcTiming.Local.OnGrid = false;
            string where = PlaceForSession(rig);
            AcGame.Guard("local car changed", () => AcEvents.RaiseLocalCarChanged(rig));
            AcLog.Info("Race: my car " + car.Id + " (" + (string.IsNullOrEmpty(skin) ? "default skin" : skin) + ") ready in " + (Time.realtimeSinceStartup - t0).ToString("0.0")
                       + " s at " + where + " " + rig.Root.transform.position.ToString("0.0"));
            bool seatedHere = false;
            try { seatedHere = AcSeat.Seated; } catch (Exception) { }
            if (movePlayer && !seatedHere) AcSpawner.PlayerBeside(rig, where);
        }

        /// <summary>Steps P's AcCarRig.Spawn to its end in a coroutine of its own (never stopped): a spawn gone stale meanwhile is cancelled (root
        /// destroyed, C's builder drained - it disposes its half-built model) and whatever still comes back is disposed.</summary>
        private static IEnumerator StepSpawn(IEnumerator build, LocalSpawn s, string carId)
        {
            while (true)
            {
                if (s.Stale && !s.Handle.Cancelled) s.Handle.Cancel();
                bool more;
                object step = null;
                try { more = build.MoveNext(); if (more) step = build.Current; }
                catch (Exception e) { AcLog.Error("Race: building my car " + carId + " failed: " + e); break; }
                if (!more) break;
                yield return step;
            }
            s.Finished = true;
            if (s.Stale) DisposeSpawn(s);
        }

        /// <summary>A finished build nobody takes (superseded, torn down, given up): its car goes.</summary>
        private static void DisposeSpawn(LocalSpawn s)
        {
            if (s == null || s.Taken) return;
            AcCarRig r = s.Rig;
            s.Rig = null;
            if (r != null && r != LocalCar) AcGame.Guard("orphan car dispose", r.Dispose);
            if (ReferenceEquals(_pending, s)) _pending = null;
        }

        /// <summary>The build in flight is superseded: cancelled (it drains in StepSpawn), or its finished car - not taken yet - disposed.</summary>
        private static void AbandonPending()
        {
            LocalSpawn s = _pending;
            if (s == null || s.Taken) { _pending = null; return; }
            s.GaveUp = true;                               // stale from now on
            if (!s.Finished) s.Handle.Cancel();            // StepSpawn drains and disposes
            else DisposeSpawn(s);
            _pending = null;
        }

        private static bool CarAllowed(string carId)
        {
            AcRules r = AcSession.Rules;
            if (r == null || r.AllowedCars == null || r.AllowedCars.Count == 0) return true;
            for (int i = 0; i < r.AllowedCars.Count; i++) if (string.Equals(r.AllowedCars[i], carId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static AcCarEntry PickCar()
        {
            AcContent content = _game != null ? _game.Content : null;
            if (content == null) return null;
            string id = !string.IsNullOrEmpty(_wantCar) ? _wantCar : AcLocal.CarId;
            AcCarEntry c = string.IsNullOrEmpty(id) ? null : content.Car(id);
            if (c != null && c.Owned && !c.IsDlcStub && CarAllowed(c.Id)) return c;
            AcCarEntry pick = null;
            AcRules r = AcSession.Rules;
            if (r != null && r.AllowedCars != null)
                for (int i = 0; i < r.AllowedCars.Count && pick == null; i++)
                {
                    AcCarEntry a = content.Car(r.AllowedCars[i]);
                    if (a != null && a.Owned && !a.IsDlcStub) pick = a;
                }
            if (pick == null)
                for (int i = 0; i < content.Cars.Count && pick == null; i++)
                    if (content.Cars[i].Owned && !content.Cars[i].IsDlcStub && CarAllowed(content.Cars[i].Id)) pick = content.Cars[i];
            if (pick != null)
                AcLog.Info("Race: " + (string.IsNullOrEmpty(id) ? "no car chosen yet" : "car '" + id + "' " + (c == null ? "not found" : !c.Owned || c.IsDlcStub ? "not installed" : "not allowed by the rules"))
                           + " - using " + pick.Id);
            return pick;
        }

        private static string PickSkin(AcCarEntry car)
        {
            string s = null;
            if (!string.IsNullOrEmpty(_wantCar) && _wantCar == car.Id) s = _wantSkin;
            else if (AcLocal.CarId == car.Id) s = AcLocal.SkinId;
            if (!string.IsNullOrEmpty(s) && car.Skin(s) != null) return s;
            return car.Skins.Count > 0 ? car.Skins[0].Id : "";
        }

        /// <summary>Where my car goes for the session state right now; returns a word for the log.</summary>
        private static string PlaceForSession(AcCarRig rig)
        {
            if (AcSession.InRace && _cmd != null && !Spectating && AcTiming.Local.GridSlot >= 0 && AcSession.Phase <= AcPhase.Countdown)
            {
                GridPlace(AcTiming.Local, AcTiming.Local.GridSlot);
                return "grid slot " + AcTiming.Local.GridSlot;
            }
            return PlaceInPits(rig, null);
        }

        /// <summary>My pit box (or the hotlap start in a hotlap session when free); timing restarts with an out lap. Returns where.</summary>
        private static string PlaceInPits(AcCarRig rig, string why)
        {
            if (rig == null) return "nowhere";
            AcSpawnPoint sp;
            string where;
            if (!MySpawnPoint(rig, out sp, out where)) { AcLog.Warn("Race: the layout has no pit box / grid slot - my car stays where it was built"); return "its build position"; }
            AcSpawner.PlaceAt(rig, sp);
            try { if (rig.Physics != null) rig.Physics.Freeze(Spectating); } catch (Exception e) { AcLog.Warn("Race: Freeze: " + e.Message); }
            AcTiming.Teleported(AcTiming.Local, true);
            if (why != null)
            {
                AcLog.Info("Race: " + why + " -> " + where);
                bool seatedInIt = false;
                try { seatedInIt = AcSeat.Seated && AcSeat.SeatedCar == rig; } catch (Exception) { }
                if (!seatedInIt && !AcSeat_SeatedAnywhere()) AcSpawner.PlayerBeside(rig, why);
            }
            return where;
        }

        private static bool AcSeat_SeatedAnywhere() { try { return AcSeat.Seated; } catch (Exception) { return false; } }

        private static bool MySpawnPoint(AcCarRig rig, out AcSpawnPoint sp, out string where)
        {
            sp = default(AcSpawnPoint); where = "";
            AcRules r = AcSession.Rules;
            AcSpawnPoint hot = AcTrackWorld.HotlapStart;
            if (r != null && r.Session == AcSessionType.Hotlap && hot.Index >= 0 && !Spectating)
            {
                bool hit;
                if (!AcSpawner.Occupied(AcSpawner.GroundUnder(hot.Marker, out hit), rig)) { sp = hot; where = "the hotlap start"; return true; }
            }
            AcSpawnPoint[] boxes = AcTrackWorld.PitBoxes;
            if (PitBox >= 0 && PitBox < boxes.Length) { sp = boxes[PitBox]; where = "pit box " + PitBox; return true; }
            AcSpawnPoint[] grid = AcTrackWorld.StartGrid;
            if (grid.Length > 0) { sp = grid[grid.Length - 1]; where = "the last grid slot (no pit box)"; return true; }
            return false;
        }

        /// <summary>My pit box: my rank among the instance's player slots (join order of slots, ours) modulo the box count, skipping boxes
        /// another car already stands in.</summary>
        private static int PickPitBox()
        {
            AcSpawnPoint[] boxes = AcTrackWorld.PitBoxes;
            int n = boxes.Length;
            if (n == 0) return -1;
            int rank = 0;
            if (AcLocal.NetSlot >= 0)
            {
                try
                {
                    var players = AcNetGame.Players;
                    for (int i = 0; i < players.Count; i++) if (players[i] != null && players[i].Slot >= 0 && players[i].Slot < AcLocal.NetSlot) rank++;
                }
                catch (Exception) { }
            }
            int start = rank % n;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n;
                bool hit;
                if (!AcSpawner.Occupied(AcSpawner.GroundUnder(boxes[i].Marker, out hit), null)) return i;
            }
            return start;
        }

        private static void RespawnLocalCar(string why)
        {
            AcLog.Info("Race: respawning my car (" + why + ")");
            DisposeLocalCar();
            if (_game == null) return;
            // the build in flight (if any) is superseded, not stopped: StepSpawn cancels and drains it (no half-built car left, nothing leaked)
            _spawnGen++;
            AbandonPending();
            _spawning = _game.StartCoroutine(RespawnRoutine(_gen));
        }

        private static IEnumerator RespawnRoutine(int gen)
        {
            int sgen = _spawnGen;
            IEnumerator spawn = SpawnRoutine(gen, true);
            while (true)
            {
                if (gen != _gen || sgen != _spawnGen) yield break;   // (the build itself runs in StepSpawn and cleans up after itself)
                bool more;
                try { more = spawn.MoveNext(); }
                catch (Exception e) { AcLog.Error("Race: respawn failed: " + e); break; }
                if (!more) break;
                yield return spawn.Current;
            }
            if (sgen == _spawnGen) _spawning = null;
            if (gen == _gen && sgen == _spawnGen && LocalCar != null && AcSession.OnTrack && !AcSession.InRace) AcTiming.Begin(AcTiming.Local, AcTimerMode.Timed, AcSpawner.CarClock(LocalCar));
        }

        private static void DisposeLocalCar()
        {
            AcCarRig r = LocalCar;
            if (r == null) return;
            CancelRepair("car removed");
            LocalCar = null;
            AcTiming.Local.Rig = null; AcTiming.Local.Begun = false; AcTiming.Local.OnGrid = false;
            try { if (AcSeat.Seated && AcSeat.SeatedCar == r) AcSeat.ForceExit(); } catch (Exception e) { AcLog.Error("Race: ForceExit: " + e); }
            if (AcLocal.Car == r) AcLocal.Car = null;
            AcGame.Guard("local car changed", () => AcEvents.RaiseLocalCarChanged(null));
            AcGame.Guard("car dispose", r.Dispose);
        }

        // ------------------------------------------------------------------ out

        private static void GoHub()
        {
            if (AcSession.Phase == AcPhase.Hub && LocalCar == null) return;
            if (_leaving != null || _game == null) return;
            AcGame.Guard("loading leave", AcLoading.Leave);
            _leaving = _game.StartCoroutine(LeaveSoon(_gen));
        }

        private static IEnumerator LeaveSoon(int gen)
        {
            float until = Time.realtimeSinceStartup + 0.4f; // ours: the front's fade to black
            while (Time.realtimeSinceStartup < until) yield return null;
            _leaving = null;
            if (gen != _gen) yield break;
            Teardown("the master went back to the hub", true);
        }

        /// <summary>Everything of the session goes; <paramref name="toHub"/> = the player is put back into the hub.</summary>
        private static void Teardown(string why, bool toHub)
        {
            _gen++;
            AcGame g = _game ?? AcGame.Current;
            if (g != null)
            {
                if (_loading != null) { try { g.StopCoroutine(_loading); } catch (Exception) { } }
                if (_spawning != null) { try { g.StopCoroutine(_spawning); } catch (Exception) { } }
                if (_leaving != null) { try { g.StopCoroutine(_leaving); } catch (Exception) { } }
            }
            _loading = null; _spawning = null; _leaving = null;
            AbandonPending();                              // a car build in flight is cancelled and drained (StepSpawn), never left half-built
            bool loaded = false;
            try { loaded = AcTrackWorld.Loaded || AcTrackWorld.Root != null; } catch (Exception) { }
            bool had = AcSession.Phase != AcPhase.Hub || LocalCar != null || loaded;
            if (!had) return;
            AcLog.Info("Race: leaving the track (" + why + ")");
            try { if (AcSeat.Seated) AcSeat.ForceExit(); } catch (Exception e) { AcLog.Error("Race: ForceExit: " + e); }
            DisposeLocalCar();
            AcGame.Guard("ai clear", AcAi.Clear);
            LightsOff();
            AcSession.Close();
            AcGame.Guard("track unload", AcTrackWorld.Unload);
            AcTiming.Clear();
            StandingList.Clear();
            PitBox = -1; Spectating = false; _sessionKey = null; _firstFinishUtc = 0;
            if (toHub)
            {
                string how;
                bool moved = false;
                try { moved = AcPlayer.Teleport(AcSpace.RoomOrigin + new Vector3(0f, 0.9f, 0f), 0f, out how); } // ours: hub spawn height (scene spawn y 3000.9)
                catch (Exception e) { how = e.Message; }
                AcLog.Info("Race: hub (" + (moved ? how : "not moved: " + how) + ")");
            }
            AcSession.SetPhase(AcPhase.Hub);
            bool cardUp = false;
            try { cardUp = AcLoading.Showing; } catch (Exception) { }
            if (cardUp) AcGame.Guard("loading hide", AcLoading.Hide);
            if (g != null && toHub) g.SetStatus("Back in the hub.", false);
            try { Resources.UnloadUnusedAssets(); } catch (Exception) { }
        }
    }
}
