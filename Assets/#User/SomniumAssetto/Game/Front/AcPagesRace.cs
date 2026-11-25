using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] RACE LOBBY: the instance master edits the session and rules with AC's own server_cfg / race.ini names (SESSION, LAPS, TIME,
    /// ABS / TC ALLOWED, STABILITY / AUTOCLUTCH / TYRE BLANKETS ALLOWED, ALLOWED TYRES OUT, JUMP START PENALTY, FUEL RATE, TYRE WEAR RATE,
    /// DAMAGE MULTIPLIER, RACE OVER TIME, AI, ALLOWED CARS) with the launcher's own ranges (AcLobby); everyone else sees the published rules
    /// read-only ("INSTANCE MASTER: name", greyed while the master is not known). During untimed practice the master's edits reach everyone
    /// (AcFront -> AcRace.RequestRules). The drivers in the instance (N's records + the master's AI: car, ready, on track / seated /
    /// spectating, laps), paged. Session controls: START &lt;SESSION&gt; (master, press twice), END SESSION, GET IN MY CAR / GET OUT, BACK TO
    /// PITS, READY, RESUME (closes the tablet), CAMERA, LEAVE TRACK (master) / LEAVE SESSION / JOIN SESSION (others).
    /// Steady state: text is rebuilt only when what it says changes.
    /// </summary>
    public sealed class AcPageLobby : AcPage
    {
        public override string Id { get { return "lobby"; } }
        public override string Caption { get { return "RACE LOBBY"; } }

        private sealed class RuleRow
        {
            public string Label;
            public Func<AcRules, string> Value;
            public Action<AcRules, int> Change;
            public TextMeshProUGUI Text;
            public AcButton Less, More;
        }

        private const int PlayerRows = 12;
        private const float RuleH = 36f;
        private readonly List<RuleRow> _rows = new List<RuleRow>();
        private readonly List<AcNetPlayer> _players = new List<AcNetPlayer>();
        private readonly TextMeshProUGUI[] _pName = new TextMeshProUGUI[PlayerRows], _pCar = new TextMeshProUGUI[PlayerRows], _pState = new TextMeshProUGUI[PlayerRows];
        private TextMeshProUGUI _who, _sessionLine, _pPage;
        private AcButton _start, _stop, _getIn, _pits, _reset, _ready, _resume, _leave, _camera, _pPrev, _pNext;
        private float _armedAt = -100f;
        private int _shownSig;
        private int _seenVersion = -1, _playerPage, _playerSig = int.MinValue, _sessionSig = int.MinValue, _whoSig = int.MinValue;
        private bool _seenMaster, _seenKnown;
        private AcRules _shownSeen;
        private AcLayoutEntry _sessionLayout;
        private AcSessionType _startSession = (AcSessionType)(-1);
        private string _leaveShown;

        protected override void Build()
        {
            Title("RACE LOBBY");
            _who = AcUi.OneLine(AcUi.Label(Root, "Who", "", 19f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 0f, 50f, 620f, 30f), 11f);
            AddRules();
            float y = 86f;
            for (int i = 0; i < _rows.Count; i++)
            {
                RuleRow row = _rows[i];
                RectTransform r = AcUi.Place(Root, "Rule " + row.Label, 0f, y, 620f, RuleH);
                AcUi.Box(r, "Back", 0f, 0f, 620f, RuleH, i % 2 == 0 ? new Color(1f, 1f, 1f, 0.04f) : new Color(0f, 0f, 0f, 0f));
                AcUi.OneLine(AcUi.Label(r, "Label", row.Label, 17f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 10f, 0f, 300f, RuleH), 10f);
                row.Text = AcUi.OneLine(AcUi.Label(r, "Value", "", 17f, AcUi.Text, TextAlignmentOptions.Center, 360f, 0f, 200f, RuleH, AcFonts.Face.Bold), 10f);
                RuleRow captured = row;
                row.Less = AcUi.Button(r, "<", 316f, 2f, 40f, RuleH - 4f, 20f, () => ChangeRule(captured, -1));
                row.More = AcUi.Button(r, ">", 566f, 2f, 40f, RuleH - 4f, 20f, () => ChangeRule(captured, 1));
                row.Less.gameObject.name = "Rule " + row.Label + " <";
                row.More.gameObject.name = "Rule " + row.Label + " >";
                row.Less.Refused = row.More.Refused = NotMaster;
                y += RuleH;
            }

            // the drivers (paged: 50 players + 18 AI do not fit)
            float x = 650f, pw = W - 650f;
            AcUi.Panel(Root, "Drivers panel", x, 50f, pw, 470f);
            AcUi.OneLine(AcUi.Label(Root, "Drivers", "DRIVERS", 20f, AcUi.Text, TextAlignmentOptions.MidlineLeft, x + 12f, 54f, 300f, 32f, AcFonts.Face.Bold));
            _pPrev = AcUi.Button(Root, "<", x + pw - 186f, 55f, 40f, 30f, 18f, PlayersPrev);
            _pPage = AcUi.OneLine(AcUi.Label(Root, "Drivers page", "", 16f, AcUi.Grey, TextAlignmentOptions.Center, x + pw - 142f, 55f, 92f, 30f), 9f);
            _pNext = AcUi.Button(Root, ">", x + pw - 48f, 55f, 40f, 30f, 18f, PlayersNext);
            _pPrev.gameObject.name = "Lobby Drivers <"; _pNext.gameObject.name = "Lobby Drivers >";
            for (int i = 0; i < PlayerRows; i++)
            {
                float py = 92f + i * 35f;
                _pName[i] = AcUi.OneLine(AcUi.Label(Root, "Player " + i, "", 18f, AcUi.Text, TextAlignmentOptions.MidlineLeft, x + 12f, py, 210f, 32f), 10f);
                _pCar[i] = AcUi.OneLine(AcUi.Label(Root, "Player car " + i, "", 16f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, x + 226f, py, 220f, 32f), 9f);
                _pState[i] = AcUi.OneLine(AcUi.Label(Root, "Player state " + i, "", 15f, AcUi.Grey, TextAlignmentOptions.MidlineRight, x + 450f, py, pw - 462f, 32f), 9f);
            }

            // session controls
            _sessionLine = AcUi.OneLine(AcUi.Label(Root, "Session line", "", 19f, AcUi.Text, TextAlignmentOptions.MidlineLeft, x, 528f, pw, 30f), 11f);
            float bw = (pw - 12f) / 2f;
            _start = AcUi.Button(Root, "START RACE", x, 562f, bw, 52f, 22f, StartRace);
            _stop = AcUi.Button(Root, "END SESSION", x + bw + 12f, 562f, bw, 52f, 22f, StopRace);
            // lead (R -> F): RESET CAR (VR has no R key) between GET IN and BACK TO PITS
            float b3 = (pw - 24f) / 3f;
            _getIn = AcUi.Button(Root, "GET IN MY CAR", x, 622f, b3, 52f, 18f, GetIn);
            _reset = AcUi.Button(Root, "RESET CAR", x + b3 + 12f, 622f, b3, 52f, 18f, ResetCarPressed);
            _pits = AcUi.Button(Root, "BACK TO PITS", x + 2f * (b3 + 12f), 622f, b3, 52f, 18f, Pits);
            _reset.gameObject.name = "Lobby Reset";
            _reset.Refused = () => Toast(AcSession.OnTrack ? "Not on the grid, during the lights or while spectating." : "Load a track first (TRACK page).");
            float tw = (pw - 36f) / 4f;
            _ready = AcUi.Button(Root, "READY", x, 682f, tw, 52f, 20f, Ready);
            _resume = AcUi.Button(Root, "RESUME", x + tw + 12f, 682f, tw, 52f, 20f, Resume);
            _camera = AcUi.Button(Root, "CAMERA", x + 2f * (tw + 12f), 682f, tw, 52f, 18f, CameraCycle);
            _leave = AcUi.Button(Root, "LEAVE TRACK", x + 3f * (tw + 12f), 682f, tw, 52f, 16f, Leave);
            _camera.gameObject.name = "Lobby Camera";
            _camera.Refused = () => Toast(AcPlayer.XrPresent ? "In a headset the view is always the cockpit." : "Sit in your car first.");
            _start.gameObject.name = "Lobby Start"; _stop.gameObject.name = "Lobby Stop"; _getIn.gameObject.name = "Lobby GetIn";
            _pits.gameObject.name = "Lobby Pits"; _ready.gameObject.name = "Lobby Ready"; _resume.gameObject.name = "Lobby Resume"; _leave.gameObject.name = "Lobby Leave";
            _start.PressSound = "select";
            _start.Refused = StartRefused;
            _stop.Refused = NotMaster;
            _leave.Refused = () => Toast("No track is loaded and no session runs to join.");
            _getIn.Refused = () => Toast(AcSession.OnTrack ? "Your car is not there yet." : "Load a track first (TRACK page).");
            _pits.Refused = () => Toast(AcSession.InRace ? "Not during a race." : "Load a track first (TRACK page).");
            _resume.Refused = () => Toast("The tablet is not linked.");
            _seenVersion = -1; _playerSig = _sessionSig = _whoSig = int.MinValue; _shownSeen = null; _leaveShown = null; _startSession = (AcSessionType)(-1);
            Refresh(true);
        }

        private void AddRules()
        {
            _rows.Clear();
            Rule(AcLocale.S("SESSION", "SESSION"), r => AcLocale.Session((int)r.Session), (r, d) => r.Session = (AcSessionType)Wrap((int)r.Session - 1 + d, 4) + 1);
            // [RACE] LAPS: the launcher's sliderRaceLaps 2..100; 0 = a timed race (server_cfg [RACE] TIME) below 2
            Rule(AcLocale.S("LAPS", "LAPS"), r => r.Session != AcSessionType.Race ? "-" : r.Laps > 0 ? r.Laps.ToString(CultureInfo.InvariantCulture) : "TIMED", (r, d) => r.Laps = StepLaps(r.Laps, d));
            // [PRACTICE|QUALIFY] TIME: the launcher's practiceslider / qualifyslider, 5-minute steps up to 90
            Rule("TIME (MIN)", r => r.TimeMinutes > 0 ? r.TimeMinutes.ToString(CultureInfo.InvariantCulture) : "UNLIMITED", (r, d) => r.TimeMinutes = Mathf.Clamp(r.TimeMinutes + d * AcLobby.TimeStep, 0, AcLobby.TimeMax));
            Rule(AcLocale.L("ABS", "ABS") + " ALLOWED", r => AcLobby.AbsChoices[Mathf.Clamp(r.AbsAllowed, 0, 2)], (r, d) => r.AbsAllowed = Wrap(r.AbsAllowed + d, 3));
            Rule(AcLocale.L("TRACTION_CONTROL", "Traction Control").ToUpperInvariant() + " ALLOWED", r => AcLobby.AbsChoices[Mathf.Clamp(r.TcAllowed, 0, 2)], (r, d) => r.TcAllowed = Wrap(r.TcAllowed + d, 3));
            Rule("STABILITY ALLOWED", r => OnOff(r.StabilityAllowed), (r, d) => r.StabilityAllowed = !r.StabilityAllowed);
            Rule("AUTOCLUTCH ALLOWED", r => OnOff(r.AutoClutchAllowed), (r, d) => r.AutoClutchAllowed = !r.AutoClutchAllowed);
            Rule(AcLocale.L("AUTOMATIC_GEARS", "Automatic Gears").ToUpperInvariant() + " ALLOWED", r => OnOff(r.AutoShiftAllowed), (r, d) => r.AutoShiftAllowed = !r.AutoShiftAllowed);
            Rule("TYRE BLANKETS ALLOWED", r => OnOff(r.TyreBlanketsAllowed), (r, d) => r.TyreBlanketsAllowed = !r.TyreBlanketsAllowed);
            Rule("ALLOWED TYRES OUT", r => r.AllowedTyresOut < 0 ? "NO CUT RULE" : r.AllowedTyresOut.ToString(CultureInfo.InvariantCulture), (r, d) => r.AllowedTyresOut = Mathf.Clamp(r.AllowedTyresOut + d, -1, 4));
            Rule("JUMP START PENALTY", r => AcLobby.JumpStartChoices[Mathf.Clamp(r.JumpStartPenalty, 0, 2)], (r, d) => r.JumpStartPenalty = Wrap(r.JumpStartPenalty + d, 3));
            Rule("FUEL RATE", r => r.FuelRate + " %", (r, d) => r.FuelRate = StepList(AcLobby.FuelRates, r.FuelRate, d));
            Rule("TYRE WEAR RATE", r => r.TyreWearRate + " %", (r, d) => r.TyreWearRate = StepList(AcLobby.TyreWearRates, r.TyreWearRate, d));
            Rule("DAMAGE MULTIPLIER", r => r.DamageMultiplier <= 0 ? "OFF" : r.DamageMultiplier + " %", (r, d) => r.DamageMultiplier = Mathf.Clamp(r.DamageMultiplier + d * AcLobby.DamageStep, 0, AcLobby.DamageMax));
            Rule("RACE OVER TIME", r => r.RaceOverTimeSeconds + " s", (r, d) => r.RaceOverTimeSeconds = Mathf.Clamp(r.RaceOverTimeSeconds + d * AcLobby.OverTimeStep, 0, AcLobby.OverTimeMax));
            Rule("AI OPPONENTS", r => r.AiCount.ToString(CultureInfo.InvariantCulture), (r, d) => r.AiCount = Mathf.Clamp(r.AiCount + d, 0, AcLobby.AiMax));
            Rule("AI LEVEL", r => r.AiLevel + " %", (r, d) => r.AiLevel = Mathf.Clamp(r.AiLevel + d, AcLobby.AiLevelMin, AcLobby.AiLevelMax));
            _rows.Add(new RuleRow { Label = "ALLOWED CARS", Value = r => AcLobby.AllowedText(r, AcMenu.SafeIsMaster()), Change = (r, d) => AcLobby.AllowedMode = Wrap(AcLobby.AllowedMode + d, 3) });
        }

        private void Rule(string label, Func<AcRules, string> value, Action<AcRules, int> change)
        {
            _rows.Add(new RuleRow { Label = label, Value = value, Change = change });
        }

        private static int Wrap(int v, int n) { return ((v % n) + n) % n; }
        private static string OnOff(bool b) { return b ? "YES" : "NO"; }

        private static int StepLaps(int laps, int d)
        {
            if (d < 0) return laps <= AcLobby.LapsMin ? 0 : laps - 1;
            return laps <= 0 ? AcLobby.LapsMin : Mathf.Min(laps + 1, AcLobby.LapsMax);
        }

        /// <summary>The next / previous value of a launcher choice list (a value not in the list snaps to the nearest one).</summary>
        private static int StepList(int[] values, int current, int d)
        {
            int at = 0, best = int.MaxValue;
            for (int i = 0; i < values.Length; i++) { int e = Mathf.Abs(values[i] - current); if (e < best) { best = e; at = i; } }
            if (best != 0) return values[at];
            return values[Mathf.Clamp(at + d, 0, values.Length - 1)];
        }

        private void ChangeRule(RuleRow row, int d)
        {
            if (!AcMenu.SafeIsMaster()) { NotMaster(); return; }
            row.Change(AcLobby.Rules, d);
            AcLobby.Changed();
            Refresh(true);
        }

        private void NotMaster()
        {
            Toast(!AcMenu.SafeMasterKnown() ? "Waiting for the instance master to be known..." : "Only the instance master (" + AcMenu.SafeMasterName() + ") sets the rules.");
        }

        private void PlayersPrev() { if (_playerPage > 0) { _playerPage--; _playerSig = int.MinValue; Refresh(false); } }
        private void PlayersNext() { _playerPage++; _playerSig = int.MinValue; Refresh(false); }

        private void StartRace()
        {
            if (AcSession.Phase < AcPhase.Practice) { Toast("Load a track first (TRACK page)."); return; }
            AcRules r = AcLobby.Rules;
            if (Time.unscaledTime - _armedAt > 4f) // ours: press twice within 4 s (Halo's ArmMap)
            {
                _armedAt = Time.unscaledTime;
                bool race = r == null || r.Session == AcSessionType.Race;
                Toast("Press " + AcFront.StartLabel(r) + " again to " + (race ? "put every driver on the grid: " : "start it now for everyone: ") + AcLobby.SessionLine(r) + ".");
                return;
            }

            _armedAt = -100f;
            AcFront.RequestStart("tablet");
        }

        private void StartRefused()
        {
            if (!AcMenu.SafeIsMaster() || !AcMenu.SafeMasterKnown()) NotMaster();
            else if (AcSession.InRace) Toast("A race is running.");
            else Toast("Load a track first (TRACK page).");
        }

        private void StopRace()
        {
            bool ok = false;
            try { ok = AcRace.RequestStop(); }
            catch (Exception e) { AcLog.Warn("Front: AcRace.RequestStop failed: " + e.Message); }
            AcLog.Info("Front: end session (" + (ok ? "master" : "refused") + ")");
            Toast(ok ? "The session ends for everyone - back to practice." : "Ending the session was refused.");
        }

        private void CameraCycle()
        {
            try { AcCarCamera.Cycle(); }
            catch (Exception e) { AcLog.Warn("Front: AcCarCamera.Cycle failed: " + e.Message); return; }
            string mode;
            try { mode = AcCameraMode.Name(AcLocal.CameraMode); } catch (Exception) { mode = "?"; }
            Toast("Camera: " + mode);
        }

        private void GetIn()
        {
            if (AcLocal.Seated)
            {
                // seated: the same button gets out (S's AcSeat.CanExit says why not: moving, race running ...)
                string why = null;
                bool can = false;
                try { can = AcSeat.CanExit(out why); } catch (Exception e) { why = e.Message; }
                bool done = false;
                if (can) { try { done = AcSeat.Exit(); } catch (Exception e) { AcLog.Warn("Front: AcSeat.Exit failed: " + e.Message); } }
                AcLog.Info("Front: get out from the tablet (" + (done ? "out" : "refused: " + (why ?? "?")) + ")");
                if (!done) Toast(string.IsNullOrEmpty(why) ? "Getting out was refused." : why);
                return;
            }

            AcCarRig car = null;
            try { car = AcRace.LocalCar ?? AcLocal.Car; } catch (Exception) { car = AcLocal.Car; }
            bool ok = false;
            try { ok = car != null && AcSeat.Enter(car, AcSeatKind.Driver); }
            catch (Exception e) { AcLog.Warn("Front: AcSeat.Enter failed: " + e.Message); }
            AcLog.Info("Front: get in my car from the tablet (" + (ok ? "seated" : "refused") + ")");
            if (ok) { AcFront.CloseTablet("getting in"); }
            else Toast(car == null ? "Your car is not there yet." : "Getting in was refused.");
        }

        private void Pits()
        {
            bool ok = false;
            try { ok = AcRace.BackToPits(); }
            catch (Exception e) { AcLog.Warn("Front: AcRace.BackToPits failed: " + e.Message); }
            AcLog.Info("Front: back to pits (" + (ok ? "done" : "refused") + ")");
            Toast(ok ? "Your car is back in your pit box." + (AcFront.SetupWaiting ? " Your setup goes in now." : "") : "Back to pits was refused.");
        }

        private void ResetCarPressed()
        {
            bool ok = false;
            try { ok = AcRace.ResetCar(); }
            catch (Exception e) { AcLog.Warn("Front: AcRace.ResetCar failed: " + e.Message); }
            AcLog.Info("Front: reset car (" + (ok ? "done" : "refused") + ")");
            if (ok) Toast("Your car is back on the track (this lap does not count).");
        }

        private void Ready()
        {
            AcLocal.Ready = !AcLocal.Ready;
            AcLog.Info("Front: lobby ready " + (AcLocal.Ready ? "on" : "off"));
            _playerSig = int.MinValue;
            Refresh(false);
        }

        private void Resume() { AcFront.CloseTablet("resume"); }

        private void Leave() { AcFront.LeaveOrJoin("lobby"); }

        public override void Tick() { Refresh(false); }

        private static bool SafeSpectating() { try { return AcRace.Spectating; } catch (Exception) { return false; } }

        private void Refresh(bool rules)
        {
            if (_start == null) return;
            bool master = AcMenu.SafeIsMaster(), known = AcMenu.SafeMasterKnown();
            AcRules shown = AcLobby.Shown;
            int shownSig = AcLobby.Sig(shown) + AcLobby.AllowedMode * 977;
            if (rules || _seenVersion != AcLobby.Version || _seenMaster != master || _seenKnown != known || !ReferenceEquals(shown, _shownSeen) || shownSig != _shownSig)
            {
                _seenVersion = AcLobby.Version;
                _seenMaster = master;
                _seenKnown = known;
                _shownSeen = shown;
                _shownSig = shownSig;
                for (int i = 0; i < _rows.Count; i++)
                {
                    RuleRow row = _rows[i];
                    string v;
                    try { v = row.Value(shown); } catch (Exception) { v = "?"; }
                    if (row.Text.text != v) row.Text.text = v;
                    row.Less.Interactable = master && known;
                    row.More.Interactable = master && known;
                }
            }

            string masterName = AcMenu.SafeMasterName();
            int whoSig = (known ? 1 : 0) + (master ? 2 : 0) + (masterName != null ? masterName.GetHashCode() * 4 : 0);
            if (whoSig != _whoSig)
            {
                _whoSig = whoSig;
                _who.text = !known ? "INSTANCE MASTER: connecting..." : master ? "You are the instance master: you set the rules and start the sessions." : "INSTANCE MASTER: " + masterName + " sets the rules.";
            }

            bool onTrack = AcSession.OnTrack;
            bool inRace = AcSession.InRace;
            bool spectating = SafeSpectating();
            bool canJoin = AcFront.CanJoinSession;
            int sessionSig = (int)AcSession.Phase * 1000 + Mathf.RoundToInt(AcSession.LoadProgress * 100f) + (AcSession.Rules != null ? (int)AcSession.Rules.Session * 100000 : 0) + (spectating ? 1 << 22 : 0) + (canJoin ? 1 << 23 : 0);
            if (sessionSig != _sessionSig || AcSession.Layout != _sessionLayout)
            {
                _sessionSig = sessionSig;
                _sessionLayout = AcSession.Layout;
                _sessionLine.text = AcMenu.PhaseLine() + (spectating ? "   -   SPECTATING (your car waits in the pits until the race ends)" : canJoin ? "   -   a session runs: JOIN SESSION" : onTrack ? "" : "   -   load a track on the TRACK page");
            }

            if (shown != null && shown.Session != _startSession)
            {
                _startSession = shown.Session;
                _start.SetLabel(AcFront.StartLabel(shown));
            }

            _start.Interactable = master && known && onTrack && !inRace;
            _start.Selected = Time.unscaledTime - _armedAt < 4f;
            _stop.Interactable = master && known && (inRace || AcSession.StartUtc > 0);
            string leave = AcFront.LeaveLabel();
            if (!ReferenceEquals(leave, _leaveShown)) { _leaveShown = leave; _leave.SetLabel(leave); }
            _leave.Interactable = AcFront.LeaveOrJoinPossible;
            AcCarRig car = null;
            try { car = AcRace.LocalCar ?? AcLocal.Car; } catch (Exception) { car = AcLocal.Car; }
            _getIn.Interactable = onTrack && (AcLocal.Seated || car != null);
            _getIn.SetLabel(AcLocal.Seated ? "GET OUT" : "GET IN MY CAR");
            _camera.Interactable = AcLocal.Seated && !AcPlayer.XrPresent;
            _pits.Interactable = onTrack && (!inRace || spectating);
            _reset.Interactable = onTrack && car != null && !spectating && AcSession.Phase != AcPhase.Grid && AcSession.Phase != AcPhase.Countdown;
            _ready.Selected = AcLocal.Ready;
            _resume.Interactable = AcFront.TabletLinked;

            RefreshPlayers(master, onTrack, spectating);
        }

        private void RefreshPlayers(bool master, bool onTrack, bool spectating)
        {
            AcLobby.Players(_players);
            int total = 1 + _players.Count;
            int pages = AcPaging.Pages(total, PlayerRows);
            _playerPage = AcPaging.Clamp(_playerPage, total, PlayerRows);
            // a signature of everything the rows say (string hashes do not allocate)
            int sig = _playerPage * 131 + total * 7 + (master ? 1 : 0) + (onTrack ? 2 : 0) + (spectating ? 4 : 0) + (AcLocal.Seated ? 8 : 0) + (AcLocal.Ready ? 16 : 0);
            sig = sig * 31 + (AcLocal.CarId != null ? AcLocal.CarId.GetHashCode() : 0) + (AcLocal.Nick != null ? AcLocal.Nick.GetHashCode() : 0);
            int from = _playerPage * PlayerRows - 1, to = from + PlayerRows;
            for (int i = Mathf.Max(0, from); i < _players.Count && i < to; i++)
            {
                AcNetPlayer p = _players[i];
                sig = sig * 31 + (p.Name != null ? p.Name.GetHashCode() : 0);
                sig = sig * 31 + (p.CarId != null ? p.CarId.GetHashCode() : 0);
                sig = sig * 31 + p.Laps * 17 + (p.Seated ? 1 : 0) + (p.OnTrack ? 2 : 0) + (p.RideInPhotonId >= 0 ? 4 : 0) + (p.Ready ? 8 : 0) + (p.IsMaster ? 16 : 0) + (p.Spectating ? 32 : 0) + (p.IsAi ? 64 : 0);
            }

            if (sig == _playerSig) return;
            _playerSig = sig;
            _pPrev.Interactable = _playerPage > 0;
            _pNext.Interactable = _playerPage < pages - 1;
            _pPage.text = (_playerPage + 1) + " / " + pages + "  (" + total + ")";
            int row = 0;
            if (_playerPage == 0)
            {
                string me = spectating ? "SPECTATING" : AcLocal.Seated ? "SEATED" : onTrack ? "ON TRACK" : "SHOWROOM";
                SetPlayer(row++, (string.IsNullOrEmpty(AcLocal.Nick) ? "You" : AcLocal.Nick) + "  (YOU)" + (master ? "  *" : ""), CarName(AcLocal.CarId), me, AcLocal.Ready);
            }

            for (int i = Mathf.Max(0, from); i < _players.Count && row < PlayerRows; i++)
            {
                AcNetPlayer p = _players[i];
                string state = p.IsAi ? (p.OnTrack ? "AI  -  ON TRACK" : "AI") : p.Spectating ? "SPECTATING" : p.Seated ? (p.RideInPhotonId >= 0 ? "PASSENGER" : "SEATED") : p.OnTrack ? "ON TRACK" : "SHOWROOM";
                if (p.Laps > 0) state += "  -  " + p.Laps + " " + AcLocale.S("Laps", "LAPS");
                SetPlayer(row++, (p.Name ?? "") + (p.IsMaster ? "  *" : "") + (p.IsAi ? "  (AI)" : ""), CarName(p.CarId), state, p.Ready && !p.IsAi);
            }

            for (int i = row; i < PlayerRows; i++) SetPlayer(i, "", "", "", false);
            Menu.ApplyLayers();
        }

        private void SetPlayer(int i, string name, string car, string state, bool ready)
        {
            if (_pName[i].text != name) _pName[i].text = name;
            if (_pCar[i].text != car) _pCar[i].text = car;
            string s = ready ? "READY  -  " + state : state;
            if (_pState[i].text != s) _pState[i].text = s;
        }

        private static string CarName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            AcContent c = AcFront.Content;
            AcCarEntry car = c != null ? c.Car(id) : null;
            return car != null ? car.Name : id;
        }
    }

    /// <summary>
    /// [F, Front] TIMING / RESULTS: the live standings (AcRace.Standings: position, driver, car badge + name, laps, best, last, gap) in AC's
    /// lap-time format, paged (36 drivers do not fit) with your own row always on screen (the last row when your page is another), the session
    /// line with the clock (AcRace.SessionTimeLeft) or "LAP n/N", and your own laps this session (AcSession.LapCompleted: time, sectors, valid /
    /// cuts). After AcSession.RaceFinished the final order stays (RESULTS) with the launcher's podium badges (badge_event_1st/2nd/3rd).
    /// </summary>
    public sealed class AcPageTiming : AcPage
    {
        public override string Id { get { return "timing"; } }
        public override string Caption { get { return "TIMING"; } }

        private const int Rows = 12, LapRows = 6;
        private readonly TextMeshProUGUI[] _pos = new TextMeshProUGUI[Rows], _name = new TextMeshProUGUI[Rows], _car = new TextMeshProUGUI[Rows], _laps = new TextMeshProUGUI[Rows], _best = new TextMeshProUGUI[Rows], _last = new TextMeshProUGUI[Rows], _gap = new TextMeshProUGUI[Rows];
        private readonly RawImage[] _badge = new RawImage[Rows], _podium = new RawImage[3];
        private readonly string[] _badgeShown = new string[Rows];
        private readonly int[] _index = new int[Rows];
        private readonly TextMeshProUGUI[] _lap = new TextMeshProUGUI[LapRows];
        private TextMeshProUGUI _head, _info, _clock, _lapsHead, _pageText;
        private AcButton _prev, _next;
        private int _sig = int.MinValue, _page, _clockSig = int.MinValue;
        private bool _manualPage;
        private AcPhase _pagePhase;

        protected override void Build()
        {
            _head = Title("TIMING");
            _info = AcUi.OneLine(AcUi.Label(Root, "Info", "", 19f, AcUi.Grey, TextAlignmentOptions.MidlineLeft, 0f, 50f, W - 520f, 30f), 11f);
            _clock = AcUi.OneLine(AcUi.Label(Root, "Clock", "", 20f, AcUi.Text, TextAlignmentOptions.MidlineRight, W - 510f, 50f, 260f, 30f, AcFonts.Face.Bold), 11f);
            _prev = AcUi.Button(Root, "<", W - 236f, 49f, 44f, 32f, 18f, PagePrev);
            _pageText = AcUi.OneLine(AcUi.Label(Root, "Page", "", 16f, AcUi.Grey, TextAlignmentOptions.Center, W - 188f, 49f, 140f, 32f), 9f);
            _next = AcUi.Button(Root, ">", W - 44f, 49f, 44f, 32f, 18f, PageNext);
            _prev.gameObject.name = "Timing <"; _next.gameObject.name = "Timing >";
            float[] x = { 0f, 70f, 380f, 780f, 870f, 1020f, 1140f };
            string[] cols = { "POS", "DRIVER", "CAR", AcLocale.S("LAPS", "LAPS"), AcLocale.S("BEST", "BEST"), AcLocale.S("LAST", "LAST"), AcLocale.S("GAP", "GAP") };
            for (int c = 0; c < cols.Length; c++)
                AcUi.OneLine(AcUi.Label(Root, "Col " + cols[c], cols[c], 15f, AcUi.Grey, c >= 3 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, x[c], 84f, (c + 1 < x.Length ? x[c + 1] : W) - x[c] - 10f, 26f));
            AcInstall inst = AcGame.Current != null ? AcGame.Current.Install : null;
            string badges = inst != null ? System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(inst.Root, "launcher"), "themes"), "default"), "graphics"), "badges") : null;
            for (int i = 0; i < Rows; i++)
            {
                float y = 112f + i * 36f;
                AcUi.Box(Root, "Row back " + i, 0f, y, W, 34f, i % 2 == 0 ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.25f));
                _pos[i] = Cell("Pos " + i, x[0] + 8f, y, 50f, TextAlignmentOptions.MidlineLeft, AcFonts.Face.Bold);
                _name[i] = Cell("Name " + i, x[1], y, x[2] - x[1] - 10f, TextAlignmentOptions.MidlineLeft, AcFonts.Face.Regular);
                _badge[i] = AcUi.Place(Root, "Badge " + i, x[2], y + 2f, 30f, 30f).gameObject.AddComponent<RawImage>();
                _badge[i].raycastTarget = false;
                _badge[i].color = new Color(1f, 1f, 1f, 0f);
                _car[i] = Cell("Car " + i, x[2] + 38f, y, x[3] - x[2] - 48f, TextAlignmentOptions.MidlineLeft, AcFonts.Face.Light);
                _laps[i] = Cell("Laps " + i, x[3], y, x[4] - x[3] - 10f, TextAlignmentOptions.MidlineRight, AcFonts.Face.Regular);
                _best[i] = Cell("Best " + i, x[4], y, x[5] - x[4] - 10f, TextAlignmentOptions.MidlineRight, AcFonts.Face.Regular);
                _last[i] = Cell("Last " + i, x[5], y, x[6] - x[5] - 10f, TextAlignmentOptions.MidlineRight, AcFonts.Face.Regular);
                _gap[i] = Cell("Gap " + i, x[6], y, W - x[6] - 10f, TextAlignmentOptions.MidlineRight, AcFonts.Face.Regular);
                if (i < 3 && badges != null)
                {
                    string file = System.IO.Path.Combine(badges, i == 0 ? "badge_event_1st.png" : i == 1 ? "badge_event_2nd.png" : "badge_event_3rd.png");
                    _podium[i] = AcUi.Picture(Root, "Podium " + i, x[1] - 40f, y + 1f, 32f, 32f, file, 64, Color.white);
                    _podium[i].gameObject.SetActive(false);
                }
            }

            _lapsHead = AcUi.OneLine(AcUi.Label(Root, "Your laps", "YOUR LAPS", 18f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 552f, W, 28f, AcFonts.Face.Bold));
            for (int i = 0; i < LapRows; i++) _lap[i] = AcUi.OneLine(AcUi.Label(Root, "Lap " + i, "", 17f, AcUi.Text, TextAlignmentOptions.MidlineLeft, 0f, 582f + i * 28f, W, 28f), 10f);
            _sig = _clockSig = int.MinValue;
        }

        private TextMeshProUGUI Cell(string name, float x, float y, float w, TextAlignmentOptions a, AcFonts.Face f)
        {
            return AcUi.OneLine(AcUi.Label(Root, name, "", 19f, AcUi.Text, a, x, y, w, 34f, f), 10f);
        }

        private void PagePrev() { if (_page > 0) { _page--; _manualPage = true; _sig = int.MinValue; Tick(); } }
        private void PageNext() { _page++; _manualPage = true; _sig = int.MinValue; Tick(); }

        public override void Tick()
        {
            if (_head == null) return;
            IReadOnlyList<AcStanding> live = null;
            try { live = AcRace.Standings; } catch (Exception) { live = null; }
            bool results = AcFront.HasResults && (AcSession.Phase == AcPhase.Finished || AcSession.Phase == AcPhase.Results || live == null || live.Count == 0);
            IReadOnlyList<AcStanding> list = results ? AcFront.Results : live;
            int n = list != null ? list.Count : 0;
            if (AcSession.Phase != _pagePhase) { _pagePhase = AcSession.Phase; _manualPage = false; } // a new session: the page follows you again

            int local = -1;
            for (int i = 0; i < n; i++) { AcStanding s = list[i]; if (s != null && s.IsLocal) { local = i; break; } }
            int pages = AcPaging.Pages(n, Rows);
            if (!_manualPage) _page = local >= 0 ? local / Rows : 0;
            _page = AcPaging.Clamp(_page, n, Rows);
            // the rows on screen: this page; your row replaces the last one when it is elsewhere
            for (int r = 0; r < Rows; r++) { int k = _page * Rows + r; _index[r] = k < n ? k : -1; }
            if (local >= 0 && local / Rows != _page) _index[Rows - 1] = local;

            TickClock(list, n, results);

            int sig = n * 7919 + (results ? 1 : 0) + (int)AcSession.Phase * 31 + AcFront.LapVersion * 131 + _page * 1031;
            for (int r = 0; r < Rows; r++)
            {
                int k = _index[r];
                AcStanding s = k >= 0 ? list[k] : null;
                if (s == null) { sig = sig * 31 + 1; continue; }
                sig = sig * 31 + k * 3 + s.Position * 7 + s.Laps * 13 + s.BestMs + s.LastMs * 3 + s.GapMs + s.LapsBehind * 17 + (s.Finished ? 1 : 0) + (s.InPit ? 2 : 0) + (s.Retired ? 4 : 0) + s.PenaltyMs;
            }

            if (sig == _sig) return;
            _sig = sig;
            _head.text = results ? "RESULTS" : "TIMING";
            _prev.Interactable = _page > 0;
            _next.Interactable = _page < pages - 1;
            _pageText.text = pages > 1 ? (_page + 1) + " / " + pages : "";
            AcRules rules = AcSession.Rules;
            _info.text = (AcSession.Layout != null ? AcSession.Layout.Name + "   -   " : "") + AcLobby.SessionLine(rules) + (AcSession.OnTrack ? "" : "   -   no session running");
            for (int r = 0; r < Rows; r++)
            {
                int k = _index[r];
                AcStanding s = k >= 0 ? list[k] : null;
                if (s == null) { Clear(r); continue; }
                bool leader = k == 0;
                Color c = s.IsLocal ? AcUi.Gold : AcUi.Text;
                Set(_pos[r], (s.Position > 0 ? s.Position : k + 1).ToString(CultureInfo.InvariantCulture), c);
                Set(_name[r], (s.Name ?? "") + (s.IsAi ? "  (AI)" : "") + (s.InPit ? "  PIT" : ""), c);
                AcCarEntry car = AcFront.Content != null && !string.IsNullOrEmpty(s.CarId) ? AcFront.Content.Car(s.CarId) : null;
                Set(_car[r], car != null ? car.Name : s.CarId ?? "", c);
                string badge = car != null ? car.BadgePath : null;
                if (_badgeShown[r] != badge) { _badgeShown[r] = badge; AcUiArt.Bind(_badge[r], badge, 64, Color.white, true); }
                Set(_laps[r], s.Laps.ToString(CultureInfo.InvariantCulture), c);
                Set(_best[r], AcUiFormat.LapTime(s.BestMs), c);
                Set(_last[r], AcUiFormat.LapTime(s.LastMs), c);
                string gap = results && s.Finished && leader ? AcUiFormat.RaceTime(s.TotalMs) : AcUiFormat.Gap(s.GapMs, s.LapsBehind, leader);
                if (s.Retired && !s.Finished) gap = "DNF";         // iteration 2: engine blown (R's AcStanding.Retired)
                if (s.PenaltyMs > 0) gap += "  +" + (s.PenaltyMs / 1000) + "s";
                Set(_gap[r], gap, c);
                if (r < 3 && _podium[r] != null) _podium[r].gameObject.SetActive(results && k == r);
            }

            if (n == 0) Set(_name[0], AcSession.OnTrack ? "No laps timed yet." : "Times show here once a session runs.", AcUi.Grey);

            List<AcLap> laps = AcFront.LocalLaps;
            _lapsHead.text = "YOUR LAPS" + (laps.Count > 0 ? "   (" + laps.Count + ")" : "");
            for (int i = 0; i < LapRows; i++)
            {
                int k = laps.Count - 1 - i;
                if (k < 0) { Set(_lap[i], "", AcUi.Text); continue; }
                AcLap l = laps[k];
                string sectors = "";
                if (l.SectorMs != null) for (int s = 0; s < l.SectorMs.Length; s++) sectors += (s == 0 ? "   S" : "  S") + (s + 1) + " " + AcUiFormat.LapTime(l.SectorMs[s]);
                Set(_lap[i], "LAP " + l.Lap + "   " + AcUiFormat.LapTime(l.TimeMs) + sectors + (l.Valid ? "" : "   INVALID" + (l.Cuts > 0 ? " (" + l.Cuts + " cuts)" : "")), l.Valid ? AcUi.Text : AcUi.Dim);
            }

            Menu.ApplyLayers();
        }

        /// <summary>The session clock (R's SessionTimeLeft, whole seconds) or the leader's lap of a lap race - rebuilt once a second at most.</summary>
        private void TickClock(IReadOnlyList<AcStanding> list, int n, bool results)
        {
            float left = -1f;
            try { left = AcRace.SessionTimeLeft; } catch (Exception) { left = -1f; }
            AcRules rules = AcSession.Rules;
            int lapsTotal = rules != null && rules.Session == AcSessionType.Race && rules.Laps > 0 && AcSession.InRace ? rules.Laps : 0;
            int leaderLaps = n > 0 && list[0] != null ? list[0].Laps : 0;
            int sig = left >= 0f ? Mathf.CeilToInt(left) : lapsTotal > 0 ? 1000000 + leaderLaps * 1000 + lapsTotal : (results ? -2 : -1);
            if (sig == _clockSig) return;
            _clockSig = sig;
            if (left >= 0f)
            {
                int s = Mathf.CeilToInt(left);
                _clock.text = "TIME LEFT  " + (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
            }
            else if (lapsTotal > 0)
            {
                int lap = Mathf.Clamp(leaderLaps + 1, 1, lapsTotal);
                _clock.text = AcLocale.S("LAP", "LAP") + "  " + lap + " / " + lapsTotal;
            }
            else _clock.text = results ? "FINISHED" : "";
        }

        private void Clear(int i)
        {
            Set(_pos[i], "", AcUi.Text); Set(_name[i], "", AcUi.Text); Set(_car[i], "", AcUi.Text); Set(_laps[i], "", AcUi.Text);
            Set(_best[i], "", AcUi.Text); Set(_last[i], "", AcUi.Text); Set(_gap[i], "", AcUi.Text);
            if (_badgeShown[i] != null) { _badgeShown[i] = null; _badge[i].texture = null; _badge[i].color = new Color(1f, 1f, 1f, 0f); }
            if (i < 3 && _podium[i] != null) _podium[i].gameObject.SetActive(false);
        }

        private static void Set(TextMeshProUGUI t, string s, Color c)
        {
            if (t.text != s) t.text = s;
            if (t.color != c) t.color = c;
        }
    }
}
