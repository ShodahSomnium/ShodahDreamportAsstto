using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// Lab scenarios of module W (World / track). Run: <c>bash DevTools/lab.sh world-magione</c>, <c>world-monza</c>, <c>world-highlands</c>
    /// (models.ini ROTATION -17 on the start-light file), <c>world-nurburgring</c> (ROTATION 180), or
    /// <c>world-track -actrack ks_vallelunga -aclayout club_circuit</c>. Each builds the layout with AcTrackWorld.Load (as R does), then:
    /// checks the marker counts against the install (research 2.1), that a ray down from every AC_START_n / AC_PIT_n hits a TAGGED track
    /// collider on layer 14 within 8 m (T1), that the pit boxes stand on an IS_PITLANE surface where the layout has one, that the avatar
    /// layer (2) collides with the track layer (14), that start-light lamp 0 (its PANEL - the KS_START_LIGHT mesh node sits at its FBX
    /// origin, only the vertices tell where the lamp is) is ahead of the pole and faces it (T5, models.ini ROTATION), the worst load frame
    /// after the first second (monza: at most 50 ms, brief #8) and that Unity's allocated memory returns near the level before the load
    /// (brief #6); takes pictures from the grid (AC_START_0, eye 1.1 m above the road, along the heading), from above, of a tree / grass
    /// billboard close up (UV orientation, research C2/T4), of the start-light panel seen from the grid with the lights off / 3 red / out
    /// at the green, then unloads. The camera uses the far plane the track gives the player's camera (AcTrackWorld.WantFar), not a lab value.
    /// LOOK at the pictures in DevTools/lab/&lt;scenario&gt;/.
    /// </summary>
    [InitializeOnLoad]
    static class AcLabWorld
    {
        static AcLabWorld()
        {
            // expected counts: research 2.1 / harness wlamps (grid, pits, gates, start lights; -1 = not checked)
            AcLabRunner.Register("world-magione", r => Run(r, "magione", "", 18, 18, 2, 4, false));
            AcLabRunner.Register("world-monza", r => Run(r, "monza", "", 26, 26, 3, 5, true));
            AcLabRunner.Register("world-highlands", r => Run(r, "ks_highlands", "layout_int", 24, -1, -1, 5, false));
            AcLabRunner.Register("world-nurburgring", r => Run(r, "ks_nurburgring", "layout_gp_a", 24, -1, -1, 5, false));
            // drag strip: no KS_START_LIGHT, a semaphore.ini start tree (3 READY steps); 2 grid slots
            AcLabRunner.Register("world-drag", r => Run(r, "ks_drag", "drag1000", 2, -1, -1, 3, false));
            AcLabRunner.Register("world-track", r => Run(r, AcLabBoot.Arg("-actrack", "magione"), AcLabBoot.Arg("-aclayout", ""), -1, -1, -1, -1, false));
        }

        static IEnumerator Run(AcLabRunner r, string track, string layoutId, int grid, int pits, int gates, int lights, bool frameCheck)
        {
            yield return r.Wait(() => r.Game.Content != null, 120f, "the content catalog");
            if (r.Failed) yield break;
            var t = r.Game.Content.Track(track);
            AcLayoutEntry layout = null;
            if (t != null)
            {
                if (!string.IsNullOrEmpty(layoutId)) layout = t.Layout(layoutId);
                else for (int i = 0; i < t.Layouts.Count && layout == null; i++) if (t.Layouts[i].Owned) layout = t.Layouts[i];
            }
            if (!r.Check(layout != null && layout.Owned, "layout " + track + "/" + layoutId + " is installed")) yield break;

            // the hub (showroom kn5 + showroom car) builds right after boot: let it finish, else its memory lands inside the load / unload
            // measurement (first lab run: 145 MB "left" after the unload were the hub's abarth500 + showroom built during the track load)
            float h0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - h0 < 60f && !(AcHub.Current != null && AcHub.Current.ShowroomBuilt && AcHub.Current.Car != null && AcHub.Current.Car.Model != null)) yield return null;
            yield return r.Frames(30);
            AcLabRunner.Log("hub settled after " + (Time.realtimeSinceStartup - h0).ToString("0.0") + " s (showroom " + (AcHub.Current != null && AcHub.Current.ShowroomBuilt) + ", car " + (AcHub.Current != null && AcHub.Current.Car != null) + ")");

            AcLodDriver.ViewerOverride = r.Cam.transform;
            float t0 = Time.realtimeSinceStartup;
            string lastStage = null;
            r.Game.StartCoroutine(AcTrackWorld.Load(layout, (f, s) => { if (s != lastStage && (s.StartsWith("Building collision") || s == "Ready" || s.StartsWith("Failed"))) { lastStage = s; AcLabRunner.Log("load " + (f * 100f).ToString("0") + "% " + s); } }));
            yield return r.Wait(() => AcTrackWorld.Loaded || (!AcTrackWorld.Loading && AcTrackWorld.LoadError != null), 600f, "the track build");
            if (!r.Check(AcTrackWorld.Loaded, "track built in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s" + (AcTrackWorld.LoadError != null ? " (" + AcTrackWorld.LoadError + ")" : ""))) yield break;

            // brief #8: no main-thread frame over 50 ms after the first second (monza); every frame of Load is sampled
            string frames = "worst load frame after 1 s: " + AcTrackWorld.WorstFrameMs.ToString("0") + " ms (" + AcTrackWorld.WorstFrameStage + ")";
            if (frameCheck) r.Check(AcTrackWorld.WorstFrameMs <= 50f, frames + " <= 50 ms"); else AcLabRunner.Log(frames);

            // the far plane the product gives the player's camera (not a lab override): shows what a client would clip
            Camera head = AcPlayer.Head();
            AcLabRunner.Log("far plane: track wants " + AcTrackWorld.WantFar.ToString("0") + " m, player camera " + (head != null ? head.farClipPlane.ToString("0") + " m" : "none in the lab") +
                            ", drawn bounds " + AcTrackWorld.DrawnBounds.size.ToString("0"));
            r.Cam.farClipPlane = head != null ? head.farClipPlane : (AcTrackWorld.WantFar > 0f ? AcTrackWorld.WantFar : 1000f);

            if (grid >= 0) r.Check(AcTrackWorld.StartGrid.Length == grid, "grid slots " + AcTrackWorld.StartGrid.Length + " (expected " + grid + ")");
            if (pits >= 0) r.Check(AcTrackWorld.PitBoxes.Length == pits, "pit boxes " + AcTrackWorld.PitBoxes.Length + " (expected " + pits + ")");
            if (gates >= 0) r.Check(AcTrackWorld.TimeGates.Length == gates, "timing gates " + AcTrackWorld.TimeGates.Length + " (expected " + gates + ")");
            if (lights >= 0) r.Check(AcTrackWorld.StartLights.Count == lights, "start lights " + AcTrackWorld.StartLights.Count + " (expected " + lights + ")");
            r.Check(AcTrackWorld.AiLine != null, "fast lane present (" + (AcTrackWorld.AiLine != null ? AcTrackWorld.AiLine.Count + " points" : "none") + ")");
            r.Check(!Physics.GetIgnoreLayerCollision(AcLayers.Avatar, AcLayers.Track), "avatar layer 2 collides with track layer 14");
            r.Check(AcSurfaceTag.Count > 0, "surface tags registered: " + AcSurfaceTag.Count);

            // every spawn marker stands over a tagged track surface (markers float 0-7.7 m: research T1)
            int miss = 0, pitlane = 0; float maxDrop = 0f;
            bool layoutHasPitlane = false;
            foreach (var s in AcTrackWorld.Surfaces.Surfaces) if (s.IsPitlane) layoutHasPitlane = true;
            var all = new List<AcSpawnPoint>(AcTrackWorld.StartGrid);
            int gridCount = all.Count;
            all.AddRange(AcTrackWorld.PitBoxes);
            for (int i = 0; i < all.Count; i++)
            {
                RaycastHit hit;
                bool ok = Physics.Raycast(all[i].Marker + Vector3.up, Vector3.down, out hit, 10f, 1 << AcLayers.Track, QueryTriggerInteraction.Ignore);
                var surf = ok ? AcTrackWorld.SurfaceAt(hit) : null;
                if (!ok || surf == null) { miss++; AcLabRunner.Log("  no tagged surface under " + (i < gridCount ? "AC_START_" : "AC_PIT_") + all[i].Index + (ok ? " (hit " + hit.collider.name + ")" : "")); continue; }
                maxDrop = Mathf.Max(maxDrop, all[i].Marker.y - hit.point.y);
                if (i >= gridCount && surf.IsPitlane) pitlane++;
            }
            r.Check(miss == 0, "a tagged surface under every grid slot / pit box (" + (all.Count - miss) + "/" + all.Count + ", highest marker " + maxDrop.ToString("0.00") + " m above the road)");
            if (layoutHasPitlane) r.Check(pitlane > 0, "pit boxes on an IS_PITLANE surface: " + pitlane + "/" + AcTrackWorld.PitBoxes.Length);
            Vector3 g; Vector3 nrm;
            if (AcTrackWorld.StartGrid.Length > 0) r.Check(AcTrackWorld.TryGround(AcTrackWorld.StartGrid[0].Marker, out g, out nrm), "TryGround under the pole slot");

            // pictures
            if (AcTrackWorld.StartGrid.Length > 0)
            {
                var pole = AcTrackWorld.StartGrid[0];
                Vector3 ground = pole.Marker;
                AcTrackWorld.TryGround(pole.Marker, out ground, out nrm);
                Vector3 eye = ground + Vector3.up * 1.1f;
                Vector3 fwd = pole.Rotation * Vector3.forward;
                r.Aim(eye, eye + fwd * 50f, 75f);
                yield return Settle();
                r.Shot("grid");
                r.Aim(eye - fwd * 8f + Vector3.up * 2f, eye + fwd * 30f, 75f);
                yield return Settle();
                r.Shot("grid_behind");
                r.Aim(ground + Vector3.up * 120f - fwd * 60f, ground + fwd * 40f, 70f);
                yield return Settle();
                r.Shot("above");
                r.Aim(ground + Vector3.up * 400f - fwd * 300f, ground + fwd * 600f, 70f);
                yield return Settle();
                r.Shot("far_view");   // far side of the track with the player's far plane

                // the start-light panel seen from the grid (T5: models.ini ROTATION), lights off / 3 red / green. harness wlamps: magione 49 m,
                // monza 42 m (+ a repeater 70 m behind facing the back of the grid), highlands 38 m (rot -17), nurburgring 71 m (rot 180), all facing the pole
                Vector3 pc, pn;
                if (AcTrackWorld.StartLights.Count > 0 && AcTrackWorld.StartLights.PanelSeenFrom(0, eye, out pc, out pn))
                {
                    Vector3 to = pc - eye; Vector3 toH = new Vector3(to.x, 0f, to.z);
                    float ahead = Vector3.Dot(toH.normalized, fwd);
                    float facing = pn.sqrMagnitude > 1e-8f ? Vector3.Dot(pn.normalized, -to.normalized) : 0f;
                    bool tree = AcTrackWorld.StartLights.IsSemaphore;   // drag / A2B tree: lamp normals unknown, only "ahead" is checked
                    r.Check(tree ? ahead > 0f : ahead > 0.5f && facing > 0.5f, (tree ? "start tree READY step 0 " : "start light 0 panel ") + toH.magnitude.ToString("0") + " m from the pole, ahead-dot " + ahead.ToString("0.00") + ", faces the pole " + facing.ToString("0.00") +
                            " (" + AcTrackWorld.StartLights.Panels(0).Length + " panel(s); T5 models.ini ROTATION)");
                    Vector3 from = ground + Vector3.up * 1.5f;
                    r.Aim(from, pc, 20f);
                    AcTrackWorld.StartLights.SetLights(0, false);
                    yield return Settle();
                    r.Shot("lights_off");
                    AcTrackWorld.StartLights.SetLights(3, false);
                    yield return r.Frames(2);
                    r.Shot("lights_3red");
                    AcTrackWorld.StartLights.SetLights(0, true);
                    yield return r.Frames(2);
                    r.Shot("lights_out_green");
                    AcTrackWorld.StartLights.SetLights(AcTrackWorld.StartLights.Count, false);
                    r.Aim(eye - fwd * 6f + Vector3.up * 1.5f, pc, 45f);
                    yield return r.Frames(2);
                    r.Shot("lights_all_red_wide");
                    if (tree)
                    {
                        AcTrackWorld.StartLights.SetLights(0, false);
                        AcTrackWorld.StartLights.SetJumpStart(true);
                        yield return r.Frames(2);
                        r.Shot("lights_jumpstart");
                        AcTrackWorld.StartLights.SetJumpStart(false);
                    }
                    AcTrackWorld.StartLights.SetLights(0, false);
                }
                else AcLabRunner.Log("no start-light panel built (start lights " + AcTrackWorld.StartLights.Count + ")");
            }

            // a tree / grass billboard close up (UV orientation: trunks down, tips up)
            Renderer bill = FindByShader("ksTree") ?? FindByShader("ksGrass") ?? FindByShader("ksPerPixelAT");
            if (bill != null)
            {
                var b = bill.bounds;
                Vector3 c = b.center;
                float d = Mathf.Clamp(b.extents.magnitude * 0.6f, 6f, 40f);
                Vector3 from = c + new Vector3(d, 0.2f * d, d * 0.3f);
                RaycastHit gh;
                if (Physics.Raycast(from + Vector3.up * 50f, Vector3.down, out gh, 200f, 1 << AcLayers.Track)) from.y = Mathf.Max(from.y, gh.point.y + 1.7f);
                r.Aim(from, c, 60f);
                yield return Settle();
                r.Shot("billboard_" + bill.sharedMaterial.name.Replace(' ', '_'));
            }
            else AcLabRunner.Log("no tree / grass renderer found");

            // unload: everything gone, memory back near the level before the load (brief #6)
            int beforeScopes = AcMaterials.ScopeCount;
            long memBefore = AcTrackWorld.MemoryBeforeLoad, memBuilt = AcTrackWorld.MemoryBuilt;
            AcTrackWorld.Unload();
            yield return r.Frames(3);
            r.Check(AcTrackWorld.Root == null && !AcTrackWorld.Loaded, "unloaded");
            r.Check(AcSurfaceTag.Count == 0, "surface tags cleared (" + AcSurfaceTag.Count + ")");
            r.Check(AcMaterials.ScopeCount < beforeScopes || beforeScopes == 0, "track material scope released (" + beforeScopes + " -> " + AcMaterials.ScopeCount + ")");
            yield return r.Wait(() => AcTrackWorld.MemoryAfterUnload != 0, 10f, "the memory reading after the unload");
            long after = AcTrackWorld.MemoryAfterUnload;
            long grew = memBuilt - memBefore, left = after - memBefore;
            // // ours: back within a quarter of what the track took, or 64 MB
            r.Check(after != 0 && left <= Math.Max(64L << 20, grew / 4), "allocated memory " + (memBefore >> 20) + " MB before, " + (memBuilt >> 20) + " MB built, " + (after >> 20) + " MB after the unload (" +
                    (left >> 20) + " MB left of " + (grew >> 20) + " MB)");
            AcLodDriver.ViewerOverride = null;
        }

        static IEnumerator Settle()
        {
            foreach (var l in UnityEngine.Object.FindObjectsByType<AcLodDriver>(FindObjectsSortMode.None)) l.SweepAll();
            for (int i = 0; i < 3; i++) yield return null;
        }

        static Renderer FindByShader(string kn5Shader)
        {
            var root = AcTrackWorld.Root;
            if (root == null) return null;
            Renderer best = null; float bestSize = 0f;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r.sharedMaterial == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null) continue;
                // the kn5 shader is not on the Unity material; match by the material name AcMaterials gives (the kn5 material name) and the cutout queue
                bool cut = r.sharedMaterial.renderQueue == AcMaterials.QueueCutout;
                string n = r.sharedMaterial.name.ToLowerInvariant();
                bool want = kn5Shader == "ksTree" ? cut && n.Contains("tree") : kn5Shader == "ksGrass" ? cut && n.Contains("grass") : cut;
                if (!want) continue;
                float s = r.bounds.size.magnitude;
                if (s > 3f && s < 200f && (best == null || Mathf.Abs(s - 25f) < Mathf.Abs(bestSize - 25f))) { best = r; bestSize = s; }
            }
            return best;
        }
    }
}
