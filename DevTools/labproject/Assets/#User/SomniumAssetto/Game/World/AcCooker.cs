using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Cooks MeshCollider data off the main thread: <c>Physics.BakeMesh(meshId, false)</c> on one background thread (documented as callable
    /// from any thread), then the main thread assigns <c>MeshCollider.sharedMesh</c>, which reuses the baked data instead of cooking again
    /// (same default cooking options). A track has 300+ physics meshes (monza 328, ~450 k triangles): cooked inline they would stall frames.
    /// If baking is refused structurally ("main thread" exception) or fails 8 times in a row, the cooker switches to main-thread cooking for good (logged).
    /// </summary>
    public static class AcCooker
    {
        internal sealed class Job { public int MeshId; public volatile bool Done; public string Error; }

        static readonly Queue<Job> Pending = new Queue<Job>();
        static readonly object Lock = new object();
        static Thread _thread;
        static volatile bool _broken;
        static int _baked, _failed, _consecutive;
        static string _brokenWhy;
        /// <summary>Failures in a row (no success between) that switch to main-thread cooking. // ours</summary>
        const int BrokenAfter = 8;

        /// <summary>A ticket for one mesh (null when off-thread baking is not available: assign on the main thread directly).</summary>
        public sealed class Ticket
        {
            internal Job J;
            public bool Done { get { return J == null || J.Done; } }
            public string Error { get { return J != null ? J.Error : null; } }
        }

        public static bool OffThread { get { return !_broken; } }
        public static int Baked { get { return _baked; } }

        /// <summary>Queues a readable mesh for baking (main thread). Do not touch the mesh until the ticket is done.</summary>
        public static Ticket Queue(Mesh mesh)
        {
            if (_broken || mesh == null) return new Ticket();
            var j = new Job { MeshId = mesh.GetInstanceID() };
            lock (Lock)
            {
                Pending.Enqueue(j);
                if (_thread == null)
                {
                    _thread = new Thread(Loop) { IsBackground = true, Name = "Assetto collider cooking" };
                    _thread.Start();
                }
                Monitor.Pulse(Lock);
            }
            return new Ticket { J = j };
        }

        static void Loop()
        {
            while (true)
            {
                Job j;
                lock (Lock)
                {
                    while (Pending.Count == 0) Monitor.Wait(Lock);
                    j = Pending.Dequeue();
                }
                if (!_broken)
                {
                    try { Physics.BakeMesh(j.MeshId, false); Interlocked.Increment(ref _baked); _consecutive = 0; }
                    catch (Exception e)
                    {
                        // a structural refusal (Unity's "can only be called from the main thread") or many failures in a row = baking off the
                        // main thread does not work here: cook on the main thread from now on. One failure alone is one mesh (e.g. destroyed by
                        // an unload while queued) and must not switch every later track to main-thread cooking.
                        j.Error = e.Message; Interlocked.Increment(ref _failed); _consecutive++;
                        string msg = e.Message ?? "";
                        bool structural = msg.IndexOf("main thread", StringComparison.OrdinalIgnoreCase) >= 0 || e is MissingMethodException || e is NotSupportedException;
                        if (structural || _consecutive >= BrokenAfter) { _broken = true; _brokenWhy = e.GetType().Name + ": " + msg; }
                    }
                }
                j.Done = true;
            }
        }

        /// <summary>One line for the build log.</summary>
        public static string Summary { get { return _broken ? "main-thread cooking (off-thread bake refused: " + _brokenWhy + ")" : "baked off the main thread: " + _baked + (_failed > 0 ? ", " + _failed + " bake failures" : ""); } }
    }
}
