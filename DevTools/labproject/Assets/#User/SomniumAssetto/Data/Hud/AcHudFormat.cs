namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [H, HUD] Number formatting into a caller's char buffer (no strings, no allocation): the HUD rewrites its texts every frame through
    /// TMP's SetCharArray / AcBitmapText, so a lap clock running at 90 Hz costs nothing. Every method writes at <c>at</c> and returns the new
    /// end index; buffers are sized by the caller (32 chars cover everything here). Formats follow what AC shows: lap times "1:23.456"
    /// (no time: "-:--.---"), gear R / N / 1.., session clock "mm:ss".
    /// </summary>
    public static class AcHudFormat
    {
        /// <summary>Non-negative or negative integer.</summary>
        public static int Int(char[] b, int at, int v)
        {
            if (v < 0) { b[at++] = '-'; v = v == int.MinValue ? int.MaxValue : -v; }
            int start = at;
            do { b[at++] = (char)('0' + v % 10); v /= 10; } while (v > 0);
            Reverse(b, start, at - 1);
            return at;
        }

        /// <summary>Integer with at least <paramref name="digits"/> digits (leading zeros).</summary>
        public static int IntPad(char[] b, int at, int v, int digits)
        {
            if (v < 0) v = 0;
            int start = at;
            do { b[at++] = (char)('0' + v % 10); v /= 10; digits--; } while (v > 0 || digits > 0);
            Reverse(b, start, at - 1);
            return at;
        }

        public static int Str(char[] b, int at, string s)
        {
            if (s == null) return at;
            for (int i = 0; i < s.Length && at < b.Length; i++) b[at++] = s[i];
            return at;
        }

        public static int Char(char[] b, int at, char c) { b[at++] = c; return at; }

        /// <summary>AC lap time: "m:ss.fff" ("1:23.456"), hours as "h:mm:ss.fff"; ms &lt;= 0 -> "-:--.---".</summary>
        public static int LapTime(char[] b, int at, int ms)
        {
            if (ms <= 0) return Str(b, at, "-:--.---");
            int frac = ms % 1000, s = ms / 1000, m = s / 60; s %= 60;
            if (m >= 60) { at = Int(b, at, m / 60); b[at++] = ':'; at = IntPad(b, at, m % 60, 2); }
            else at = Int(b, at, m);
            b[at++] = ':';
            at = IntPad(b, at, s, 2);
            b[at++] = '.';
            return IntPad(b, at, frac, 3);
        }

        /// <summary>Signed seconds with <paramref name="decimals"/> (1..3) decimals: "+0.23" / "-1.05"; 0 -> "+0.00" // ours: sign always shown.</summary>
        public static int Delta(char[] b, int at, int ms, int decimals)
        {
            b[at++] = ms < 0 ? '-' : '+';
            if (ms < 0) ms = -ms;
            if (decimals < 1) decimals = 1; if (decimals > 3) decimals = 3;
            int div = decimals == 1 ? 100 : decimals == 2 ? 10 : 1;
            int units = (ms + div / 2) / div;                 // rounded to the shown precision
            int scale = decimals == 1 ? 10 : decimals == 2 ? 100 : 1000;
            at = Int(b, at, units / scale);
            b[at++] = '.';
            return IntPad(b, at, units % scale, decimals);
        }

        /// <summary>Gap in a standings row: laps behind -> "+1 L" (AC style lap gap), else "+12.345" seconds; 0 -> "".</summary>
        public static int Gap(char[] b, int at, int ms, int lapsBehind)
        {
            if (lapsBehind > 0) { b[at++] = '+'; at = Int(b, at, lapsBehind); b[at++] = ' '; b[at++] = 'L'; return at; }
            if (ms <= 0) return at;
            b[at++] = '+';
            int s = ms / 1000;
            if (s >= 60) { at = Int(b, at, s / 60); b[at++] = ':'; at = IntPad(b, at, s % 60, 2); }
            else at = Int(b, at, s);
            b[at++] = '.';
            return IntPad(b, at, ms % 1000, 3);
        }

        /// <summary>Gear as AC draws it: -1 R, 0 N, n.</summary>
        public static int Gear(char[] b, int at, int gear)
        {
            if (gear < 0) { b[at++] = 'R'; return at; }
            if (gear == 0) { b[at++] = 'N'; return at; }
            return Int(b, at, gear);
        }

        /// <summary>Session clock "mm:ss" (or "h:mm:ss"); negative -> "00:00".</summary>
        public static int Clock(char[] b, int at, int seconds)
        {
            if (seconds < 0) seconds = 0;
            int h = seconds / 3600, m = (seconds / 60) % 60, s = seconds % 60;
            if (h > 0) { at = Int(b, at, h); b[at++] = ':'; }
            at = IntPad(b, at, m, 2);
            b[at++] = ':';
            return IntPad(b, at, s, 2);
        }

        /// <summary>"a/b" (lap 2/5, position 2/6).</summary>
        public static int OfTotal(char[] b, int at, int a, int total)
        {
            at = Int(b, at, a);
            b[at++] = '/';
            return Int(b, at, total);
        }

        /// <summary>True when the first <paramref name="len"/> chars of <paramref name="a"/> equal <paramref name="b"/> (length <paramref name="blen"/>).</summary>
        public static bool Same(char[] a, int len, char[] b, int blen)
        {
            if (len != blen) return false;
            for (int i = 0; i < len; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void Reverse(char[] b, int i, int j)
        {
            while (i < j) { char t = b[i]; b[i] = b[j]; b[j] = t; i++; j--; }
        }
    }
}
