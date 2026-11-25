using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// [F, Front] Lab scenarios (DevTools/lab.sh):
    ///  front-tablet   the "ASSETTO CORSA" app against a stand-in of the client's tablet (Editor/LabStubs, found by type name like the real one):
    ///                 link, the home button first with its caption, the menu built into the app frame, Small -> Large on open, VR-ray clicks
    ///                 (TrackedDeviceGraphicRaycaster) through DRIVE -> a car -> TRACK -> a layout -> SETTINGS -> a row, Back, shutdown.
    ///  front-hub      the showroom hub (showroom.kn5 through W's builder, the plain floor otherwise), the menu panel placed in front of the
    ///                 camera, the pages (DRIVE, TRACK, LOBBY, GARAGE, TIMING, SETTINGS, HOW TO PLAY), fonts / skin / words read.
    ///  front-loading  the loading card (world-space, as a headset sees it) for -actrack (default magione) with progress, then the reveal.
    ///  front-session  a real session on -actrack (default magione) with the stand-in tablet: the tablet closes on Loading, the showroom car goes,
    ///                 practice: hub hidden, rules edited in the lobby reach AcSession.Rules (RequestRules) and the car's aids, a GARAGE value goes
    ///                 into the stopped car, START names the session, TIMING page, no tablet = the panel stands on the track, LEAVE TRACK =
    ///                 hub back with its light and showroom car.
    ///  front-garage-repair  (iteration 2) practice on -actrack (magione) at DAMAGE 100 with -accar (ferrari_458): the GARAGE REPAIR rows
    ///                 OFF on a clean car, a 100 km/h front hit (P DebugAddImpact) -> body / body + suspension rows ON with R's estimate in
    ///                 seconds, engine row OFF; REPAIR BODY + SUSPENSION -> R freezes and counts down, the car is clean after. Pictures
    ///                 garage_repair_*.png.
    /// </summary>
    [InitializeOnLoad]
    public static class AcLabFront
    {
        static AcLabFront()
        {
            AcLabRunner.Register("front-tablet", r => Tablet(r));
            AcLabRunner.Register("front-hub", r => Hub(r));
            AcLabRunner.Register("front-loading", r => Loading(r));
            AcLabRunner.Register("front-session", r => Session(r));
            AcLabRunner.Register("front-garage-repair", r => GarageRepair(r));
        }

        private static Vector3 Eye { get { return AcSpace.RoomOrigin + new Vector3(0f, 1.7f, 0f); } }

        private static IEnumerator Ready(AcLabRunner r)
        {
            r.Cam.nearClipPlane = 0.02f;
            r.Cam.farClipPlane = 3000f;
            yield return r.Wait(() => AcFront.Started && AcFront.BootApplied && AcHub.Current != null, 60f, "the front end (install read, the game's ui applied)");
            if (r.Failed) yield break;
            yield return r.Frames(20);
            r.Check(AcLocale.Count > 100, "the game's words read (system/locales: " + AcLocale.Count + " keys; PRACTICE = '" + AcLocale.Session(1) + "')");
            r.Check(AcUi.SkinApplied, "skin.ini / launcher colours applied (accent " + AcUi.Accent + ", title " + AcUi.TitleBack + ")");
            r.Check(AcFonts.Ready, "Segoe UI built from content/fonts (" + AcFonts.Report + ")");
            r.Check(!string.IsNullOrEmpty(AcLocal.CarId), "a car is chosen at boot (" + AcLocal.CarId + "/" + AcLocal.SkinId + ")");
            // REQUESTS A -> F / W -> F: their rows are in SETTINGS before any car or track (FrontBootDone calls AcSfx.Preload / RegisterSettings)
            r.Check(AcSettingsUi.Find("world.weather") != null && AcSettingsUi.Find("world.time") != null, "the TRACK rows (weather, time of day) are in SETTINGS before any track load");
            r.Check(AcSettingsUi.Find("audio.engine") != null, "the AUDIO rows are in SETTINGS before the first car");
        }

        /// <summary>A soft wait (no lab failure on timeout): true when <paramref name="cond"/> came true in time.</summary>
        private static IEnumerator Until(Func<bool> cond, float timeout, bool[] result)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < timeout) yield return null;
            result[0] = cond();
        }

        // ------------------------------------------------------------------ front-hub

        private static IEnumerator Hub(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            AcHub hub = AcHub.Current;
            yield return r.Wait(() => hub.ShowroomReport != "not started" && (hub.ShowroomBuilt || hub.ShowroomReport.StartsWith("the builder") || hub.ShowroomReport.Contains("failed")), 180f, "the showroom build");
            AcLabRunner.Log("showroom: " + hub.ShowroomReport);
            r.Check(hub.ShowroomBuilt, "the showroom kn5 was built by W's AcKn5Builder (" + hub.ShowroomReport + ")");
            r.Aim(Eye, Eye + new Vector3(0f, 0f, 10f), 60f);
            if (hub.Placer != null) hub.Placer.Place(r.Cam, "lab");
            yield return r.Frames(30);
            Canvas panel = hub.Panel;
            if (!r.Check(panel != null, "the hub has its menu panel")) yield break;
            Vector3 pc = panel.transform.position;
            AcLabRunner.Log("panel at " + (pc - AcSpace.RoomOrigin).ToString("0.00") + ", " + panel.GetComponentsInChildren<TMP_Text>(true).Length + " texts");
            AcMenu menu = HubMenu();
            if (!r.Check(menu != null, "the hub menu is registered")) yield break;
            r.Aim(Eye - panel.transform.forward * 0.4f, pc, 62f);
            r.Shot("hub_1_panel", 1280, 720);
            r.Aim(Eye + new Vector3(0f, 0.6f, -6f), Eye + new Vector3(0f, -0.5f, 6f), 80f);
            r.Shot("hub_2_room", 1280, 720);
            yield return Until(() => hub.Car != null, 30f, new bool[1]); // the showroom car builds after the room (slower in a fresh lab copy)
            r.Aim(AcSpace.RoomOrigin + new Vector3(3.5f, 1.3f, 1.5f), AcSpace.RoomOrigin + AcHub.CarSpot + new Vector3(0f, 0.6f, 0f), 60f);
            r.Shot("hub_3_car", 1280, 720);
            r.Check(hub.Car != null, "the chosen car stands on the showroom floor (P's AcCarRig Kind Showroom: " + (hub.Car != null && hub.Car.Car != null ? hub.Car.Car.Id : "none") + ")");
            string[] pages = { "drive", "track", "lobby", "garage", "timing", "settings", "about" };
            for (int i = 0; i < pages.Length; i++)
            {
                menu.Show(pages[i]);
                yield return r.Frames(45); // pictures decode one per frame
                yield return Until(() => false, 1.5f, new bool[1]); // and the batch editor runs frames fast: give the previews real time too
                r.Check(menu.Current != null && menu.Current.Id == pages[i], "the " + pages[i] + " page shows");
                r.Aim(Eye - panel.transform.forward * 0.4f, pc, 62f);
                r.Shot("hub_page_" + (i + 4) + "_" + pages[i], 1280, 720);
            }

            AcLabRunner.Log("ui art: " + AcUiArt.Decoded + " pictures decoded, " + AcUiArt.Failed + " failed, " + AcUiArt.Cached + " cached");
            r.Check(AcUiArt.Decoded > 10 && AcUiArt.Failed == 0, "badges / previews decoded from the install");

            // a desktop mouse click (pointer -1 through the panel's GraphicRaycaster, as the client's monitor mouse) on the GARAGE nav item
            r.Aim(Eye - panel.transform.forward * 0.4f, pc, 62f);
            yield return r.Frames(2);
            GameObject taken = MouseClick(r.Cam, panel, FindButton(menu.Root, "Nav garage"));
            yield return r.Frames(10);
            r.Check(taken != null && menu.Current != null && menu.Current.Id == "garage", "a desktop mouse click on GARAGE opens it (page " + (menu.Current != null ? menu.Current.Id : "-") + ")");
            AcButton aid = FindButton(menu.Root, "Assist AUTO BLIP >");
            int blip = AcFront.AssistInt("garage.autoblip");
            if (aid != null) { MouseClick(r.Cam, panel, aid); yield return r.Frames(5); }
            r.Check(aid != null && AcFront.AssistInt("garage.autoblip") != blip, "a mouse click on a GARAGE aid changes it (AUTO BLIP " + blip + " -> " + AcFront.AssistInt("garage.autoblip") + ")");
            AcSettings.SetInt("garage.autoblip", blip);
            r.Check(FindButtonPrefix(menu.Root, "Setup ") != null, "the GARAGE lists the car's setup rows (setup.ini / tyres.ini)");
            r.Shot("hub_page_11_garage_mouse", 1280, 720);
            // the hub light comes from the showroom's own colorCurves.ini, not from RenderSettings
            r.Check(AcMaterials.GlobalsSet, "the World shaders' light globals are set (showroom colorCurves.ini through AcMaterials.SetLight)");
        }

        /// <summary>The monitor mouse: a screen point over the widget, tested by the canvas's GraphicRaycaster (pointerId -1); the top hit is clicked.</summary>
        private static GameObject MouseClick(Camera cam, Canvas canvas, Component widget)
        {
            EventSystem es = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (es == null) es = new GameObject("Lab EventSystem").AddComponent<EventSystem>();
            var raycaster = canvas != null ? canvas.GetComponent<GraphicRaycaster>() : null;
            if (raycaster == null || widget == null) { AcLabRunner.Log("mouse at '" + (widget != null ? widget.name : "-") + "': no GraphicRaycaster / widget"); return null; }
            if (canvas.worldCamera != cam) canvas.worldCamera = cam;
            var rt = (RectTransform)widget.transform;
            Vector3 target = rt.TransformPoint(rt.rect.center);
            Vector3 screen = cam.WorldToScreenPoint(target);
            var data = new PointerEventData(es) { position = new Vector2(screen.x, screen.y), pointerId = -1, button = PointerEventData.InputButton.Left };
            Hits.Clear();
            raycaster.Raycast(data, Hits);
            GameObject hit = Hits.Count > 0 ? Hits[0].gameObject : null;
            if (hit == null) { AcLabRunner.Log("mouse at '" + widget.name + "' (" + screen.ToString("0") + "): nothing hit"); return null; }
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerEnterHandler);
            GameObject down = ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler);
            data.pointerPress = down;
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerUpHandler);
            GameObject click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (click != null && click == down) ExecuteEvents.Execute(click, data, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerExitHandler);
            AcLabRunner.Log("mouse at '" + widget.name + "': top graphic '" + hit.name + "', click taken by '" + (click != null && click == down ? click.name : "nobody") + "'");
            return click != null && click == down ? click : null;
        }

        private static AcMenu HubMenu()
        {
            AcHub hub = AcHub.Current;
            foreach (AcMenu m in AcFront.AllMenus) if (m != null && !m.Tablet && m.Root != null && hub != null && m.Root.IsChildOf(hub.transform)) return m;
            return null;
        }

        // ------------------------------------------------------------------ front-loading

        private static IEnumerator Loading(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            string id = AcLabBoot.Arg("-actrack", "magione");
            AcContent c = AcFront.Content;
            AcTrackEntry t = c != null ? c.Track(id) : null;
            AcLayoutEntry l = t != null && t.Layouts.Count > 0 ? t.Layouts[0] : null;
            if (!r.Check(l != null, "track " + id + " is in the catalog")) yield break;
            AcLoadingScreen.ForceWorld = true;
            r.Aim(Eye, Eye + new Vector3(0f, 0f, 10f), 70f);
            AcLoading.Show(l, AcLobby.Draft());
            AcLoading.Progress(0.42f, "Building the track: " + l.Name);
            // time based (batch runs ~2000 fps): the bar catches up at 0.5/s and the card fades in at 2.5/s, so give it 1.5 s // ours
            float tShow = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tShow < 1.5f) yield return null;
            AcLoadingScreen s = AcLoadingScreen.Current;
            if (!r.Check(s != null && s.Card != null, "the loading card exists")) yield break;
            Transform card = s.Card.transform;
            r.Aim(card.position - card.forward * 2.4f, card.position, 75f);
            r.Shot("loading_1_card", 1920, 1080); // full size: small Light text loses 1 px strokes at 720p
            r.Check(s.Shown > 0.3f, "the bar follows the progress (" + s.Shown.ToString("0.00") + ")");
            AcLoading.Hide();
            yield return r.Frames(3);
            r.Check(AcLoadingScreen.Current == null || AcLoadingScreen.Current.CoverAlpha > 0.9f, "Hide: black at once");
            float t0 = Time.realtimeSinceStartup;
            while (AcLoadingScreen.Current != null && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            r.Check(AcLoadingScreen.Current == null, "the cover fades away within a second or so (" + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s)");
            AcLoadingScreen.ForceWorld = false;
        }

        // ------------------------------------------------------------------ front-tablet (LabStubs, by reflection: the editor assembly does not reference them)

        private static Type Stub(string name) { return Type.GetType("SomniumSpace.Worlds.SomniumAssetto.LabStubs." + name + ", SomniumAssetto.LabStubs"); }

        private sealed class Fake { public GameObject Root; public Component Control; public CanvasGroup Group; public Canvas Home; public RectTransform HomeRect; public Transform Grid; public Button Back, Resize; public int Resizes; }

        private static Fake BuildFake(Vector3 at, Camera cam)
        {
            var f = new Fake();
            f.Root = new GameObject("Tablet.XR(Clone)");
            f.Root.transform.position = at;
            f.Control = f.Root.AddComponent(Stub("TabletControl"));
            var canvases = new GameObject("Canvases", typeof(RectTransform));
            canvases.transform.SetParent(f.Root.transform, false);
            f.Group = canvases.AddComponent<CanvasGroup>();
            canvases.transform.localScale = Vector3.one * 0.0004f;
            f.Control.GetType().GetField("Group").SetValue(f.Control, f.Group);
            f.Control.GetType().GetMethod("Activate").Invoke(f.Control, null);
            var tabs = new GameObject("Tablet.XR.Tabs", typeof(RectTransform));
            tabs.transform.SetParent(canvases.transform, false);
            tabs.AddComponent(Stub("TabsContainer"));
            var home = new GameObject("Tablet.XR.HomeTab", typeof(RectTransform));
            home.layer = 9;
            home.transform.SetParent(tabs.transform, false);
            home.AddComponent(Stub("HomeTab"));
            f.Home = home.AddComponent<Canvas>();
            f.Home.renderMode = RenderMode.WorldSpace;
            f.Home.worldCamera = cam;
            home.AddComponent<TrackedDeviceGraphicRaycaster>();
            home.AddComponent<GraphicRaycaster>(); // the desktop (non-XR) tablet's mouse: the app canvas gets both, like the home canvas
            f.HomeRect = (RectTransform)home.transform;
            f.HomeRect.sizeDelta = new Vector2(960f, 1080f);
            home.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 1f);
            var content = new GameObject("ContentView", typeof(RectTransform));
            content.transform.SetParent(home.transform, false);
            var cr = (RectTransform)content.transform; cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
            var grid = new GameObject("Grid", typeof(RectTransform));
            grid.transform.SetParent(content.transform, false);
            var gr = (RectTransform)grid.transform; gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one; gr.offsetMin = new Vector2(20f, 20f); gr.offsetMax = new Vector2(-20f, -20f);
            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(220f, 250f);
            layout.spacing = new Vector2(16f, 16f);
            f.Grid = grid.transform;
            // another Dreamport's clone first: never the model for ours
            foreach (string a in new[] { "ETS.Tablet.Button", "App.WORLDS", "App.AVATARS", "App.SOCIAL", "App.MAP", "App.CAMERA" })
            {
                var go = new GameObject(a, typeof(RectTransform), typeof(Image), typeof(Button));
                go.layer = 5;
                go.transform.SetParent(f.Grid, false);
                go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);
                go.AddComponent(Stub("SwitchTabButton"));
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(go.transform, false);
                ((RectTransform)icon.transform).sizeDelta = new Vector2(150f, 150f);
                var cap = new GameObject("Caption", typeof(RectTransform));
                cap.transform.SetParent(go.transform, false);
                ((RectTransform)cap.transform).anchoredPosition = new Vector2(0f, -100f);
                var t = cap.AddComponent<TextMeshProUGUI>();
                t.text = a.Replace("App.", ""); t.fontSize = 30f; t.alignment = TextAlignmentOptions.Center;
            }

            var worlds = new GameObject("Tab.Worlds", typeof(RectTransform));
            worlds.transform.SetParent(tabs.transform, false);
            var wc = new GameObject("ContentView", typeof(RectTransform));
            wc.transform.SetParent(worlds.transform, false);
            wc.SetActive(false);
            var bar = new GameObject("Tablet.XR.StatusBar", typeof(RectTransform));
            bar.transform.SetParent(f.Root.transform, false);
            bar.AddComponent(Stub("TabletStatusBarView"));
            f.Back = new GameObject("Tablet.XR.StatusBar.Back", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            f.Back.transform.SetParent(bar.transform, false);
            f.Back.gameObject.SetActive(false);
            f.Resize = new GameObject("Tablet.XR.StatusBar.Resize", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            f.Resize.transform.SetParent(bar.transform, false);
            Fake ff = f;
            f.Resize.onClick.AddListener(() => { ff.Resizes++; ff.HomeRect.sizeDelta = new Vector2(ff.HomeRect.sizeDelta.x > 1440f ? 960f : 1920f, 1080f); });
            var meshHome = new GameObject("HomeButton");
            meshHome.transform.SetParent(f.Root.transform, false);
            meshHome.AddComponent(Stub("XRInteractableButton"));
            return f;
        }

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        /// <summary>The VR laser: a ray from a hand below the camera through the widget, tested by the canvas's TrackedDeviceGraphicRaycaster; the top hit is clicked.</summary>
        private static GameObject RayClick(Camera cam, Canvas canvas, Component widget)
        {
            EventSystem es = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (es == null) es = new GameObject("Lab EventSystem").AddComponent<EventSystem>();
            var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster == null || widget == null) return null;
            var rt = (RectTransform)widget.transform;
            Vector3 target = rt.TransformPoint(rt.rect.center);
            Vector3 from = cam.transform.position + cam.transform.right * 0.15f - cam.transform.up * 0.25f;
            Vector3 dir = (target - from).normalized;
            var data = new TrackedDeviceEventData(es) { rayPoints = new List<Vector3> { from, target + dir * 0.3f }, layerMask = ~0, pointerId = 7 };
            Hits.Clear();
            raycaster.Raycast(data, Hits);
            GameObject hit = Hits.Count > 0 ? Hits[0].gameObject : null;
            if (hit == null) { AcLabRunner.Log("ray at '" + widget.name + "': nothing hit"); return null; }
            var pd = new TrackedDeviceEventData(es) { pointerId = 7, button = PointerEventData.InputButton.Left };
            ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerEnterHandler);
            GameObject down = ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerDownHandler);
            pd.pointerPress = down;
            ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerUpHandler);
            GameObject click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (click != null && click == down) ExecuteEvents.Execute(click, pd, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerExitHandler);
            AcLabRunner.Log("ray at '" + widget.name + "': top graphic '" + hit.name + "', click taken by '" + (click != null && click == down ? click.name : "nobody") + "'");
            return click != null && click == down ? click : null;
        }

        private static AcButton FindButton(Transform root, string name)
        {
            foreach (AcButton b in root.GetComponentsInChildren<AcButton>(true)) if (b.gameObject.name == name) return b;
            return null;
        }

        private static AcButton FindButtonPrefix(Transform root, string prefix)
        {
            foreach (AcButton b in root.GetComponentsInChildren<AcButton>(false)) if (b.gameObject.name.StartsWith(prefix, StringComparison.Ordinal) && b.Interactable) return b;
            return null;
        }

        private static IEnumerator Tablet(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            if (!r.Check(Stub("TabletControl") != null, "the LabStubs assembly is loaded")) yield break;
            AcTabletApp app = AcTabletApp.Instance;
            if (!r.Check(app != null && app.Link != null, "the tablet app exists (AcFront.Start)")) yield break;
            AcTabletLink link = app.Link;
            link.SearchInterval = 0.2f;
            r.Check(!link.IsLinked, "no tablet yet: not linked");
            Vector3 at = Eye + new Vector3(0f, -0.05f, 0.6f);
            int wokeBefore = (int)Stub("SwitchTabButton").GetField("Woke").GetValue(null);
            Fake f = BuildFake(at, r.Cam);
            yield return r.Wait(() => link.IsLinked, 20f, "the link to the stand-in tablet");
            if (!link.IsLinked) yield break;
            AcLabRunner.Log("link report: " + link.Report);
            yield return r.Frames(40);
            Button ours = null;
            foreach (Transform c in f.Grid) if (c.name == AcTabletLink.Owned + "Button") ours = c.GetComponent<Button>();
            r.Check(ours != null && ours.transform.GetSiblingIndex() == 0, "the ASSETTO CORSA button is the first of the home grid");
            TMP_Text cap = ours != null ? ours.GetComponentInChildren<TMP_Text>(true) : null;
            r.Check(cap != null && cap.text == "ASSETTO CORSA", "its caption reads ASSETTO CORSA ('" + (cap != null ? cap.text : "-") + "')");
            int wokeAfter = (int)Stub("SwitchTabButton").GetField("Woke").GetValue(null);
            r.Check(wokeAfter == wokeBefore + 6, "no client script woke on the clone (" + (wokeAfter - wokeBefore) + " wakes = the 6 stand-in buttons)");
            r.Check(ours != null && ours.GetComponent(Stub("SwitchTabButton")) == null, "the clone was stripped to Unity UI + TMP");
            r.Check(app.Menu != null && app.Menu.Root != null && app.Menu.Root.IsChildOf(link.Frame), "the menu is built into the app's frame");
            // the icon: painted with the launcher's logo at boot (before the link); the button must show THAT texture, alive
            r.Check(app.IconFromInstall && app.Icon != null && link.IconShown == app.Icon, "the home button shows the launcher-logo icon (from install " + app.IconFromInstall + ", shown " + (link.IconShown != null ? link.IconShown.name : "none") + ")");
            app.PaintIconFromInstall(null); // a no-op once painted: the sprite must not end up on a destroyed texture
            yield return r.Frames(10);
            r.Check(link.IconShown != null && link.IconShown == app.Icon, "the icon texture is still alive after the update checks");
            r.Aim(at + new Vector3(0f, 0.02f, -0.45f), at, 60f);
            r.Shot("tablet_1_home", 1280, 720);
            if (ours != null) ours.onClick.Invoke();
            yield return r.Frames(20);
            r.Check(link.AppOpen, "the app opens from its home button");
            r.Check(f.Resizes == 1, "a Small tablet was made Large for the 1600 x 900 menu (" + f.Resizes + " presses)");
            yield return r.Frames(20);
            r.Aim(at + new Vector3(0f, 0.02f, -0.55f), at, 60f);
            r.Shot("tablet_2_app", 1280, 720);

            AcMenu menu = app.Menu;
            Canvas canvas = link.AppCanvas;
            // DRIVE: a ray click on the nav item, then on an owned car
            GameObject taken = RayClick(r.Cam, canvas, FindButton(menu.Root, "Nav drive"));
            yield return r.Frames(30);
            r.Check(taken != null && menu.Current != null && menu.Current.Id == "drive", "a VR ray click on DRIVE opens the car list (page " + (menu.Current != null ? menu.Current.Id : "-") + ")");
            AcButton car = FindButtonPrefix(menu.Root, "Car ");
            if (r.Check(car != null, "the DRIVE page lists cars"))
            {
                string carName = car.gameObject.name; // the click rebuilds the grid (the button is destroyed)
                taken = RayClick(r.Cam, canvas, car);
                yield return r.Frames(40);
                r.Check(!ReferenceEquals(taken, null), "a ray click picks " + carName); // the clicked button is destroyed by the grid refresh (Unity null)
                r.Shot("tablet_3_drive", 1280, 720);
                AcButton choose = FindButton(menu.Root, "Drive Choose");
                string before = AcLocal.CarId;
                if (choose != null) choose.Press();
                yield return r.Frames(5);
                r.Check(!string.IsNullOrEmpty(AcLocal.CarId) && AcSettings.Text("drive.car", "") == AcLocal.CarId, "DRIVE THIS CAR sets AcLocal.CarId and drive.car (" + before + " -> " + AcLocal.CarId + "/" + AcLocal.SkinId + ")");
            }

            // TRACK: nav, a track, a layout
            taken = RayClick(r.Cam, canvas, FindButton(menu.Root, "Nav track"));
            yield return r.Frames(40);
            r.Check(menu.Current != null && menu.Current.Id == "track", "a ray click on TRACK opens the track list");
            AcButton track = FindButtonPrefix(menu.Root, "Track ks_") ?? FindButtonPrefix(menu.Root, "Track ");
            if (track != null) { RayClick(r.Cam, canvas, track); yield return r.Frames(40); }
            AcButton lay = FindButtonPrefix(menu.Root, "Layout ");
            r.Check(lay != null, "the chosen track lists its layouts");
            r.Shot("tablet_4_track", 1280, 720);

            // SETTINGS: nav, then a row's ">" (front.large flips), and back
            taken = RayClick(r.Cam, canvas, FindButton(menu.Root, "Nav settings"));
            yield return r.Frames(20);
            r.Check(menu.Current != null && menu.Current.Id == "settings", "a ray click on SETTINGS opens the settings");
            bool large = AcSettings.Flag("front.large", true);
            AcButton row = FindButton(menu.Root, "Setting front.large >");
            if (r.Check(row != null, "the TABLET rows are drawn (front.large)"))
            {
                RayClick(r.Cam, canvas, row);
                yield return r.Frames(5);
                r.Check(AcSettings.Flag("front.large", true) != large, "a ray click on a settings row changes it");
                AcSettings.SetFlag("front.large", large);
            }

            r.Shot("tablet_5_settings", 1280, 720);

            // RACE LOBBY / TIMING / GARAGE with the desktop mouse (pointer -1 through the app canvas's GraphicRaycaster)
            r.Aim(at + new Vector3(0f, 0.02f, -0.55f), at, 60f);
            yield return r.Frames(2);
            taken = MouseClick(r.Cam, canvas, FindButton(menu.Root, "Nav lobby"));
            yield return r.Frames(20);
            r.Check(taken != null && menu.Current != null && menu.Current.Id == "lobby", "a desktop mouse click on RACE LOBBY opens it");
            AcButton start = FindButton(menu.Root, "Lobby Start");
            r.Check(start != null && start.Label == AcFront.StartLabel(AcLobby.Rules), "the START button names the lobby's session ('" + (start != null ? start.Label : "-") + "')");
            AcButton leave = FindButton(menu.Root, "Lobby Leave");
            r.Check(leave != null && !leave.Interactable, "LEAVE / JOIN is grey in the hub with no session (" + (leave != null ? leave.Label : "-") + ")");
            r.Shot("tablet_6_lobby", 1280, 720);
            taken = MouseClick(r.Cam, canvas, FindButton(menu.Root, "Nav timing"));
            yield return r.Frames(20);
            r.Check(taken != null && menu.Current != null && menu.Current.Id == "timing", "a desktop mouse click on TIMING opens it");
            r.Shot("tablet_7_timing", 1280, 720);
            taken = MouseClick(r.Cam, canvas, FindButton(menu.Root, "Nav garage"));
            yield return r.Frames(40);
            r.Check(taken != null && menu.Current != null && menu.Current.Id == "garage", "a desktop mouse click on GARAGE opens it");
            r.Shot("tablet_8_garage", 1280, 720);

            // the car is held while the app is open and the player drives (S reads AcFront.HoldCar; DisarmPedals on release in VR)
            bool wasDriving = AcLocal.Driving;
            AcLocal.Driving = true; // simulated: seated driving (S's flag; restored below)
            yield return r.Frames(3);
            r.Check(AcFront.HoldCar && AcLocal.TabletOpen, "HoldCar while driving with the app open (HoldCar " + AcFront.HoldCar + ", TabletOpen " + AcLocal.TabletOpen + ")");
            bool tabletUp = false;
            try { tabletUp = AcDriveInput.TabletUp; } catch (Exception e) { AcLabRunner.Log("AcDriveInput.TabletUp: " + e.Message); }
            r.Check(tabletUp, "S's AcDriveInput.TabletUp follows HoldCar (no throttle)");

            f.Back.onClick.Invoke();
            yield return r.Frames(5);
            r.Check(!AcFront.HoldCar, "HoldCar ends when the app closes (the pedals re-arm after both triggers are released - S)");
            AcLocal.Driving = wasDriving;
            if (ours != null) ours.onClick.Invoke();
            yield return r.Frames(10);
            r.Check(link.AppOpen, "the app opens again");
            f.Back.onClick.Invoke();
            yield return r.Frames(10);
            r.Check(!link.AppOpen && f.Resizes == 4, "Back closes the app and gives the tablet its size back each time (2 opens: " + f.Resizes + " presses)");
            AcTabletApp.Shutdown();
            yield return r.Frames(5);
            int left = 0;
            foreach (Transform c in f.Grid) if (c.name.StartsWith(AcTabletLink.Owned)) left++;
            r.Check(left == 0, "shutdown takes the button off the client's tablet (" + left + " left)");
            UnityEngine.Object.Destroy(f.Root);
        }

        // ------------------------------------------------------------------ front-session (a real track through R, the editor is the master alone)

        private static IEnumerator Session(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            string id = AcLabBoot.Arg("-actrack", "magione");
            AcContent c = AcFront.Content;
            AcTrackEntry t = c != null ? c.Track(id) : null;
            AcLayoutEntry layout = t != null && t.Layouts.Count > 0 ? t.Layouts[0] : null;
            if (!r.Check(layout != null && layout.Owned, "track " + id + " is in the catalog and owned")) yield break;
            AcHub hub = AcHub.Current;
            var ok = new bool[1];
            yield return Until(() => hub.Car != null, 120f, ok);
            r.Check(ok[0], "the showroom car stands in the hub before the load");

            // the stand-in tablet, app open
            AcTabletApp app = AcTabletApp.Instance;
            AcTabletLink link = app != null ? app.Link : null;
            if (!r.Check(link != null && Stub("TabletControl") != null, "the tablet app and the LabStubs exist")) yield break;
            link.SearchInterval = 0.2f;
            Vector3 at = Eye + new Vector3(0f, -0.05f, 0.6f);
            Fake f = BuildFake(at, r.Cam);
            yield return r.Wait(() => link.IsLinked, 20f, "the link to the stand-in tablet");
            if (!link.IsLinked) yield break;
            yield return r.Frames(20);
            link.OpenApp();
            yield return r.Frames(10);
            r.Check(link.AppOpen, "the app is open before the load");
            int deactivatesBefore = (int)f.Control.GetType().GetField("DeactivateCalls").GetValue(f.Control);

            // LOAD: the tablet closes, the showroom car goes
            bool loadOk = AcFront.RequestLoad(layout, "lab");
            if (!r.Check(loadOk, "AcFront.RequestLoad(" + layout.Key + ") accepted (alone = master)")) yield break;
            yield return r.Wait(() => AcSession.Phase == AcPhase.Loading || AcSession.OnTrack, 30f, "the Loading phase");
            yield return r.Frames(5);
            int deactivates = (int)f.Control.GetType().GetField("DeactivateCalls").GetValue(f.Control);
            r.Check(!link.AppOpen && deactivates > deactivatesBefore, "the tablet closed when the track started loading (DeActivate calls " + deactivatesBefore + " -> " + deactivates + ")");
            r.Check(hub.Car == null, "the showroom car was removed for the load (no second car model while driving)");
            yield return r.Wait(() => AcSession.OnTrack, 600f, "practice on " + layout.Key);
            if (r.Failed) yield break;
            yield return r.Wait(() => AcLocal.Car != null && AcLocal.Car.Physics != null, 120f, "the local car in the pit box");
            if (r.Failed) yield break;
            yield return r.Frames(30);
            r.Check(!hub.Shown && !AcLoading.Showing, "practice: the hub is hidden and the loading card gone");
            r.Check(hub.Car == null, "practice: no showroom car is built while on the track");

            // rules edited in the lobby during practice reach R (debounced 1 s) and the car's aids follow
            AcMenu menu = app.Menu;
            if (menu == null) foreach (AcMenu m in AcFront.AllMenus) if (m != null && m.Root != null) { menu = m; break; }
            AcAssists aidsBefore = AcLocal.Car.Physics.Assists;
            int wearBefore = AcSession.Rules.TyreWearRate;
            if (menu != null) { menu.Show("lobby"); yield return r.Frames(5); }
            AcButton more = menu != null ? FindButton(menu.Root, "Rule TYRE WEAR RATE >") : null;
            if (more != null) more.Press();
            yield return Until(() => AcSession.Rules.TyreWearRate != wearBefore, 5f, ok);
            r.Check(ok[0] && AcSession.Rules.TyreWearRate == AcLobby.Rules.TyreWearRate, "a lobby rule change in practice reaches the session (TYRE_WEAR_RATE " + wearBefore + " -> " + AcSession.Rules.TyreWearRate + ")");
            yield return r.Frames(20);
            r.Check(!ReferenceEquals(AcLocal.Car.Physics.Assists, aidsBefore), "the car's aids were re-applied for the new rules");
            if (more != null) { FindButton(menu.Root, "Rule TYRE WEAR RATE <").Press(); }

            // START names the session
            AcSessionType was = AcLobby.Rules.Session;
            AcLobby.Rules.Session = AcSessionType.Qualify;
            AcLobby.Changed();
            // the page ticks at 4 Hz only while it is visible: the tablet closed for the load, so open the app again (as the player would)
            if (!link.AppOpen) link.OpenApp();
            AcButton start = menu != null ? FindButton(menu.Root, "Lobby Start") : null;
            string wantStart = "START " + AcLocale.Session((int)AcSessionType.Qualify);
            yield return Until(() => start != null && start.Label == wantStart, 0.8f, ok); // under R's 1 s rules debounce
            r.Check(start != null && start.Label == "START " + AcLocale.Session((int)AcSessionType.Qualify), "the START button says the lobby's session ('" + (start != null ? start.Label : "-") + "')");
            AcLobby.Rules.Session = was;
            AcLobby.Changed();
            yield return r.Frames(10);

            // a GARAGE value goes into the stopped car
            List<AcSetupRow> rows = AcFront.SetupFor(AcLocal.Car.Car);
            yield return Until(() => (rows = AcFront.SetupFor(AcLocal.Car.Car)) != null, 10f, ok);
            AcSetupRow fuel = null;
            if (rows != null) foreach (AcSetupRow s in rows) if (s.Key == "FUEL") fuel = s;
            if (r.Check(fuel != null, "the car's setup.ini has a FUEL row"))
            {
                float want = Mathf.Clamp(fuel.Min + 3f * fuel.Step, fuel.Min, fuel.Max);
                AcFront.SetSetup(AcLocal.Car.Car.Id, "FUEL", want);
                yield return Until(() => !AcFront.SetupWaiting, 5f, ok);
                float now = AcLocal.Car.Physics.Core != null ? AcLocal.Car.Physics.Core.FuelLitres : -1f;
                r.Check(ok[0] && Mathf.Abs(now - want) < 0.01f, "a GARAGE FUEL change went into the stopped car (" + want + " l wanted, " + now + " l in the car)");
                AcSettings.SetText("setup." + AcLocal.Car.Car.Id + ".FUEL", "");
            }

            // TIMING
            if (menu != null) { menu.Show("timing"); yield return r.Frames(10); }
            r.Check(menu != null && menu.Current != null && menu.Current.Id == "timing", "the TIMING page shows in practice");

            // no tablet: the panel stands on the track in front of the player on foot
            AcTabletApp.Shutdown();
            UnityEngine.Object.Destroy(f.Root);
            yield return Until(() => hub.PanelOnTrack && hub.Panel != null && hub.Panel.gameObject.activeInHierarchy, 3f, ok);
            r.Check(ok[0] || AcLocal.Seated, "without a tablet the menu panel stands on the track (on foot: " + !AcLocal.Seated + ", on track: " + hub.PanelOnTrack + ")");
            if (ok[0])
            {
                Camera head = AcPlayer.Head();
                if (head == null)
                {
                    // the editor has no player: stand the lab camera where R would put the player, beside the car in its pit box
                    head = r.Cam;
                    Transform carT = AcLocal.Car.Root.transform;
                    Vector3 eye = carT.position + carT.right * 2.5f + Vector3.up * 1.7f; // ours
                    r.Aim(eye, eye + carT.forward * 10f, 62f);
                    yield return r.Frames(2);
                }
                hub.Placer.Place(head, "lab");
                yield return r.Frames(5);
                Transform p = hub.Panel.transform;
                r.Aim(p.position - p.forward * 2.6f + Vector3.up * 0.2f, p.position, 62f);
                r.Shot("session_1_panel_on_track", 1280, 720);
            }

            // LEAVE TRACK: the hub comes back with its own light and the showroom car
            bool left = AcFront.LeaveOrJoin("lab");
            r.Check(left, "LEAVE TRACK accepted (master)");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Hub, 30f, "back in the hub");
            yield return r.Frames(10);
            r.Check(hub.Shown && !hub.PanelOnTrack, "the hub is shown and the panel is back in it");
            yield return Until(() => hub.Car != null, 120f, ok);
            r.Check(ok[0], "the showroom car is built again in the hub");
            r.Aim(AcSpace.RoomOrigin + new Vector3(3.5f, 1.3f, 1.5f), AcSpace.RoomOrigin + AcHub.CarSpot + new Vector3(0f, 0.6f, 0f), 60f);
            r.Shot("session_2_hub_again", 1280, 720);
        }
        // ------------------------------------------------------------------ front-garage-repair (iteration 2, DAMAGE_DESIGN 10 F)

        private static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        private static string RowSeconds(Transform root, string label)
        {
            Transform row = FindDeep(root, "Repair " + label);
            Transform s = row != null ? row.Find("Seconds") : null;
            TextMeshProUGUI t = s != null ? s.GetComponent<TextMeshProUGUI>() : null;
            return t != null ? t.text : "?";
        }

        private static IEnumerator GarageShot(AcLabRunner r, AcHub hub, string name)
        {
            if (hub == null || hub.Panel == null || !hub.Panel.gameObject.activeInHierarchy) { AcLabRunner.Log("garage-repair: no panel on the track for " + name); yield break; }
            Transform p = hub.Panel.transform;
            r.Aim(p.position - p.forward * 2.4f + Vector3.up * 0.1f, p.position, 62f);
            yield return r.Frames(3);
            r.Shot(name, 1280, 720);
        }

        private static IEnumerator GarageRepair(AcLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            AcContent c = AcFront.Content;
            string id = AcLabBoot.Arg("-actrack", "magione");
            AcTrackEntry t = c != null ? c.Track(id) : null;
            AcLayoutEntry layout = t != null && t.Layouts.Count > 0 ? t.Layouts[0] : null;
            if (!r.Check(layout != null && layout.Owned, "track " + id + " owned")) yield break;
            AcCarEntry car = c.Car(AcLabBoot.Arg("-accar", "ferrari_458"));
            if (car == null || !car.Owned) for (int i = 0; i < c.Cars.Count && (car == null || !car.Owned); i++) car = c.Cars[i];
            // flow pass 2026-11-02: through F (AcLocal.CarId follows) - R's ChooseCar alone left the GARAGE's SETUP head on the boot car (ABARTH 500)
            r.Check(AcFront.ChooseCar(car, car.Skins.Count > 0 ? car.Skins[0].Id : "", "lab") && AcLocal.CarId == car.Id, "F's ChooseCar " + car.Id + " (AcLocal.CarId " + AcLocal.CarId + ")");
            AcRules rules = AcRace.DefaultRules();
            r.Check(rules.DamageMultiplier == AcDamageRules.Current.DefaultMultiplier, "R's lobby default DAMAGE_MULTIPLIER = server_cfg.ini (" + rules.DamageMultiplier + " / " + AcDamageRules.Current.DefaultMultiplier + ")");
            rules.Session = AcSessionType.Practice; rules.AiCount = 0; rules.DamageMultiplier = 100;
            r.Check(AcRace.RequestLoad(layout, rules), "RequestLoad " + layout.Key + " (practice, DAMAGE 100)");
            yield return r.Wait(() => AcSession.OnTrack && AcLocal.Car != null && AcLocal.Car.Physics != null, 900f, "my car on " + layout.Key);
            if (r.Failed) yield break;
            AcCarPhysics ph = AcLocal.Car.Physics;
            var ok = new bool[1];
            yield return Until(() => AcRace.InPitBoxStopped, 20f, ok);
            r.Check(ok[0], "my car stands in its pit box (R: InPitBoxStopped)");
            r.Check(ph.DamageRate > 0.99f, "DamageRate from DAMAGE 100 = " + ph.DamageRate);
            AcHub hub = AcHub.Current;
            yield return Until(() => hub.PanelOnTrack && hub.Panel != null && hub.Panel.gameObject.activeInHierarchy, 5f, ok);
            Camera head = AcPlayer.Head();
            if (head == null)
            {
                head = r.Cam;
                Transform carT = AcLocal.Car.Root.transform;
                Vector3 eye = carT.position + carT.right * 2.5f + Vector3.up * 1.7f;
                r.Aim(eye, eye + carT.forward * 10f, 62f);
                yield return r.Frames(2);
            }
            if (hub.Placer != null) hub.Placer.Place(head, "lab");
            AcMenu menu = null;
            foreach (AcMenu m in AcFront.AllMenus) if (m != null && m.Root != null) { menu = m; break; }
            if (!r.Check(menu != null, "a menu to show the GARAGE on")) yield break;
            menu.Show("garage");
            yield return r.Frames(20);
            AcButton body = FindButton(menu.Root, "Repair REPAIR BODY button"), susp = FindButton(menu.Root, "Repair REPAIR BODY + SUSPENSION button"), eng = FindButton(menu.Root, "Repair REPAIR ENGINE button");
            if (!r.Check(body != null && susp != null && eng != null, "the three REPAIR rows exist")) yield break;
            r.Check(!body.Interactable && !susp.Interactable && !eng.Interactable, "clean car: every row OFF");
            yield return GarageShot(r, hub, "garage_repair_1_clean");

            r.Check(ph.DebugAddImpact(AcDamageZone.Front, 100f, 0), "a 100 km/h front hit at the front-left corner counted");
            yield return Until(() => body.Interactable, 3f, ok);
            AcCarDamage d = ph.Damage;
            AcLabRunner.Log("garage-repair: damage F " + d.Zone[0].ToString("0") + " km/h, body " + d.BodyPct.ToString("0") + " %, susp " + d.SuspPct.ToString("0") + " %, engine " + d.EnginePct.ToString("0")
                            + " %; seconds body " + d.RepairSeconds(AcRepair.Body).ToString("0.0") + " / +susp " + d.RepairSeconds(AcRepair.Suspension).ToString("0.0") + " / engine " + d.RepairSeconds(AcRepair.Engine).ToString("0.0"));
            r.Check(body.Interactable, "damaged body: REPAIR BODY ON");
            r.Check(susp.Interactable, "REPAIR BODY + SUSPENSION ON");
            r.Check(!eng.Interactable, "engine undamaged: REPAIR ENGINE OFF");
            string wantBody = Mathf.CeilToInt(d.RepairSeconds(AcRepair.Body)) + " s", wantSusp = Mathf.CeilToInt(d.RepairSeconds(AcRepair.Suspension | AcRepair.Body)) + " s";
            string gotBody = RowSeconds(menu.Root, "REPAIR BODY"), gotSusp = RowSeconds(menu.Root, "REPAIR BODY + SUSPENSION");
            r.Check(gotBody == wantBody && gotSusp == wantSusp, "row seconds = RepairSeconds: body '" + gotBody + "' (want " + wantBody + "), body + susp '" + gotSusp + "' (want " + wantSusp + ")");
            yield return GarageShot(r, hub, "garage_repair_2_damaged");

            // the timed repair on a lighter hit (the 100 km/h one takes AC's 200 + 300 s): P repairs instantly, then a 12 km/h front touch
            // (BodyPct 20 -> 2 x BODY_REPAIR_TIME_SEC) and REPAIR BODY. The page ticks at 4 Hz: real-time waits before reading its texts.
            ph.Repair(AcRepair.All);
            float tw = Time.realtimeSinceStartup + 0.6f;
            yield return r.Wait(() => Time.realtimeSinceStartup > tw, 5f, "a page tick");
            r.Check(!body.Interactable && RowSeconds(menu.Root, "REPAIR BODY") == "-", "instant Repair(All): rows OFF, estimate '-' ('" + RowSeconds(menu.Root, "REPAIR BODY") + "')");
            r.Check(ph.DebugAddImpact(AcDamageZone.Front, 12f, -1), "a 12 km/h front touch counted (no corner)");
            float total = d.RepairSeconds(AcRepair.Body);
            yield return Until(() => body.Interactable, 3f, ok);
            body.Press();
            yield return r.Frames(5);
            r.Check(AcRace.RepairActive && ph.Frozen, "REPAIR BODY started: R froze the car (" + AcRace.RepairTotalSeconds.ToString("0.0") + " s)");
            r.Check(Mathf.Abs(AcRace.RepairTotalSeconds - total) < 0.01f, "repair time = RepairSeconds(Body) (" + total.ToString("0.0") + " s)");
            tw = Time.realtimeSinceStartup + 1.6f;
            yield return r.Wait(() => Time.realtimeSinceStartup > tw, 5f, "a page tick");
            yield return GarageShot(r, hub, "garage_repair_3_repairing");
            yield return r.Wait(() => !AcRace.RepairActive, total + 10f, "the repair countdown");
            tw = Time.realtimeSinceStartup + 0.6f;
            yield return r.Wait(() => Time.realtimeSinceStartup > tw, 5f, "a page tick");
            r.Check(d.Zone[0] == 0f && d.SuspPct == 0f && !ph.Frozen, "after the repair: body clean, the car released");
            r.Check(!body.Interactable && !susp.Interactable && RowSeconds(menu.Root, "REPAIR BODY") == "-", "clean again: rows OFF, estimate '-'");
            yield return GarageShot(r, hub, "garage_repair_4_done");
            AcFront.LeaveOrJoin("lab");
            yield return r.Wait(() => AcSession.Phase == AcPhase.Hub, 30f, "back in the hub");
        }

    }
}
