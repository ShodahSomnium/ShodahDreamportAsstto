using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] What AC's own config files say about its HUD, read once from the player's install (and the player's AC user config in
    /// Documents/Assetto Corsa/cfg, which AC itself lets override the install's cfg/): units, the time-difference display, session-info
    /// colours, the ksGui skin colours, and the in-game strings of the player's AC language (system/locales/&lt;lang&gt;.ini, falling back to
    /// en.ini). Unity-free; every missing file just leaves the documented default. VERIFIED keys (install 2026-10-31):
    /// cfg/gameplay.ini [OPTIONS] USE_MPH=0, [TIME_DIFFERENCE] IS_ACTIVE=1 POS_Y=200, [GUI] SHOW_SECTORS=1, [OVERLAY_LEADERBOARD] ACTIVE=3;
    /// system/cfg/session_info.ini [OPTIONS] PRATICE_VISIBLE=1 QUALIFY_VISIBLE=1 RACE_VISIBLE=1 BACKGROUND_COLOR=0,0,0,0.3 FONT_COLOR=1,1,1,1;
    /// system/cfg/messages.ini [SYSTEM] SERVER_PLAYER_JOINED=1 SERVER_PLAYER_DISCONNECTED=1; content/gui/skin.ini [FORM] BACK=0,0,0,128
    /// TITLE_BACK=255,0,0,255; Documents cfg/launcher.ini LANGUAGE=en.
    /// </summary>
    public sealed class AcHudConfig
    {
        public bool UseMph;
        /// <summary>gameplay.ini [TIME_DIFFERENCE] IS_ACTIVE: the big live delta at the top of the screen.</summary>
        public bool TimeDifference = true;
        /// <summary>[TIME_DIFFERENCE] POS_Y: its distance from the top of the screen in AC pixels (1080-high reference in our layout).</summary>
        public float TimeDifferencePosY = 200f;
        /// <summary>[GUI] SHOW_SECTORS.</summary>
        public bool ShowSectors = true;
        /// <summary>[OVERLAY_LEADERBOARD] ACTIVE (0 off; AC's default 3).</summary>
        public int OverlayLeaderboard = 3;
        /// <summary>The standings tower's default: AC draws no leaderboard overlay with [OVERLAY_LEADERBOARD] ACTIVE=0.</summary>
        public bool LeaderboardDefault { get { return OverlayLeaderboard != 0; } }
        /// <summary>The session box is drawn in AC session type <paramref name="acSession"/> (1 practice, 2 qualify, 3 race, 4 hotlap - AcSessionType
        /// values; session_info.ini *_VISIBLE, hotlap follows practice).</summary>
        public bool SessionVisible(int acSession)
        {
            return acSession == 3 ? RaceVisible : acSession == 2 ? QualifyVisible : PracticeVisible;
        }
        /// <summary>system/cfg/session_info.ini [OPTIONS] PRATICE_VISIBLE (AC's own spelling) / QUALIFY_VISIBLE / RACE_VISIBLE: the session box
        /// (name + clock / laps) is drawn in that session type.</summary>
        public bool PracticeVisible = true, QualifyVisible = true, RaceVisible = true;
        /// <summary>system/cfg/messages.ini [SYSTEM] SERVER_PLAYER_JOINED / SERVER_PLAYER_DISCONNECTED: AC's "player joined / left" system messages
        /// (1 = shown, AC's default). The file's ABS / TC / TURBO / BRAKE_BIAS keys gate the messages AC shows when the driver changes an aid level
        /// in the car - the Dreamport has no in-car aid changes, so they are not read.</summary>
        public bool MsgPlayerJoined = true, MsgPlayerLeft = true;
        /// <summary>session_info.ini colours (r, g, b, a in 0..1).</summary>
        public float[] SessionBack = { 0f, 0f, 0f, 0.3f };
        public float[] SessionFore = { 1f, 1f, 1f, 1f };
        /// <summary>skin.ini [FORM] BACK / TITLE_BACK / FORE (0..1).</summary>
        public float[] FormBack = { 0f, 0f, 0f, 0.5f };
        public float[] TitleBack = { 1f, 0f, 0f, 1f };
        public float[] FormFore = { 1f, 1f, 1f, 1f };
        /// <summary>Language code of the strings (launcher.ini LANGUAGE, default en).</summary>
        public string Language = "en";
        /// <summary>Which files were read (for the one-time log line).</summary>
        public readonly List<string> Read = new List<string>();

        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Number of locale strings loaded.</summary>
        public int StringCount { get { return _strings.Count; } }

        /// <summary>The player's AC string for <paramref name="key"/> (system/locales key, e.g. "BEST", "POS.", "PRACTICE"), or
        /// <paramref name="fallback"/> (AC's English text) when absent.</summary>
        public string S(string key, string fallback)
        {
            string v;
            return key != null && _strings.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }

        /// <summary>Reads everything (worker-safe). <paramref name="acRoot"/> = AcInstall.Root; <paramref name="documentsDir"/> = the user's
        /// Documents folder (null = skip the user overrides).</summary>
        public static AcHudConfig Load(string acRoot, string documentsDir)
        {
            var c = new AcHudConfig();
            if (string.IsNullOrEmpty(acRoot)) return c;
            string userCfg = null;
            try { if (!string.IsNullOrEmpty(documentsDir)) userCfg = Path.Combine(Path.Combine(documentsDir, "Assetto Corsa"), "cfg"); }
            catch (Exception) { userCfg = null; }

            AcIni gameplay = Pick(c, Path.Combine(Path.Combine(acRoot, "cfg"), "gameplay.ini"), userCfg != null ? Path.Combine(userCfg, "gameplay.ini") : null);
            if (gameplay != null)
            {
                c.UseMph = gameplay.GetInt("OPTIONS", "USE_MPH", 0) != 0;
                c.TimeDifference = gameplay.GetInt("TIME_DIFFERENCE", "IS_ACTIVE", 1) != 0;
                c.TimeDifferencePosY = gameplay.GetFloat("TIME_DIFFERENCE", "POS_Y", 200f);
                c.ShowSectors = gameplay.GetInt("GUI", "SHOW_SECTORS", 1) != 0;
                c.OverlayLeaderboard = gameplay.GetInt("OVERLAY_LEADERBOARD", "ACTIVE", 3);
            }

            AcIni session = Ini(c, Path.Combine(Path.Combine(Path.Combine(acRoot, "system"), "cfg"), "session_info.ini"));
            if (session != null)
            {
                Rgba(session.Get("OPTIONS", "BACKGROUND_COLOR"), c.SessionBack, 1f);
                Rgba(session.Get("OPTIONS", "FONT_COLOR"), c.SessionFore, 1f);
                c.PracticeVisible = session.GetInt("OPTIONS", "PRATICE_VISIBLE", session.GetInt("OPTIONS", "PRACTICE_VISIBLE", 1)) != 0;
                c.QualifyVisible = session.GetInt("OPTIONS", "QUALIFY_VISIBLE", 1) != 0;
                c.RaceVisible = session.GetInt("OPTIONS", "RACE_VISIBLE", 1) != 0;
            }

            AcIni messages = Ini(c, Path.Combine(Path.Combine(Path.Combine(acRoot, "system"), "cfg"), "messages.ini"));
            if (messages != null)
            {
                c.MsgPlayerJoined = messages.GetInt("SYSTEM", "SERVER_PLAYER_JOINED", 1) != 0;
                c.MsgPlayerLeft = messages.GetInt("SYSTEM", "SERVER_PLAYER_DISCONNECTED", 1) != 0;
            }

            AcIni skin = Ini(c, Path.Combine(Path.Combine(Path.Combine(acRoot, "content"), "gui"), "skin.ini"));
            if (skin != null)
            {
                Rgba(skin.Get("FORM", "BACK"), c.FormBack, 255f);
                Rgba(skin.Get("FORM", "TITLE_BACK"), c.TitleBack, 255f);
                Rgba(skin.Get("FORM", "FORE"), c.FormFore, 255f);
            }

            if (userCfg != null)
            {
                AcIni launcher = Ini(c, Path.Combine(userCfg, "launcher.ini"));
                if (launcher != null)
                {
                    string lang = launcher.Get("WINDOW", "LANGUAGE");
                    if (!string.IsNullOrEmpty(lang) && lang.Length <= 8 && lang.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) c.Language = lang.Trim().ToLowerInvariant();
                }
            }
            string locales = Path.Combine(Path.Combine(acRoot, "system"), "locales");
            if (!c.LoadStrings(Path.Combine(locales, c.Language + ".ini")) && c.Language != "en")
            {
                c.Language = "en";
                c.LoadStrings(Path.Combine(locales, "en.ini"));
            }
            return c;
        }

        /// <summary>The user's copy wins over the install's (AC reads Documents/Assetto Corsa/cfg first).</summary>
        private static AcIni Pick(AcHudConfig c, string installPath, string userPath)
        {
            if (userPath != null)
            {
                AcIni u = Ini(c, userPath);
                if (u != null) return u;
            }
            return Ini(c, installPath);
        }

        private static AcIni Ini(AcHudConfig c, string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return null;
                AcIni ini = AcIni.Load(path);
                c.Read.Add(path);
                return ini;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Parses "r,g,b[,a]" into 0..1 values (<paramref name="scale"/> 255 for byte colours).</summary>
        private static void Rgba(string v, float[] into, float scale)
        {
            if (string.IsNullOrEmpty(v)) return;
            float[] f = AcText.Floats(v);
            if (f == null || f.Length < 3) return;
            for (int i = 0; i < 4 && i < f.Length; i++)
            {
                float x = f[i] / scale;
                into[i] = x < 0f ? 0f : (x > 1f ? 1f : x);
            }
        }

        /// <summary>system/locales/*.ini: "[STRINGS]" then key=value lines (keys contain spaces, dots, "%"; split at the first '=').</summary>
        public bool LoadStrings(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                string text = AcText.ReadFile(path);
                int n = 0;
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == '[' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                    if (key.Length > 0 && key[0] == '﻿') key = key.Substring(1);
                    _strings[key] = val.Replace("\\n", "\n");
                    n++;
                }
                Read.Add(path);
                return n > 0;
            }
            catch (Exception) { return false; }
        }
    }
}
