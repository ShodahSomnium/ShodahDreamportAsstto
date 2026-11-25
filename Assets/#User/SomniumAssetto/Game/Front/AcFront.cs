using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] The front end's conductor (AcGame.Front.cs calls it, only after the whitelist gate, so a visitor never gets any of it): settings,
    /// the hub (AC's showroom, AcHub) and its menu panel, the "ASSETTO CORSA" Somnium tablet app (AcTabletApp + AcTabletLink), the game's words /
    /// colours / fonts / pictures (AcLocale, AcGuiSkin, AcFonts, AcUiArt), the loading card (AcLoading, following AcSession), the lobby rules
    /// (AcLobby), the hub's keep rule, and holding the car while the tablet is open. Both menus (tablet frame, hub panel) are AcMenu instances with
    /// the same pages (<see cref="BuildMenu"/>). Other modules read <see cref="LobbyRules"/> (R), <see cref="HoldCar"/> (S), <see cref="Assists"/> and
    /// <see cref="SetupValue"/> (P / R). AcLoading is in AcLoading.cs, AcSettingsUi in AcSettingsUi.cs. Only ADD members.
    /// </summary>
    public static class AcFront
    {
        private static AcGame _game;
        private static AcHub _hub;
        private static AcMenu _hubMenu;
        private static readonly List<AcMenu> Menus = new List<AcMenu>();
        private static bool _started, _bootApplied;
        private static float _nextMenuTick, _nextKeep, _awaySince = -1f;
        private static int _keepMoves;
        private static bool _held, _holdLogged;
        private static string _pendingToast;

        // session follow-up (4 Hz): rules to R, assists on a rules change, spectating to N, the panel without a tablet, garage values to the car
        private static float _nextSession;
        private static AcRules _rulesSeen;
        private static int _rulesSentSig = int.MinValue;
        private static float _rulesDirtyAt = -1f;
        private static bool _wasMaster, _panelOnTrack, _panelLogged, _specLogged, _rulesLogged;
        private static int _rulesSent;
        private static int _hubLookFrame = -1;
        private static float _lobbyReloadAt = -1f;
        private static readonly HashSet<string> SetupPending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _compoundPending;
        private static string _pendingCar;

        // car specs (P's AcCarRig.SpecFor on a worker) for the Garage's tyre compounds
        private static readonly Dictionary<string, AcCarSpec> SpecCache = new Dictionary<string, AcCarSpec>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SpecLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // boot data read on a worker
        private static Thread _bootThread;
        private static volatile bool _bootReady;
        private static AcGuiSkin _skin;
        private static AcAssistsIni _assists;
        private static int _localeKeys;

        // results / laps (TIMING page)
        private static readonly List<AcStanding> ResultList = new List<AcStanding>();
        private static readonly List<AcLap> Laps = new List<AcLap>();

        // setup rows per car (worker reads of data.acd)
        private static readonly Dictionary<string, List<AcSetupRow>> SetupCache = new Dictionary<string, List<AcSetupRow>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SetupLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SetupGate = new object();

        public static bool Started { get { return _started; } }
        public static AcHub Hub { get { return _hub; } }
        public static AcTabletApp App { get { return AcTabletApp.Instance; } }
        /// <summary>The content catalog (null before the boot read it).</summary>
        public static AcContent Content { get { AcGame g = AcGame.Current; return g != null ? g.Content : null; } }
        /// <summary>The game's words / colours / assists were read and applied.</summary>
        public static bool BootApplied { get { return _bootApplied; } }
        /// <summary>The player's assists.ini defaults (null before the boot).</summary>
        public static AcAssistsIni AssistDefaults { get { return _assists; } }
        public static bool TabletLinked { get { AcTabletApp a = AcTabletApp.Instance; return a != null && a.Link != null && a.Link.IsLinked; } }

        /// <summary>[for R] The instance master's lobby rules as edited on the tablet (a copy; AllowedCars filled). R uses them for START when the
        /// session's rules are older.</summary>
        public static AcRules LobbyRules { get { return AcLobby.Draft(); } }
        /// <summary>[for S] The local driver's car should take no pedals now: the tablet app is open in front of them, or (VR) the Somnium tablet
        /// is open at all - its buttons are pressed with the trigger that is the throttle (ETS lesson). Setting "front.holdcar". When it ends F
        /// calls AcDriveInput.DisarmPedals (VR) so a trigger still held does not become throttle.</summary>
        public static bool HoldCar { get; private set; }
        /// <summary>The final order after AcSession.RaceFinished (TIMING page); empty before.</summary>
        public static IReadOnlyList<AcStanding> Results { get { return ResultList; } }
        public static bool HasResults { get { return ResultList.Count > 0; } }
        /// <summary>The local player's laps this session (AcSession.LapCompleted with IsLocal).</summary>
        public static List<AcLap> LocalLaps { get { return Laps; } }
        public static int LapVersion { get; private set; }
        /// <summary>Bumped on every AcSettings change (pages refresh setting-driven text only when it moves - no strings at 4 Hz otherwise).</summary>
        public static int SettingsVersion { get; private set; }
        /// <summary>[for P, if tyre temperatures are ever simulated] The player's TYRE BLANKETS choice (assists.ini TYRE_BLANKETS default) within
        /// the session's TYRE_BLANKETS_ALLOWED: true = the tyres leave the pits warm. Nobody reads it yet (REQUESTS F -> P).</summary>
        public static bool TyreBlankets
        {
            get
            {
                bool on = AssistInt("garage.blankets") != 0;
                AcRules r = AcSession.Rules;
                return on && (!AcSession.OnTrack || r == null || r.TyreBlanketsAllowed);
            }
        }

        // ------------------------------------------------------------------ the hooks

        /// <summary>Called by AcGame right after the gate (settings load, hub, tablet link).</summary>
        public static void Start(AcGame game)
        {
            AcSettings.Load(); // before the boot reads the install override
            if (_started) return;
            _started = true;
            _game = game;
            AcPlayer.EnsureEditorEventSystem();
            AcLobby.Load();
            _hub = AcHub.Create(game.transform);
            Canvas panel = _hub.BuildPanel(AcMenu.Width, AcMenu.Height);
            _hubMenu = BuildMenu(panel.transform, false);
            _hub.SetPanelShown(AcSettings.Flag("front.panel", true));
            game.StatusChanged += OnStatus;
            AcSession.PhaseChanged += OnPhase;
            AcSession.TrackLoading += OnTrackLoading;
            AcSession.LapCompleted += OnLap;
            AcSession.RaceFinished += OnRaceFinished;
            AcSession.RaceStarted += OnRaceStarted;
            AcEvents.Message += OnMessage;
            AcEvents.LocalCarChanged += OnLocalCar;
            AcSettings.Changed += OnSetting;
            try { AcTabletApp.Create(game); }
            catch (Exception e) { AcLog.Warn("Tablet: the app could not start (" + e.GetType().Name + ": " + e.Message + ") - the hub panel is the menu."); }
            AcLog.Info("Front: the showroom hub and its menu are up (whitelisted player); the tablet app is searching.");
        }

        /// <summary>The install search finished (AcGame.Install / Content set, or a status error): read the game's words, colours, assists on a
        /// worker; fonts, showroom, the remembered car on the main thread.</summary>
        public static void BootDone()
        {
            if (!_started || _game == null) return;
            AcInstall inst = _game.Install;
            if (inst == null) { AcLog.Info("Front: no install - the menus show the status line and the folder setting."); return; }
            try { AcFonts.Build(inst); }
            catch (Exception e) { AcLog.Warn("Fonts: building failed: " + e.Message); }
            // REQUESTS A -> F / W -> F: the AUDIO and TRACK rows exist before the first car / track (weather and time of day can be chosen in
            // the hub), and the first UI click is not silent
            int rowsBefore = AcSettingsUi.Rows.Count;
            AcGame.Guard("sfx preload", AcSfx.Preload);
            AcGame.Guard("track settings", AcTrackWorld.RegisterSettings);
            AcLog.Info("Front: audio preloaded and the TRACK / AUDIO settings rows asked for at boot (" + rowsBefore + " -> " + AcSettingsUi.Rows.Count + " rows).");
            if (_hub != null)
            {
                _hub.BuildShowroom(inst);
                _hub.ApplyShowroomLight(inst, "boot");
            }
            if (_bootThread == null)
            {
                _bootReady = false;
                _bootThread = new Thread(() =>
                {
                    try
                    {
                        _localeKeys = AcLocale.Load(inst);
                        _skin = AcGuiSkin.Load(inst);
                        _assists = AcAssistsIni.Load(inst);
                        AcLauncherSliders.Load(inst);
                    }
                    catch (Exception e) { AcLog.Warn("Front: reading the game's ui files failed: " + e.Message); }
                    _bootReady = true;
                }) { IsBackground = true, Name = "Assetto front" };
                _bootThread.Start();
            }
        }

        /// <summary>Once a frame.</summary>
        public static void Tick()
        {
            AcSettings.Tick();
            if (!_started) return;
            AcUiArt.Tick();
            if (_bootReady && !_bootApplied) ApplyBoot();
            TickHold();
            if (_hubLookFrame >= 0 && Time.frameCount >= _hubLookFrame) TickHubLook();
            if (Time.unscaledTime >= _nextSession)
            {
                _nextSession = Time.unscaledTime + 0.25f;
                TickSession();
            }

            if (Time.unscaledTime >= _nextMenuTick)
            {
                _nextMenuTick = Time.unscaledTime + 0.25f;
                for (int i = Menus.Count - 1; i >= 0; i--)
                {
                    AcMenu m = Menus[i];
                    if (m == null || m.Root == null) { Menus.RemoveAt(i); continue; }
                    if (!m.Root.gameObject.activeInHierarchy) continue;
                    m.Tick();
                }
            }

            TickKeep();
        }

        /// <summary>World closing: release the tablet, destroy the hub and panels, save settings.</summary>
        public static void Stop()
        {
            if (_started)
            {
                _started = false;
                if (_game != null) _game.StatusChanged -= OnStatus;
                AcSession.PhaseChanged -= OnPhase;
                AcSession.TrackLoading -= OnTrackLoading;
                AcSession.LapCompleted -= OnLap;
                AcSession.RaceFinished -= OnRaceFinished;
                AcSession.RaceStarted -= OnRaceStarted;
                AcEvents.Message -= OnMessage;
                AcEvents.LocalCarChanged -= OnLocalCar;
                AcSettings.Changed -= OnSetting;
                AcGame.Guard("tablet shutdown", AcTabletApp.Shutdown);
                AcGame.Guard("loading shutdown", AcLoading.Shutdown);
                AcGame.Guard("ui art shutdown", AcUiArt.Shutdown);
                AcGame.Guard("fonts shutdown", AcFonts.Shutdown);
                if (_hub != null) UnityEngine.Object.Destroy(_hub.gameObject);
                _hub = null;
                _hubMenu = null;
                Menus.Clear();
                ResultList.Clear();
                Laps.Clear();
                lock (SetupGate) { SetupCache.Clear(); SetupLoading.Clear(); SpecCache.Clear(); SpecLoading.Clear(); }
                SetupPending.Clear();
                _compoundPending = false;
                _pendingCar = null;
                _rulesSeen = null;
                _rulesSentSig = int.MinValue;
                _rulesDirtyAt = -1f;
                _wasMaster = false; // a re-start sees the master hand-over again (the draft re-seeds from the session)
                _lobbyReloadAt = -1f;
                _panelOnTrack = false;
                _hubLookFrame = -1;
                _game = null;
                _bootApplied = false;
                _bootReady = false;
                _bootThread = null;
                HoldCar = false;
                _held = false;
                AcLocal.TabletOpen = false;
            }

            AcSettings.Save();
        }

        /// <summary>A short line on the tablet and the hub panel (refusals, hints).</summary>
        public static void Toast(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (Menus.Count == 0) { _pendingToast = text; return; }
            for (int i = 0; i < Menus.Count; i++) if (Menus[i] != null && Menus[i].Root != null) Menus[i].Toast(text);
        }

        // ------------------------------------------------------------------ menus

        /// <summary>Builds the ASSETTO CORSA menu (all pages) into <paramref name="parent"/> (the tablet frame or the hub panel).</summary>
        public static AcMenu BuildMenu(Transform parent, bool tablet)
        {
            if (parent == null) return null;
            var pages = new List<AcPage> { new AcPageDrive(), new AcPageTrack(), new AcPageLobby(), new AcPageGarage(), new AcPageTiming(), new AcPageSettings(), new AcPageAbout() };
            AcMenu m;
            try { m = AcMenu.Build(parent, tablet, pages); }
            catch (Exception e) { AcLog.Error("Front: building the " + (tablet ? "tablet" : "hub") + " menu failed: " + e); return null; }
            Menus.Add(m);
            if (_game != null) m.SetStatus(_game.Status, _game.StatusIsError);
            if (_pendingToast != null) { m.Toast(_pendingToast); _pendingToast = null; }
            return m;
        }

        /// <summary>A menu that was destroyed (the tablet went away).</summary>
        public static void ForgetMenu(AcMenu m) { Menus.Remove(m); }

        public static IReadOnlyList<AcMenu> AllMenus { get { return Menus; } }

        private static void ApplyBoot()
        {
            _bootApplied = true;
            AcUi.ApplySkin(_skin);
            AcLog.Info("Front: the game's ui read - " + _localeKeys + " words (system/locales), skin.ini " + (_skin != null && _skin.SkinRead ? "read" : "missing") + ", launcher accent " +
                       (_skin != null ? _skin.Accent + (_skin.AccentRead ? " (" + _skin.AccentUses + " uses in the css)" : " (default)") : "?") + ", assists from " + (_assists != null && _assists.Source != null ? _assists.Source : "defaults") + ", lobby sliders from " + AcLauncherSliders.Source + " (laps " + AcLauncherSliders.RaceLaps + ", minutes " + AcLauncherSliders.QualifyMinutes + ", AI " + AcLauncherSliders.AiLevel + ", damage " + AcLauncherSliders.Damage + ", fuel " + AcLauncherSliders.FuelRate + ", tyre wear " + AcLauncherSliders.TyreWear + ")");
            RestoreCar();
            _lobbyReloadAt = Time.unscaledTime + 1f; // R reads server_cfg.ini on its first tick with the install: the lobby re-seeds from it then
            AcTabletApp app = AcTabletApp.Instance;
            if (app != null)
            {
                app.PaintIconFromInstall(_game != null ? _game.Install : null);
                app.RebuildMenu();
            }

            if (_hub != null && _hubMenu != null && _hubMenu.Root != null)
            {
                string page = _hubMenu.Current != null ? _hubMenu.Current.Id : null;
                Transform parent = _hubMenu.Root.parent;
                Menus.Remove(_hubMenu);
                UnityEngine.Object.Destroy(_hubMenu.Root.gameObject);
                _hubMenu = BuildMenu(parent, false);
                if (_hubMenu != null && page != null) _hubMenu.Show(page);
            }
        }

        private static void RestoreCar()
        {
            AcContent c = Content;
            if (c == null) return;
            string id = AcSettings.Text("drive.car", "");
            AcCarEntry car = c.Car(id);
            string how = "remembered";
            if (car == null || !car.Owned)
            {
                car = null;
                how = "default (first owned car)"; // ours: AC's launcher remembers the last car; with none, the first owned car by name
                foreach (AcCarEntry e in c.Cars) if (e.Owned) { car = e; break; }
            }

            if (car == null) return;
            string skin = AcSettings.Text("drive.skin", "");
            if (car.Skin(skin) == null)
            {
                List<AcSkinEntry> ordered = AcCarFilter.SkinsInOrder(car);
                skin = ordered.Count > 0 ? ordered[0].Id : "";
            }

            ChooseCar(car, skin, how);
        }

        // ------------------------------------------------------------------ actions the pages call

        /// <summary>The local player's car: AcRace.ChooseCar (R) first; only when R took it (or no track is loaded, where R just remembers it)
        /// AcLocal, AcSettings drive.car / drive.skin and the showroom car (built only while the hub is shown) follow. False = refused
        /// (R toasts why: a race is running, the master's rules do not allow the car).</summary>
        public static bool ChooseCar(AcCarEntry car, string skinId, string why)
        {
            if (car == null || !car.Owned) return false;
            string skin = skinId ?? "";
            bool ok = false;
            try { ok = AcRace.ChooseCar(car, skin); }
            catch (Exception e) { AcLog.Warn("Front: AcRace.ChooseCar failed: " + e.Message); }
            bool commit = ok || !AcSession.OnTrack;
            AcLog.Info("Front: drive choice " + car.Id + "/" + skin + " (" + why + (ok ? ", race took it" : commit ? ", kept for the next track (race did not answer yes)" : ", refused by the race on this track - not kept") + ")");
            if (!commit) return false;
            AcLocal.CarId = car.Id;
            AcLocal.SkinId = skin;
            AcSettings.SetText("drive.car", car.Id);
            AcSettings.SetText("drive.skin", skin);
            // the showroom car only in the hub (building a second full car while driving is a hitch and GPU memory)
            if (_hub != null && AcSession.Phase == AcPhase.Hub) _hub.SetCar(car, skin);
            return true;
        }

        /// <summary>The master's (or a lone player's) track pick with the lobby's rules. Logs "Front: track pick key (master|refused)".</summary>
        public static bool RequestLoad(AcLayoutEntry layout, string why)
        {
            if (layout == null) return false;
            bool master = AcMenu.SafeIsMaster();
            bool ok = false;
            if (master && layout.Owned)
            {
                try { ok = AcRace.RequestLoad(layout, AcLobby.Draft()); }
                catch (Exception e) { AcLog.Warn("Front: AcRace.RequestLoad failed: " + e.Message); }
            }

            AcLog.Info("Front: track pick " + layout.Key + " (" + (ok ? "master" : "refused" + (!master ? ": not the instance master" : !layout.Owned ? ": not installed" : ": the race module said no")) + ", " + why + ")");
            if (ok) { _rulesSentSig = DraftSig(); AcSettings.SetText("drive.track", layout.Key); Toast("Loading " + layout.Name + " for everyone..."); }
            else Toast(!master ? "Only the instance master (" + AcMenu.SafeMasterName() + ") picks the track." : "The track could not be loaded now.");
            return ok;
        }

        /// <summary>The master's START (the lobby's session: RACE = grid + lights; QUALIFY / PRACTICE / HOTLAP = that session starts now for
        /// everyone, R's session model). Logs "Front: start race|qualify|... (master|refused)".</summary>
        public static bool RequestStart(string why)
        {
            AcRules rules = AcLobby.Rules;
            bool race = rules == null || rules.Session == AcSessionType.Race;
            string session = AcLocale.Session(rules != null ? (int)rules.Session : (int)AcSessionType.Race);
            bool ok = false;
            try { ok = AcRace.RequestStart(); }
            catch (Exception e) { AcLog.Warn("Front: AcRace.RequestStart failed: " + e.Message); }
            if (ok) _rulesSentSig = DraftSig();
            AcLog.Info("Front: start " + session.ToLowerInvariant() + " (" + (ok ? "master" : "refused") + ", " + rules + ", " + why + ")");
            Toast(ok ? (race ? "Everyone to the grid!" : session + " starts now for everyone.") : "The " + session.ToLowerInvariant() + " could not start now.");
            return ok;
        }

        /// <summary>Miles for lengths / speeds: H's AcHud.UseMph (the player's "hud.units", else AC's own gameplay.ini USE_MPH); km when H fails.</summary>
        public static bool UseMph()
        {
            try { return AcHud.UseMph(); }
            catch (Exception e) { AcLog.WarnOnce("Front: AcHud.UseMph failed (" + e.GetType().Name + ") - lengths in km"); return false; }
        }

        /// <summary>The START button's words for the lobby's session ("START RACE", "START QUALIFY" ... with AC's own session names).</summary>
        public static string StartLabel(AcRules rules)
        {
            return "START " + AcLocale.Session(rules != null ? (int)rules.Session : (int)AcSessionType.Race);
        }

        /// <summary>Leave / join for this client: the master's LEAVE TRACK (everyone back to the hub), anyone else's LEAVE SESSION (alone, R's
        /// RequestHub for a non-master) and JOIN SESSION (back into the master's running session, R's JoinSession).</summary>
        public static bool LeaveOrJoin(string why)
        {
            bool master = AcMenu.SafeIsMaster() && AcMenu.SafeMasterKnown();
            bool join = CanJoinSession;
            bool ok = false;
            try { ok = join ? AcRace.JoinSession() : AcRace.RequestHub(); }
            catch (Exception e) { AcLog.Warn("Front: AcRace." + (join ? "JoinSession" : "RequestHub") + " failed: " + e.Message); }
            string what = join ? "join session" : master ? "leave track" : "leave session";
            AcLog.Info("Front: " + what + " (" + (ok ? "done" : "refused") + ", " + why + ")");
            if (join) Toast(ok ? "Back into the instance master's session..." : "There is no session to join now.");
            else if (master) Toast(ok ? "Everyone goes back to the showroom." : "Leaving the track was refused.");
            else Toast(ok ? "You left the session (JOIN SESSION brings you back)." : "Leaving the session was refused.");
            return ok;
        }

        /// <summary>The instance master runs a session on a track and this client is in the hub (left it alone, or arrived late).</summary>
        public static bool CanJoinSession
        {
            get
            {
                if (AcSession.Phase != AcPhase.Hub) return false;
                if (AcMenu.SafeIsMaster() && AcMenu.SafeMasterKnown()) return false;
                try
                {
                    AcRaceCommand cur = AcNetGame.RaceControl != null ? AcNetGame.RaceControl.Current : null;
                    return cur != null && cur.Phase != AcPhase.Hub && !string.IsNullOrEmpty(cur.TrackId);
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>The leave / join button's words (see <see cref="LeaveOrJoin"/>).</summary>
        public static string LeaveLabel()
        {
            if (CanJoinSession) return "JOIN SESSION";
            return AcMenu.SafeIsMaster() && AcMenu.SafeMasterKnown() ? "LEAVE TRACK" : "LEAVE SESSION";
        }

        /// <summary>The leave / join button can be pressed now.</summary>
        public static bool LeaveOrJoinPossible
        {
            get { return CanJoinSession || (AcSession.Phase != AcPhase.Hub && AcSession.Phase != AcPhase.Loading); }
        }

        /// <summary>Closes the app and the client's tablet (RESUME, getting in).</summary>
        public static bool CloseTablet(string why)
        {
            AcTabletApp a = AcTabletApp.Instance;
            return a != null && a.Link != null && a.Link.CloseTablet(why);
        }

        // ------------------------------------------------------------------ assists and setup (Garage)

        /// <summary>An assist setting with the player's assists.ini as the default ("garage.abs" 0-2, "garage.tc", "garage.stability" 0-100,
        /// "garage.autoshift" / "garage.autoclutch" / "garage.autoblip" 0/1).</summary>
        public static int AssistInt(string key)
        {
            AcAssistsIni d = _assists ?? new AcAssistsIni();
            int def;
            switch (key)
            {
                case "garage.abs": def = d.Abs; break;
                case "garage.tc": def = d.Tc; break;
                case "garage.stability": def = d.Stability; break;
                case "garage.autoshift": def = d.AutoShifter ? 1 : 0; break;
                case "garage.autoclutch": def = d.AutoClutch ? 1 : 0; break;
                case "garage.autoblip": def = d.AutoBlip ? 1 : 0; break;
                case "garage.blankets": def = d.TyreBlankets ? 1 : 0; break;
                default: def = 0; break;
            }

            return AcSettings.Int(key, def);
        }

        /// <summary>[for P / R] The local player's driving aids within <paramref name="rules"/> (server_cfg ABS_ALLOWED / TC_ALLOWED 0 forbidden,
        /// 1 factory, 2 any; STABILITY / AUTOCLUTCH allowed; automatic gears allowed). A new object each call.</summary>
        public static AcAssists Assists(AcRules rules)
        {
            var a = new AcAssists();
            FillAssists(a, rules);
            return a;
        }

        /// <summary>Same as <see cref="Assists"/> into an existing object (no allocation; the Garage page at 4 Hz).</summary>
        public static void FillAssists(AcAssists a, AcRules rules)
        {
            if (a == null) return;
            a.Abs = Mathf.Clamp(AssistInt("garage.abs"), 0, 2);
            a.Tc = Mathf.Clamp(AssistInt("garage.tc"), 0, 2);
            a.Stability = Mathf.Clamp(AssistInt("garage.stability"), 0, 100);
            a.AutoShift = AssistInt("garage.autoshift") != 0;
            a.AutoClutch = AssistInt("garage.autoclutch") != 0;
            a.AutoBlip = AssistInt("garage.autoblip") != 0;
            if (rules != null && AcSession.OnTrack)
            {
                a.Abs = AcAssistsIni.Allowed(a.Abs, rules.AbsAllowed);
                a.Tc = AcAssistsIni.Allowed(a.Tc, rules.TcAllowed);
                if (!rules.StabilityAllowed) a.Stability = 0;
                if (!rules.AutoClutchAllowed) a.AutoClutch = false;
                if (!rules.AutoShiftAllowed) a.AutoShift = false;
            }
        }

        /// <summary>Writes <see cref="Assists"/> into the local car's physics (P's AcCarPhysics.Assists), guarded.</summary>
        public static void ApplyAssistsToLocalCar()
        {
            try
            {
                AcCarRig car = AcLocal.Car;
                if (car == null || car.Physics == null) return;
                car.Physics.Assists = Assists(AcSession.Rules);
            }
            catch (Exception e) { AcLog.WarnOnce("Front: applying the assists to the car failed (" + e.GetType().Name + ")"); }
        }

        /// <summary>The car's setup rows (data.acd setup.ini) - null while they are being read on a worker (asked again later).</summary>
        public static List<AcSetupRow> SetupFor(AcCarEntry car)
        {
            if (car == null) return null;
            lock (SetupGate)
            {
                List<AcSetupRow> rows;
                if (SetupCache.TryGetValue(car.Id, out rows)) return rows;
                if (SetupLoading.Contains(car.Id)) return null;
                SetupLoading.Add(car.Id);
            }

            string id = car.Id, dir = car.Dir;
            var t = new Thread(() =>
            {
                List<AcSetupRow> rows;
                try { rows = AcSetupRows.Read(AcdArchive.Open(dir)); }
                catch (Exception e) { rows = new List<AcSetupRow>(); AcLog.Warn("Front: reading " + id + "'s setup.ini failed: " + e.Message); }
                lock (SetupGate) { SetupCache[id] = rows; SetupLoading.Remove(id); }
            }) { IsBackground = true, Name = "Assetto setup " + id };
            t.Start();
            return null;
        }

        /// <summary>The Garage's "basic setup": tyre pressures, fuel and brake rows (setup.ini TAB TYRES / GENERIC, keys with BRAKE / BIAS). // ours (selection)</summary>
        public static bool IsBasicSetup(AcSetupRow s)
        {
            if (s == null) return false;
            string tab = (s.Tab ?? "").ToUpperInvariant(), key = (s.Key ?? "").ToUpperInvariant();
            return tab == "TYRES" || tab == "GENERIC" || key.Contains("BRAKE") || key.Contains("BIAS") || key == "FUEL";
        }

        /// <summary>[for P] The player's setup value for <paramref name="carId"/> / setup.ini key; true when the player set it, false when it is the
        /// car's own default (<paramref name="value"/> = that default, NaN when unknown or the rows are not read yet).</summary>
        public static bool SetupValue(string carId, string key, out float value)
        {
            value = float.NaN;
            if (string.IsNullOrEmpty(carId) || string.IsNullOrEmpty(key)) return false;
            string k = "setup." + carId + "." + key; // pages call this only when a setting changed (AcPageGarage's signature), P at spawn
            string t = AcSettings.Text(k, "");
            if (t.Length > 0)
            {
                float v = AcSettings.Num(k, float.NaN);
                if (!float.IsNaN(v)) { value = v; return true; }
            }

            lock (SetupGate)
            {
                List<AcSetupRow> rows;
                if (SetupCache.TryGetValue(carId, out rows))
                    foreach (AcSetupRow r in rows) if (string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)) { value = r.Default; break; }
            }

            return false;
        }

        /// <summary>Back to the car's own values (every "setup.car.*" key emptied).</summary>
        public static void ResetSetup(AcCarEntry car)
        {
            if (car == null) return;
            List<AcSetupRow> rows;
            lock (SetupGate) { if (!SetupCache.TryGetValue(car.Id, out rows)) return; }
            foreach (AcSetupRow r in rows)
            {
                AcSettings.SetText("setup." + car.Id + "." + r.Key, "");
                if (IsBasicSetup(r)) MarkSetupPending(car.Id, r.Key);
            }

            if (AcSettings.Text("setup." + car.Id + ".COMPOUND", "").Length > 0)
            {
                AcSettings.SetText("setup." + car.Id + ".COMPOUND", "");
                MarkCompoundPending(car.Id);
            }

            AcLog.Info("Front: setup of " + car.Id + " back to the car's own values.");
        }

        /// <summary>The Garage's change of one setup.ini value (AcSettings "setup.car.KEY"); it reaches the car on the track when the car stands
        /// still (P's AcCarPhysics.ApplySetup), otherwise at its next spawn - P reads <see cref="SetupValue"/> then.</summary>
        public static void SetSetup(string carId, string key, float value)
        {
            if (string.IsNullOrEmpty(carId) || string.IsNullOrEmpty(key)) return;
            AcSettings.SetNum("setup." + carId + "." + key, value);
            MarkSetupPending(carId, key);
        }

        private static void MarkSetupPending(string carId, string key)
        {
            if (!string.Equals(carId, AcLocal.CarId, StringComparison.OrdinalIgnoreCase)) return;
            if (_pendingCar != carId) { SetupPending.Clear(); _compoundPending = false; _pendingCar = carId; }
            SetupPending.Add(key);
        }

        private static void MarkCompoundPending(string carId)
        {
            if (!string.Equals(carId, AcLocal.CarId, StringComparison.OrdinalIgnoreCase)) return;
            if (_pendingCar != carId) { SetupPending.Clear(); _compoundPending = false; _pendingCar = carId; }
            _compoundPending = true;
        }

        /// <summary>Garage values not yet in the car on the track (they go in when it stands still).</summary>
        public static bool SetupWaiting { get { return SetupPending.Count > 0 || _compoundPending; } }

        /// <summary>The car's parsed physics spec (P's AcCarRig.SpecFor, read on a worker; null while reading or when it failed) - the Garage's
        /// tyre compounds (tyres.ini) come from it.</summary>
        public static AcCarSpec SpecOf(AcCarEntry car)
        {
            if (car == null || !car.Owned) return null;
            lock (SetupGate)
            {
                AcCarSpec s;
                if (SpecCache.TryGetValue(car.Id, out s)) return s;
                if (SpecLoading.Contains(car.Id)) return null;
                SpecLoading.Add(car.Id);
            }

            AcCarEntry c = car;
            var t = new Thread(() => ReadSpec(c)) { IsBackground = true, Name = "Assetto spec " + car.Id };
            t.Start();
            return null;
        }

        /// <summary>The car's spec is still being read for the Garage (see <see cref="SpecOf"/>).</summary>
        public static bool SpecReading(AcCarEntry car)
        {
            if (car == null) return false;
            lock (SetupGate) { return SpecLoading.Contains(car.Id); }
        }

        private static void ReadSpec(AcCarEntry car)
        {
            AcCarSpec spec = null;
            try { spec = AcCarRig.SpecFor(car, false); }
            catch (Exception e) { AcLog.Warn("Front: reading " + car.Id + "'s tyres for the garage failed: " + e.Message); }
            lock (SetupGate) { SpecCache[car.Id] = spec; SpecLoading.Remove(car.Id); }
        }

        /// <summary>The player's tyre compound for <paramref name="carId"/> (tyres.ini index; -1 = the car's default, tyres.ini DEFAULT).</summary>
        public static int CompoundChoice(string carId)
        {
            if (string.IsNullOrEmpty(carId)) return -1;
            return AcSettings.Int("setup." + carId + ".COMPOUND", -1);
        }

        /// <summary>The Garage's tyre compound choice (-1 = the car's default); reaches the car on the track like <see cref="SetSetup"/>.</summary>
        public static void SetCompoundChoice(string carId, int index)
        {
            if (string.IsNullOrEmpty(carId)) return;
            if (index < 0) AcSettings.SetText("setup." + carId + ".COMPOUND", "");
            else AcSettings.SetInt("setup." + carId + ".COMPOUND", index);
            MarkCompoundPending(carId);
        }

        /// <summary>Puts the compound choice into the local car (P's SetCompound resets pressures: the player's set pressures go back in).</summary>
        private static bool ApplyCompound(AcCarRig rig, bool onlyIfChosen)
        {
            if (rig == null || rig.Physics == null || rig.Car == null) return false;
            int idx = CompoundChoice(rig.Car.Id);
            if (idx < 0 && onlyIfChosen) return false;
            AcCarSpec spec = rig.Physics.Spec;
            int n = spec != null ? spec.Compounds.Count : 0;
            if (n == 0) return false;
            if (idx < 0 || idx >= n) idx = Mathf.Clamp(spec.DefaultCompound, 0, n - 1);
            rig.Physics.SetCompound(idx);
            string[] wheels = { "PRESSURE_LF", "PRESSURE_RF", "PRESSURE_LR", "PRESSURE_RR" };
            for (int i = 0; i < wheels.Length; i++)
            {
                float v;
                if (SetupValue(rig.Car.Id, wheels[i], out v)) rig.Physics.ApplySetup(wheels[i], v);
            }

            AcLog.Info("Front: " + rig.Car.Id + " tyres: " + spec.Compounds[idx].Name + " (garage).");
            return true;
        }

        /// <summary>4 Hz: Garage values waiting for the car go in once it stands still (AC changes the setup only in the pits / stopped).</summary>
        private static void TickSetupApply()
        {
            if (!SetupWaiting) return;
            AcCarRig rig = AcLocal.Car;
            if (rig == null || rig.Physics == null || rig.Car == null) return;
            if (!string.Equals(rig.Car.Id, _pendingCar, StringComparison.OrdinalIgnoreCase)) { SetupPending.Clear(); _compoundPending = false; return; }
            if (rig.Physics.SpeedKmh > 1f) return; // ours: "stands still"
            int applied = 0;
            if (_compoundPending) { if (ApplyCompound(rig, false)) applied++; _compoundPending = false; }
            foreach (string key in SetupPending)
            {
                float v;
                SetupValue(rig.Car.Id, key, out v);
                if (!float.IsNaN(v) && rig.Physics.ApplySetup(key, v)) applied++;
            }

            SetupPending.Clear();
            if (applied > 0)
            {
                AcLog.Info("Front: " + applied + " garage value(s) put into the car on the track (" + rig.Car.Id + ", stopped).");
                Toast("Setup applied to your car.");
            }
        }

        // ------------------------------------------------------------------ events

        private static void OnStatus(string text, bool error)
        {
            for (int i = 0; i < Menus.Count; i++) if (Menus[i] != null && Menus[i].Root != null) Menus[i].SetStatus(text, error);
        }

        private static void OnPhase(AcPhase from, AcPhase to)
        {
            if (to == AcPhase.Loading)
            {
                if (_hub != null)
                {
                    _hub.AttachPanel();
                    _panelOnTrack = false;
                    _hub.SetPanelShown(false);
                    _hub.ClearCar(); // the showroom car goes while a track loads and runs (built again in the hub)
                }

                if (from == AcPhase.Hub || from == AcPhase.Loading) { Laps.Clear(); ResultList.Clear(); LapVersion++; }
                if (!AcLoading.Showing || AcLoading.Layout != AcSession.Layout) AcLoading.Show(AcSession.Layout, AcSession.Rules);
            }
            else if (to >= AcPhase.Practice)
            {
                if (from == AcPhase.Loading || AcLoading.Showing) AcLoading.Hide();
                if (_hub != null) _hub.SetShown(false);
                if (to == AcPhase.Grid) { ResultList.Clear(); LapVersion++; }
                ApplyAssistsToLocalCar();
            }
            else if (to == AcPhase.Hub)
            {
                if (_hub != null)
                {
                    _hub.AttachPanel();
                    _panelOnTrack = false;
                    _hub.SetShown(true);
                    _hub.SetPanelShown(AcSettings.Flag("front.panel", true));
                    if (_hub.Placer != null) _hub.Placer.Replace();
                    AcContent c = Content;
                    AcCarEntry car = c != null && !string.IsNullOrEmpty(AcLocal.CarId) ? c.Car(AcLocal.CarId) : null;
                    if (car != null) _hub.SetCar(car, AcLocal.SkinId);
                }

                // the showroom's own light again (W's track look is gone; W restores its default in the same frame - so next frame)
                _hubLookFrame = Time.frameCount + 1;
                if (from != AcPhase.Hub) AcLoading.BackToHub();
            }
        }

        private static void OnTrackLoading(float progress, string stage)
        {
            if (!AcLoading.Showing && AcSession.Phase == AcPhase.Loading) AcLoading.Show(AcSession.Layout, AcSession.Rules);
            AcLoading.Progress(progress, stage);
        }

        private static void OnLap(AcLap lap)
        {
            if (lap == null || !lap.IsLocal) return;
            Laps.Add(lap);
            if (Laps.Count > 200) Laps.RemoveAt(0); // ours: a session's worth
            LapVersion++;
        }

        private static void OnRaceStarted()
        {
            ResultList.Clear();
            LapVersion++;
        }

        private static void OnRaceFinished()
        {
            ResultList.Clear();
            try
            {
                IReadOnlyList<AcStanding> s = AcRace.Standings;
                if (s != null)
                {
                    for (int i = 0; i < s.Count; i++)
                    {
                        AcStanding a = s[i];
                        if (a == null) continue;
                        ResultList.Add(new AcStanding
                        {
                            Position = a.Position, PlayerId = a.PlayerId, IsLocal = a.IsLocal, IsAi = a.IsAi, Name = a.Name, CarId = a.CarId, Laps = a.Laps, BestMs = a.BestMs,
                            LastMs = a.LastMs, GapMs = a.GapMs, LapsBehind = a.LapsBehind, RaceDistance = a.RaceDistance, Finished = a.Finished, TotalMs = a.TotalMs, PenaltyMs = a.PenaltyMs, InPit = a.InPit,
                        });
                    }
                }
            }
            catch (Exception e) { AcLog.Warn("Front: copying the results failed: " + e.Message); }
            LapVersion++;
            AcLog.Info("Front: results kept for the TIMING page (" + ResultList.Count + " drivers" + (ResultList.Count > 0 ? ", winner " + ResultList[0].Name : "") + ").");
        }

        private static void OnMessage(string text, float seconds) { Toast(text); }

        private static void OnLocalCar(AcCarRig rig)
        {
            ApplyAssistsToLocalCar();
            if (rig == null) return;
            // P applied every setup.ini value at spawn; the tyre compound is ours (tyres.ini, not a setup.ini item)
            try { ApplyCompound(rig, true); }
            catch (Exception e) { AcLog.WarnOnce("Front: applying the tyre compound failed (" + e.GetType().Name + ": " + e.Message + ")"); }
            SetupPending.Clear();
            _compoundPending = false;
        }

        private static void OnSetting(string key)
        {
            SettingsVersion++;
            if (string.Equals(key, "front.panel", StringComparison.OrdinalIgnoreCase) && _hub != null && AcSession.Phase == AcPhase.Hub)
                _hub.SetPanelShown(AcSettings.Flag("front.panel", true));
        }

        // ------------------------------------------------------------------ the session follow-up (4 Hz, no allocation in steady state)

        /// <summary>What the master's lobby draft is (its version, and the master's car when ALLOWED CARS depends on it).</summary>
        private static int DraftSig()
        {
            int sig = AcLobby.Version * 7919;
            if (AcLobby.AllowedMode != 0 && AcLocal.CarId != null) sig += AcLocal.CarId.GetHashCode();
            return sig;
        }

        private static void TickSession()
        {
            // the lobby re-seeds once from R's install defaults (server_cfg.ini) after the boot
            if (_lobbyReloadAt > 0f && Time.unscaledTime >= _lobbyReloadAt)
            {
                _lobbyReloadAt = -1f;
                AcGame.Guard("lobby defaults", AcLobby.Load);
                // a track already loading / loaded (a load inside that first second): the draft = the session's rules, not the defaults
                bool seeded = SeedLobbyFromSession();
                _rulesSentSig = DraftSig(); // a (re-)seed is not a master edit: nothing goes out until the master changes a rule
                AcLog.Info("Front: lobby rules seeded from " + (seeded ? "the running session (AcSession.Rules)" : AcLobby.DefaultsSource) + " (" + AcLobby.Rules + ").");
            }

            // R replaces AcSession.Rules with every applied command: the aids follow at once (rules changed inside practice too)
            AcRules cur = AcSession.Rules;
            if (!ReferenceEquals(cur, _rulesSeen))
            {
                _rulesSeen = cur;
                if (AcSession.OnTrack) ApplyAssistsToLocalCar();
            }

            TickRulesToRace();
            TickSpectating();
            TickPanelFallback();
            TickSetupApply();
        }

        /// <summary>While a track loads or runs, the lobby draft becomes a copy of AcSession.Rules (true when it did).</summary>
        private static bool SeedLobbyFromSession()
        {
            if (AcSession.Phase == AcPhase.Hub || AcSession.Rules == null) return false;
            try { AcLobby.SeedFrom(AcSession.Rules); return true; }
            catch (Exception e) { AcLog.WarnOnce("Front: the lobby could not take the session's rules (" + e.GetType().Name + ")"); return false; }
        }

        /// <summary>The master's rule edits during untimed practice reach everyone (R's RequestRules, debounced 1 s); a running qualify or race
        /// keeps its rules (START / the next track takes the draft).</summary>
        private static void TickRulesToRace()
        {
            bool master = AcMenu.SafeIsMaster() && AcMenu.SafeMasterKnown();
            if (master != _wasMaster)
            {
                _wasMaster = master;
                if (master)
                {
                    // a new master on a track adopts the running rules as their draft (not their own remembered lobby), so their first
                    // edit changes one rule and keeps the rest; their edits go out from now on
                    if (SeedLobbyFromSession()) AcLog.Info("Front: I am the instance master on a track - the lobby draft starts from the session's rules (" + AcLobby.Rules + ").");
                    _rulesSentSig = DraftSig();
                }
            }

            if (!master || !AcSession.OnTrack || AcSession.InRace || AcSession.StartUtc > 0) { _rulesDirtyAt = -1f; return; }
            int sig = DraftSig();
            if (sig == _rulesSentSig) { _rulesDirtyAt = -1f; return; }
            if (_rulesDirtyAt < 0f) { _rulesDirtyAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - _rulesDirtyAt < 1f) return; // ours: one publish per burst of < > presses
            _rulesDirtyAt = -1f;
            _rulesSentSig = sig;
            bool ok = false;
            try { ok = AcRace.RequestRules(AcLobby.Draft()); }
            catch (Exception e) { AcLog.Warn("Front: AcRace.RequestRules failed: " + e.Message); }
            _rulesSent++;
            if (!_rulesLogged || !ok)
            {
                _rulesLogged = true;
                AcLog.Info("Front: lobby rules sent to everyone during practice (" + (ok ? "taken" : "refused") + ", " + AcLobby.Rules + "; logged once).");
            }
        }

        /// <summary>N publishes my "spectating" bit (AcNetGame.Spectating, set by F): R's late joiner whose car waits while a race runs.</summary>
        private static void TickSpectating()
        {
            bool spec = false;
            try { spec = AcRace.Spectating; } catch (Exception) { spec = false; }
            try
            {
                if (AcNetGame.Spectating == spec) return;
                AcNetGame.Spectating = spec;
            }
            catch (Exception e) { AcLog.WarnOnce("Front: AcNetGame.Spectating could not be set (" + e.GetType().Name + ")"); return; }
            if (spec && !_specLogged)
            {
                _specLogged = true;
                AcLog.Info("Front: spectating - a race runs without me; my record says so (AcNetGame.Spectating; logged once).");
            }
        }

        /// <summary>No linked tablet on a track (acceptance 2: the panel always): the hub's menu panel leaves the hidden hub and stands in front
        /// of the player while they are on foot (seated, the car's own keys work; get out to use it).</summary>
        private static void TickPanelFallback()
        {
            if (_hub == null || _hub.Panel == null || _game == null) return;
            bool want = AcSession.OnTrack && !TabletLinked && !AcLocal.Seated && AcSettings.Flag("front.panel", true);
            if (want == _panelOnTrack) return;
            _panelOnTrack = want;
            if (want)
            {
                _hub.DetachPanel(_game.transform);
                _hub.SetPanelShown(true);
                if (!_panelLogged)
                {
                    _panelLogged = true;
                    AcLog.Info("Front: no tablet on the track - the menu panel stands in front of you while you are on foot (logged once).");
                }
            }
            else if (AcSession.OnTrack)
            {
                _hub.SetPanelShown(false);
            }
        }

        private static void TickHubLook()
        {
            _hubLookFrame = -1;
            if (_hub == null || _game == null || AcSession.Phase != AcPhase.Hub) return;
            _hub.ApplyShowroomLight(_game.Install, "back in the hub");
        }

        // ------------------------------------------------------------------ holding the car while the tablet is open (ETS's HoldInputWhileVisible)

        private static void TickHold()
        {
            AcTabletApp app = AcTabletApp.Instance;
            AcTabletLink link = app != null ? app.Link : null;
            bool linked = link != null && link.IsLinked;
            bool appOpen = linked && link.AppOpen;
            bool tabletOpen = linked && link.TabletOpen;
            AcLocal.TabletOpen = appOpen || tabletOpen;
            bool vr = AcPlayer.XrPresent;
            bool hold = AcLocal.Driving && AcSettings.Flag("front.holdcar", true) && (appOpen || (vr && tabletOpen));
            HoldCar = hold;
            if (hold && !_held)
            {
                if (!_holdLogged)
                {
                    _holdLogged = true;
                    AcLog.Info("Front: the car's pedals are held while " + (appOpen ? "the ASSETTO CORSA app" : "the Somnium tablet") + " is open (AcFront.HoldCar); they come back when it closes.");
                }

                if (!appOpen && vr) Toast("Your tablet is open, so the car waits - close the tablet to drive.");
            }
            else if (!hold && _held && vr)
            {
                try { AcDriveInput.DisarmPedals(); }
                catch (Exception e) { AcLog.WarnOnce("Front: AcDriveInput.DisarmPedals failed (" + e.GetType().Name + ")"); }
            }

            _held = hold;
        }

        // ------------------------------------------------------------------ the hub's keep rule

        private static void TickKeep()
        {
            if (_hub == null || !_hub.Shown || AcSession.Phase != AcPhase.Hub || Time.unscaledTime < _nextKeep) return;
            _nextKeep = Time.unscaledTime + 0.5f;
            if (!AcPlayer.HasBridgePlayer) return;
            Vector3 feet;
            if (!AcPlayer.TryFeet(out feet)) return;
            if (!AcHub.IsAway(feet)) { _awaySince = -1f; return; }
            if (_awaySince < 0f) { _awaySince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _awaySince < 2f) return; // Halo's rule: 2 s off the platform
            _awaySince = -1f;
            string how, src;
            float yaw;
            if (!AcPlayer.TryFacingYaw(out yaw, out src)) yaw = 0f;
            bool moved = AcPlayer.Teleport(AcSpace.RoomOrigin + new Vector3(0f, 0.2f, 0f), yaw, out how);
            _keepMoves++;
            if (_keepMoves <= 5) AcLog.Info("Hub: the player was outside the showroom (" + (feet - AcSpace.RoomOrigin).ToString("0") + ") - put back (" + (moved ? how : "not moved: " + how) + ").");
        }
    }
}
