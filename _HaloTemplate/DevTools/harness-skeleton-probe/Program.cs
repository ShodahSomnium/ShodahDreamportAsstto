using System;

/// <summary>
/// AcHarness: a dotnet console that compiles Assets/#User/SomniumAssetto/Data/** (Unity-free) to probe the player's Assetto Corsa
/// install, dump formats and write PNGs (Png.Write) to look at. Usage: dotnet run -c Release -- &lt;command&gt; [args].
/// Commands live in Probe.cs (module D adds its own probes there or in Probe*.cs files with their own case lines).
/// </summary>
static class Program
{
    static int Main(string[] a)
    {
        if (a.Length == 0)
        {
            Console.WriteLine("commands: " + Probe.Commands);
            return 1;
        }

        return Probe.Run(a);
    }
}
