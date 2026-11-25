using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// The scene's root ("Ac Game" object). Access first (private testing): Awake sets the log sinks and runs the gate -
    /// nothing below starts before Core/AcAccess says the player is on the whitelist; a visitor gets only Game/Access/AcIsolation
    /// (every other player hidden and silent), no text, no hub, no menu. Then <see cref="StartDreamport"/>: shader carriers, the
    /// front (hub + tablet app), the instance/net layer, and on a worker thread the look for the player's own Assetto Corsa install
    /// (Data/Install/AcInstall) followed by the content catalog (Data/Install/AcContent: cars, skins, tracks, layouts).
    ///
    /// Modules hook in through the partial methods below (each lives in its own AcGame.*.cs file); a module that is not there
    /// compiles away. Everything a module does is called inside try/catch: a missing piece becomes a status line, never a crash.
    /// </summary>
    public sealed partial class AcGame : MonoBehaviour
    {
        [Tooltip("Every carrier material of a custom shader (Resources/Ac*.mat). Shaders reach the upload only through them.")]
        [SerializeField] private Material[] _shaderCarriers;

        [Tooltip("A first guess where Assetto Corsa is installed (the folder with AssettoCorsa.exe and content). Only a hint: AcInstall also walks Steam's libraryfolders.vdf (app 244210).")]
        [SerializeField] private string _installHint = @"E:\SteamLibrary\steamapps\common\assettocorsa";

        public static AcGame Current { get; private set; }

        private bool _started, _isolating;
        private AcInstall _install;
        private AcContent _content;

        // Boot (worker thread -> main thread).
        private Thread _bootThread;
        private volatile bool _bootDone;
        private volatile string _bootError;
        private volatile string _bootScanError;       // the catalog scan failed (the install itself was found)
        private volatile string _bootHintProblem;     // why the folder set in Settings was not used (null when it was, or none was set)
        private AcInstall _bootInstall;
        private AcContent _bootContent;
        private long _bootMs, _bootScanMs;
        private bool _bootHandled;

        // Per-frame module calls, built once (a lambda capturing this would allocate a delegate every frame).
        private Action _netTickCall, _frontTickCall, _raceTickCall;

        /// <summary>The player's install (null until found).</summary>
        public AcInstall Install { get { return _install; } }

        /// <summary>The content catalog (cars, skins, tracks, layouts) - built on the boot thread right after the install was found.</summary>
        public AcContent Content { get { return _content; } }

        /// <summary>Looks for the install again (the player set the folder after "not found"); false while a search is still running.</summary>
        public bool Rescan()
        {
            if (_install != null || !_bootDone || !_bootHandled) return false;
            _bootHandled = false;
            StartBoot();
            return true;
        }
        public string InstallHint { get { return _installHint; } }
        public Material[] ShaderCarriers { get { return _shaderCarriers; } }

        /// <summary>The menu-side status line (the hub's panel shows it; set by boot and by loads).</summary>
        public string Status { get; private set; }
        public bool StatusIsError { get; private set; }
        public event Action<string, bool> StatusChanged;

        public void SetStatus(string text, bool error)
        {
            Status = text; StatusIsError = error;
            try { var h = StatusChanged; if (h != null) h(text, error); }
            catch (Exception e) { AcLog.Warn("Status: " + e.Message); }
        }

        // ------------------------------------------------------------------ module hooks (implemented in AcGame.*.cs)

        /// <summary>Front end (Game/Front, AcGame.Front.cs): the hub, the tablet app, the loading card.</summary>
        partial void FrontStart();
        partial void FrontBootDone();
        partial void FrontTick();
        partial void FrontStop();

        /// <summary>Net + instance (Game/Net, AcGame.Net.cs): the slot layer, the instance master, race control.</summary>
        partial void NetStart();
        partial void NetTick();
        partial void NetStop();

        /// <summary>The race (Game/Race, AcGame.Race.cs): session flow, track load, grid, lights, timing.</summary>
        partial void RaceTick();
        partial void RaceStop();

        private readonly List<KeyValuePair<int, string>> _heldLog = new List<KeyValuePair<int, string>>();

        private void Awake()
        {
            // A visitor must see "nothing about the Dreamport": the world's log lines are held back until the whitelist verdict -
            // flushed to Player.log for a whitelisted player, dropped (and nothing more written) for a visitor.
            _heldLog.Clear();
            AcLog.Sink = s => HoldLog(0, s);
            AcLog.WarnSink = s => HoldLog(1, s);
            AcLog.ErrorSink = s => HoldLog(2, s);
            StartCoroutine(Gate());
        }

        private void HoldLog(int level, string line)
        {
            if (_heldLog.Count < 500) _heldLog.Add(new KeyValuePair<int, string>(level, line));
        }

        private void ReleaseLog(bool allowed)
        {
            if (!allowed)
            {
                AcLog.Sink = null;
                AcLog.WarnSink = null;
                AcLog.ErrorSink = null;
                _heldLog.Clear();
                return;
            }

            AcLog.Sink = Debug.Log;
            AcLog.WarnSink = Debug.LogWarning;
            AcLog.ErrorSink = Debug.LogError;
            foreach (var kv in _heldLog)
            {
                if (kv.Key == 0) Debug.Log(kv.Value);
                else if (kv.Key == 1) Debug.LogWarning(kv.Value);
                else Debug.LogError(kv.Value);
            }

            _heldLog.Clear();
        }

        /// <summary>
        /// The whitelist gate (Core/AcAccess). While the client has not reported the username, and for good when the player is
        /// not on the list, only the isolation runs with the rule "nobody is with me": every other player is hidden and silent
        /// on this screen, and NOTHING of the Dreamport is built, read, added, loaded or saved.
        /// </summary>
        private IEnumerator Gate()
        {
            IEnumerator check = AcAccess.Check();
            bool waited = false;
            while (check.MoveNext())
            {
                if (!_isolating) StartIsolation(NobodyRule("waiting for the whitelist check"));
                if (!waited)
                {
                    waited = true;
                    ShowPlainFloor();
                }

                yield return check.Current;
            }

            if (!AcAccess.Allowed)
            {
                StartIsolation(NobodyRule("visitor (not on the whitelist)"));
                ShowPlainFloor();
                ReleaseLog(false);
                yield break;
            }

            ReleaseLog(true);
            HidePlainFloor();
            StartDreamport();
        }

        // ------------------------------------------------------------------ the visitor's plane

        private GameObject _plainFloor;

        /// <summary>Plain grey walkable plane under the spawn point: all a visitor (or anyone before the verdict) ever sees.</summary>
        private void ShowPlainFloor()
        {
            if (_plainFloor != null) return;
            try
            {
                _plainFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _plainFloor.name = "Floor";
                _plainFloor.transform.SetParent(transform, false);
                _plainFloor.transform.position = AcSpace.RoomOrigin;
                _plainFloor.transform.rotation = Quaternion.identity;
                _plainFloor.transform.localScale = new Vector3(6f, 1f, 6f);
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                var r = _plainFloor.GetComponent<Renderer>();
                if (r != null && sh != null)
                {
                    var m = new Material(sh);
                    Color grey = new Color(0.42f, 0.42f, 0.42f, 1f);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", grey);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
                    r.sharedMaterial = m;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Floor: " + e.Message);
            }
        }

        private void HidePlainFloor()
        {
            if (_plainFloor == null) return;
            var r = _plainFloor.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null) Destroy(r.sharedMaterial);
            Destroy(_plainFloor);
            _plainFloor = null;
        }

        private AcIsolation.Rule NobodyRule(string who)
        {
            return new AcIsolation.Rule
            {
                Name = who + ": nobody is with you",
                IsWithMe = id => false,
                WithText = "with you",
                ApartText = "not shown to a visitor",
                WorldRoot = transform,
            };
        }

        /// <summary>A whitelisted player's rule: the other player is on the whitelist too (see AcGear). Everyone else is a visitor: hidden and silent.</summary>
        private AcIsolation.Rule WhitelistRule()
        {
            return new AcIsolation.Rule
            {
                Name = "whitelisted: other whitelisted players",
                IsWithMe = AcGear.HasRecord,
                WithText = "is on the whitelist (a Dreamport player)",
                ApartText = "not on the whitelist (a visitor)",
                ShowDelay = 0f,
                DecideInterval = 0f,
                WorldRoot = transform,
            };
        }

        private void StartIsolation(AcIsolation.Rule rule)
        {
            try
            {
                AcIsolation.Start(rule);
                _isolating = true;
                AddOnce<AcIsolationDriver>();
            }
            catch (Exception e)
            {
                AcLog.Error("Access: starting the isolation failed: " + e);
            }
        }

        public T AddOnce<T>() where T : Component
        {
            try
            {
                T c = GetComponent<T>();
                if (c == null) c = gameObject.AddComponent<T>();
                return c;
            }
            catch (Exception e)
            {
                AcLog.Error("Awake: adding " + typeof(T).Name + " failed: " + e);
                return null;
            }
        }

        // ------------------------------------------------------------------ the Dreamport

        /// <summary>Everything the world does, for a whitelisted player only.</summary>
        private void StartDreamport()
        {
            _started = true;
            Current = this;
            AcLog.Info(AcVersion.Label + ": " + AcVersion.Summary);
            _netTickCall = () => NetTick();
            _frontTickCall = () => FrontTick();
            _raceTickCall = () => RaceTick();
            AcShaders.Carry(_shaderCarriers);
            StartIsolation(WhitelistRule());
            SetStatus("Looking for your Assetto Corsa install...", false);
            Guard("front start", () => FrontStart());
            Guard("net start", () => NetStart());
            StartBoot();
        }

        /// <summary>Runs a module call, logging (not throwing) a failure.</summary>
        public static void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { AcLog.Error(what + ": " + e); }
        }

        /// <summary>Worker thread: find the install, then build the content catalog (disk only, no Unity API).</summary>
        private void StartBoot()
        {
            string hint = AcSettings.InstallOverride;
            if (string.IsNullOrEmpty(hint)) hint = _installHint;
            _bootDone = false;
            _bootError = null;
            _bootInstall = null;
            _bootContent = null;
            _bootScanError = null;
            _bootThread = new Thread(() => Boot(hint)) { IsBackground = true, Name = "Assetto boot" };
            _bootThread.Start();
        }

        private void Boot(string hint)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                AcInstall inst = AcInstall.Find(hint);
                _bootHintProblem = AcInstall.HintProblem;
                if (inst == null)
                    _bootError = _bootHintProblem != null
                        ? "Assetto Corsa was not found: " + _bootHintProblem + ". Set the folder with AssettoCorsa.exe and content in Settings."
                        : "Assetto Corsa was not found. Install Assetto Corsa from Steam (the folder with AssettoCorsa.exe and content), or set the folder in Settings.";
                else _bootInstall = inst;
            }
            catch (Exception e)
            {
                _bootError = "Reading the install failed: " + e.Message;
            }

            _bootMs = sw.ElapsedMilliseconds;
            if (_bootInstall != null)
            {
                // The catalog: cars, skins, tracks, layouts (a failed scan keeps the install; the front shows an empty list).
                try { _bootContent = AcContent.Scan(_bootInstall); }
                catch (Exception e) { _bootContent = null; _bootScanError = "Reading the content folder failed: " + e.Message; }
                _bootScanMs = sw.ElapsedMilliseconds - _bootMs;
            }

            _bootDone = true;
        }

        private void Update()
        {
            if (!_started) return;
            if (_bootDone && !_bootHandled)
            {
                _bootHandled = true;
                FinishBoot();
            }

            Guard("net tick", _netTickCall);
            Guard("front tick", _frontTickCall);
            Guard("race tick", _raceTickCall);
        }

        private void FinishBoot()
        {
            if (_bootError != null)
            {
                AcLog.Warn("Boot: " + _bootError);
                SetStatus(_bootError, true);
                return;
            }

            _install = _bootInstall;
            _content = _bootContent;
            if (_bootHintProblem != null) AcLog.Warn("Boot: the folder set in Settings was not used (" + _bootHintProblem + "); found the install in a usual place instead");
            if (_bootScanError != null) AcLog.Warn("Boot: " + _bootScanError);
            if (_content != null)
            {
                int cars = _content.Cars != null ? _content.Cars.Count : 0;
                int tracks = _content.Tracks != null ? _content.Tracks.Count : 0;
                // The one boot fact line (contract wording): "Boot: install <root>, N owned cars (M listed), K playable layouts".
                AcLog.Info("Boot: install " + _install.Root + ", " + _content.OwnedCars + " owned cars (" + cars + " listed), " + _content.OwnedLayouts
                           + " playable layouts (" + tracks + " tracks; found in " + _bootMs + " ms, scanned in " + _bootScanMs + " ms)");
                SetStatus("Assetto Corsa found: " + _install.Root + "  -  " + _content.OwnedCars + " cars, " + _content.OwnedLayouts + " track layouts", false);
            }
            else
            {
                AcLog.Info("Boot: install " + _install.Root + ", 0 owned cars (0 listed), 0 playable layouts (" + _bootMs + " ms)");
                SetStatus("Assetto Corsa found: " + _install.Root + "  -  " + (_bootScanError ?? "the content catalog is empty"), _bootScanError != null);
            }

            Guard("front boot", () => FrontBootDone());
        }

        private void OnDestroy()
        {
            Guard("race stop", () => RaceStop());
            Guard("net stop", () => NetStop());
            Guard("front stop", () => FrontStop());
            // First and on its own: other players' hidden renderers, colliders and voices must be restored even if a later
            // step throws (idempotent: the driver's OnDestroy calls it too). Then the verdict is forgotten (statics survive).
            try
            {
                AcRemotes.Reset();
            }
            catch (Exception e)
            {
                AcLog.Error("Access: isolation shutdown failed: " + e);
            }

            AcAccess.Reset();
            _isolating = false;
            if (Current == this) Current = null;
        }
    }
}
