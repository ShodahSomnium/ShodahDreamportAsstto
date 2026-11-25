using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [R, Race] Iteration 2 (DAMAGE_DESIGN.md 1.4, 2.1, 9 W/R): whether car models built from now on load the damage textures (txDamage,
    /// txDamageMask, the shared DAMAGE_GLASS normal). W's AcShading and C's FlattenDamageNormals read <see cref="TexturesWanted"/> at build;
    /// R sets it from the session's DAMAGE_MULTIPLIER and the local setting <c>cars.visualdamage</c> before every spawn (local car, and on
    /// every rules change, which N's remote cars and I's AI cars built later see too). Off (damage 0, or visual damage off) = iteration 1:
    /// no slots, no extra GPU memory, damage normals flattened. A 0 -> &gt;0 change mid-session reaches the paint at the next spawn (logged once).
    /// </summary>
    public static class AcCarDamageGate
    {
        /// <summary>Local setting: AC's VISUALDAMAGE (default on). Off: this client draws no damage on any car (zones still count).</summary>
        public const string VisualSettingKey = "cars.visualdamage";

        /// <summary>Car models built now load the damage texture slots (read by W / C at build).</summary>
        public static bool TexturesWanted;

        private static bool _known, _lastWanted;

        /// <summary>The setting <c>cars.visualdamage</c> (default on).</summary>
        public static bool VisualOn
        {
            get { try { return AcSettings.Flag(VisualSettingKey, true); } catch (Exception) { return true; } }
        }

        /// <summary>Sets <see cref="TexturesWanted"/> from the rules (null = the session's) and the setting; logs a change once.</summary>
        public static void Refresh(AcRules rules)
        {
            AcRules r = rules ?? AcSession.Rules;
            int m = r != null ? r.DamageMultiplier : 0;
            bool want = m > 0 && VisualOn;
            if (want) AcMaterials.WarmDamageGlass();          // fixer 2026-11-02: the crack map decodes on a worker now, not at the first break (cheap when done)
            if (_known && want == _lastWanted) { TexturesWanted = want; return; }
            bool was = _known && _lastWanted;
            _known = true; _lastWanted = want;
            TexturesWanted = want;
            AcLog.Info("Race: damage textures " + (want ? "ON" : "off") + " for cars built from now on (DAMAGE_MULTIPLIER " + m + " %, "
                       + VisualSettingKey + " " + (VisualOn ? "on" : "off") + ")"
                       + (want && !was && AcSession.OnTrack ? " - cars already on the track show paint dents after their next spawn (back to pits keeps the car; a new session / car change rebuilds it)" : ""));
        }

        /// <summary>World closing: forget the last state (the next Refresh logs again).</summary>
        public static void Reset() { _known = false; TexturesWanted = false; }
    }
}
