using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] GARAGE: your driving aids (AC's assists.ini: ABS / TRACTION CONTROL off / factory / on, STABILITY CONTROL 0-100 %, AUTOMATIC
    /// GEARS, AUTO CLUTCH, AUTO BLIP, TYRE BLANKETS; your own Documents\Assetto Corsa\cfg\assists.ini is the default) within the instance
    /// master's rules, and a basic setup of your car: the tyre compound (tyres.ini through P's AcCarSpec.Compounds) and the car's own data.acd
    /// setup.ini rows for tyre pressures, fuel, brake bias / brake power (MIN / MAX / STEP from the car, labels from system/locales/setup/en.ini,
    /// the car's own values as defaults). Values are kept per car (AcSettings "setup.car.KEY"); AcFront.Assists / AcFront.SetupValue give them
    /// to the physics at spawn, and a change goes into the car on the track as soon as it stands still (AcFront.SetSetup). Text is rebuilt only
    /// when a setting, the rules or the car change.
    /// </summary>
    public sealed class AcPageGarage : AcPage
    {
        public override string Id { get { return "garage"; } }
        public override string Caption { get { return "GARAGE"; } }
        public override bool Available { get { return AcFront.Content != null; } }
        public override string WhyNot { get { return "Your Assetto Corsa install is not read yet."; } }

        private sealed class Line
        {
            public TextMeshProUGUI Value;
            public Func<string> Read;
            public AcButton Less, More;
        }

        private static readonly string[] OffFactoryOn = { "OFF", "FACTORY", "ON" };
        private readonly List<Line> _assists = new List<Line>();
        private readonly List<Line> _setup = new List<Line>();
        private readonly AcAssists _aids = new AcAssists();
        private RectTransform _setupRoot;
        private TextMeshProUGUI _setupHead, _rulesLine, _applyLine;
        private string _setupCar;
        private bool _setupBuilt, _compoundRow;
        private int _page, _sig = int.MinValue;
        private const int SetupRows = 10;

        protected override void Build()
        {
            Title("GARAGE");
            AcUi.OneLine(AcUi.Label(Root, "Assists head", "DRIVING AIDS", 20f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 54f, 600f, 32f, AcFonts.Face.Bold));
            _rulesLine = AcUi.Label(Root, "Rules line", "", 16f, AcUi.Grey, TextAlignmentOptions.TopLeft, 0f, 92f, 600f, 44f);
            float y = 140f;
            Assist(ref y, AcLocale.L("ABS", "ABS"), () => OffFactoryOn[_aids.Abs], d => AcSettings.SetInt("garage.abs", Wrap(AcFront.AssistInt("garage.abs") + d, 3)));
            Assist(ref y, AcLocale.L("TRACTION_CONTROL", "Traction Control").ToUpperInvariant(), () => OffFactoryOn[_aids.Tc], d => AcSettings.SetInt("garage.tc", Wrap(AcFront.AssistInt("garage.tc") + d, 3)));
            // assists.ini STABILITY_CONTROL 0..100: AC's launcher steps it by 10 // ours (the step)
            Assist(ref y, "STABILITY CONTROL", () => _aids.Stability + " %", d => AcSettings.SetInt("garage.stability", Mathf.Clamp(AcFront.AssistInt("garage.stability") + d * 10, 0, 100)));
            Assist(ref y, AcLocale.L("AUTOMATIC_GEARS", "Automatic Gears").ToUpperInvariant(), () => OnOff(_aids.AutoShift), d => AcSettings.SetInt("garage.autoshift", 1 - AcFront.AssistInt("garage.autoshift")));
            Assist(ref y, "AUTO CLUTCH", () => OnOff(_aids.AutoClutch), d => AcSettings.SetInt("garage.autoclutch", 1 - AcFront.AssistInt("garage.autoclutch")));
            Assist(ref y, "AUTO BLIP", () => OnOff(_aids.AutoBlip), d => AcSettings.SetInt("garage.autoblip", 1 - AcFront.AssistInt("garage.autoblip")));
            Assist(ref y, "TYRE BLANKETS", () => OnOff(AcFront.TyreBlankets), d => AcSettings.SetInt("garage.blankets", 1 - AcFront.AssistInt("garage.blankets")));
            AcUi.Label(Root, "Assists note", "Defaults are your own Assetto Corsa choices (Documents\\Assetto Corsa\\cfg\\assists.ini). The instance master's rules (RACE LOBBY) may limit them.", 15f, AcUi.Dim, TextAlignmentOptions.TopLeft, 0f, y + 10f, 600f, 60f, AcFonts.Face.Light);
            BuildRepair(540f);

            _setupHead = AcUi.OneLine(AcUi.Label(Root, "Setup head", "SETUP", 20f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 640f, 54f, W - 640f, 32f, AcFonts.Face.Bold), 12f);
            _applyLine = AcUi.OneLine(AcUi.Label(Root, "Setup apply", "", 15f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 640f, H - 34f, W - 640f, 30f, AcFonts.Face.Light), 9f);
            _setupRoot = AcUi.Place(Root, "Setup", 640f, 92f, W - 640f, H - 130f);
            _setupBuilt = false;
            _setupCar = null;
            _sig = int.MinValue;
        }

        private void Assist(ref float y, string label, Func<string> read, Action<int> change)
        {
            RectTransform r = AcUi.Place(Root, "Assist " + label, 0f, y, 600f, 42f);
            AcUi.OneLine(AcUi.Label(r, "Label", label, 18f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, 300f, 42f), 10f);
            var line = new Line { Read = read };
            line.Value = AcUi.OneLine(AcUi.Label(r, "Value", "", 18f, AcUi.Text, TextAlignmentOptions.Center, 350f, 0f, 200f, 42f, AcFonts.Face.Bold), 10f);
            line.Less = AcUi.Button(r, "<", 306f, 4f, 40f, 34f, 20f, () => { change(-1); AcFront.ApplyAssistsToLocalCar(); Refresh(true); });
            line.More = AcUi.Button(r, ">", 556f, 4f, 40f, 34f, 20f, () => { change(1); AcFront.ApplyAssistsToLocalCar(); Refresh(true); });
            line.Less.gameObject.name = "Assist " + label + " <";
            line.More.gameObject.name = "Assist " + label + " >";
            _assists.Add(line);
            y += 46f;
        }

        // ------------------------------------------------------------------ REPAIR (iteration 2, DAMAGE_DESIGN 4 F)

        /// <summary>One AC pit-app repair row: the install's art (content/gui/pitstop/&lt;art&gt;_ON|OFF.png, the exe-referenced names), AC's own
        /// row label (exe string, re-typed), R's estimate (car.ini [PIT_STOP] seconds per 10 %) and the REPAIR button (R: AcRace.Repair).</summary>
        private sealed class RepairRow
        {
            public AcRepair What;
            public string ArtOn, ArtOff;
            public RawImage Art;
            public TextMeshProUGUI Secs;
            public AcButton Go;
            public int ShownSecs = int.MinValue, ShownOn = -1;
        }

        private readonly List<RepairRow> _repair = new List<RepairRow>();
        private TextMeshProUGUI _repairStatus;
        private RawImage _repairOp;
        private int _repairKey = int.MinValue;
        private string _pitDir, _repairWhy;

        private void BuildRepair(float y)
        {
            _repair.Clear();
            AcGame g = AcGame.Current;
            _pitDir = g != null && g.Install != null ? System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(g.Install.Root, "content"), "gui"), "pitstop") : null;
            AcUi.OneLine(AcUi.Label(Root, "Repair head", "REPAIR", 20f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, y, 600f, 30f, AcFonts.Face.Bold));
            y += 34f;
            RepairLine(ref y, AcRepair.Body, "REPAIR BODY", "repair_body");
            RepairLine(ref y, AcRepair.Suspension, "REPAIR BODY + SUSPENSION", "repair_sus");        // repair_sus_* (exe); repair_susp_* is unreferenced
            RepairLine(ref y, AcRepair.Engine, "REPAIR ENGINE", "repair_engine");
            _repairOp = AcUi.Picture(Root, "Repair operating", 0f, y, 44f, 34f, Art("operating_repair"), 128, Color.white);
            _repairStatus = AcUi.OneLine(AcUi.Label(Root, "Repair status", "", 16f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 52f, y, 548f, 34f), 10f);
            _repairKey = int.MinValue;
        }

        private string Art(string name) { return _pitDir != null ? System.IO.Path.Combine(_pitDir, name + ".png") : null; }

        private void RepairLine(ref float y, AcRepair what, string label, string art)
        {
            RectTransform r = AcUi.Place(Root, "Repair " + label, 0f, y, 600f, 42f);
            var row = new RepairRow { What = what, ArtOn = Art(art + "_ON"), ArtOff = Art(art + "_OFF") };
            row.Art = AcUi.Picture(r, "Art", 0f, 1f, 56f, 40f, row.ArtOff, 128, Color.white);
            AcUi.OneLine(AcUi.Label(r, "Label", label, 17f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 64f, 0f, 290f, 42f), 10f);
            row.Secs = AcUi.OneLine(AcUi.Label(r, "Seconds", "", 17f, AcUi.Text, TextAlignmentOptions.MidlineRight, 356f, 0f, 110f, 42f, AcFonts.Face.Bold), 10f);
            AcRepair w = what;
            row.Go = AcUi.Button(r, "REPAIR", 480f, 4f, 120f, 34f, 16f, () => DoRepair(w));
            row.Go.gameObject.name = "Repair " + label + " button";
            _repair.Add(row);
            y += 44f;
        }

        private void DoRepair(AcRepair what)
        {
            bool ok = false;
            try { ok = AcRace.Repair(what); }        // R freezes the car, counts down, repairs; refuses with a toast
            catch (Exception e) { AcLog.Warn("Front: AcRace.Repair failed: " + e.Message); }
            if (ok) AcLog.Info("Front: GARAGE repair " + what + " started (" + AcRace.RepairTotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
            RefreshRepair(true);
        }

        /// <summary>Each tick: per row ON/OFF art + button state (R's CanRepair: in my box, stopped, that part damaged, no repair running) and the
        /// estimate in whole seconds; the status line = the countdown with operating_repair.png while repairing, else the estimated pit stop time
        /// of a full repair or why not. Texts change only when their whole-second value / state changes (no per-frame allocation).</summary>
        private void RefreshRepair(bool force)
        {
            if (_repairStatus == null) return;
            bool active = false; string why = "";
            try { active = AcRace.RepairActive; AcRace.CanRepair(AcRepair.All, out why); } catch (Exception) { active = false; why = ""; }
            for (int i = 0; i < _repair.Count; i++)
            {
                RepairRow row = _repair[i];
                bool can = false; float est = 0f; string w;
                try { can = AcRace.CanRepair(row.What, out w); est = AcRace.RepairEstimate(row.What); } catch (Exception) { can = false; w = ""; }
                int on = can ? 1 : 0;
                if (force || on != row.ShownOn)
                {
                    row.ShownOn = on;
                    row.Go.Interactable = can;
                    AcUiArt.Bind(row.Art, can ? row.ArtOn : row.ArtOff, 128, Color.white, true);
                }
                int secs = est > 0f ? Mathf.CeilToInt(est) : 0;
                if (force || secs != row.ShownSecs) { row.ShownSecs = secs; row.Secs.text = secs > 0 ? secs + " s" : "-"; }
            }
            int key;
            if (active)
            {
                float left = 0f;
                try { left = AcRace.RepairSecondsLeft; } catch (Exception) { }
                key = 1000000 + Mathf.CeilToInt(left);
                if (force || key != _repairKey) { _repairStatus.text = "REPAIRING  " + Mathf.CeilToInt(left) + " s"; _repairStatus.color = AcUi.Text; }
            }
            else
            {
                float all = 0f;
                try { all = AcRace.RepairEstimate(AcRepair.All); } catch (Exception) { }
                bool none = all <= 0f;
                AcRules rr = AcSession.Rules;
                if (rr != null && rr.DamageMultiplier <= 0 && AcSession.OnTrack) why = "Damage is off in this session (RACE LOBBY: DAMAGE MULTIPLIER).";
                key = none ? -1 : Mathf.CeilToInt(all);
                if (force || key != _repairKey || !ReferenceEquals(why, _repairWhy))
                {
                    _repairStatus.text = none ? (string.IsNullOrEmpty(why) ? "Nothing to repair." : why)
                                              : "ESTIMATED PIT STOP TIME  " + Mathf.CeilToInt(all) + " s" + (string.IsNullOrEmpty(why) ? "" : "   -   " + why);
                    _repairStatus.color = AcUi.Grey;
                }
            }
            if (_repairOp.gameObject.activeSelf != active) _repairOp.gameObject.SetActive(active);
            _repairKey = key;
            _repairWhy = why;
        }

        private static int Wrap(int v, int n) { return ((v % n) + n) % n; }
        private static string OnOff(bool b) { return b ? "ON" : "OFF"; }

        public override void Shown() { Refresh(true); RefreshRepair(true); }
        public override void Tick() { Refresh(false); RefreshRepair(false); }

        private void Refresh(bool force)
        {
            if (_rulesLine == null) return;
            string car = AcLocal.CarId;
            if (car != _setupCar) { _setupCar = car; _setupBuilt = false; _page = 0; }
            if (!_setupBuilt) { BuildSetup(); force = true; }
            AcRules r = AcSession.Rules;
            bool waiting = AcFront.SetupWaiting;
            int sig = AcFront.SettingsVersion * 31 + AcLobby.Sig(r) * 7 + (AcSession.OnTrack ? 1 : 0) + (waiting ? 2 : 0) + (AcLocal.Car != null ? 4 : 0) + _page * 131;
            if (!force && sig == _sig) return; // nothing it shows changed
            _sig = sig;
            AcFront.FillAssists(_aids, r);
            for (int i = 0; i < _assists.Count; i++)
            {
                string v;
                try { v = _assists[i].Read(); } catch (Exception) { v = "?"; }
                if (_assists[i].Value.text != v) _assists[i].Value.text = v;
            }

            string rl = AcSession.OnTrack && r != null ? "This session: ABS " + AcLobby.AbsChoices[Mathf.Clamp(r.AbsAllowed, 0, 2)] + ", TC " + AcLobby.AbsChoices[Mathf.Clamp(r.TcAllowed, 0, 2)] +
                                            (r.StabilityAllowed ? "" : ", no stability control") + (r.AutoShiftAllowed ? "" : ", manual gears") + (r.AutoClutchAllowed ? "" : ", no auto clutch") +
                                            (r.TyreBlanketsAllowed ? "" : ", no tyre blankets") : "No session: your own choices apply when you drive.";
            if (_rulesLine.text != rl) _rulesLine.text = rl;

            for (int i = 0; i < _setup.Count; i++)
            {
                string v;
                try { v = _setup[i].Read(); } catch (Exception) { v = "?"; }
                if (_setup[i].Value.text != v) _setup[i].Value.text = v;
            }

            string apply = !AcSession.OnTrack ? "Your setup goes into the car when it is put in your pit box." :
                           waiting ? "Waiting: the change goes into your car as soon as it stands still (or BACK TO PITS)." :
                           "Changes go into your car on the track as soon as it stands still.";
            if (_applyLine.text != apply) _applyLine.text = apply;
        }

        private void BuildSetup()
        {
            AcContent c = AcFront.Content;
            AcCarEntry car = c != null && !string.IsNullOrEmpty(_setupCar) ? c.Car(_setupCar) : null;
            AcUi.Clear(_setupRoot);
            _setup.Clear();
            float w = W - 640f;
            if (car == null) { _setupHead.text = "SETUP"; AcUi.Label(_setupRoot, "None", "Choose your car on the DRIVE page.", 18f, AcUi.Grey, TextAlignmentOptions.TopLeft, 0f, 0f, w, 40f); _setupBuilt = true; return; }
            _setupHead.text = "SETUP  -  " + car.Name.ToUpperInvariant();
            List<AcSetupRow> all = AcFront.SetupFor(car);
            AcCarSpec spec = AcFront.SpecOf(car);
            bool specDone = spec != null || !AcFront.SpecReading(car);
            if (all == null || !specDone) { AcUi.Label(_setupRoot, "Loading", "Reading the car's setup.ini and tyres.ini...", 18f, AcUi.Grey, TextAlignmentOptions.TopLeft, 0f, 0f, w, 40f); return; } // retried next tick
            _setupBuilt = true;
            var rows = new List<AcSetupRow>();
            foreach (AcSetupRow s in all) if (AcFront.IsBasicSetup(s)) rows.Add(s);
            _compoundRow = spec != null && spec.Compounds.Count > 1;
            int count = rows.Count + (_compoundRow ? 1 : 0);
            if (count == 0) { AcUi.Label(_setupRoot, "Empty", "This car's setup.ini has no tyre, fuel or brake rows.", 18f, AcUi.Grey, TextAlignmentOptions.TopLeft, 0f, 0f, w, 40f); return; }
            int pages = AcPaging.Pages(count, SetupRows);
            _page = AcPaging.Clamp(_page, count, SetupRows);
            float y = 0f;
            string carId = car.Id;
            for (int i = _page * SetupRows; i < count && i < (_page + 1) * SetupRows; i++)
            {
                if (_compoundRow && i == 0)
                {
                    AcCarSpec sp = spec;
                    Row(ref y, w, "TYRES", AcLocale.Setup("TYRES", "Tyres"), () => CompoundText(carId, sp), () => StepCompound(carId, sp, -1), () => StepCompound(carId, sp, 1));
                    continue;
                }

                AcSetupRow s = rows[i - (_compoundRow ? 1 : 0)];
                Row(ref y, w, s.Key, s.Label, () => SetupText(carId, s), () => StepSetup(carId, s, -1), () => StepSetup(carId, s, 1));
            }

            float by = SetupRows * 46f + 8f;
            if (pages > 1)
            {
                AcButton prev = AcUi.Button(_setupRoot, "<", 0f, by, 60f, 42f, 22f, () => { _page--; _setupBuilt = false; Refresh(true); });
                AcButton next = AcUi.Button(_setupRoot, ">", 70f, by, 60f, 42f, 22f, () => { _page++; _setupBuilt = false; Refresh(true); });
                prev.Interactable = _page > 0;
                next.Interactable = _page < pages - 1;
            }

            AcButton reset = AcUi.Button(_setupRoot, "CAR DEFAULTS", w - 220f, by, 220f, 42f, 18f, () => { AcFront.ResetSetup(car); Refresh(true); Toast("Setup back to " + car.Name + "'s own values."); });
            reset.gameObject.name = "Setup Reset";
            Menu.ApplyLayers();
        }

        private void Row(ref float y, float w, string key, string label, Func<string> read, Action less, Action more)
        {
            RectTransform r = AcUi.Place(_setupRoot, "Setup " + key, 0f, y, w, 42f);
            AcUi.OneLine(AcUi.Label(r, "Label", label, 18f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, w - 300f, 42f), 10f);
            var line = new Line { Read = read };
            line.Value = AcUi.OneLine(AcUi.Label(r, "Value", "", 18f, AcUi.Text, TextAlignmentOptions.Center, w - 250f, 0f, 200f, 42f, AcFonts.Face.Bold), 10f);
            line.Less = AcUi.Button(r, "<", w - 294f, 4f, 40f, 34f, 20f, () => { less(); Refresh(true); });
            line.More = AcUi.Button(r, ">", w - 44f, 4f, 40f, 34f, 20f, () => { more(); Refresh(true); });
            line.Less.gameObject.name = "Setup " + key + " <";
            line.More.gameObject.name = "Setup " + key + " >";
            _setup.Add(line);
            y += 46f;
        }

        private static string SetupText(string carId, AcSetupRow s)
        {
            float v;
            bool set = AcFront.SetupValue(carId, s.Key, out v);
            return set || !float.IsNaN(v) ? AcUiFormat.Number(v) + (set ? "" : "  (car)") : "CAR DEFAULT";
        }

        private static void StepSetup(string carId, AcSetupRow s, int d)
        {
            float v;
            AcFront.SetupValue(carId, s.Key, out v);
            if (float.IsNaN(v)) v = d > 0 ? s.Min : s.Max;
            else v = Mathf.Clamp(v + d * s.Step, s.Min, s.Max);
            AcFront.SetSetup(carId, s.Key, v);
        }

        private static string CompoundText(string carId, AcCarSpec spec)
        {
            int n = spec.Compounds.Count;
            int i = AcFront.CompoundChoice(carId);
            bool set = i >= 0 && i < n;
            if (!set) i = Mathf.Clamp(spec.DefaultCompound, 0, n - 1);
            AcTyreCompound t = spec.Compounds[i];
            string name = !string.IsNullOrEmpty(t.Name) ? t.Name : t.ShortName;
            return name + (!string.IsNullOrEmpty(t.ShortName) && t.ShortName != name ? " (" + t.ShortName + ")" : "") + (set ? "" : "  (car)");
        }

        private static void StepCompound(string carId, AcCarSpec spec, int d)
        {
            int n = spec.Compounds.Count;
            int i = AcFront.CompoundChoice(carId);
            if (i < 0 || i >= n) i = Mathf.Clamp(spec.DefaultCompound, 0, n - 1);
            AcFront.SetCompoundChoice(carId, Wrap(i + d, n));
        }
    }

    /// <summary>
    /// [F, Front] SETTINGS: the rows every module registered in AcSettingsUi (grouped: AUDIO, CONTROLS, VR, HUD, TABLET ...), drawn as
    /// &lt; value &gt; steppers / choices / toggles or a VR-safe drag slider, rebuilt when a module adds rows; and the Assetto Corsa folder
    /// (AcSettings "install": a text field on a monitor, SET checks it now, RESCAN looks again when the install was not found).
    /// </summary>
    public sealed class AcPageSettings : AcPage
    {
        public override string Id { get { return "settings"; } }
        public override string Caption { get { return "SETTINGS"; } }

        private const int RowsPerColumn = 10;
        private readonly List<KeyValuePair<AcSettingsUi.Row, TextMeshProUGUI>> _values = new List<KeyValuePair<AcSettingsUi.Row, TextMeshProUGUI>>();
        private readonly List<KeyValuePair<AcSettingsUi.Row, AcSlider>> _sliders = new List<KeyValuePair<AcSettingsUi.Row, AcSlider>>();
        private int _version = -1, _page, _settingsSeen = int.MinValue;
        private RectTransform _rows;
        private TextMeshProUGUI _folderNow;
        private TMP_InputField _field;

        protected override void Build()
        {
            Title("SETTINGS");
            AcUi.OneLine(AcUi.Label(Root, "Hint", "Saved on this computer.", 16f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 0f, 48f, 600f, 28f, AcFonts.Face.Light));
            _rows = AcUi.Place(Root, "Rows", 0f, 82f, W, RowsPerColumn * 46f + 56f);
            BuildRows();
            BuildFolder(H - 150f);
        }

        private void BuildRows()
        {
            _version = AcSettingsUi.Version;
            _settingsSeen = int.MinValue;
            AcUi.Clear(_rows);
            _values.Clear();
            _sliders.Clear();
            // group headers + rows in registration order, grouped
            var groups = new List<string>();
            var lines = new List<AcSettingsUi.Row>();
            IReadOnlyList<AcSettingsUi.Row> rows = AcSettingsUi.Rows;
            for (int i = 0; i < rows.Count; i++) if (!groups.Contains(rows[i].Group)) groups.Add(rows[i].Group);
            foreach (string g in groups)
            {
                lines.Add(null); // a header slot
                for (int i = 0; i < rows.Count; i++) if (rows[i].Group == g) lines.Add(rows[i]);
            }

            int perPage = RowsPerColumn * 2;
            int pages = AcPaging.Pages(lines.Count, perPage);
            _page = AcPaging.Clamp(_page, lines.Count, perPage);
            float colW = (W - 30f) / 2f;
            int groupIndex = -1;
            // the header text for slot i: the group of the next row
            for (int slot = 0; slot < perPage; slot++)
            {
                int i = _page * perPage + slot;
                if (i >= lines.Count) break;
                float x = slot < RowsPerColumn ? 0f : colW + 30f, y = (slot % RowsPerColumn) * 46f;
                AcSettingsUi.Row row = lines[i];
                if (row == null)
                {
                    string g = i + 1 < lines.Count && lines[i + 1] != null ? lines[i + 1].Group : "";
                    groupIndex++;
                    AcUi.OneLine(AcUi.Label(_rows, "Group " + g, g.ToUpperInvariant(), 18f, AcUi.Accent, TextAlignmentOptions.BottomLeft, x, y, colW, 40f, AcFonts.Face.Bold));
                    AcUi.Box(_rows, "Group rule " + g, x, y + 40f, colW, 2f, AcUi.Accent);
                    continue;
                }

                RectTransform r = AcUi.Place(_rows, "Setting " + row.Key, x, y, colW, 44f);
                AcSettingsUi.Row captured = row;
                if (row.Kind == "slider")
                {
                    AcSlider s = AcSlider.Create(r, row.Label, 0f, -6f, colW, row.Min, row.Max, row.Step > 0f ? row.Step : 0.05f, row.Number, null, v => captured.Set(v));
                    _sliders.Add(new KeyValuePair<AcSettingsUi.Row, AcSlider>(row, s));
                    continue;
                }

                AcUi.Wrapped(AcUi.Label(r, "Label", row.Label.ToUpperInvariant(), 18f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, colW - 300f, 44f), 12f); // upper case like the AC rows
                TextMeshProUGUI value = AcUi.Wrapped(AcUi.Label(r, "Value", row.Value(), 18f, AcUi.Text, TextAlignmentOptions.Center, colW - 250f, 0f, 200f, 44f, AcFonts.Face.Bold), 12f);
                AcUi.Button(r, "<", colW - 294f, 5f, 40f, 34f, 20f, () => { captured.Change(-1); value.text = captured.Value(); }).gameObject.name = "Setting " + row.Key + " <";
                AcUi.Button(r, ">", colW - 44f, 5f, 40f, 34f, 20f, () => { captured.Change(1); value.text = captured.Value(); }).gameObject.name = "Setting " + row.Key + " >";
                _values.Add(new KeyValuePair<AcSettingsUi.Row, TextMeshProUGUI>(row, value));
            }

            if (pages > 1)
            {
                float by = RowsPerColumn * 46f + 8f;
                AcButton prev = AcUi.Button(_rows, "<", 0f, by, 70f, 42f, 22f, () => { _page--; BuildRows(); });
                AcButton next = AcUi.Button(_rows, ">", 80f, by, 70f, 42f, 22f, () => { _page++; BuildRows(); });
                prev.Interactable = _page > 0;
                next.Interactable = _page < pages - 1;
                AcUi.OneLine(AcUi.Label(_rows, "Page", (_page + 1) + " / " + pages, 18f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 170f, by, 200f, 42f));
            }

            Menu.ApplyLayers();
        }

        private void BuildFolder(float y)
        {
            AcUi.Panel(Root, "Folder panel", 0f, y, W, 146f);
            AcUi.OneLine(AcUi.Label(Root, "Folder label", "ASSETTO CORSA FOLDER", 18f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 16f, y + 6f, 300f, 32f, AcFonts.Face.Bold));
            _folderNow = AcUi.OneLine(AcUi.Label(Root, "Folder now", "", 16f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 320f, y + 6f, W - 336f, 32f), 9f);
            _field = InputField(Root, "Folder field", 16f, y + 46f, 760f, 46f, AcSettings.InstallOverride);
            AcButton set = AcUi.Button(Root, "SET", 790f, y + 44f, 140f, 50f, 20f, SetFolder);
            AcButton def = AcUi.Button(Root, "DEFAULT", 940f, y + 44f, 150f, 50f, 18f, DefaultFolder);
            AcButton rescan = AcUi.Button(Root, "RESCAN", 1100f, y + 44f, W - 1116f, 50f, 18f, Rescan);
            set.gameObject.name = "Folder Set"; def.gameObject.name = "Folder Default"; rescan.gameObject.name = "Folder Rescan";
            AcUi.OneLine(AcUi.Label(Root, "Folder hint", "The folder with AssettoCorsa.exe and content (found through Steam by itself; type one only if yours is elsewhere - a monitor keyboard is needed).", 14f, AcUi.Dim, TextAlignmentOptions.MidlineLeft, 16f, y + 102f, W - 32f, 30f, AcFonts.Face.Light), 9f);
            RefreshFolder();
        }

        private static TMP_InputField InputField(Transform parent, string name, float x, float y, float w, float h, string text)
        {
            try
            {
                RectTransform r = AcUi.Place(parent, name, x, y, w, h);
                Image bg = r.gameObject.AddComponent<Image>();
                bg.color = AcUi.ControlBack;
                RectTransform area = AcUi.Place(r, "Text Area", 10f, 4f, w - 20f, h - 8f);
                area.gameObject.AddComponent<RectMask2D>();
                TextMeshProUGUI ph = AcUi.OneLine(AcUi.Label(area, "Placeholder", "e.g. D:\\Steam\\steamapps\\common\\assettocorsa", 17f, AcUi.Dim, TextAlignmentOptions.MidlineLeft, 0f, 0f, w - 20f, h - 8f));
                TextMeshProUGUI t = AcUi.OneLine(AcUi.Label(area, "Text", "", 17f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 0f, w - 20f, h - 8f));
                var f = r.gameObject.AddComponent<TMP_InputField>();
                f.textViewport = area;
                f.textComponent = t;
                f.placeholder = ph;
                f.targetGraphic = bg;
                f.lineType = TMP_InputField.LineType.SingleLine;
                f.text = text ?? "";
                var colors = f.colors;
                colors.selectedColor = AcUi.InputFocus; // skin.ini TEXT_INPUT FOCUS_BACK
                f.colors = colors;
                return f;
            }
            catch (Exception e)
            {
                AcLog.Warn("Front: the folder field could not be made: " + e.Message);
                return null;
            }
        }

        private void RefreshFolder()
        {
            if (_folderNow == null) return;
            AcGame g = AcGame.Current;
            string now = g != null && g.Install != null ? g.Install.Root : "not found";
            string set = AcSettings.InstallOverride;
            string t = "In use: " + now + (string.IsNullOrEmpty(set) ? "" : "   (set: " + set + ")");
            if (_folderNow.text != t) _folderNow.text = t;
        }

        private void SetFolder()
        {
            string typed = _field != null ? _field.text.Trim() : "";
            string why = "type the folder with AssettoCorsa.exe and content";
            bool ok = !string.IsNullOrEmpty(typed) && AcInstall.IsInstall(typed, out why);
            if (!ok)
            {
                AcLog.Warn("Settings: Assetto Corsa folder '" + typed + "' not used: " + why);
                Toast("That folder was not saved: " + why + ".");
                return;
            }

            AcSettings.InstallOverride = typed;
            AcSettings.Save();
            AcLog.Info("Settings: Assetto Corsa folder set to " + typed);
            AcGame g = AcGame.Current;
            if (g != null && g.Install == null && g.Rescan()) Toast("Assetto Corsa found in " + typed + ": reading it now.");
            else Toast("Saved: " + typed + " is used the next time you enter the world.");
            RefreshFolder();
        }

        private void DefaultFolder()
        {
            AcSettings.InstallOverride = "";
            AcSettings.Save();
            if (_field != null) _field.text = "";
            Toast("The Assetto Corsa folder is looked for through Steam the next time you enter the world.");
            RefreshFolder();
        }

        private void Rescan()
        {
            AcGame g = AcGame.Current;
            bool ok = g != null && g.Rescan();
            AcLog.Info("Settings: rescan " + (ok ? "started" : "not started (" + (g != null && g.Install != null ? "the install is already read" : "a search is running") + ")"));
            Toast(ok ? "Looking for Assetto Corsa again..." : g != null && g.Install != null ? "Your install is already read (" + g.Install.Root + ")." : "A search is still running.");
        }

        public override void Tick()
        {
            if (_rows == null) return;
            if (_version != AcSettingsUi.Version) { BuildRows(); return; }
            // values change only through AcSettings (a press here, another module, the folder): no strings at 4 Hz otherwise
            int sig = AcFront.SettingsVersion * 31 + (AcGame.Current != null && AcGame.Current.Install != null ? 1 : 0);
            if (sig == _settingsSeen) return;
            _settingsSeen = sig;
            for (int i = 0; i < _values.Count; i++)
            {
                string v = _values[i].Key.Value();
                if (_values[i].Value.text != v) _values[i].Value.text = v;
            }

            for (int i = 0; i < _sliders.Count; i++)
            {
                float v = _sliders[i].Key.Number;
                if (Mathf.Abs(_sliders[i].Value.Value - v) > 1e-4f) _sliders[i].Value.SetValue(v, false);
            }

            RefreshFolder();
        }
    }

    /// <summary>[F, Front] ABOUT: how to play in a headset and on a monitor, and where everything comes from.</summary>
    public sealed class AcPageAbout : AcPage
    {
        public override string Id { get { return "about"; } }
        public override string Caption { get { return "HOW TO PLAY"; } }

        protected override void Build()
        {
            Title("HOW TO PLAY");
            string text =
                "DRIVE: pick your car and skin. TRACK: the instance master (or you, alone) loads a track - everyone in the instance goes there and finds their car in their pit box. " +
                "Walk to your car (or GET IN MY CAR in the RACE LOBBY), open the door and sit in.\n\n" +
                "HEADSET: grab the car's own steering wheel with one or both hands and turn it; right trigger throttle, left trigger brake; A / X shift up / down. " +
                "The view follows the car by heading only (Settings: horizon lock).\n\n" +
                "MONITOR: W / S or the arrow keys throttle and brake, A / D steer, Q / E or Shift / Ctrl shift, Space handbrake, C camera, H lights, R back on the track, F get out. " +
                "A gamepad works too.\n\n" +
                "RACE: the instance master sets the rules in the RACE LOBBY and presses START RACE: every driver goes to the grid, the lights go out, and the TIMING page shows the order. " +
                "While the tablet is open your car waits (its pedals come back when you let go of both triggers).\n\n" +
                "Everything you see and hear - cars, skins, tracks, sounds, these menus' pictures and fonts - is read from your own Assetto Corsa install. Nothing of the game is in this world.";
            var t = AcUi.Label(Root, "Text", text, 21f, AcUi.Text, TextAlignmentOptions.TopLeft, 0f, 64f, W, H - 120f, AcFonts.Face.Light);
            t.overflowMode = TextOverflowModes.Overflow;
            AcUi.OneLine(AcUi.Label(Root, "Credits", "Assetto Corsa (c) Kunos Simulazioni / 505 Games. A Somnium Space Dreamport.", 15f, AcUi.Dim, TextAlignmentOptions.Left, 0f, H - 34f, W, 28f), 9f);
        }
    }
}
