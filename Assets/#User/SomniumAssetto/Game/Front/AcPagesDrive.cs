using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] DRIVE: every car of the install (owned ones selectable, DLC stubs greyed "not installed"), filtered by brand; the chosen car's
    /// ui_car.json name / brand / class / specs, its skins as preview.jpg thumbnails decoded on demand at reduced size (one page at a time, never
    /// all 965), the skin's own name / number / driver; DRIVE THIS CAR -> AcLocal.CarId / SkinId, AcSettings drive.car / drive.skin,
    /// AcRace.ChooseCar (R respawns the car in the pit box on a track) and the showroom car in the hub.
    /// </summary>
    public sealed class AcPageDrive : AcPage
    {
        public override string Id { get { return "drive"; } }
        public override string Caption { get { return "DRIVE"; } }
        public override bool Available { get { return AcFront.Content != null; } }
        public override string WhyNot { get { return "Your Assetto Corsa install is not read yet."; } }

        private const int BrandChips = 8;
        private readonly List<AcCarEntry> _cars = new List<AcCarEntry>();
        private List<string> _brands = new List<string>();
        private List<AcSkinEntry> _skins = new List<AcSkinEntry>();
        private string _brand = "";
        private int _brandPage;
        private AcCarEntry _car;
        private AcSkinEntry _skin;
        private AcGrid _grid, _skinGrid;
        private RectTransform _brandRow, _detail;
        private RawImage _preview;
        private TextMeshProUGUI _name, _line, _specs, _skinLine, _current;
        private AcButton _choose;
        private string _currentShown;

        protected override void Build()
        {
            Title("DRIVE  -  CHOOSE YOUR CAR");
            AcContent c = AcFront.Content;
            if (c == null) return;
            _brands = AcCarFilter.Brands(c.Cars, true);
            _brandRow = AcUi.Place(Root, "Brands", 0f, 54f, W, 40f);
            _grid = AcGrid.Create(Root, "Cars", 0f, 104f, 3, 4, 236f, 120f, 10f);
            _grid.Fill = FillCar;
            _current = AcUi.OneLine(AcUi.Label(Root, "Current", "", 18f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 0f, 670f, 728f, 26f), 11f);
            _choose = AcUi.Button(Root, "DRIVE THIS CAR", 0f, 700f, 728f, 50f, 26f, Choose);
            _choose.gameObject.name = "Drive Choose";
            _choose.PressSound = "select";
            _choose.Refused = ChooseRefused;

            _detail = AcUi.Place(Root, "Detail", 748f, 104f, W - 748f, H - 104f);
            float dw = W - 748f;
            _name = AcUi.OneLine(AcUi.Label(_detail, "Name", "", 30f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, dw, 40f, AcFonts.Face.Light), 16f);
            _line = AcUi.OneLine(AcUi.Label(_detail, "Line", "", 20f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 0f, 40f, dw, 26f), 12f);
            _specs = AcUi.Label(_detail, "Specs", "", 17f, AcUi.Text, TextAlignmentOptions.TopLeft, 0f, 68f, dw, 26f);
            AcUi.Box(_detail, "Preview back", 0f, 98f, dw, 294f, new Color(0f, 0f, 0f, 0.6f));
            _preview = AcUi.Place(_detail, "Preview", 0f, 98f, dw, 294f).gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false;
            _preview.color = new Color(1f, 1f, 1f, 0f);
            _skinLine = AcUi.OneLine(AcUi.Label(_detail, "Skin", "", 19f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 396f, dw, 28f), 11f);
            _skinGrid = AcGrid.Create(_detail, "Skins", 0f, 430f, 4, 2, 122f, 70f, 8f);
            _skinGrid.Fill = FillSkin;

            AcCarEntry start = c.Car(AcLocal.CarId);
            if (start == null) foreach (AcCarEntry e in c.Cars) if (e.Owned) { start = e; break; }
            Filter("");
            Select(start, start != null ? start.Skin(AcLocal.SkinId) : null, true);
        }

        private void Filter(string brand)
        {
            _brand = brand ?? "";
            AcCarFilter.Filter(AcFront.Content.Cars, _brand, true, _cars);
            _grid.Count = _cars.Count;
            _grid.Page = 0;
            if (_car != null) { int i = _cars.IndexOf(_car); if (i >= 0) _grid.ShowItem(i); }
            _grid.Refresh();
            BuildBrands();
        }

        private void BuildBrands()
        {
            AcUi.Clear(_brandRow);
            int pages = AcPaging.Pages(_brands.Count, BrandChips);
            _brandPage = AcPaging.Clamp(_brandPage, _brands.Count, BrandChips);
            float x = 0f;
            AcButton prev = AcUi.Button(_brandRow, "<", x, 0f, 40f, 40f, 22f, BrandsPrev);
            prev.Interactable = _brandPage > 0;
            x += 48f;
            AcButton all = AcUi.Button(_brandRow, "ALL BRANDS", x, 0f, 130f, 40f, 17f, ShowAll);
            all.Selected = _brand.Length == 0;
            all.gameObject.name = "Brand all";
            x += 138f;
            for (int i = _brandPage * BrandChips; i < _brands.Count && i < (_brandPage + 1) * BrandChips; i++)
            {
                string b = _brands[i];
                AcButton chip = AcUi.Button(_brandRow, b.ToUpperInvariant(), x, 0f, 120f, 40f, 16f, () => Filter(b));
                chip.Selected = string.Equals(b, _brand, StringComparison.OrdinalIgnoreCase);
                chip.gameObject.name = "Brand " + b;
                x += 126f;
            }

            AcButton next = AcUi.Button(_brandRow, ">", W - 40f, 0f, 40f, 40f, 22f, BrandsNext);
            next.Interactable = _brandPage < pages - 1;
            Menu.ApplyLayers();
        }

        private void BrandsPrev() { _brandPage--; BuildBrands(); }
        private void BrandsNext() { _brandPage++; BuildBrands(); }
        private void ShowAll() { Filter(""); }

        private void FillCar(int item, RectTransform cell)
        {
            AcCarEntry car = _cars[item];
            AcButton b = cell.gameObject.AddComponent<AcButton>();
            b.Transparent = true;
            b.Build("", 18f, () => Select(car, null, false));
            b.gameObject.name = "Car " + car.Id;
            b.Selected = car == _car;
            AcUi.Box(cell, "Badge back", 8f, 10f, 68f, 68f, new Color(0f, 0f, 0f, 0.35f));
            AcUi.Picture(cell, "Badge", 10f, 12f, 64f, 64f, car.BadgePath, 128, car.Owned ? Color.white : new Color(1f, 1f, 1f, 0.35f));
            TextMeshProUGUI n = AcUi.Label(cell, "Name", car.Name, 19f, car.Owned ? AcUi.Text : AcUi.Dim, TextAlignmentOptions.TopLeft, 84f, 8f, 146f, 64f);
            n.enableAutoSizing = true; n.fontSizeMax = 19f; n.fontSizeMin = 12f;
            AcUi.OneLine(AcUi.Label(cell, "Line", car.Owned ? AcUiFormat.CarLine(car.Brand, car.Class) : "NOT INSTALLED", 14f, car.Owned ? AcUi.Grey : new Color(0.85f, 0.35f, 0.3f, 1f), TextAlignmentOptions.TopLeft, 84f, 78f, 146f, 22f), 9f);
            bool allowed = AcCarFilter.Allowed(AcSession.OnTrack ? AcSession.Rules.AllowedCars : null, car.Id);
            if (car.Owned && !allowed) AcUi.OneLine(AcUi.Label(cell, "Not allowed", "NOT IN THIS RACE", 13f, AcUi.Gold, TextAlignmentOptions.TopLeft, 84f, 98f, 146f, 20f), 9f);
        }

        private void Select(AcCarEntry car, AcSkinEntry skin, bool quiet)
        {
            _car = car;
            _skins = AcCarFilter.SkinsInOrder(car);
            _skin = skin ?? (car != null && car.Id == AcLocal.CarId ? car.Skin(AcLocal.SkinId) : null) ?? (_skins.Count > 0 ? _skins[0] : null);
            if (_grid != null)
            {
                int i = car != null ? _cars.IndexOf(car) : -1;
                if (quiet && i >= 0) _grid.ShowItem(i); // the page opens on the current car
                _grid.Refresh();
            }

            ShowCar();
            Menu.ApplyLayers();
        }

        private void ShowCar()
        {
            if (_car == null)
            {
                _name.text = "No car";
                _line.text = _specs.text = _skinLine.text = "";
                _skinGrid.Count = 0;
                _skinGrid.Refresh();
                return;
            }

            _name.text = _car.Name;
            _line.text = _car.Owned ? AcUiFormat.CarLine(_car.Brand, _car.Class) + (string.IsNullOrEmpty(_car.Year) ? "" : "  -  " + _car.Year) : "NOT INSTALLED (DLC)";
            _specs.text = AcUiFormat.Specs(_car.Specs);
            _skinGrid.Count = _skins.Count;
            int si = _skin != null ? _skins.IndexOf(_skin) : -1;
            if (si >= 0) _skinGrid.ShowItem(si); else _skinGrid.Page = 0;
            _skinGrid.Refresh();
            ShowSkin();
            _choose.Interactable = _car.Owned && _skin != null;
        }

        private void ShowSkin()
        {
            string path = _skin != null ? _skin.PreviewPath : null;
            if (path == null && _car != null && !_car.Owned)
            {
                // a DLC stub shows its dlc_preview.png
                string dlc = System.IO.Path.Combine(System.IO.Path.Combine(_car.Dir, "ui"), "dlc_preview.png");
                if (System.IO.File.Exists(dlc)) path = dlc;
            }

            _preview.rectTransform.sizeDelta = new Vector2(W - 748f, 294f);
            _preview.rectTransform.anchoredPosition = new Vector2(0f, -98f);
            AcUiArt.Bind(_preview, path, 640, Color.white, true);
            if (_skin == null) { _skinLine.text = ""; return; }
            string s = _skin.Name;
            if (!string.IsNullOrEmpty(_skin.Number)) s += "   #" + _skin.Number;
            if (!string.IsNullOrEmpty(_skin.Driver)) s += "   " + _skin.Driver;
            _skinLine.text = s;
        }

        private void FillSkin(int item, RectTransform cell)
        {
            AcSkinEntry skin = _skins[item];
            AcButton b = cell.gameObject.AddComponent<AcButton>();
            b.Transparent = true;
            b.Build("", 14f, () => { _skin = skin; _skinGrid.Refresh(); ShowSkin(); Menu.ApplyLayers(); });
            b.gameObject.name = "Skin " + skin.Id;
            b.Selected = skin == _skin;
            AcUi.Picture(cell, "Thumb", 3f, 3f, 116f, 64f, skin.PreviewPath, 192, Color.white);
        }

        private void Choose()
        {
            if (_car == null || !_car.Owned || _skin == null) return;
            if (AcSession.OnTrack && !AcCarFilter.Allowed(AcSession.Rules.AllowedCars, _car.Id))
            {
                Toast(_car.Name + " is not allowed by the instance master's rules for this session.");
                AcUi.Sound("negative");
                return;
            }

            // R refuses during a race / for a car the rules do not allow (and toasts why itself): nothing is kept then
            if (!AcFront.ChooseCar(_car, _skin.Id, "tablet")) { AcUi.Sound("negative"); return; }
            Toast("Your car: " + _car.Name + " (" + _skin.Name + ")" + (AcSession.OnTrack ? " - waiting in your pit box." : "."));
        }

        private void ChooseRefused()
        {
            Toast(_car != null && !_car.Owned ? _car.Name + " is not installed (a DLC you do not own)." : "Pick a car and a skin first.");
        }

        public override void Tick()
        {
            if (_current == null) return;
            // rebuilt only when the choice changes (no string at 4 Hz)
            if (_currentShown != null && ReferenceEquals(AcLocal.CarId, _shownCar) && ReferenceEquals(AcLocal.SkinId, _shownSkin)) return;
            _shownCar = AcLocal.CarId;
            _shownSkin = AcLocal.SkinId;
            AcContent c = AcFront.Content;
            AcCarEntry mine = c != null ? c.Car(AcLocal.CarId) : null;
            AcSkinEntry skin = mine != null ? mine.Skin(AcLocal.SkinId) : null;
            string t = mine != null ? "YOUR CAR:  " + mine.Name + (skin != null ? "   (" + skin.Name + ")" : "") : "No car chosen yet.";
            if (t != _currentShown) { _currentShown = t; _current.text = t; }
        }

        private string _shownCar, _shownSkin;
    }

    /// <summary>
    /// [F, Front] TRACK: every track of the install (owned ones loadable, DLC stubs greyed), its layouts with the layout's own preview.png and
    /// outline.png, name, city / country, length, direction and pit boxes (ui_track.json); LOAD TRACK -> AcRace.RequestLoad(layout, the lobby's
    /// rules) for the instance master (or a player alone); everyone else sees the page read-only with the master's name.
    /// </summary>
    public sealed class AcPageTrack : AcPage
    {
        public override string Id { get { return "track"; } }
        public override string Caption { get { return "TRACK"; } }
        public override bool Available { get { return AcFront.Content != null; } }
        public override string WhyNot { get { return "Your Assetto Corsa install is not read yet."; } }

        private AcGrid _grid;
        private RectTransform _detail, _layouts;
        private TextMeshProUGUI _name, _info, _session, _onTrack;
        private RawImage _preview, _outline;
        private AcButton _load, _leave;
        private AcTrackEntry _track;
        private AcLayoutEntry _layout;
        private string _onTrackShown;
        private int _lobbySeen = -1;

        protected override void Build()
        {
            Title("TRACK  -  CHOOSE WHERE EVERYONE DRIVES");
            AcContent c = AcFront.Content;
            if (c == null) return;
            _grid = AcGrid.Create(Root, "Tracks", 0f, 54f, 3, 3, 236f, 168f, 10f);
            _grid.Fill = FillTrack;
            _grid.Count = c.Tracks.Count;
            _onTrack = AcUi.Label(Root, "On track", "", 19f, AcUi.Grey, TextAlignmentOptions.TopLeft, 0f, 640f, 728f, 60f);

            float dw = W - 748f;
            _detail = AcUi.Place(Root, "Detail", 748f, 54f, dw, H - 54f);
            _name = AcUi.OneLine(AcUi.Label(_detail, "Name", "", 30f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, dw, 40f, AcFonts.Face.Light), 16f);
            _layouts = AcUi.Place(_detail, "Layouts", 0f, 46f, dw, 6 * 44f);
            AcUi.Box(_detail, "Preview back", 0f, 316f, 262f, 148f, new Color(0f, 0f, 0f, 0.6f));
            _preview = Pic(_detail, "Preview", 0f, 316f, 262f, 148f);
            AcUi.Box(_detail, "Outline back", 270f, 316f, dw - 270f, 148f, new Color(0f, 0f, 0f, 0.6f));
            _outline = Pic(_detail, "Outline", 274f, 320f, dw - 278f, 140f);
            _info = AcUi.Label(_detail, "Info", "", 19f, AcUi.Text, TextAlignmentOptions.TopLeft, 0f, 472f, dw, 110f);
            _session = AcUi.OneLine(AcUi.Label(_detail, "Session", "", 19f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 0f, 586f, dw, 26f), 11f);
            _load = AcUi.Button(_detail, "LOAD TRACK", 0f, 620f, dw * 0.6f - 6f, 54f, 24f, Load);
            _load.gameObject.name = "Track Load";
            _load.PressSound = "select";
            _load.Refused = LoadRefused;
            _leave = AcUi.Button(_detail, "LEAVE TRACK", dw * 0.6f + 6f, 620f, dw * 0.4f - 6f, 54f, 20f, Leave);
            _leave.gameObject.name = "Track Leave";
            _leave.Refused = LeaveRefused;

            AcLayoutEntry start = AcSession.Layout;
            if (start == null)
            {
                string key = AcSettings.Text("drive.track", "");
                int slash = key.IndexOf('/');
                start = slash > 0 ? c.Layout(key.Substring(0, slash), key.Substring(slash + 1)) : c.Layout(key, "");
            }

            AcTrackEntry t = start != null ? c.Track(start.TrackId) : null;
            if (t == null) foreach (AcTrackEntry e in c.Tracks) if (e.Owned) { t = e; break; }
            if (t != null) _grid.ShowItem(c.Tracks.IndexOf(t));
            SelectTrack(t, start);
        }

        private static RawImage Pic(Transform parent, string name, float x, float y, float w, float h)
        {
            RawImage r = AcUi.Place(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            r.raycastTarget = false;
            r.color = new Color(1f, 1f, 1f, 0f);
            return r;
        }

        private void FillTrack(int item, RectTransform cell)
        {
            AcTrackEntry t = AcFront.Content.Tracks[item];
            bool owned = t.Owned;
            AcButton b = cell.gameObject.AddComponent<AcButton>();
            b.Transparent = true;
            b.Build("", 16f, () => SelectTrack(t, null));
            b.gameObject.name = "Track " + t.Id;
            b.Selected = t == _track;
            AcLayoutEntry first = t.Layouts.Count > 0 ? t.Layouts[0] : null;
            AcUi.Picture(cell, "Preview", 8f, 6f, 220f, 124f, first != null ? first.PreviewPath : null, 256, owned ? Color.white : new Color(1f, 1f, 1f, 0.35f));
            AcUi.OneLine(AcUi.Label(cell, "Name", t.Name ?? t.Id, 17f, owned ? AcUi.Text : AcUi.Dim, TextAlignmentOptions.MidlineLeft, 8f, 132f, 220f, 32f), 10f);
            if (!owned) AcUi.OneLine(AcUi.Label(cell, "Stub", "NOT INSTALLED", 13f, new Color(0.85f, 0.35f, 0.3f, 1f), TextAlignmentOptions.TopRight, 8f, 8f, 216f, 20f));
            else if (t.Layouts.Count > 1) AcUi.OneLine(AcUi.Label(cell, "Layouts", t.Layouts.Count + " LAYOUTS", 13f, AcUi.Text, TextAlignmentOptions.TopRight, 8f, 8f, 216f, 20f));
        }

        private void SelectTrack(AcTrackEntry t, AcLayoutEntry layout)
        {
            _track = t;
            if (layout == null || t == null || layout.TrackId != t.Id)
            {
                layout = null;
                if (t != null) foreach (AcLayoutEntry l in t.Layouts) if (l.Owned) { layout = l; break; }
                if (layout == null && t != null && t.Layouts.Count > 0) layout = t.Layouts[0];
            }

            if (_grid != null) _grid.Refresh();
            AcUi.Clear(_layouts);
            float dw = W - 748f;
            if (t != null)
            {
                for (int i = 0; i < t.Layouts.Count && i < 6; i++)
                {
                    AcLayoutEntry l = t.Layouts[i];
                    AcButton b = AcUi.Button(_layouts, l.Name + (l.Owned ? "" : "   (not installed)"), 0f, i * 44f, dw, 40f, 18f, () => SelectLayout(l));
                    b.gameObject.name = "Layout " + l.Key;
                    b.Caption.alignment = TextAlignmentOptions.MidlineLeft;
                    b.Selected = l == layout;
                }
            }

            SelectLayout(layout);
        }

        private void SelectLayout(AcLayoutEntry l)
        {
            _layout = l;
            for (int i = 0; i < _layouts.childCount; i++)
            {
                AcButton b = _layouts.GetChild(i).GetComponent<AcButton>();
                if (b != null) b.Selected = l != null && b.gameObject.name == "Layout " + l.Key;
            }

            _name.text = _track != null ? _track.Name : "";
            float dw = W - 748f;
            Rebind(_preview, l != null ? l.PreviewPath : null, 0f, 316f, 262f, 148f, 512);
            Rebind(_outline, l != null ? l.OutlinePath : null, 274f, 320f, dw - 278f, 140f, 512);
            if (l == null) { _info.text = ""; RefreshButtons(); return; }
            string place = string.IsNullOrEmpty(l.City) ? l.Country : string.IsNullOrEmpty(l.Country) ? l.City : l.City + ", " + l.Country;
            string len = AcUiFormat.Length(l.LengthM, AcFront.UseMph());
            _info.text = l.Name + "\n" + (string.IsNullOrEmpty(place) ? "" : place + "\n") + (len.Length > 0 ? len : "") + (string.IsNullOrEmpty(l.Run) ? "" : "   -   " + l.Run) +
                         (l.Pitboxes > 0 ? "   -   " + l.Pitboxes + " pit boxes" : "");
            RefreshButtons();
            Menu.ApplyLayers();
        }

        private static void Rebind(RawImage img, string path, float x, float y, float w, float h, int maxW)
        {
            img.rectTransform.sizeDelta = new Vector2(w, h);
            img.rectTransform.anchoredPosition = new Vector2(x, -y);
            AcUiArt.Bind(img, path, maxW, Color.white, true);
        }

        private void RefreshButtons()
        {
            bool master = AcMenu.SafeIsMaster() && AcMenu.SafeMasterKnown();
            _load.Interactable = master && _layout != null && _layout.Owned && AcSession.Phase != AcPhase.Loading && !AcSession.InRace;
            _load.SetLabel(AcSession.Layout != null && AcSession.Layout == _layout ? "RELOAD TRACK" : "LOAD TRACK");
            string leave = AcFront.LeaveLabel();
            if (!ReferenceEquals(leave, _leaveShown)) { _leaveShown = leave; _leave.SetLabel(leave); }
            _leave.Interactable = AcFront.LeaveOrJoinPossible;
            AcRules shown = AcLobby.Shown;
            int sig = AcLobby.Sig(shown);
            if (sig != _sessionSig || _session.text.Length == 0)
            {
                _sessionSig = sig;
                _session.text = "SESSION: " + AcLobby.SessionLine(shown) + "   (set in the RACE LOBBY)";
            }
        }

        private string _leaveShown;
        private int _sessionSig, _onTrackSig = int.MinValue;

        private void Load()
        {
            if (_layout == null) return;
            AcFront.RequestLoad(_layout, "tablet");
        }

        private void LoadRefused()
        {
            if (!AcMenu.SafeMasterKnown()) Toast("Waiting for the instance master to be known...");
            else if (!AcMenu.SafeIsMaster()) Toast("Only the instance master (" + AcMenu.SafeMasterName() + ") picks the track.");
            else if (_layout != null && !_layout.Owned) Toast(_layout.Name + " is not installed (a DLC you do not own).");
            else if (AcSession.InRace) Toast("A race is running - end it in the RACE LOBBY first.");
            else if (AcSession.Phase == AcPhase.Loading) Toast("A track is loading.");
            else Toast("Pick a track and a layout first.");
        }

        private void Leave() { AcFront.LeaveOrJoin("track page"); }

        private void LeaveRefused()
        {
            Toast(AcSession.Phase == AcPhase.Loading ? "A track is loading." : "No track is loaded and no session runs to join.");
        }

        public override void Tick()
        {
            if (_load == null) return;
            RefreshButtons();
            bool master = AcMenu.SafeIsMaster();
            string masterName = AcMenu.SafeMasterName();
            int sig = (int)AcSession.Phase * 7 + (master ? 1 : 0) + (AcSession.Layout != null ? AcSession.Layout.GetHashCode() * 31 : 0) + (masterName != null ? masterName.GetHashCode() * 131 : 0) + (AcFront.CanJoinSession ? 3 : 0);
            if (sig == _onTrackSig && _onTrackShown != null) return; // rebuilt only when what it says changes
            _onTrackSig = sig;
            string t = AcSession.Layout != null ? "ON TRACK:  " + AcSession.Layout.Name + "  -  " + AcSession.Phase.ToString().ToUpperInvariant() : AcFront.CanJoinSession ? "The instance master's session runs: JOIN SESSION takes you there." : "No track loaded: you are in the showroom.";
            if (!master) t += "\nThe instance master " + masterName + " picks the track.";
            if (t != _onTrackShown) { _onTrackShown = t; _onTrack.text = t; }
            if (_lobbySeen != AcLobby.Version) _lobbySeen = AcLobby.Version;
        }
    }
}
