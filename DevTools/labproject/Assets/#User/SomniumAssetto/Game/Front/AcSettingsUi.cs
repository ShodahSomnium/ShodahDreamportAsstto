using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] The settings rows other modules register at their start (S: VR steering mode, vr.horizonlock; A: volumes; H: units...) and the
    /// tablet's Settings page draws (AcPageSettings), grouped by <see cref="Row.Group"/>. Values live in AcSettings under the module's key prefix
    /// ("seat.", "audio.", "hud."...). Registering a key again replaces its row. The shared rows of the lead's AcSettings (volume, haptics,
    /// left-handed) and F's own tablet options are registered here.
    /// </summary>
    public static class AcSettingsUi
    {
        /// <summary>One registered row.</summary>
        public sealed class Row
        {
            public string Key, Label, Group;
            /// <summary>"toggle", "stepper", "choice", "slider".</summary>
            public string Kind;
            public float Min, Max, Step, Default;
            public string[] Choices;
            /// <summary>Stepper / slider text: value x <see cref="Show"/> in the .NET format <see cref="Format"/> ("0.##" when null).</summary>
            public string Format;
            public float Show = 1f;

            /// <summary>The current value as the page shows it ("ON" / "OFF", the choice, the number).</summary>
            public string Value()
            {
                switch (Kind)
                {
                    case "toggle": return AcSettings.Flag(Key, Default > 0.5f) ? "ON" : "OFF";
                    case "choice":
                        if (Choices == null || Choices.Length == 0) return "";
                        return Choices[Mathf.Clamp(AcSettings.Int(Key, (int)Default), 0, Choices.Length - 1)];
                    default:
                        return (AcSettings.Num(Key, Default) * Show).ToString(string.IsNullOrEmpty(Format) ? "0.##" : Format, CultureInfo.InvariantCulture);
                }
            }

            /// <summary>One step down (-1) or up (+1); a toggle flips, a choice wraps.</summary>
            public void Change(int d)
            {
                switch (Kind)
                {
                    case "toggle": AcSettings.SetFlag(Key, !AcSettings.Flag(Key, Default > 0.5f)); break;
                    case "choice":
                    {
                        int n = Choices != null ? Choices.Length : 0;
                        if (n == 0) return;
                        int i = Mathf.Clamp(AcSettings.Int(Key, (int)Default), 0, n - 1);
                        AcSettings.SetInt(Key, (i + d + n) % n);
                        break;
                    }
                    default:
                    {
                        float step = Step > 0f ? Step : 1f;
                        float v = Mathf.Clamp(AcSettings.Num(Key, Default) + d * step, Min, Max);
                        AcSettings.SetNum(Key, Mathf.Round(v / step) * step);
                        break;
                    }
                }
            }

            /// <summary>Sets a stepper / slider number directly (clamped).</summary>
            public void Set(float v) { AcSettings.SetNum(Key, Mathf.Clamp(v, Min, Max)); }
            public float Number { get { return AcSettings.Num(Key, Default); } }
        }

        private static readonly List<Row> List = new List<Row>();

        /// <summary>All rows in registration order.</summary>
        public static IReadOnlyList<Row> Rows { get { return List; } }
        /// <summary>A row was added (the page rebuilds).</summary>
        public static event Action Changed;
        /// <summary>Grows with every add (the pages rebuild their rows when it changes).</summary>
        public static int Version { get; private set; }

        static AcSettingsUi()
        {
            // The lead's shared keys (AcSettings Volume / Haptics / LeftHanded) and the front's own options.
            Slider("AUDIO", "volume", "MASTER VOLUME", 0f, 1f, 0.05f, 1f);
            Toggle("CONTROLS", "haptics", "CONTROLLER VIBRATION", true);
            Toggle("CONTROLS", "lefthanded", "LEFT-HANDED", false);
            Toggle("TABLET", "front.large", "WIDE TABLET FOR THE APP", true);
            Toggle("TABLET", "front.carry", "TABLET RIDES WITH THE CAR (VR)", true);
            Toggle("TABLET", "front.holdcar", "HOLD THE CAR WHILE THE TABLET IS OPEN", true);
            Toggle("TABLET", "front.panel", "HUB MENU PANEL", true);
            // iteration 2 (DAMAGE_DESIGN 9 F): AC's VISUALDAMAGE (R's AcCarDamageGate reads it; off = this client draws no damage, zones still
            // count). The damage displayer row "hud.damage" is registered by H with the HUD rows (default = Documents gameplay.ini
            // [DAMAGE_DISPLAYER] IS_ACTIVE, known only once the install is read).
            Toggle("CARS", "cars.visualdamage", "VISUAL DAMAGE", true);
        }

        /// <summary>An on/off row (AcSettings.Flag(key)).</summary>
        public static void Toggle(string group, string key, string label, bool def) { Add(new Row { Group = group, Key = key, Label = label, Kind = "toggle", Default = def ? 1f : 0f }); }

        /// <summary>A number row (AcSettings.Num(key)) with &lt; &gt; buttons.</summary>
        public static void Stepper(string group, string key, string label, float min, float max, float step, float def) { Add(new Row { Group = group, Key = key, Label = label, Kind = "stepper", Min = min, Max = max, Step = step, Default = def }); }

        /// <summary>A number row shown as value x <paramref name="show"/> in <paramref name="format"/> (e.g. 0..1 as "0 %" with show 100).</summary>
        public static void Stepper(string group, string key, string label, float min, float max, float step, float def, string format, float show) { Add(new Row { Group = group, Key = key, Label = label, Kind = "stepper", Min = min, Max = max, Step = step, Default = def, Format = format, Show = show }); }

        /// <summary>A choice row (AcSettings.Int(key) = index into <paramref name="choices"/>).</summary>
        public static void Choice(string group, string key, string label, string[] choices, int def) { Add(new Row { Group = group, Key = key, Label = label, Kind = "choice", Choices = choices, Default = def }); }

        /// <summary>A drag slider row (VR-safe, with - / + buttons) for a 0..1-style number (volumes); shown as a percentage of <paramref name="max"/>.</summary>
        public static void Slider(string group, string key, string label, float min, float max, float step, float def) { Add(new Row { Group = group, Key = key, Label = label, Kind = "slider", Min = min, Max = max, Step = step, Default = def }); }

        /// <summary>The row of <paramref name="key"/> (null when none).</summary>
        public static Row Find(string key)
        {
            for (int i = 0; i < List.Count; i++) if (string.Equals(List[i].Key, key, StringComparison.OrdinalIgnoreCase)) return List[i];
            return null;
        }

        private static void Add(Row r)
        {
            if (r == null || string.IsNullOrEmpty(r.Key)) return;
            if (string.IsNullOrEmpty(r.Group)) r.Group = "GENERAL";
            for (int i = 0; i < List.Count; i++)
            {
                if (string.Equals(List[i].Key, r.Key, StringComparison.OrdinalIgnoreCase)) { List[i] = r; Version++; Raise(); return; }
            }

            List.Add(r);
            Version++;
            Raise();
        }

        private static void Raise()
        {
            var h = Changed;
            if (h != null) { try { h(); } catch (Exception e) { AcLog.Error("SettingsUi: a Changed handler failed: " + e); } }
        }
    }
}
