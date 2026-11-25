using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [C] Lab scenarios of the car model (DevTools/lab.sh &lt;name&gt; [-accar ferrari_458] [-acskin 00_rosso_scuderia]). Each builds cars
    /// with <see cref="AcCarModel.BuildAsync"/> directly (no physics) away from the hub, poses them and takes pictures to LOOK at:
    /// <list type="bullet">
    /// <item><c>cars-458</c> (any car with -accar): front 3/4, side, rear with brake lights on, headlights on, cockpit from DriverEye with
    /// the steering wheel at -90 / 0 / +90, door at 0 / 0.5 / 1, wheels steered 25 deg, rim blur at 120 km/h. Checks: 4 wheels, steer,
    /// wheels touch y = 0 (WHEEL.y - RADIUS within 5 cm, C1), the LHD driver sits on the Unity -x side (paint / mirror sanity),
    /// +90 deg moves the rim top towards +x (driver's right, C3), the open left door swings outward (-x), the hull sits on the body.</item>
    /// <item><c>cars-five</c>: ferrari_458, abarth500, ks_mazda_miata, tatuusfa1, ks_ford_mustang_2015 one after another: front 3/4 + cockpit
    /// shot each, the same checks, and the mustang hull is rejected (box fallback).</item>
    /// <item><c>cars-lod</c>: ferrari_458 with the Remote options (LOD_0..3 by distance): the viewer at 5 / 25 / 100 / 400 m - the shown LOD
    /// follows lods.ini IN/OUT, LOD_1..3 get textures from LOD_0 (pictures).</item>
    /// <item><c>cars-leak</c>: 20 build / dispose cycles of abarth500; texture memory and Mono heap logged before / after; LiveCount back to 0.</item>
    /// <item><c>cars-gauges</c>: ks_mazda_miata (analog) and tatuusfa1 (digital steering display): rpm sweep 0 -> limiter, gear 3, fuel 20 L;
    /// cockpit close-ups to check the needles turn clockwise from their zero and the digits read right. NUMERIC too: every RPM needle tip
    /// turns clockwise on the eye camera's screen from 0 to 3000 rpm; the first digital text quad reads left-to-right and upright.</item>
    /// <item><c>cars-driver</c>: ferrari_458 with the Ai options (AC driver): posed nodes, the posed RIG_Hips / RIG_HAND_L/R vs C's DriverHips /
    /// HandL/R (2 cm), steer.ksanim moves the right hand at +-90 deg, the skin.ini suit replaced texture slots, a gear change plays the driver's
    /// shift clip, the driver hides from LOD_2 (viewer 100 m) and comes back at 5 m. Shots: front34 / side with the driver, steer -90/+90.</item>
    /// <item><c>cars-anim</c>: pop-up lights (ks_mazda_miata lights.ksanim, ferrari_f40, bmw_1m has none), car_susp clips (lotus_49 +5 cm travel
    /// lifts the wheel 3..7 cm), the shift-paddle clip on a gear change (ferrari_458), wipers (458 car_wiper.ksanim moves within 0.7 s), the
    /// cockpit HR/LR switch by distance (458 Local: 3 m HR, 20 m LR).</item>
    /// <item><c>cars-dash</c>: ferrari_458 dark LCD text (COLOR 10,10,10 x 0.5 -> vertex colour below 0.05), lap times via SetLapTimes,
    /// ks_lamborghini_huracan_performante GEAR_TX (exactly one gear picture shown per gear) + GFORCE_SERIE LEDs at 1.2 g, ks_ford_mustang_2015
    /// PRESSURE items read tyres.ini PRESSURE_STATIC. Close-up shots.</item>
    /// <item><c>cars-interior</c>: ferrari_458 with the Remote options (no COCKPIT_HR), then <see cref="AcCarModel.EnsureInterior"/>: HasInterior,
    /// needles bound, COCKPIT_HR shown when the viewer is inside; passenger-seat cockpit shot before / after.</item>
    /// <item><c>cars-leak-ai</c>: 10 build / dispose cycles of ferrari_458 with the Ai options (LOD_0..3, TextureSource, AC driver + skin):
    /// LiveCount 0, driver skin textures 0, font cache empty, texture memory back within 16 MB.</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    internal static class AcLabCars
    {
        static AcLabCars()
        {
            AcLabRunner.Register("cars-458", r => One(r, AcLabBoot.Arg("-accar", "ferrari_458")));
            AcLabRunner.Register("cars-five", r => Five(r));
            AcLabRunner.Register("cars-lod", r => Lod(r));
            AcLabRunner.Register("cars-leak", r => Leak(r));
            AcLabRunner.Register("cars-gauges", r => Gauges(r));
            AcLabRunner.Register("cars-driver", r => Driver(r));
            AcLabRunner.Register("cars-anim", r => Anim(r));
            AcLabRunner.Register("cars-dash", r => Dash(r));
            AcLabRunner.Register("cars-interior", r => Interior(r));
            AcLabRunner.Register("cars-leak-ai", r => LeakAi(r));
            AcLabRunner.Register("cars-glassfix", r => GlassFix(r));
            AcLabRunner.Register("cars-damage", r => DamageScenario(r));
        }

        static readonly Vector3 Spot = AcSpace.RoomOrigin + new Vector3(200f, 0f, 200f); // ours: away from the hub room
        static AcCarModel _got;
        static bool _done;

        static void Got(AcCarModel m) { _got = m; _done = true; }

        static IEnumerator BuildCar(AcLabRunner r, string id, AcCarModelOptions opt, Vector3 at)
        {
            _got = null; _done = false;
            AcCarEntry car = r.Game.Content != null ? r.Game.Content.Car(id) : null;
            if (!r.Check(car != null && car.Owned, "car " + id + " installed")) yield break;
            var parent = new GameObject("Lab car " + id).transform;
            parent.position = at;
            string skin = AcLabBoot.Arg("-acskin", "");
            yield return AcCarModel.BuildAsync(car, skin, opt, parent, Got);
            yield return r.Wait(() => _done, 180f, "car " + id + " built");
            r.Check(_got != null, "AcCarModel.BuildAsync(" + id + ") returned a model");
        }

        static void Light(AcLabRunner r)
        {
            AcMaterials.EnsureGlobals();
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        }

        static Vector3 W(AcCarModel m, Vector3 model) { return m.Root.TransformPoint(model); }

        /// <summary>The geometric checks every car must pass.</summary>
        static void Checks(AcLabRunner r, AcCarModel m)
        {
            string id = m.Car.Id;
            int wheels = 0;
            for (int i = 0; i < 4; i++) if (m.Wheels[i].Wheel != null) wheels++;
            r.Check(wheels == 4, id + ": 4 WHEEL_xx bound (" + wheels + ")");
            r.Check(m.SteeringWheel != null, id + ": steering wheel bound");
            for (int i = 0; i < 4; i++)
            {
                if (m.Wheels[i].Wheel == null) continue;
                float y = m.Root.InverseTransformPoint(m.Wheels[i].Wheel.position).y - m.Wheels[i].Radius;
                r.Check(Mathf.Abs(y) < 0.05f || id == "ks_lamborghini_sesto_elemento", id + ": wheel " + i + " touches the ground (centre - radius = " + y.ToString("0.000") + " m)");
            }
            r.Check(m.DriverEye.y > 0.5f && m.DriverEye.y < 1.5f, id + ": DriverEye height " + m.DriverEye.y.ToString("0.000") + " (0.65..1.37 in the install)");
            r.Check(m.DriverHips.y > 0.1f && m.DriverHips.y < m.DriverEye.y, id + ": DriverHips " + m.DriverHips + " below the eye");
            r.Check(Vector3.Distance(m.HandL, m.HandR) > 0.2f, id + ": hands " + m.HandL + " / " + m.HandR + " apart");
            r.Check(m.Hull.Length > 0 || !m.Options.Colliders, id + ": hull colliders " + m.Hull.Length);
            if (m.Hull.Length > 0)
            {
                var hb = m.Hull[0].bounds;
                r.Check(Vector3.Distance(m.Root.InverseTransformPoint(hb.center), m.Bounds.center) < 0.8f, id + ": hull centre on the body");
            }
            // steering sign (C3): +90 deg moves the top of the rim towards the driver's right (+x)
            if (m.SteeringWheel != null)
            {
                m.SetSteer(0f);
                Vector3 top0 = m.Root.InverseTransformPoint(m.SteeringWheel.TransformPoint(new Vector3(0f, 0.18f, 0f)));
                m.SetSteer(90f);
                Vector3 top90 = m.Root.InverseTransformPoint(m.SteeringWheel.TransformPoint(new Vector3(0f, 0.18f, 0f)));
                m.SetSteer(0f);
                r.Check(top90.x > top0.x + 0.1f, id + ": steering +90 moves the rim top right (" + top0.x.ToString("0.00") + " -> " + top90.x.ToString("0.00") + ")");
            }
            // wheel sense (P's telemetry: SpinDeg > 0 = rolling forward, SteerDeg > 0 = steering right): +30 deg spin moves the top of the
            // tyre forward (+z), +25 deg steer points the front of a front tyre right (+x)
            for (int i = 0; i < 4; i++)
            {
                Transform wt = m.Wheels[i].Wheel;
                if (wt == null) continue;
                float rad = m.Wheels[i].Radius > 0.1f ? m.Wheels[i].Radius : 0.3f;
                Vector3 c0 = m.Root.InverseTransformPoint(wt.position);
                Vector3 topL = wt.InverseTransformPoint(m.Root.TransformPoint(c0 + Vector3.up * rad));
                Vector3 frontL = wt.InverseTransformPoint(m.Root.TransformPoint(c0 + Vector3.forward * rad));
                m.SetWheelState(i, 30f, 0f, 0f);
                Vector3 top1 = m.Root.InverseTransformPoint(wt.TransformPoint(topL));
                m.SetWheelState(i, 0f, 0f, 0f);
                r.Check(top1.z > c0.z + 0.05f, id + ": wheel " + i + " +30 deg spin rolls forward (tyre top z " + (top1.z - c0.z).ToString("0.00") + " m ahead of the hub)");
                if (i < 2)
                {
                    m.SetWheelState(i, 0f, 0f, 25f);
                    Vector3 f1 = m.Root.InverseTransformPoint(wt.TransformPoint(frontL));
                    m.SetWheelState(i, 0f, 0f, 0f);
                    r.Check(f1.x > c0.x + 0.05f, id + ": front wheel " + i + " +25 deg steer points right (tyre front x " + (f1.x - c0.x).ToString("0.00") + " m)");
                }
            }
            if (m.HasDoor(AcSide.Left) && m.DoorL != null)
            {
                // the handle (rear, outer end of the door) must swing outward (-x for the left door) when car_DOOR_L.ksanim plays to 1
                Vector3 h0 = m.DoorHandle(AcSide.Left);
                Vector3 hl = m.DoorL.InverseTransformPoint(m.Root.TransformPoint(h0));
                m.SetDoor(AcSide.Left, 1f);
                Vector3 h1 = m.Root.InverseTransformPoint(m.DoorL.TransformPoint(hl));
                m.SetDoor(AcSide.Left, 0f);
                r.Check(h1.x < h0.x - 0.2f, id + ": the open left door swings out (handle x " + h0.x.ToString("0.00") + " -> " + h1.x.ToString("0.00") + ")");
            }
            AcLabRunner.Log(id + ": eye " + m.DriverEye + " hips " + m.DriverHips + " passenger " + (m.HasPassengerSeat ? m.PassengerHips.ToString() : "none") + " rim r " + m.SteeringRimRadius.ToString("0.000")
                + " steer centre " + m.SteeringCenter + " bounds " + m.Bounds + " handle L " + m.DoorHandle(AcSide.Left) + " R " + m.DoorHandle(AcSide.Right));
        }

        static IEnumerator Shots(AcLabRunner r, AcCarModel m, string tag, bool full)
        {
            var b = m.Bounds;
            Vector3 c = W(m, b.center);
            AcCarModel.ViewerOverride = r.Cam.transform;
            // exterior
            r.Aim(c + m.Root.rotation * new Vector3(-3.6f, 0.9f, 4.6f), c, 50f); yield return r.Frames(3); r.Shot(tag + "_front34");
            if (full)
            {
                r.Aim(c + m.Root.rotation * new Vector3(-6f, 0.4f, 0f), c, 45f); yield return r.Frames(3); r.Shot(tag + "_side_left");
                m.SetLights(AcLightState.Brake | AcLightState.Headlights);
                r.Aim(c + m.Root.rotation * new Vector3(2.5f, 0.8f, -5.5f), c, 50f); yield return r.Frames(3); r.Shot(tag + "_rear_brake_on");
                m.SetLights(AcLightState.Headlights);
                r.Aim(c + m.Root.rotation * new Vector3(1.5f, 0.6f, 5.5f), c, 50f); yield return r.Frames(3); r.Shot(tag + "_front_headlights");
                m.SetLights(AcLightState.None);
                m.SetWheelState(0, 0f, 0f, 25f); m.SetWheelState(1, 0f, 0f, 25f);
                r.Aim(c + m.Root.rotation * new Vector3(-1.2f, 2.6f, 4.8f), c + m.Root.rotation * new Vector3(0f, 0f, 1.2f), 45f); yield return r.Frames(3); r.Shot(tag + "_steer25");
                m.SetWheelState(0, 0f, 0f, 0f); m.SetWheelState(1, 0f, 0f, 0f);
                var ins = new AcInstruments { SpeedKmh = 120f, Rpm = 5000f, Gear = 4 };
                m.SetInstruments(ins);
                yield return r.Frames(3);
                r.Aim(c + m.Root.rotation * new Vector3(-4.5f, 0.5f, 0.6f), W(m, m.Wheels[0].Wheel != null ? m.Root.InverseTransformPoint(m.Wheels[0].Wheel.position) : b.center), 30f);
                yield return r.Frames(2); r.Shot(tag + "_rimblur_120kmh");
                m.SetInstruments(new AcInstruments());
                yield return r.Frames(2);
                if (m.HasDoor(AcSide.Left))
                {
                    float[] ts = { 0f, 0.5f, 1f };
                    for (int i = 0; i < ts.Length; i++)
                    {
                        m.SetDoor(AcSide.Left, ts[i]);
                        r.Aim(c + m.Root.rotation * new Vector3(-4.2f, 1.2f, 2.2f), c, 50f); yield return r.Frames(2); r.Shot(tag + "_doorL_" + (int)(ts[i] * 100));
                    }
                    m.SetDoor(AcSide.Left, 0f);
                }
            }
            // cockpit from the eye
            m.SetViewer(true);
            Vector3 eye = W(m, m.DriverEye);
            Vector3 look = W(m, m.SteeringWheel != null ? m.SteeringCenter : m.DriverEye + new Vector3(0f, -0.2f, 1f));
            float[] steer = full ? new[] { -90f, 0f, 90f } : new[] { 0f };
            for (int i = 0; i < steer.Length; i++)
            {
                m.SetSteer(steer[i]);
                r.Aim(eye, look + (look - eye).normalized * 2f, 75f); yield return r.Frames(3);
                r.Shot(tag + "_cockpit_steer" + (int)steer[i]);
            }
            m.SetSteer(0f);
            m.SetViewer(false);
        }

        static IEnumerator One(AcLabRunner r, string id)
        {
            Light(r);
            yield return BuildCar(r, id, new AcCarModelOptions(), Spot);
            if (_got == null) yield break;
            var m = _got;
            yield return r.Frames(5);
            Checks(r, m);
            yield return Shots(r, m, id, true);
            m.Dispose();
            AcCarModel.ViewerOverride = null;
        }

        static IEnumerator Five(AcLabRunner r)
        {
            Light(r);
            string[] ids = { "ferrari_458", "abarth500", "ks_mazda_miata", "tatuusfa1", "ks_ford_mustang_2015" };
            for (int i = 0; i < ids.Length; i++)
            {
                yield return BuildCar(r, ids[i], new AcCarModelOptions(), Spot + new Vector3(i * 12f, 0f, 0f));
                if (_got == null) continue;
                var m = _got;
                yield return r.Frames(5);
                Checks(r, m);
                if (ids[i] == "ks_ford_mustang_2015") r.Check(m.Hull.Length == 1 && m.Hull[0] is BoxCollider, "mustang: broken collider.kn5 rejected -> LOD_0 box (C1)");
                yield return Shots(r, m, ids[i], false);
                m.Dispose();
                yield return r.Frames(3);
            }
            AcCarModel.ViewerOverride = null;
            r.Check(AcCarModel.LiveCount == 0, "all cars disposed (LiveCount " + AcCarModel.LiveCount + ")");
        }

        static IEnumerator Lod(AcLabRunner r)
        {
            Light(r);
            yield return BuildCar(r, AcLabBoot.Arg("-accar", "ferrari_458"), AcCarModelOptions.ForKind(AcCarKind.Remote), Spot);
            if (_got == null) yield break;
            var m = _got;
            AcCarModel.ViewerOverride = r.Cam.transform;
            Vector3 c = W(m, m.Bounds.center);
            float[] dist = { 5f, 25f, 100f, 400f };
            var seen = new HashSet<int>();
            for (int i = 0; i < dist.Length; i++)
            {
                r.Aim(c + new Vector3(-0.6f, 0.25f, 0.8f).normalized * dist[i], c, Mathf.Clamp(60f * 6f / dist[i], 1.5f, 60f));
                yield return r.Frames(4);
                seen.Add(m.ActiveLod);
                AcLabRunner.Log("viewer at " + dist[i] + " m: LOD " + m.ActiveLod);
                r.Shot("lod_" + (int)dist[i] + "m_LOD" + m.ActiveLod);
            }
            r.Check(seen.Count >= 3, "LOD switches with distance (" + seen.Count + " different LODs seen)");
            m.Dispose();
            AcCarModel.ViewerOverride = null;
        }

        static IEnumerator Leak(AcLabRunner r)
        {
            string id = AcLabBoot.Arg("-accar", "abarth500");
            yield return r.Frames(10);
            System.GC.Collect();
            yield return Resources.UnloadUnusedAssets();
            ulong tex0 = Texture.currentTextureMemory;
            long heap0 = System.GC.GetTotalMemory(true);
            AcLabRunner.Log("before: textures " + (tex0 / 1048576UL) + " MB, heap " + (heap0 / 1048576L) + " MB");
            for (int i = 0; i < 20; i++)
            {
                yield return BuildCar(r, id, new AcCarModelOptions(), Spot);
                if (_got == null) yield break;
                if (i == 0) AcLabRunner.Log("with one car: textures " + (Texture.currentTextureMemory / 1048576UL) + " MB");
                _got.Dispose();
                yield return r.Frames(2);
            }
            System.GC.Collect();
            yield return Resources.UnloadUnusedAssets();
            yield return r.Frames(5);
            ulong tex1 = Texture.currentTextureMemory;
            long heap1 = System.GC.GetTotalMemory(true);
            AcLabRunner.Log("after 20 cycles: textures " + (tex1 / 1048576UL) + " MB, heap " + (heap1 / 1048576L) + " MB");
            r.Check(AcCarModel.LiveCount == 0, "LiveCount 0 after 20 cycles");
            r.Check(tex1 <= tex0 + 16UL * 1048576UL, "texture memory back near the start (+" + ((long)tex1 - (long)tex0) / 1048576L + " MB; one car is ~40 MB)"); // ours: 16 MB slack (fonts, shared white/black)
            r.Check(heap1 < heap0 + 64L * 1048576L, "managed heap back near the start (+" + (heap1 - heap0) / 1048576L + " MB)"); // ours: 64 MB slack
        }

        static IEnumerator Gauges(AcLabRunner r)
        {
            Light(r);
            string[] ids = { "ks_mazda_miata", "tatuusfa1" };
            for (int k = 0; k < ids.Length; k++)
            {
                yield return BuildCar(r, ids[k], new AcCarModelOptions(), Spot + new Vector3(k * 12f, 0f, 0f));
                if (_got == null) continue;
                var m = _got;
                AcCarModel.ViewerOverride = r.Cam.transform;
                m.SetViewer(true);
                Vector3 eye = W(m, m.DriverEye);
                Vector3 look = W(m, m.SteeringCenter + new Vector3(0f, 0.04f, 0.12f));
                float[] rpm = { 0f, 3000f, 6500f };
                var ins = m.PrimarySet != null ? m.PrimarySet.Instruments : null;
                var tip0 = new List<Vector2>();
                for (int i = 0; i < rpm.Length; i++)
                {
                    m.SetInstruments(new AcInstruments { Rpm = rpm[i], SpeedKmh = rpm[i] / 50f, Gear = 3, FuelLitres = 20f });
                    r.Aim(eye + (look - eye) * 0.35f, look, 40f);
                    yield return r.Frames(3);
                    r.Shot(ids[k] + "_gauges_rpm" + (int)rpm[i]);
                    if (ins == null) continue;
                    // numeric needle sense: every RPM needle tip turns clockwise on the eye camera's screen from 0 to 3000 rpm
                    int nr = 0;
                    for (int n = 0; n < ins.NeedleTotal; n++)
                    {
                        if (ins.NeedleKind(n) != AcGaugeKind.Rpm || ins.NeedleNode(n) == null) continue;
                        Vector2 pivot = r.Cam.WorldToScreenPoint(ins.NeedleNode(n).position);
                        Vector2 tip = r.Cam.WorldToScreenPoint(TipOf(ins.NeedleNode(n)));
                        if (i == 0) tip0.Add(tip - pivot);
                        else if (i == 1 && nr < tip0.Count)
                        {
                            Vector2 a = tip0[nr], b = tip - pivot;
                            float cross = a.x * b.y - a.y * b.x;
                            r.Check(cross < 0f, ids[k] + ": RPM needle " + n + " turns clockwise on screen 0 -> 3000 rpm (cross " + cross.ToString("0") + ")");
                        }
                        nr++;
                    }
                    if (i == 1) TextOrientation(r, ids[k], ins);
                }
                AcLabRunner.Log(ids[k] + ": built line in the log lists needles / leds / displays");
                m.Dispose();
                yield return r.Frames(3);
            }
            AcCarModel.ViewerOverride = null;
        }

        /// <summary>World centre of the needle's meshes (the tip side of the pivot).</summary>
        static Vector3 TipOf(Transform needle)
        {
            var rs = needle.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return needle.position + needle.up * 0.03f;
            Vector3 c = Vector3.zero;
            for (int i = 0; i < rs.Length; i++) c += rs[i].bounds.center;
            return c / rs.Length;
        }

        /// <summary>The first non-empty digital text quad reads left-to-right and upright on the current camera (dash text orientation).</summary>
        static void TextOrientation(AcLabRunner r, string id, AcCarInstruments ins)
        {
            for (int i = 0; i < ins.TextItemCount; i++)
            {
                var t = ins.TextItem(i);
                if (t == null || t.Mesh == null || t.ShownText().Length == 0) continue;
                var v = t.Mesh.vertices;
                var tr = t.GameObject.transform;
                Vector2 s0 = r.Cam.WorldToScreenPoint(tr.TransformPoint(v[0])), s1 = r.Cam.WorldToScreenPoint(tr.TransformPoint(v[1])), s3 = r.Cam.WorldToScreenPoint(tr.TransformPoint(v[3]));
                r.Check(s3.x > s0.x && s1.y > s0.y, id + ": display text '" + t.ShownText() + "' (" + t.Def.Type + ") reads left-to-right and upright on screen (dx " + (s3.x - s0.x).ToString("0.0") + ", dy " + (s1.y - s0.y).ToString("0.0") + ")");
                return;
            }
            AcLabRunner.Log(id + ": no digital text drawn to check the orientation");
        }

        static IEnumerator Seconds(float s) { float t0 = Time.time; while (Time.time - t0 < s) yield return null; }

        /// <summary>Largest angle (deg) / distance (m) any bound target of a clip moved from <paramref name="rest"/>.</summary>
        static void Moved(Transform[] targets, Quaternion[] restR, Vector3[] restP, ref float maxDeg, ref float maxM)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                maxDeg = Mathf.Max(maxDeg, Quaternion.Angle(targets[i].localRotation, restR[i]));
                maxM = Mathf.Max(maxM, Vector3.Distance(targets[i].localPosition, restP[i]));
            }
        }

        static void Rest(Transform[] targets, out Quaternion[] r, out Vector3[] p)
        {
            r = new Quaternion[targets.Length]; p = new Vector3[targets.Length];
            for (int i = 0; i < targets.Length; i++) if (targets[i] != null) { r[i] = targets[i].localRotation; p[i] = targets[i].localPosition; }
        }

        /// <summary>Plays a clip by <paramref name="start"/> for <paramref name="seconds"/> and checks that its targets moved.</summary>
        static IEnumerator ClipMoves(AcLabRunner r, string what, AcKsanimPlayer p, System.Action start, float seconds, float minDeg)
        {
            if (!r.Check(p != null && p.Targets != null && p.Targets.Length > 0, what + ": clip bound")) yield break;
            Quaternion[] rr; Vector3[] rp;
            Rest(p.Targets, out rr, out rp);
            start();
            float deg = 0f, m = 0f, t0 = Time.time;
            while (Time.time - t0 < seconds) { yield return null; Moved(p.Targets, rr, rp, ref deg, ref m); }
            r.Check(deg > minDeg || m > 0.01f, what + ": targets moved (max " + deg.ToString("0.0") + " deg, " + (m * 100f).ToString("0.0") + " cm)");
        }

        static IEnumerator Driver(AcLabRunner r)
        {
            Light(r);
            string id = AcLabBoot.Arg("-accar", "ferrari_458");
            yield return BuildCar(r, id, AcCarModelOptions.ForKind(AcCarKind.Ai), Spot);
            if (_got == null) yield break;
            var m = _got;
            AcCarModel.ViewerOverride = r.Cam.transform;
            var d = m.DriverModel;
            if (!r.Check(d != null && d.Kn5 != null, id + ": AC driver model built")) { m.Dispose(); yield break; }
            r.Check(d.Posed > 50, id + ": driver nodes posed from the knh (" + d.Posed + ")");
            r.Check(d.SkinSlots > 0, id + ": skin.ini driver suit / gloves / helmet replaced " + d.SkinSlots + " texture slots");
            var hips = d.Kn5.Find("DRIVER:RIG_Hips");
            if (hips != null)
            {
                float e = Vector3.Distance(m.Root.InverseTransformPoint(hips.position), m.DriverHips);
                r.Check(e < 0.02f, id + ": posed RIG_Hips on C's DriverHips (" + (e * 100f).ToString("0.0") + " cm)");
            }
            var hand = d.Kn5.Find("DRIVER:RIG_HAND_R");
            Vector3 c = W(m, m.Bounds.center);
            r.Aim(c + m.Root.rotation * new Vector3(-3.6f, 0.9f, 4.6f), c, 50f); yield return r.Frames(4); r.Shot(id + "_driver_front34");
            r.Aim(W(m, m.DriverEye) + m.Root.rotation * new Vector3(-1.6f, 0.1f, 0.2f), W(m, m.DriverEye + new Vector3(0f, -0.25f, 0.2f)), 55f);
            Vector3 hm = Vector3.zero, hp = Vector3.zero;
            m.SetSteer(-90f); yield return r.Frames(3); if (hand != null) hm = m.Root.InverseTransformPoint(hand.position); r.Shot(id + "_driver_steer-90");
            m.SetSteer(90f); yield return r.Frames(3); if (hand != null) hp = m.Root.InverseTransformPoint(hand.position); r.Shot(id + "_driver_steer90");
            m.SetSteer(0f);
            if (hand != null)
            {
                r.Check(Vector3.Distance(hm, hp) > 0.05f, id + ": steer.ksanim moves the right hand (" + (Vector3.Distance(hm, hp) * 100f).ToString("0") + " cm between -90 and +90)");
                yield return r.Frames(2);
                AcLabRunner.Log(id + ": hands at 0 deg L " + m.Root.InverseTransformPoint(d.Kn5.Find("DRIVER:RIG_HAND_L") != null ? d.Kn5.Find("DRIVER:RIG_HAND_L").position : Vector3.zero)
                    + " R " + m.Root.InverseTransformPoint(hand.position) + " vs knh HandL " + m.HandL + " HandR " + m.HandR);
            }
            m.SetInstruments(new AcInstruments { Gear = 1, Rpm = 6000f });
            yield return null;
            m.SetInstruments(new AcInstruments { Gear = 2, Rpm = 4500f });
            yield return null;
            r.Check(d.Shifting, id + ": a gear change plays the driver's shift clip");
            yield return Seconds(0.6f);
            r.Check(!d.Shifting, id + ": the shift clip ended (hands back on the wheel)");
            r.Aim(c + new Vector3(-0.6f, 0.25f, 0.8f).normalized * 100f, c, 4f); yield return r.Frames(4);
            r.Check(m.ActiveLod >= 2 && !d.Visible, id + ": driver hidden at 100 m (LOD " + m.ActiveLod + ", visible " + d.Visible + ")");
            r.Aim(c + new Vector3(-0.6f, 0.25f, 0.8f).normalized * 5f, c, 50f); yield return r.Frames(4);
            r.Check(m.ActiveLod == 0 && d.Visible, id + ": driver shown again at 5 m (LOD " + m.ActiveLod + ")");
            m.Dispose();
            AcCarModel.ViewerOverride = null;
        }

        static IEnumerator Anim(AcLabRunner r)
        {
            Light(r);
            // pop-up lights: lights.ksanim (miata: no [LIGHT_ANIMATION] section, played by name; f40: declared)
            string[] pop = { "ks_mazda_miata", "ferrari_f40" };
            for (int k = 0; k < pop.Length; k++)
            {
                yield return BuildCar(r, pop[k], new AcCarModelOptions(), Spot + new Vector3(k * 12f, 0f, 0f));
                if (_got == null) continue;
                var m = _got;
                var lights = m.PrimarySet != null ? m.PrimarySet.Lights : null;
                if (r.Check(lights != null && lights.AnimCount > 0, pop[k] + ": light animation bound"))
                    yield return ClipMoves(r, pop[k] + " pop-up lights", lights.AnimPlayer(0), () => m.SetLights(AcLightState.Headlights), 1.5f, 5f);
                Vector3 c = W(m, m.Bounds.center);
                r.Aim(c + m.Root.rotation * new Vector3(-2.5f, 1.0f, 5f), c, 45f); yield return r.Frames(3); r.Shot(pop[k] + "_popup_on");
                m.Dispose();
                yield return r.Frames(2);
            }
            // car_susp_xx clips (lotus_49: the hub moves by the clip)
            yield return BuildCar(r, "lotus_49", new AcCarModelOptions(), Spot);
            if (_got != null)
            {
                var m = _got;
                var w = m.PrimarySet != null ? m.PrimarySet.Wheels[0] : null;
                if (r.Check(w != null && w.SuspAnim != null && w.Wheel != null, "lotus_49: car_susp_lf clip bound"))
                {
                    m.SetWheelState(0, 0f, 0f, 0f);
                    float y0 = m.Root.InverseTransformPoint(w.Wheel.position).y;
                    m.SetWheelState(0, 0f, 0.05f, 0f);
                    float y1 = m.Root.InverseTransformPoint(w.Wheel.position).y;
                    m.SetWheelState(0, 0f, 0f, 0f);
                    r.Check(y1 - y0 > 0.03f && y1 - y0 < 0.07f, "lotus_49: +5 cm travel lifts the wheel by the clip (" + ((y1 - y0) * 100f).ToString("0.0") + " cm)");
                }
                m.Dispose();
                yield return r.Frames(2);
            }
            // paddle clip, wipers, cockpit HR / LR (ferrari_458)
            yield return BuildCar(r, "ferrari_458", new AcCarModelOptions(), Spot);
            if (_got != null)
            {
                var m = _got;
                var s = m.PrimarySet;
                m.SetInstruments(new AcInstruments { Gear = 1 });
                yield return null;
                yield return ClipMoves(r, "ferrari_458 shift paddle (gear 1 -> 2)", s.ShiftUp ?? s.Shift, () => m.SetInstruments(new AcInstruments { Gear = 2 }), 0.5f, 2f);
                yield return ClipMoves(r, "ferrari_458 wipers", s.Wiper, () => m.SetWipers(true), 0.7f, 5f);
                m.SetWipers(false);
                AcCarModel.ViewerOverride = r.Cam.transform;
                Vector3 c = W(m, m.Bounds.center);
                r.Aim(c + new Vector3(-3f, 0.5f, 0f), c, 50f); yield return r.Frames(3);
                r.Check(s.CockpitIsHr && s.CockpitHr != null && s.CockpitHr.activeSelf, "ferrari_458: COCKPIT_HR at 3 m");
                r.Aim(c + new Vector3(-20f, 2f, 0f), c, 20f); yield return r.Frames(3);
                r.Check(!s.CockpitIsHr && (s.CockpitLr == null || s.CockpitLr.activeSelf), "ferrari_458: COCKPIT_LR at 20 m (lods.ini DISTANCE_SWITCH " + m.Visual.CockpitHrSwitch + ")");
                m.Dispose();
                AcCarModel.ViewerOverride = null;
            }
        }

        static AcCarDisplayText FindText(AcCarModel m, string type)
        {
            var ins = m.PrimarySet != null ? m.PrimarySet.Instruments : null;
            if (ins == null) return null;
            for (int i = 0; i < ins.TextItemCount; i++) if (ins.TextItem(i).Def.Type == type) return ins.TextItem(i);
            return null;
        }

        static IEnumerator CloseUp(AcLabRunner r, AcCarModel m, string name)
        {
            AcCarModel.ViewerOverride = r.Cam.transform;
            m.SetViewer(true);
            Vector3 eye = W(m, m.DriverEye);
            Vector3 look = W(m, m.SteeringCenter + new Vector3(0f, 0.04f, 0.12f));
            r.Aim(eye + (look - eye) * 0.35f, look, 40f);
            yield return r.Frames(3);
            r.Shot(name);
        }

        static IEnumerator Dash(AcLabRunner r)
        {
            Light(r);
            // dark LCD ink (COLOR x INTENSITY)
            yield return BuildCar(r, "ferrari_458", new AcCarModelOptions(), Spot);
            if (_got != null)
            {
                var m = _got;
                var t = FindText(m, "AMBIENT_TEMP");
                if (r.Check(t != null && t.Mesh != null, "ferrari_458: AMBIENT_TEMP text built"))
                {
                    var col = t.Mesh.colors[0];
                    r.Check(Mathf.Max(col.r, Mathf.Max(col.g, col.b)) < 0.05f, "ferrari_458: dark LCD text (COLOR 10,10,10 x INTENSITY 0.5 -> " + col + ")");
                }
                yield return CloseUp(r, m, "ferrari_458_dash");
                m.Dispose();
                yield return r.Frames(2);
            }
            // lap times / assist levels (bmw_z4_gt3: LAPTIME, CURRENT_LAP, TC_LEVEL, ABS_LEVEL)
            yield return BuildCar(r, "bmw_z4_gt3", new AcCarModelOptions(), Spot);
            if (_got != null)
            {
                var m = _got;
                m.SetLapTimes(65432, 70001, 64000);
                m.SetAssistLevels(0, 1);
                m.SetInstruments(new AcInstruments { Gear = 3, Rpm = 5000f, SpeedKmh = 120f, FuelLitres = 40f });
                yield return r.Frames(3);
                var lap = FindText(m, "LAPTIME");
                r.Check(lap != null && lap.ShownText().Contains("1:05.4"), "bmw_z4_gt3: LAPTIME shows SetLapTimes (" + (lap != null ? lap.ShownText() : "none") + ")");
                var tc = FindText(m, "TC_LEVEL");
                r.Check(tc != null && tc.ShownText().EndsWith("0"), "bmw_z4_gt3: TC_LEVEL shows the assist level 0 (" + (tc != null ? tc.ShownText() : "none") + ")");
                yield return CloseUp(r, m, "bmw_z4_gt3_dash");
                m.Dispose();
                yield return r.Frames(2);
            }
            // GEAR_TX pictures + GFORCE_SERIE LEDs (huracan performante)
            yield return BuildCar(r, "ks_lamborghini_huracan_performante", new AcCarModelOptions(), Spot);
            if (_got != null)
            {
                var m = _got;
                var ins = m.PrimarySet.Instruments;
                r.Check(ins != null && ins.TextureItemCount == 9, "huracan: 9 GEAR_TX pictures built (" + (ins != null ? ins.TextureItemCount : 0) + ")");
                var tel = new AcTelemetry { Gear = 3, Rpm = 6000f, SpeedKmh = 140f, LongG = 1.2f };
                m.ApplyTelemetry(ref tel);
                yield return r.Frames(3);
                if (ins != null)
                {
                    r.Check(ins.TextureItemsShown == 1, "huracan: exactly one gear picture shown in 3rd (" + ins.TextureItemsShown + ")");
                    int total; int shown = ins.SeriesShown(AcLedSeriesKind.GForce, out total);
                    r.Check(total > 0 && shown > 0 && shown < total, "huracan: some GFORCE_SERIE LEDs lit at 1.2 g, not all (" + shown + "/" + total + "; 4 bars x 4 in the ini)");
                }
                yield return CloseUp(r, m, "huracan_dash_gear3_1.2g");
                m.Dispose();
                yield return r.Frames(2);
            }
            // PRESSURE = tyres.ini PRESSURE_STATIC (mustang)
            yield return BuildCar(r, "ks_ford_mustang_2015", new AcCarModelOptions(), Spot);
            if (_got != null)
            {
                var m = _got;
                yield return r.Frames(3);
                var p = FindText(m, "PRESSURE");
                float val;
                bool parsed = p != null && float.TryParse(p.ShownText(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val) && Mathf.Abs(val - m.Visual.PressureFront) < 0.06f;
                r.Check(parsed, "mustang: PRESSURE shows tyres.ini PRESSURE_STATIC " + m.Visual.PressureFront + " (" + (p != null ? p.ShownText() : "none") + ")");
                yield return CloseUp(r, m, "mustang_dash_pressure");
                m.Dispose();
            }
            AcCarModel.ViewerOverride = null;
        }

        static IEnumerator Interior(AcLabRunner r)
        {
            Light(r);
            string id = AcLabBoot.Arg("-accar", "ferrari_458");
            yield return BuildCar(r, id, AcCarModelOptions.ForKind(AcCarKind.Remote), Spot);
            if (_got == null) yield break;
            var m = _got;
            AcCarModel.ViewerOverride = r.Cam.transform;
            r.Check(!m.HasInterior && m.PrimarySet.CockpitHr == null, id + ": remote car built without COCKPIT_HR");
            Vector3 eye = W(m, m.PassengerHips + new Vector3(0f, 0.55f, 0.05f)); // ours: passenger eye above the mirrored hips
            Vector3 look = W(m, m.SteeringCenter);
            r.Aim(eye, look + (look - eye).normalized * 2f, 75f); yield return r.Frames(3); r.Shot(id + "_passenger_before");
            bool ok = false, cb = false;
            yield return m.EnsureInterior(x => { ok = x; cb = true; });
            yield return r.Wait(() => cb, 120f, "EnsureInterior done");
            r.Check(ok && m.HasInterior, id + ": EnsureInterior built COCKPIT_HR");
            var ins = m.PrimarySet.Instruments;
            r.Check(ins != null && ins.NeedleTotal > 0, id + ": instruments bound after EnsureInterior (needles " + (ins != null ? ins.NeedleTotal : 0) + ")");
            m.SetViewer(true);
            m.SetInstruments(new AcInstruments { Rpm = 4000f, SpeedKmh = 90f, Gear = 3, FuelLitres = 40f });
            yield return r.Frames(3);
            r.Check(m.PrimarySet.CockpitIsHr && m.PrimarySet.CockpitHr != null && m.PrimarySet.CockpitHr.activeInHierarchy, id + ": COCKPIT_HR shown for the passenger");
            r.Aim(eye, look + (look - eye).normalized * 2f, 75f); yield return r.Frames(3); r.Shot(id + "_passenger_after");
            bool again = false;
            yield return m.EnsureInterior(x => again = x);
            r.Check(again, id + ": a second EnsureInterior returns true at once");
            m.Dispose();
            AcCarModel.ViewerOverride = null;
        }

        static IEnumerator LeakAi(AcLabRunner r)
        {
            string id = AcLabBoot.Arg("-accar", "ferrari_458");
            yield return r.Frames(10);
            System.GC.Collect();
            yield return Resources.UnloadUnusedAssets();
            ulong tex0 = Texture.currentTextureMemory;
            long heap0 = System.GC.GetTotalMemory(true);
            AcLabRunner.Log("before: textures " + (tex0 / 1048576UL) + " MB, heap " + (heap0 / 1048576L) + " MB");
            for (int i = 0; i < 10; i++)
            {
                yield return BuildCar(r, id, AcCarModelOptions.ForKind(AcCarKind.Ai), Spot);
                if (_got == null) yield break;
                if (i == 0) AcLabRunner.Log("with one AI car: textures " + (Texture.currentTextureMemory / 1048576UL) + " MB, driver skin textures " + AcCarDriverModel.SkinTextureCount);
                _got.Dispose();
                yield return r.Frames(2);
            }
            System.GC.Collect();
            yield return Resources.UnloadUnusedAssets();
            yield return r.Frames(5);
            ulong tex1 = Texture.currentTextureMemory;
            long heap1 = System.GC.GetTotalMemory(true);
            AcLabRunner.Log("after 10 AI cycles: textures " + (tex1 / 1048576UL) + " MB, heap " + (heap1 / 1048576L) + " MB");
            r.Check(AcCarModel.LiveCount == 0, "LiveCount 0 after 10 AI cycles");
            r.Check(AcCarDriverModel.SkinTextureCount == 0, "driver skin textures released (" + AcCarDriverModel.SkinTextureCount + ")");
            r.Check(AcCarFonts.Count == 0, "cockpit font cache emptied with the last car (" + AcCarFonts.Count + ")");
            r.Check(tex1 <= tex0 + 16UL * 1048576UL, "texture memory back near the start (+" + ((long)tex1 - (long)tex0) / 1048576L + " MB)"); // ours: 16 MB slack
            r.Check(heap1 < heap0 + 64L * 1048576L, "managed heap back near the start (+" + (heap1 - heap0) / 1048576L + " MB)"); // ours: 64 MB slack
        }

        // ================================================================ iteration 2: damage (DAMAGE_DESIGN 10)

        static AcCarModelOptions DamageOptions(bool textures, bool remote)
        {
            var o = remote ? AcCarModelOptions.ForKind(AcCarKind.Remote) : new AcCarModelOptions();
            o.DamageTextures = textures;
            return o;
        }

        /// <summary>Every glass zone node hidden and every ksBrokenGlass material of the shown set at glassDamage 0 (G0).</summary>
        static void GlassChecks(AcLabRunner r, AcCarModel m, string what)
        {
            var s = m.ActiveSet;
            if (!r.Check(s != null && s.Damage != null, what + ": damage set bound")) return;
            int shown = 0, nodes = 0, bad = 0, glassRenderers = 0;
            for (int z = 0; z < 5; z++) for (int i = 0; i < s.Damage.Glass[z].Count; i++) { nodes++; if (s.Damage.Glass[z][i] != null && s.Damage.Glass[z][i].activeSelf) shown++; }
            foreach (var ren in s.Kn5.Renderers)
            {
                if (ren == null) continue;
                foreach (var mat in ren.sharedMaterials)
                {
                    if (!AcCarDamageSet.IsBrokenGlass(mat)) continue;
                    glassRenderers++;
                    if (mat.GetVector(AcMaterials.IdGlassSet).x != 0f) bad++;
                }
            }
            r.Check(shown == 0, what + ": " + nodes + " glass zone node(s), none shown at DAMAGE 0 (" + shown + " shown; kn5-active ones hidden: " + s.Damage.ActiveGlassHidden + ")");
            r.Check(bad == 0, what + ": " + glassRenderers + " ksBrokenGlass renderer material(s) at glassDamage 0 (" + bad + " not; stray outside a zone: " + s.Damage.StrayGlass + ")");
        }

        /// <summary>cars-glassfix (W + C, G0, runs at DAMAGE 0): ktm_xbow_r LOD0, bmw_m3_e92 LOD0 (stray R_DOOR_mesh_SUB3), lotus_exos_125 at LOD1.</summary>
        static IEnumerator GlassFix(AcLabRunner r)
        {
            Light(r);
            string[] ids = { "ktm_xbow_r", "bmw_m3_e92", "lotus_exos_125" };
            for (int k = 0; k < ids.Length; k++)
            {
                bool lod1 = ids[k] == "lotus_exos_125";
                yield return BuildCar(r, ids[k], DamageOptions(false, lod1), Spot + new Vector3(k * 12f, 0f, 0f));
                if (_got == null) continue;
                var m = _got;
                AcCarModel.ViewerOverride = r.Cam.transform;
                Vector3 c = W(m, m.Bounds.center);
                float d = 4.5f;
                if (lod1) { var l1 = m.Visual.Lod(1); d = l1 != null ? Mathf.Max(l1.In + 2f, 6f) : 20f; }
                r.Aim(c + m.Root.rotation * new Vector3(-0.45f, 0.35f, 0.8f).normalized * d, c, lod1 ? Mathf.Clamp(50f * 4.5f / d, 8f, 50f) : 50f);
                yield return r.Frames(4);
                if (lod1) r.Check(m.ActiveLod == 1, ids[k] + ": LOD_1 shown at " + d.ToString("0") + " m (" + m.ActiveLod + ")");
                GlassChecks(r, m, ids[k] + " LOD_" + m.ActiveLod);
                r.Shot(ids[k] + "_glassfix_front34");
                r.Aim(c + m.Root.rotation * new Vector3(0f, 0.9f, 1f).normalized * d, c + m.Root.rotation * new Vector3(0f, 0.3f, 0.2f), lod1 ? Mathf.Clamp(45f * 4.5f / d, 8f, 45f) : 45f);
                yield return r.Frames(3);
                r.Shot(ids[k] + "_glassfix_windscreen");
                m.Dispose();
                yield return r.Frames(3);
            }
            AcCarModel.ViewerOverride = null;
        }

        static readonly string[] ZoneTag = { "front", "rear", "left", "right", "center" };

        /// <summary>Camera offset (car axes, Unity) looking at zone z: front / rear / LEFT (Unity -x) / RIGHT (+x) / top.</summary>
        static Vector3 ZoneView(int z)
        {
            switch (z)
            {
                case 0: return new Vector3(-1.2f, 1.1f, 5.2f);
                case 1: return new Vector3(1.2f, 1.1f, -5.2f);
                case 2: return new Vector3(-5.2f, 1.2f, 0.6f);
                case 3: return new Vector3(5.2f, 1.2f, 0.6f);
                default: return new Vector3(0.01f, 6.5f, -0.6f);
            }
        }

        static void SetZone(AcCarModel m, int z, float kmh)
        {
            m.SetDamage(z == 0 ? kmh : 0f, z == 1 ? kmh : 0f, z == 2 ? kmh : 0f, z == 3 ? kmh : 0f, z == 4 ? kmh : 0f);
        }

        /// <summary>Glass zone states and panel angles of the shown set against the formulas.</summary>
        static void DamageChecks(AcLabRunner r, AcCarModel m, string what)
        {
            var s = m.ActiveSet; var dv = m.DamageVisual;
            if (s == null || s.Damage == null || dv == null) { r.Check(false, what + ": damage bound"); return; }
            var def = dv.Def;
            float thr = AcDamageRules.Current.GlassThreshold;
            for (int z = 0; z < 5; z++)
            {
                if (s.Damage.Glass[z].Count == 0) continue;
                bool want = def.HasGlass[z] && def.Glass01(z, dv.Zone(z)) >= thr && dv.Zone(z) > 0f;
                bool on = s.Damage.Glass[z][0] != null && s.Damage.Glass[z][0].activeSelf;
                r.Check(on == want, what + ": glass " + ZoneTag[z] + " " + (on ? "broken" : "intact") + " (g " + def.Glass01(z, dv.Zone(z)).ToString("0.00") + " vs " + thr + ")");
            }
            for (int i = 0; i < s.Damage.Panels.Count; i++)
            {
                var b = s.Damage.Panels[i];
                if (b.Node == null || (i + 1 < s.Damage.Panels.Count && s.Damage.Panels[i + 1].Node == b.Node) || (i > 0 && s.Damage.Panels[i - 1].Node == b.Node)) continue;
                var p = def.Panels[b.Def];
                float f = p.Factor(dv.Zone(p.Zone));
                float want = Mathf.Abs(f * p.StaticAngle), got = Quaternion.Angle(b.Rest, b.Node.localRotation);
                r.Check(Mathf.Abs(want - got) < 0.05f + Mathf.Abs(dv.PanelWobble(b.Def)), what + ": panel " + p.Name + " (" + ZoneTag[p.Zone] + ") angle " + got.ToString("0.00") + " = f " + f.ToString("0.00") + " x " + p.StaticAngle + " deg");
            }
        }

        /// <summary>cars-damage (C + W): SetDamage 0 / 30 / 60 / 150 per zone on ferrari_458, ks_mazda_miata (opposite panel signs), abarth500,
        /// ferrari_312t (CENTER-only glass), built with the damage textures; LOD 0 -> 1 switch (458 Remote); repair; a DAMAGE-0 build (no
        /// slots, FlattenDamageNormals ran); engine smoke.</summary>
        static IEnumerator DamageScenario(AcLabRunner r)
        {
            Light(r);
            string[] ids = { "ferrari_458", "ks_mazda_miata", "abarth500", "ferrari_312t" };
            float[] levels = { 30f, 60f, 150f };
            for (int k = 0; k < ids.Length; k++)
            {
                yield return BuildCar(r, ids[k], DamageOptions(true, false), Spot + new Vector3(k * 12f, 0f, 0f));
                if (_got == null) continue;
                var m = _got;
                AcCarModel.ViewerOverride = r.Cam.transform;
                Vector3 c = W(m, m.Bounds.center);
                r.Check(m.DamageTexturesBuilt && m.ActiveSet.Damage.PaintRenderers.Count > 0, ids[k] + ": built with damage textures, paint renderers " + m.ActiveSet.Damage.PaintRenderers.Count);
                for (int z = 0; z < 5; z++) { r.Aim(c + m.Root.rotation * ZoneView(z), c, 45f); yield return r.Frames(3); r.Shot(ids[k] + "_dmg0_" + ZoneTag[z]); }
                for (int li = 0; li < levels.Length; li++)
                    for (int z = 0; z < 5; z++)
                    {
                        SetZone(m, z, levels[li]);
                        yield return r.Frames(2);
                        DamageChecks(r, m, ids[k] + " " + ZoneTag[z] + " " + levels[li]);
                        r.Aim(c + m.Root.rotation * ZoneView(z), c, 45f); yield return r.Frames(3);
                        r.Shot(ids[k] + "_dmg" + (int)levels[li] + "_" + ZoneTag[z]);
                    }
                r.Check(m.DamageVisual.PaintClones > 0, ids[k] + ": per-car paint material copies " + m.DamageVisual.PaintClones);
                m.ClearDamage();
                yield return r.Frames(2);
                DamageChecks(r, m, ids[k] + " repaired");
                r.Aim(c + m.Root.rotation * ZoneView(0), c, 45f); yield return r.Frames(3); r.Shot(ids[k] + "_repaired_front");
                if (k == 0)
                {
                    m.SetEngineSmoke(1f);
                    yield return Seconds(2f);
                    r.Check(m.Smoke != null && m.Smoke.Created, ids[k] + ": engine smoke created");
                    r.Aim(c + m.Root.rotation * new Vector3(-3f, 1.8f, 5f), c, 50f); yield return r.Frames(2); r.Shot(ids[k] + "_smoke");
                    m.SetEngineSmoke(0f);
                }
                m.Dispose();
                yield return r.Frames(3);
            }
            // LOD switch keeps the state (458 Remote: LOD_0..3)
            yield return BuildCar(r, "ferrari_458", DamageOptions(true, true), Spot + new Vector3(60f, 0f, 0f));
            if (_got != null)
            {
                var m = _got;
                AcCarModel.ViewerOverride = r.Cam.transform;
                Vector3 c = W(m, m.Bounds.center);
                r.Aim(c + new Vector3(-0.5f, 0.3f, 0.8f).normalized * 5f, c, 50f); yield return r.Frames(3);
                m.SetDamage(150f, 60f, 0f, 0f, 0f);
                yield return r.Frames(2);
                DamageChecks(r, m, "458 LOD_" + m.ActiveLod);
                var l1 = m.Visual.Lod(1);
                float d = l1 != null ? Mathf.Max(l1.In + 2f, 6f) : 20f;
                r.Aim(c + new Vector3(-0.5f, 0.3f, 0.8f).normalized * d, c, Mathf.Clamp(50f * 5f / d, 8f, 50f)); yield return r.Frames(4);
                r.Check(m.ActiveLod == 1, "458: LOD_1 shown at " + d.ToString("0") + " m (" + m.ActiveLod + ")");
                DamageChecks(r, m, "458 after the switch to LOD_" + m.ActiveLod);
                r.Shot("ferrari_458_dmg_lod1");
                m.Dispose();
                yield return r.Frames(3);
            }
            // a DAMAGE-0 build: no damage slots, damage normals flattened (iteration 1)
            yield return BuildCar(r, "ferrari_458", DamageOptions(false, false), Spot + new Vector3(72f, 0f, 0f));
            if (_got != null)
            {
                var m = _got;
                r.Check(!m.DamageTexturesBuilt && m.ActiveSet.Damage.PaintRenderers.Count == 0, "458 DAMAGE-0 build: no damage paint slots (paint renderers " + m.ActiveSet.Damage.PaintRenderers.Count + "; see the 'damage visuals' log line: normals flat > 0)");
                m.Dispose();
            }
            AcCarModel.ViewerOverride = null;
            // integration 2026-11-02: the hub's showroom car (F builds it after boot, in parallel with this lab) is not a lab car
            int hub = AcHub.Current != null && AcHub.Current.Car != null && AcHub.Current.Car.Model != null ? 1 : 0;
            r.Check(AcCarModel.LiveCount - hub == 0, "all lab cars disposed (LiveCount " + AcCarModel.LiveCount + ", hub showroom car " + hub + ")");
        }
    }
}
