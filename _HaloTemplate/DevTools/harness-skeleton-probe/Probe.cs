using System;
using SomniumSpace.Worlds.SomniumAssetto;

/// <summary>The probe switch. "install [hint]": AcInstall.Find + AcContent.Scan, prints the counts (the same as the game's boot log).</summary>
static class Probe
{
    public const string Commands = "install [hint]";

    public static int Run(string[] a)
    {
        AcLog.Sink = Console.WriteLine;
        AcLog.WarnSink = s => Console.WriteLine("WARN " + s);
        AcLog.ErrorSink = s => Console.WriteLine("ERROR " + s);
        switch (a[0])
        {
            case "install": return Install(a.Length > 1 ? a[1] : @"E:\SteamLibrary\steamapps\common\assettocorsa");
        }

        Console.WriteLine("unknown command '" + a[0] + "' (commands: " + Commands + ")");
        return 1;
    }

    static int Install(string hint)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AcInstall inst = AcInstall.Find(hint);
        if (inst == null)
        {
            Console.WriteLine("not found" + (AcInstall.HintProblem != null ? ": " + AcInstall.HintProblem : ""));
            return 1;
        }

        Console.WriteLine("install " + inst.Root + " (content " + inst.Content + ", " + sw.ElapsedMilliseconds + " ms)");
        if (AcInstall.HintProblem != null) Console.WriteLine("hint not used: " + AcInstall.HintProblem);
        sw.Restart();
        AcContent c = AcContent.Scan(inst);
        if (c == null) { Console.WriteLine("scan returned null"); return 1; }
        int cars = c.Cars != null ? c.Cars.Count : 0, tracks = c.Tracks != null ? c.Tracks.Count : 0;
        Console.WriteLine("content: " + cars + " cars (" + c.OwnedCars + " installed), " + tracks + " tracks, " + c.OwnedLayouts + " installed layouts (" + sw.ElapsedMilliseconds + " ms)");
        return 0;
    }
}
