using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Logging for code that must also run outside Unity (the offline harness). Game code sets Sink to Debug.Log;
    /// the harness sets it to Console.WriteLine. Every line carries the [SomniumAssetto] tag so Player.log greps work.
    /// </summary>
    public static class AcLog
    {
        public const string Tag = "[SomniumAssetto] ";
        public static Action<string> Sink;
        public static Action<string> WarnSink;
        public static Action<string> ErrorSink;

        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);

        public static void Info(string message) { var s = Sink; if (s != null) s(Tag + message); }
        public static void Warn(string message) { var s = WarnSink ?? Sink; if (s != null) s(Tag + message); }
        public static void Error(string message) { var s = ErrorSink ?? WarnSink ?? Sink; if (s != null) s(Tag + message); }

        /// <summary>Logs <paramref name="message"/> the first time it is seen this visit (one-time Player.log facts, stub notices). Pass a
        /// constant string from per-frame code: the lookup does not allocate.</summary>
        public static void InfoOnce(string message)
        {
            if (message == null) return;
            lock (Seen) { if (!Seen.Add(message)) return; }
            Info(message);
        }

        /// <summary>Warning variant of <see cref="InfoOnce"/>.</summary>
        public static void WarnOnce(string message)
        {
            if (message == null) return;
            lock (Seen) { if (!Seen.Add(message)) return; }
            Warn(message);
        }
    }
}
