using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>Receives a finished clip upload (no closure per clip: the bank passes itself and the sample index).</summary>
    internal interface IUploadSink { void Uploaded(int tag, AudioClip clip, bool ok); }

    /// <summary>
    /// [A, Audio] The audio worker thread ("Assetto audio") and the main-thread clip upload. Banks are parsed and FSB5 PCM16 samples
    /// converted to floats on the worker; the main thread turns them into AudioClips (AudioClip.Create + SetData - the route the
    /// Somnium client plays; OnAudioFilterRead is not called for world code) a slice at a time, at most <see cref="UploadFloatsPerFrame"/>
    /// floats a frame, so a 50 MB bank never stalls one frame. Decoded-but-not-uploaded PCM is capped (<see cref="PendingCap"/>) so the
    /// worker waits instead of holding a whole bank of floats. Pump runs once a frame (AcAudioHost.Update).
    /// </summary>
    internal static class AcAudioWorker
    {
        /// <summary>Upload budget a frame: 2 x 65536 floats (about 0.5 MB, ETS's proven value).</summary>
        public const int UploadFloatsPerFrame = 131072;
        /// <summary>At most this many decoded floats wait for upload (32 MB). // ours</summary>
        public const long PendingCap = 8L * 1024 * 1024;

        internal abstract class Job
        {
            /// <summary>Worker thread. Must not touch Unity objects.</summary>
            internal abstract void Run();
            /// <summary>Main thread, after Run.</summary>
            internal abstract void Done();
            internal Exception Error;
        }

        sealed class Upload
        {
            public AudioClip Clip;
            public float[] Data;
            public int Channels, DoneFloats;
            public IUploadSink Sink;
            public int Tag;
        }

        static readonly object Lock = new object();
        static readonly LinkedList<Job> High = new LinkedList<Job>(), Low = new LinkedList<Job>();
        static readonly Queue<Job> Finished = new Queue<Job>();
        static readonly Queue<Upload> Uploads = new Queue<Upload>();
        static Thread _thread;
        static long _pending;
        // each worker thread has its own stop flag and the generation it serves: Stop() starts a new generation, so a job still running on the
        // old thread when the world closes never lands in a later session (its Done would create clips for a bank nobody frees)
        sealed class Worker { public volatile bool Stop; public int Gen; }
        static Worker _worker;
        static int _gen;
        [ThreadStatic] static Worker _threadWorker;


        public static long PendingFloats { get { return Interlocked.Read(ref _pending); } }
        public static long UploadedFloats { get; private set; }
        public static int Queued { get { lock (Lock) return High.Count + Low.Count; } }
        public static bool Busy { get { lock (Lock) return High.Count + Low.Count > 0 || Finished.Count > 0; } }

        public static void Enqueue(Job job, bool high)
        {
            if (job == null) return;
            lock (Lock)
            {
                if (high) High.AddLast(job); else Low.AddLast(job);
                Monitor.PulseAll(Lock);
            }
            if (_thread == null || !_thread.IsAlive || _worker == null || _worker.Stop)
            {
                Worker w;
                lock (Lock) { w = new Worker { Gen = _gen }; _worker = w; }
                _thread = new Thread(Loop) { IsBackground = true, Name = "Assetto audio" };
                _thread.Start(w);
            }
        }

        /// <summary>Called by a decode job: blocks the worker while too much decoded PCM waits for upload.</summary>
        internal static void WaitForRoom(long floats)
        {
            Worker w = _threadWorker;
            lock (Lock)
            {
                while (!(w != null && w.Stop) && Interlocked.Read(ref _pending) > 0 && Interlocked.Read(ref _pending) + floats > PendingCap) Monitor.Wait(Lock, 200);
                if (w == null || w.Gen == _gen) Interlocked.Add(ref _pending, floats);   // a stopped generation reserves nothing (Stop zeroed the count)
            }
        }

        /// <summary>Gives back room taken by <see cref="WaitForRoom"/> for PCM that will not be uploaded.</summary>
        /// <summary>Counts PCM that did not come through <see cref="WaitForRoom"/> (a large UI wav) toward the pending cap before QueueUpload.</summary>
        internal static void Reserve(long floats) { Interlocked.Add(ref _pending, floats); }

        internal static void Release(long floats)
        {
            Worker w = _threadWorker;
            if (w != null && w.Gen != _gen) return;                      // its reservation was dropped by Stop
            Interlocked.Add(ref _pending, -floats);
            lock (Lock) Monitor.PulseAll(Lock);
        }

        static void Loop(object state)
        {
            Worker w = (Worker)state;
            _threadWorker = w;
            while (true)
            {
                Job j = null;
                lock (Lock)
                {
                    while (!w.Stop && High.Count == 0 && Low.Count == 0) Monitor.Wait(Lock, 1000);
                    if (w.Stop) return;
                    if (High.Count > 0) { j = High.First.Value; High.RemoveFirst(); }
                    else { j = Low.First.Value; Low.RemoveFirst(); }
                }
                try { j.Run(); }
                catch (Exception e) { j.Error = e; }
                lock (Lock) { if (w.Gen == _gen && !w.Stop) Finished.Enqueue(j); }   // a stopped generation's job is dropped (no Done)
            }
        }

        /// <summary>Queues a clip upload (main thread). <paramref name="data"/> counts toward the pending cap until uploaded.</summary>
        internal static void QueueUpload(AudioClip clip, float[] data, int channels, IUploadSink sink, int tag)
        {
            Uploads.Enqueue(new Upload { Clip = clip, Data = data, Channels = Math.Max(1, channels), Sink = sink, Tag = tag });
        }

        /// <summary>Main thread, once a frame: finished jobs' Done, then up to <see cref="UploadFloatsPerFrame"/> floats of SetData.</summary>
        public static void Pump()
        {
            for (int guard = 0; guard < 64; guard++)
            {
                Job j;
                lock (Lock) { if (Finished.Count == 0) break; j = Finished.Dequeue(); }
                try { j.Done(); }
                catch (Exception e) { AcLog.Error("Audio: a finished job failed: " + e); }
            }
            int budget = UploadFloatsPerFrame;
            while (budget > 0 && Uploads.Count > 0)
            {
                var u = Uploads.Peek();
                bool ok = true;
                try
                {
                    if (u.Clip == null) ok = false;
                    else
                    {
                        int len = u.Data.Length, n;
                        float[] buf = ChunkBuffer(u.Channels);
                        if (u.Channels > 8 || (u.DoneFloats == 0 && (len <= budget || len <= buf.Length)))
                        {
                            u.Clip.SetData(u.Data, 0);             // the whole clip in one call (no copy)
                            n = len;
                            u.DoneFloats = len;
                        }
                        else
                        {
                            // fixed-size chunks from one shared buffer (SetData writes the whole array and must not wrap); the tail
                            // re-writes the overlap before it so the last chunk is full size too: no allocation at all
                            n = buf.Length;
                            int off = u.DoneFloats;
                            if (off + n > len) off = len - n;
                            Array.Copy(u.Data, off, buf, 0, n);
                            u.Clip.SetData(buf, off / u.Channels);
                            u.DoneFloats = off + n;
                        }
                        budget -= n;
                        UploadedFloats += n;
                    }
                }
                catch (Exception e) { ok = false; AcLog.WarnOnce("Audio: clip upload failed: " + e.Message); }
                if (!ok || u.DoneFloats >= u.Data.Length)
                {
                    Uploads.Dequeue();
                    Interlocked.Add(ref _pending, -u.Data.Length);
                    lock (Lock) Monitor.PulseAll(Lock);
                    try { if (u.Sink != null) u.Sink.Uploaded(u.Tag, u.Clip, ok); }
                    catch (Exception e) { AcLog.Error("Audio: upload callback failed: " + e); }
                    u.Data = null;
                }
            }
        }

        /// <summary>Upload chunk: 16384 floats (a multiple of the channel count) - 8 chunks fill a frame's budget.</summary>
        const int Chunk = 16384;
        static readonly float[][] Chunks = new float[9][];

        /// <summary>The shared chunk buffer for a channel count (allocated once per channel count, at the first upload).</summary>
        static float[] ChunkBuffer(int channels)
        {
            int c = Math.Max(1, Math.Min(8, channels));
            if (Chunks[c] == null) Chunks[c] = new float[Chunk - Chunk % c];
            return Chunks[c];
        }

        public static void Stop()
        {
            lock (Lock) { if (_worker != null) _worker.Stop = true; _worker = null; _gen++; High.Clear(); Low.Clear(); Monitor.PulseAll(Lock); }
            while (Uploads.Count > 0)
            {
                // the clip was created before it was queued and reaches its bank / AcSfx only when uploaded: destroyed here, else it leaks
                var u = Uploads.Dequeue();
                if (u.Clip != null) { try { UnityEngine.Object.Destroy(u.Clip); } catch (Exception) { } }
                u.Clip = null; u.Data = null;
            }
            lock (Lock) { Finished.Clear(); Interlocked.Exchange(ref _pending, 0); }   // in-flight reservations of the old generation are void
            _thread = null;
        }
    }
}
