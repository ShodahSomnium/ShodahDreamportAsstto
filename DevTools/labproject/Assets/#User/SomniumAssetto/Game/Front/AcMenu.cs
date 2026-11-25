using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] One page of the tablet app (ETS's EtsTabletPage pattern): built lazily on first show into its own root inside the menu's content
    /// area, <see cref="Tick"/> at 4 Hz while shown, <see cref="Available"/> / <see cref="WhyNot"/> for the nav button. Each host (the tablet frame,
    /// the hub panel) has its own page objects; shared state lives in static models (AcLobby, AcSettings, AcLocal).
    /// </summary>
    public abstract class AcPage
    {
        public abstract string Id { get; }
        public abstract string Caption { get; }
        public virtual bool Available { get { return true; } }
        public virtual string WhyNot { get { return null; } }

        public AcMenu Menu { get; internal set; }
        public RectTransform Root { get; internal set; }
        public bool Built { get; private set; }
        public bool Visible { get { return Root != null && Root.gameObject.activeInHierarchy; } }
        protected float W { get { return AcMenu.ContentW; } }
        protected float H { get { return AcMenu.ContentH; } }

        /// <summary>Draws the page into <see cref="Root"/> (ContentW x ContentH units, top-left origin).</summary>
        protected abstract void Build();
        public virtual void Shown() { }
        public virtual void Hidden() { }
        public virtual void Tick() { }

        internal void EnsureBuilt()
        {
            if (Built || Root == null) return;
            Built = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { Build(); }
            catch (Exception e) { AcLog.Error("Front: building the " + Caption + " page failed: " + e); }
            Menu.ApplyLayers();
            if (watch.ElapsedMilliseconds > 30) AcLog.Info("Front: the " + (Menu.Tablet ? "tablet's" : "hub's") + " " + Caption + " page was built in " + watch.ElapsedMilliseconds + " ms.");
        }

        /// <summary>Throws the page away; it is built again when shown (data changed: install read, rows registered).</summary>
        public void Rebuild()
        {
            if (Root == null) return;
            AcUi.Clear(Root);
            Built = false;
            if (Visible) { EnsureBuilt(); Shown(); }
        }

        protected void Toast(string text) { if (Menu != null) Menu.Toast(text); }

        /// <summary>The page title in AC's ksGui form look (red title bar).</summary>
        protected TextMeshProUGUI Title(string text) { return AcUi.TitleBar(Root, text, 0f, 0f, W, 44f); }
    }

    /// <summary>
    /// [F, Front] The ASSETTO CORSA menu: a header (the launcher's AC logo, the app title, "INSTANCE MASTER: name" with a crown, the version), a
    /// nav column of pages, the page area and a footer with the status line / toasts. Built twice by AcFront - into the tablet app's frame and
    /// into the hub's panel (Halo's "one builder, two hosts"). Layout in canvas units from the top-left (AcUi), 1600 x 900.
    /// </summary>
    public sealed class AcMenu
    {
        public const float Width = 1600f, Height = 900f;
        public const float HeaderH = 74f, NavW = 290f, FooterH = 52f;
        public const float ContentX = NavW + 20f, ContentY = HeaderH + 12f, ContentW = Width - ContentX - 20f, ContentH = Height - ContentY - FooterH - 10f;

        public RectTransform Root { get; private set; }
        public bool Tablet { get; private set; }
        public AcPage Current { get; private set; }
        public readonly List<AcPage> Pages = new List<AcPage>();
        public Action ApplyLayersHook;

        private readonly List<AcButton> _nav = new List<AcButton>();
        private TextMeshProUGUI _status, _master, _phase;
        private Image _crown;
        private string _statusText = "";
        private bool _statusError;
        private string _toast;
        private float _toastUntil;
        private string _masterShown, _phaseShown;

        public static AcMenu Build(Transform parent, bool tablet, IList<AcPage> pages)
        {
            var m = new AcMenu { Tablet = tablet };
            RectTransform root = AcUi.Place(parent, "Ac Menu", 0f, 0f, Width, Height);
            m.Root = root;
            AcUi.Box(root, "Backdrop", 0f, 0f, Width, Height, AcUi.Page, true);
            // header: the launcher's logo, the title, the master line
            AcUi.Box(root, "Header", 0f, 0f, Width, HeaderH, new Color(0f, 0f, 0f, 1f));
            AcUi.Box(root, "Header rule", 0f, HeaderH - 3f, Width, 3f, AcUi.Accent);
            AcInstall inst = AcGame.Current != null ? AcGame.Current.Install : null;
            if (inst != null)
                AcUi.Picture(root, "Logo", 16f, 6f, 72f, 60f, System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(inst.Root, "launcher"), "themes"), "default"), System.IO.Path.Combine("graphics", "logo.png")), 128, Color.white);
            AcUi.OneLine(AcUi.Label(root, "App title", "ASSETTO CORSA", 34f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 100f, 0f, 420f, HeaderH - 3f, AcFonts.Face.Light), 18f);
            m._phase = AcUi.OneLine(AcUi.Label(root, "Phase", "", 20f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 520f, 0f, 420f, HeaderH - 3f), 12f);
            m._crown = AcUi.Box(root, "Crown", 960f, 22f, 34f, 26f, Color.white);
            m._crown.sprite = AcCrown.Sprite;
            m._crown.preserveAspect = true;
            m._master = AcUi.OneLine(AcUi.Label(root, "Instance master", "", 22f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 1000f, 0f, 580f, HeaderH - 3f), 12f);

            // nav column
            AcUi.Box(root, "Nav back", 0f, HeaderH, NavW, Height - HeaderH - FooterH, new Color(0f, 0f, 0f, 0.55f));
            float y = ContentY;
            for (int i = 0; i < pages.Count; i++)
            {
                AcPage p = pages[i];
                p.Menu = m;
                p.Root = AcUi.Place(root, p.Caption + " Page", ContentX, ContentY, ContentW, ContentH);
                p.Root.gameObject.SetActive(false);
                m.Pages.Add(p);
                AcPage captured = p;
                AcButton b = AcUi.Button(root, p.Caption, 14f, y, NavW - 28f, 60f, 26f, () => m.Show(captured.Id));
                b.gameObject.name = "Nav " + p.Id;
                b.Caption.alignment = TextAlignmentOptions.MidlineLeft;
                AcFonts.Apply(b.Caption, AcFonts.Face.Light);
                b.Refused = () => m.Toast(captured.WhyNot ?? (captured.Caption + " is not available now."));
                m._nav.Add(b);
                y += 68f;
            }

            AcUi.OneLine(AcUi.Label(root, "Version", AcVersion.Label, 15f, AcUi.Dim, TextAlignmentOptions.BottomLeft, 14f, Height - FooterH - 92f, NavW - 20f, 22f), 9f);
            var tag = AcUi.Label(root, "Tagline", "Everything you see and hear is read from your own Assetto Corsa install.", 15f, AcUi.Dim, TextAlignmentOptions.TopLeft, 14f, Height - FooterH - 66f, NavW - 24f, 56f, AcFonts.Face.Light);
            tag.overflowMode = TextOverflowModes.Overflow;

            // footer: the status line (boot, loads) and toasts (refusals, hints)
            AcUi.Box(root, "Footer", 0f, Height - FooterH, Width, FooterH, new Color(0f, 0f, 0f, 1f));
            m._status = AcUi.OneLine(AcUi.Label(root, "Status", "", 20f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 20f, Height - FooterH, Width - 40f, FooterH), 11f);
            // the first page that can show now (before the install is read: HOW TO PLAY / SETTINGS)
            AcPage first = null;
            for (int i = 0; i < m.Pages.Count && first == null; i++)
            {
                bool ok;
                try { ok = m.Pages[i].Available; } catch (Exception) { ok = false; }
                if (ok) first = m.Pages[i];
            }

            if (first != null) m.Show(first.Id);
            m.RefreshChrome();
            return m;
        }

        public AcPage Find(string id)
        {
            for (int i = 0; i < Pages.Count; i++) if (Pages[i].Id == id) return Pages[i];
            return null;
        }

        /// <summary>Shows a page by id (refused with its WhyNot when not available).</summary>
        public void Show(string id)
        {
            AcPage p = Find(id);
            if (p == null) return;
            if (!p.Available) { Toast(p.WhyNot ?? (p.Caption + " is not available now.")); return; }
            if (Current != null && Current != p)
            {
                try { Current.Hidden(); } catch (Exception e) { AcLog.Warn("Front: hiding " + Current.Caption + " failed: " + e.Message); }
                Current.Root.gameObject.SetActive(false);
            }

            Current = p;
            p.Root.gameObject.SetActive(true);
            p.EnsureBuilt();
            try { p.Shown(); } catch (Exception e) { AcLog.Warn("Front: showing " + p.Caption + " failed: " + e.Message); }
            try { p.Tick(); } catch (Exception e) { AcLog.Warn("Front: " + p.Caption + " tick failed: " + e.Message); }
            for (int i = 0; i < _nav.Count; i++) _nav[i].Selected = Pages[i] == p;
            ApplyLayers();
        }

        public void ApplyLayers()
        {
            var h = ApplyLayersHook;
            if (h != null) { try { h(); } catch (Exception) { } }
        }

        public void SetStatus(string text, bool error)
        {
            _statusText = text ?? "";
            _statusError = error;
            RefreshFooter();
        }

        /// <summary>A short line in the footer for 8 s (refusals, hints). // ours: 8 s</summary>
        public void Toast(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _toast = text;
            _toastUntil = Time.unscaledTime + 8f;
            RefreshFooter();
        }

        private void RefreshFooter()
        {
            if (_status == null) return;
            bool toast = _toast != null && Time.unscaledTime < _toastUntil;
            string t = toast ? _toast : _statusText;
            if (_status.text != t) _status.text = t;
            Color c = toast ? AcUi.Gold : _statusError ? new Color(1f, 0.35f, 0.3f, 1f) : AcUi.Grey;
            if (_status.color != c) _status.color = c;
        }

        /// <summary>4 Hz (AcFront): nav availability, master line, footer, the shown page's tick.</summary>
        public void Tick()
        {
            if (Root == null) return;
            RefreshChrome();
            RefreshFooter();
            if (Current != null && Current.Visible)
            {
                try { Current.Tick(); }
                catch (Exception e) { AcLog.WarnOnce("Front: the " + Current.Caption + " page's tick failed: " + e.Message); }
            }
        }

        private void RefreshChrome()
        {
            for (int i = 0; i < _nav.Count && i < Pages.Count; i++)
            {
                bool ok;
                try { ok = Pages[i].Available; } catch (Exception) { ok = false; }
                _nav[i].Interactable = ok;
            }

            bool known = SafeMasterKnown();
            bool mine = SafeIsMaster();
            string name = SafeMasterName();
            if (_masterShown == null || known != _masterKnown || mine != _masterMine || !string.Equals(name, _masterName, StringComparison.Ordinal))
            {
                _masterKnown = known; _masterMine = mine; _masterName = name;
                _masterShown = !known ? "INSTANCE MASTER: connecting..." : "INSTANCE MASTER: " + name + (mine ? "  (YOU)" : "");
                _master.text = _masterShown;
                _master.color = mine ? AcUi.Gold : AcUi.Text;
                _crown.gameObject.SetActive(known);
            }

            // the phase line is rebuilt only when what it says changes (no garbage at 4 Hz in steady state)
            int sig = (int)AcSession.Phase * 1000 + Mathf.RoundToInt(AcSession.LoadProgress * 100f) + (AcSession.Rules != null ? (int)AcSession.Rules.Session * 100000 : 0);
            if (_phaseShown == null || sig != _phaseSig || AcSession.Layout != _phaseLayout)
            {
                _phaseSig = sig;
                _phaseLayout = AcSession.Layout;
                _phaseShown = PhaseLine();
                _phase.text = _phaseShown;
            }
        }

        private bool _masterKnown, _masterMine;
        private string _masterName;
        private int _phaseSig;
        private AcLayoutEntry _phaseLayout;

        internal static string PhaseLine()
        {
            AcPhase p = AcSession.Phase;
            AcLayoutEntry l = AcSession.Layout;
            switch (p)
            {
                case AcPhase.Hub: return "SHOWROOM";
                case AcPhase.Loading: return "LOADING " + (l != null ? l.Name : "") + " " + Mathf.RoundToInt(AcSession.LoadProgress * 100f) + " %";
                default: return (l != null ? l.Name : "") + "  -  " + AcLocale.Session((int)AcSession.Rules.Session) + (AcSession.InRace ? "  " + p.ToString().ToUpperInvariant() : "");
            }
        }

        internal static bool SafeIsMaster() { try { return AcNetGame.IsMaster; } catch (Exception) { return true; } }
        internal static bool SafeMasterKnown() { try { return AcNetGame.MasterKnown; } catch (Exception) { return true; } }
        internal static string SafeMasterName() { try { return AcNetGame.MasterName ?? "?"; } catch (Exception) { return "?"; } }
    }

    /// <summary>
    /// [F, Front] A paged grid of cells (no scroll view: a VR ray drag on a ScrollRect is unproven in the client): cols x rows cells, "<" / ">"
    /// buttons and "n / m" under it. The page fills cells through <see cref="Fill"/> (cell index on the page, item index, the cell's rect).
    /// </summary>
    public sealed class AcGrid
    {
        public int Cols, Rows;
        public float CellW, CellH, Gap;
        public int Page;
        public int Count;
        public RectTransform Root, Cells;
        public Action<int, RectTransform> Fill;
        private TextMeshProUGUI _pageText;
        private AcButton _prev, _next;

        public int PerPage { get { return Cols * Rows; } }
        public int Pages { get { return AcPaging.Pages(Count, PerPage); } }

        public static AcGrid Create(Transform parent, string name, float x, float y, int cols, int rows, float cellW, float cellH, float gap)
        {
            var g = new AcGrid { Cols = cols, Rows = rows, CellW = cellW, CellH = cellH, Gap = gap };
            float w = cols * cellW + (cols - 1) * gap, h = rows * cellH + (rows - 1) * gap;
            g.Root = AcUi.Place(parent, name, x, y, w, h + 52f);
            g.Cells = AcUi.Place(g.Root, "Cells", 0f, 0f, w, h);
            g._prev = AcUi.Button(g.Root, "<", 0f, h + 10f, 70f, 42f, 26f, g.Prev);
            g._next = AcUi.Button(g.Root, ">", w - 70f, h + 10f, 70f, 42f, 26f, g.Next);
            g._prev.gameObject.name = name + " <";
            g._next.gameObject.name = name + " >";
            g._pageText = AcUi.OneLine(AcUi.Label(g.Root, "Page", "", 20f, AcUi.Grey, TextAlignmentOptions.Center, 80f, h + 10f, w - 160f, 42f));
            return g;
        }

        public void Prev() { if (Page > 0) { Page--; Refresh(); } }
        public void Next() { if (Page < Pages - 1) { Page++; Refresh(); } }

        /// <summary>Shows the page holding item <paramref name="index"/>.</summary>
        public void ShowItem(int index)
        {
            Page = AcPaging.Clamp(AcPaging.PageOf(index, PerPage), Count, PerPage);
        }

        /// <summary>Rebuilds the cells of the current page.</summary>
        public void Refresh()
        {
            Page = AcPaging.Clamp(Page, Count, PerPage);
            AcUi.Clear(Cells);
            int first = Page * PerPage;
            for (int c = 0; c < PerPage; c++)
            {
                int item = first + c;
                if (item >= Count) break;
                float x = (c % Cols) * (CellW + Gap), y = (c / Cols) * (CellH + Gap);
                RectTransform cell = AcUi.Place(Cells, "Cell " + item, x, y, CellW, CellH);
                if (Fill != null)
                {
                    try { Fill(item, cell); }
                    catch (Exception e) { AcLog.WarnOnce("Front: a grid cell failed: " + e.Message); }
                }
            }

            int pages = Pages;
            _pageText.text = Count == 0 ? "" : (Page + 1) + " / " + pages;
            _prev.Interactable = Page > 0;
            _next.Interactable = Page < pages - 1;
            _prev.gameObject.SetActive(pages > 1);
            _next.gameObject.SetActive(pages > 1);
        }
    }
}
