using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [integration, iteration 2] The whole damage path in ONE scene (DAMAGE_DESIGN.md 1.3 -> 1.7 -> 2 -> 3 -> 5 -> 7 -> 1.6), the seams no module lab
    /// covers alone: a real PhysX crash into an untagged wall -> P's zone km/h -> CarDamaged -> P's push into C's visuals (paint clones, glass,
    /// panels) + aero / toe + H's displayer timer -> the wire pair (ToWire) -> a second rig built as a REMOTE puppet applies it (ApplyWire, the
    /// remote client's path) and draws the same ZoneByte state -> Repair(All) clears both. DAMAGE 0 first: nothing at all.
    /// Run: bash DevTools/lab.sh damage-e2e [-accar ferrari_458]. Pictures in DevTools/lab/damage-e2e/.
    /// Not here (own labs): the timed pit-box repair and DNF (race-repair), the GARAGE rows (front-garage-repair), two real net clients (N's
    /// offline NetTest loopback), engine life (physics-engine), forced per-zone visuals on 4 cars (cars-damage), the G0 glass fix (cars-glassfix).
    /// </summary>
    [InitializeOnLoad]
    static class AcLabDamage
    {
        static AcLabDamage() { AcLabRunner.Register("damage-e2e", r => EndToEnd(r)); }

        private static readonly Vector3 PlaneCentre = new Vector3(-5000f, 0f, 5000f); // ours: away from tracks and AcLabPhysics' plane
        private const float WallZ = 300f;                                              // ours: plane-local z of the wall face
        private static GameObject _plane, _wall;

        static string F(float v, string fmt) { return v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }
        static void Log(string s) { AcLabRunner.Log(s); }

        static void BuildStage()
        {
            if (_plane != null) return;
            _plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _plane.name = "Ac Lab Damage Plane";
            _plane.layer = AcLayers.Track;
            _plane.transform.position = PlaneCentre + Vector3.down * 0.5f;
            _plane.transform.localScale = new Vector3(300f, 1f, 1000f);
            AcSurfaceTag.Register(_plane.GetComponent<Collider>(), new AcSurface { Key = "ROAD", Friction = 1f }, false); // a road: no damage
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mr = _plane.GetComponent<MeshRenderer>();
            if (mr != null && sh != null) mr.sharedMaterial = new Material(sh) { color = new Color(0.35f, 0.36f, 0.38f) };
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "Ac Lab Damage Wall";
            _wall.layer = AcLayers.Track;                    // untagged = a wall (AcSurfaceTag.Of == null) -> damage
            _wall.transform.position = PlaneCentre + new Vector3(0f, 1f, WallZ + 0.5f);
            _wall.transform.localScale = new Vector3(80f, 2f, 1f);
            var light = new GameObject("Ac Lab Damage Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            light.intensity = 1.3f;
        }

        static IEnumerator Spawn(AcLabRunner r, AcCarEntry car, AcCarKind kind, Action<AcCarRig> got)
        {
            AcCarRig rig = null; bool done = false;
            yield return AcCarRig.Spawn(car, car.Skins.Count > 0 ? car.Skins[0].Id : "", new AcCarRigOptions { Kind = kind, Audio = false }, x => { rig = x; done = true; });
            yield return r.Wait(() => done, 120f, "AcCarRig.Spawn " + car.Id + " (" + kind + ")");
            got(rig);
        }

        /// <summary>As AcLabPhysics.PushInto: the car (heading <paramref name="yaw"/>) 1 m short of the wall at plane x <paramref name="x"/>,
        /// settled, then its body velocity set toward the wall every fixed step until the damage moved or 2 s passed.</summary>
        static IEnumerator PushInto(AcLabRunner r, AcCarPhysics ph, float yaw, float kmh, float x)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Bounds box = ph.DamageBox;
            float halfZ = Mathf.Abs((rot * new Vector3(box.extents.x, 0f, 0f)).z) + Mathf.Abs((rot * new Vector3(0f, 0f, box.extents.z)).z);
            Vector3 at = PlaneCentre + new Vector3(x, 0f, WallZ - halfZ - (rot * box.center).z - 1.0f);
            ph.ResetTo(at + Vector3.up * 2f, rot);
            ph.Input = AcCarInput.Neutral;
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 0.8f, 10f, "settle before the hit");
            int v0 = ph.Damage.MechVersion;
            Vector3 vel = Vector3.forward * (kmh / 3.6f);
            float tHit = Time.time;
            while (ph.Damage.MechVersion == v0 && Time.time - tHit < 2f)
            {
                ph.Body.linearVelocity = vel; ph.Body.angularVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();
            }
            t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 0.6f, 10f, "after the hit");
        }

        static string Zones(AcCarDamage d) { return F(d.Zone[0], "0.0") + "/" + F(d.Zone[1], "0.0") + "/" + F(d.Zone[2], "0.0") + "/" + F(d.Zone[3], "0.0") + "/" + F(d.Zone[4], "0.0"); }

        static string Glass(AcCarDamageVisual v)
        {
            if (v == null) return "-";
            string s = "";
            for (int z = 0; z < 5; z++) s += v.GlassBroken(z) ? "X" : ".";
            return s;
        }

        static int PanelsBent(AcCarModel m, out float maxF)
        {
            maxF = 0f; int n = 0;
            if (m == null || m.DamageVisual == null || m.ActiveSet == null || m.ActiveSet.Damage == null) return 0;
            for (int i = 0; i < m.ActiveSet.Damage.Panels.Count; i++)
            {
                float f = m.DamageVisual.PanelFactor(m.ActiveSet.Damage.Panels[i].Def);
                if (f > 0f) n++;
                if (f > maxF) maxF = f;
            }
            return n;
        }

        /// <summary>C's visual state equals the damage state's ZoneByte values (the owner / remote quantisation rule).</summary>
        static bool VisualMatches(AcCarModel m, AcCarDamage d, out string why)
        {
            why = "";
            if (m == null || m.DamageVisual == null) { why = "no damage visual"; return false; }
            bool ok = true;
            for (int z = 0; z < 5; z++)
            {
                float v = m.DamageVisual.Zone(z);
                if (Mathf.Abs(v - d.ZoneByte(z)) > 0.001f) ok = false;
                why += (z > 0 ? "/" : "") + F(v, "0");
            }
            why = "C zones " + why + " vs ZoneByte " + d.ZoneByte(0) + "/" + d.ZoneByte(1) + "/" + d.ZoneByte(2) + "/" + d.ZoneByte(3) + "/" + d.ZoneByte(4);
            return ok;
        }

        /// <summary>Moves the car 40 m back from the wall (ResetTo keeps the damage), lets it settle, then two pictures (front 3/4 left, right side).</summary>
        static IEnumerator Shots(AcLabRunner r, AcCarRig rig, string name)
        {
            rig.Physics.ResetTo(PlaneCentre + new Vector3(-30f, 2f, WallZ - 40f), Quaternion.identity);
            float t0 = Time.time;
            yield return r.Wait(() => Time.time - t0 > 1.0f, 10f, "settle for the pictures");
            Transform t = rig.Root.transform;
            Vector3 c = t.position + Vector3.up * 0.5f;
            r.Aim(c + t.forward * 5.5f + t.right * -3.0f + Vector3.up * 1.4f, c, 45f); r.Shot(name + "-front34");
            r.Aim(c + t.right * 5.5f + t.forward * 1.0f + Vector3.up * 1.0f, c, 45f); r.Shot(name + "-right");
        }

        static IEnumerator EndToEnd(AcLabRunner r)
        {
            yield return r.Wait(() => r.Game != null && r.Game.Content != null, 180f, "the content catalog");
            if (r.Failed) yield break;
            string id = AcLabBoot.Arg("-accar", "ferrari_458");
            AcCarEntry car = r.Game.Content.Car(id);
            if (car == null || !car.Owned) { r.Fail("car '" + id + "' not owned / not found"); yield break; }
            BuildStage();

            // R's gate as a DAMAGE 100 session sets it before a spawn (W / C read it at build)
            AcCarDamageGate.Refresh(new AcRules { DamageMultiplier = 100 });
            r.Check(AcCarDamageGate.TexturesWanted, "gate: damage textures wanted at DAMAGE 100 with VISUAL DAMAGE on");
            AcCarRig rig = null, remote = null;
            yield return Spawn(r, car, AcCarKind.Local, x => rig = x);
            if (!r.Check(rig != null && rig.Physics != null && rig.Model != null, "local rig with physics and model (" + car.Id + ")")) yield break;
            yield return Spawn(r, car, AcCarKind.Remote, x => remote = x);
            if (!r.Check(remote != null && remote.Physics != null && remote.Physics.Puppet && remote.Model != null, "remote rig (puppet) with model")) yield break;
            AcCarPhysics ph = rig.Physics;
            AcCarModel m = rig.Model;
            r.Check(m.DamageTexturesBuilt && remote.Model.DamageTexturesBuilt, "both models built with the damage textures (W slots)");
            remote.Physics.ApplyPuppet(PlaneCentre + new Vector3(30f, 0.05f, WallZ - 12f), Quaternion.identity, Vector3.zero, default(AcTelemetry));
            AcCarRig oldLocal = AcLocal.Car;
            AcLocal.Car = rig;                              // H's displayer follows "my car"
            int events = 0; AcDamageChange all = AcDamageChange.None; float maxKmh = 0f;
            Action<AcCarRig, AcDamageChange, float> onDmg = (cr, what, k) => { if (cr == rig) { events++; all |= what; if (k > maxKmh) maxKmh = k; } };
            AcEvents.CarDamaged += onDmg;
            try
            {
                ph.SetOccupied(true); ph.SetDriven(true);
                Log("damage box " + ph.DamageBox.center.ToString("0.00") + " size " + ph.DamageBox.size.ToString("0.00") + ", panels " + (m.ActiveSet != null ? m.ActiveSet.Damage.Panels.Count : -1)
                    + ", glass nodes " + (m.ActiveSet != null ? m.ActiveSet.Damage.GlassNodeCount : -1));

                // ---- DAMAGE 0 = iteration 1: a 60 km/h wall hit changes nothing anywhere
                ph.DamageRate = 0f;
                int ver0 = ph.Damage.Version;
                yield return PushInto(r, ph, 0f, 60f, -20f);
                yield return r.Frames(3);
                int d0, d1; ph.Damage.ToWire(out d0, out d1);
                r.Check(ph.Damage.Pristine && ph.Damage.Version == ver0 && events == 0 && d0 == 0 && d1 == 0,
                        "DAMAGE 0: front 60 -> pristine (" + Zones(ph.Damage) + "), Version unchanged, CarDamaged " + events + "x, wire " + d0 + " " + d1);
                r.Check(m.DamageVisual != null && m.DamageVisual.PaintClones == 0 && m.DamageVisual.GlassClones == 0 && !m.DamageVisual.AnyDamage,
                        "DAMAGE 0: C made no material copies and shows nothing (glass " + Glass(m.DamageVisual) + ")");
                float cl0, cd0; float worst0 = 1f;
                for (int w = 0; w < rig.Spec.Wings.Count; w++) { ph.Damage.AeroFactors(rig.Spec.Wings[w], out cl0, out cd0); worst0 = Mathf.Max(worst0, Mathf.Max(cl0, cd0)); }
                r.Check(worst0 == 1f, "DAMAGE 0: every wing factor is exactly 1");

                // ---- DAMAGE 100: front 60 into the wall (a real PhysX contact)
                ph.DamageRate = 1f;
                float toeBefore = ph.Chassis.Wheels[0].ToeDeg;
                yield return PushInto(r, ph, 0f, 60f, 0f);
                yield return r.Frames(3);
                AcCarDamage d = ph.Damage;
                r.Check(d.Zone[0] >= 48f && d.Zone[0] <= 72f && d.Zone[1] < 3f && d.Zone[2] < 3f && d.Zone[3] < 3f, "front 60 -> FRONT within 20 % (" + Zones(d) + ")");
                r.Check(events >= 1 && (all & AcDamageChange.Zones) != 0 && maxKmh >= 48f, "CarDamaged raised " + events + "x (" + all + ", max " + F(maxKmh, "0.0") + " km/h)");
                string why;
                r.Check(VisualMatches(m, d, out why), "P pushed the ZoneByte state into C: " + why);
                float maxF; int bent = PanelsBent(m, out maxF);
                r.Check(m.DamageVisual.PaintClones > 0, "C: per-car paint copies " + m.DamageVisual.PaintClones + " (dents on), panels bent " + bent + " (max f " + F(maxF, "0.00") + "), glass " + Glass(m.DamageVisual));
                float cl, cd, worstCl = 1f, worstCd = 1f;
                for (int w = 0; w < rig.Spec.Wings.Count; w++) { d.AeroFactors(rig.Spec.Wings[w], out cl, out cd); worstCl = Mathf.Max(worstCl, cl); worstCd = Mathf.Max(worstCd, cd); }
                Log("aero after front 60: worst clDiv " + F(worstCl, "0.000") + " cdMul " + F(worstCd, "0.000") + "; toe LF " + F(toeBefore, "0.00") + " -> " + F(ph.Chassis.Wheels[0].ToeDeg, "0.00")
                    + " deg, rods mm " + F(d.RodOffset[0] * 1000f, "0.0") + "/" + F(d.RodOffset[1] * 1000f, "0.0") + "; body " + F(d.BodyPct, "0") + " %, repair body " + F(d.RepairSeconds(AcRepair.Body), "0") + " s");
                bool hasFrontAero = false;
                for (int w = 0; w < rig.Spec.Wings.Count; w++) if (rig.Spec.Wings[w].ZoneFrontCl != 0f || rig.Spec.Wings[w].ZoneFrontCd != 0f) hasFrontAero = true;
                if (hasFrontAero) r.Check(worstCl > 1f || worstCd > 1f, "aero: a front-zone wing lost downforce / gained drag");
                float hudLeft = AcHud.DamageShowLeft;
                Log("HUD displayer time left after the hit: " + F(hudLeft, "0.0") + " s (H subscribed: " + (hudLeft > 0f ? "yes" : "no - AcHud not started in this lab or hidden") + ")");
                yield return Shots(r, rig, "e2e-front60");

                // ---- a right-side hit 45 (heading -90: the car's RIGHT faces the wall)
                yield return PushInto(r, ph, -90f, 45f, 20f);
                yield return r.Frames(3);
                r.Check(d.Zone[3] >= 36f, "right side 45 -> RIGHT (" + Zones(d) + "), glass " + Glass(m.DamageVisual));
                r.Check(VisualMatches(m, d, out why), "C follows the second hit: " + why);
                yield return Shots(r, rig, "e2e-right45");

                // ---- the net seam: owner ToWire -> remote ApplyWire (what N does each frame) -> P's push on the puppet -> C
                d.ToWire(out d0, out d1);
                bool changed = remote.Physics.Damage.ApplyWire(d0, d1);
                AcDamageChange rWhat = remote.Physics.Damage.TakeChanges();
                yield return r.Frames(3);
                r.Check(changed && (rWhat & AcDamageChange.Zones) != 0, "remote ApplyWire(" + d0.ToString("X8") + ", " + d1.ToString("X8") + ") changed (" + rWhat + ")");
                r.Check(VisualMatches(remote.Model, d, out why), "remote draws the owner's ZoneByte state: " + why);
                r.Check(Glass(remote.Model.DamageVisual) == Glass(m.DamageVisual), "remote glass " + Glass(remote.Model.DamageVisual) + " = owner glass " + Glass(m.DamageVisual));
                int rd0, rd1; remote.Physics.Damage.ToWire(out rd0, out rd1);
                r.Check(rd0 == d0 && rd1 == d1, "remote re-encodes the identical pair (" + rd0.ToString("X8") + " " + rd1.ToString("X8") + ")");
                r.Check(!remote.Physics.Damage.ApplyWire(d0, d1), "the same pair again changes nothing (no re-push)");
                {
                    Transform t = remote.Root.transform; Vector3 c = t.position + Vector3.up * 0.5f;
                    r.Aim(c + t.forward * 5.5f + t.right * 3.5f + Vector3.up * 1.4f, c, 45f); r.Shot("e2e-remote-front34");
                }

                // ---- repair: owner Repair(All) -> CarDamaged(Repaired) -> C pristine; the zero pair clears the remote
                int ev0 = events; all = AcDamageChange.None;
                ph.Repair(AcRepair.All);
                yield return r.Frames(3);
                ph.Damage.ToWire(out d0, out d1);
                r.Check(d.Pristine && events == ev0 + 1 && (all & AcDamageChange.Repaired) != 0 && d0 == 0 && d1 == 0, "Repair(All): pristine, CarDamaged(Repaired) once, wire 0 0");
                r.Check(VisualMatches(m, d, out why) && !m.DamageVisual.AnyDamage && Glass(m.DamageVisual) == ".....", "C pristine after the repair: " + why + ", glass " + Glass(m.DamageVisual));
                bent = PanelsBent(m, out maxF);
                r.Check(bent == 0, "panels back at rest (" + bent + " bent)");
                float clR, cdR, worstR = 1f;
                for (int w = 0; w < rig.Spec.Wings.Count; w++) { d.AeroFactors(rig.Spec.Wings[w], out clR, out cdR); worstR = Mathf.Max(worstR, Mathf.Max(clR, cdR)); }
                r.Check(worstR == 1f && Mathf.Abs(ph.Chassis.Wheels[0].ToeDeg - toeBefore) < 1e-4f, "aero factors 1 and toe LF back to " + F(ph.Chassis.Wheels[0].ToeDeg, "0.00"));
                changed = remote.Physics.Damage.ApplyWire(0, 0);
                rWhat = remote.Physics.Damage.TakeChanges();
                yield return r.Frames(3);
                r.Check(changed && (rWhat & AcDamageChange.Repaired) != 0 && remote.Physics.Damage.Pristine && !remote.Model.DamageVisual.AnyDamage, "remote: zero pair = repaired and pristine (" + rWhat + ")");
                yield return Shots(r, rig, "e2e-repaired");

                // ---- reset keeps damage (never a free repair)
                yield return PushInto(r, ph, 0f, 40f, -10f);
                float kept = d.Zone[0];
                ph.ResetTo(PlaneCentre + new Vector3(0f, 2f, 0f), Quaternion.identity);
                yield return r.Frames(3);
                r.Check(kept >= 3f && d.Zone[0] == kept && VisualMatches(m, d, out why), "ResetTo keeps the damage and the visuals (front " + F(kept, "0.0") + ")");

                // ---- multiplier on -> off mid-session: R repairs (here by hand as R's ApplyDamageRate does), nothing accumulates after
                ph.DamageRate = 0f; ph.Repair(AcRepair.All);
                yield return PushInto(r, ph, 0f, 60f, 10f);
                yield return r.Frames(2);
                r.Check(d.Pristine, "after switching damage off: pristine and a new hit adds nothing (" + Zones(d) + ")");
            }
            finally
            {
                AcEvents.CarDamaged -= onDmg;
                AcLocal.Car = oldLocal;
                AcCarDamageGate.Reset();
            }
        }
    }
}
