using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [F, Front] Pictures read from the player's install for the menus (car badges, skin preview.jpg, track preview / outline png, launcher art),
    /// on demand and never all at once (965 skin previews of 1022 x 574 exist here): a worker thread reads the file, the main thread decodes it
    /// with ImageConversion.LoadImage (WoW's UiPortraits recipe, shipped), a picture wider than asked is box-filtered down on the worker and
    /// uploaded small. At most one decode per frame (<see cref="DecodeBudgetMs"/>); a cache keeps recent pictures and drops the oldest not on screen.
    /// <see cref="Bind"/> fills a RawImage when its picture is ready (invisible until then, aspect kept inside its rect).
    /// </summary>
    public static class AcUiArt
    {
        /// <summary>Main-thread milliseconds per frame for decodes and uploads (at least one is done per frame). // ours</summary>
        public const float DecodeBudgetMs = 4f;
        /// <summary>Pictures kept (oldest not on screen dropped beyond this). // ours</summary>
        public const int CacheSize = 160;

        private sealed class Entry
        {
            public string Key, Path;
            public int MaxW;
            public volatile int Stage;          // 0 to read, 1 bytes read, 2 to shrink, 3 small pixels ready, 4 done, 5 failed
            public byte[] Bytes;
            public Color32[] Big;
            public int BigW, BigH;
            public Color32[] Small;
            public int SmallW, SmallH;
            public Texture2D Tex;
            public float LastUse;
            public string Error;
        }

        private sealed class BindReq
        {
            public RawImage Image;
            public Entry Entry;
            public Color Color;
            public bool Fit;
            public Vector2 Size, Pos;
        }

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<BindReq> Pending = new List<BindReq>();
        private static readonly List<BindReq> Shown = new List<BindReq>();
        private static readonly Queue<Entry> Work = new Queue<Entry>();
        private static readonly Queue<Entry> MainReady = new Queue<Entry>();
        private static volatile int _mainReady;
        private static readonly object Gate = new object();
        private static readonly AutoResetEvent Signal = new AutoResetEvent(false);
        private static Thread _thread;
        private static volatile bool _stop;
        private static float _nextPrune;
        private static int _decoded, _failed;
        private static bool _firstReported;

        public static int Decoded { get { return _decoded; } }
        public static int Failed { get { return _failed; } }
        public static int Cached { get { return Cache.Count; } }

        /// <summary>Fills <paramref name="img"/> with the picture at <paramref name="path"/> (at most <paramref name="maxW"/> pixels wide) when ready.</summary>
        public static void Bind(RawImage img, string path, int maxW, Color color, bool fit)
        {
            if (img == null) return;
            // a newer request for the same image replaces an older one still waiting (a page switching pictures fast)
            for (int i = Pending.Count - 1; i >= 0; i--) if (Pending[i].Image == img) Pending.RemoveAt(i);
            if (string.IsNullOrEmpty(path)) { img.texture = null; SetAlpha(img, color, 0f); return; }
            Entry e = Want(path, maxW);
            var rt = img.rectTransform;
            var b = new BindReq { Image = img, Entry = e, Color = color, Fit = fit, Size = rt.sizeDelta, Pos = rt.anchoredPosition };
            if (e.Stage == 4) Apply(b);
            else if (e.Stage == 5) SetAlpha(img, color, 0f);
            else Pending.Add(b);
        }

        /// <summary>The picture if it is ready (queued for loading otherwise); null while loading or when it failed.</summary>
        public static Texture2D Get(string path, int maxW)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Entry e = Want(path, maxW);
            return e.Stage == 4 ? e.Tex : null;
        }

        private static Entry Want(string path, int maxW)
        {
            maxW = Mathf.Clamp(maxW, 16, 2048);
            string key = path + "@" + maxW;
            Entry e;
            if (!Cache.TryGetValue(key, out e))
            {
                e = new Entry { Key = key, Path = path, MaxW = maxW };
                Cache[key] = e;
                Enqueue(e);
            }

            e.LastUse = Time.unscaledTime;
            return e;
        }

        private static void Enqueue(Entry e)
        {
            EnsureThread();
            lock (Gate) Work.Enqueue(e);
            Signal.Set();
        }

        private static void EnsureThread()
        {
            if (_thread != null && _thread.IsAlive) return;
            _stop = false;
            _thread = new Thread(Worker) { IsBackground = true, Name = "Assetto ui art" };
            _thread.Start();
        }

        private static void Worker()
        {
            while (!_stop)
            {
                Entry e = null;
                lock (Gate) if (Work.Count > 0) e = Work.Dequeue();
                if (e == null) { Signal.WaitOne(500); continue; }
                try
                {
                    if (e.Stage == 0)
                    {
                        e.Bytes = File.ReadAllBytes(e.Path);
                        e.Stage = 1;
                        lock (Gate) { MainReady.Enqueue(e); _mainReady = MainReady.Count; }
                    }
                    else if (e.Stage == 2)
                    {
                        Shrink(e);
                        e.Stage = 3;
                        lock (Gate) { MainReady.Enqueue(e); _mainReady = MainReady.Count; }
                    }
                }
                catch (Exception ex)
                {
                    e.Error = ex.GetType().Name + ": " + ex.Message;
                    e.Bytes = null; e.Big = null;
                    e.Stage = 5;
                }
            }
        }

        /// <summary>Box filter: every small pixel is the mean of the big pixels it covers (worker thread).</summary>
        private static void Shrink(Entry e)
        {
            int bw = e.BigW, bh = e.BigH;
            int sw = Mathf.Max(1, e.MaxW), sh = Mathf.Max(1, (int)Math.Round((double)bh * sw / bw));
            var small = new Color32[sw * sh];
            Color32[] big = e.Big;
            for (int y = 0; y < sh; y++)
            {
                int y0 = y * bh / sh, y1 = Math.Max(y0 + 1, (y + 1) * bh / sh);
                for (int x = 0; x < sw; x++)
                {
                    int x0 = x * bw / sw, x1 = Math.Max(x0 + 1, (x + 1) * bw / sw);
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int row = yy * bw;
                        for (int xx = x0; xx < x1; xx++)
                        {
                            Color32 c = big[row + xx];
                            r += c.r; g += c.g; b += c.b; a += c.a; n++;
                        }
                    }

                    small[y * sw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }

            e.Small = small; e.SmallW = sw; e.SmallH = sh;
            e.Big = null;
        }

        /// <summary>Once a frame (AcFront.Tick): decodes / uploads within the budget, fills waiting images, prunes the cache.</summary>
        public static void Tick()
        {
            if (Pending.Count == 0 && _mainReady == 0 && Time.unscaledTime < _nextPrune) return;
            float t0 = Time.realtimeSinceStartup;
            int done = 0;
            while (done == 0 || (Time.realtimeSinceStartup - t0) * 1000f < DecodeBudgetMs)
            {
                Entry e = null;
                lock (Gate) { if (MainReady.Count > 0) e = MainReady.Dequeue(); _mainReady = MainReady.Count; }
                if (e == null) break;
                if (e.Stage == 1 || e.Stage == 3) { MainStep(e); done++; }
            }

            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                BindReq b = Pending[i];
                if (b.Image == null) { Pending.RemoveAt(i); continue; }
                if (b.Entry.Stage == 4) { Apply(b); Pending.RemoveAt(i); }
                else if (b.Entry.Stage == 5) { SetAlpha(b.Image, b.Color, 0f); Pending.RemoveAt(i); Report(b.Entry); }
            }

            if (Time.unscaledTime >= _nextPrune) { _nextPrune = Time.unscaledTime + 2f; Prune(); }
        }

        private static void MainStep(Entry e)
        {
            try
            {
                if (e.Stage == 1)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    bool ok = ImageConversion.LoadImage(tex, e.Bytes, false);
                    e.Bytes = null;
                    if (!ok || tex.width <= 2) { UnityEngine.Object.Destroy(tex); e.Error = "not a PNG/JPG"; e.Stage = 5; _failed++; return; }
                    if (tex.width > e.MaxW * 1.2f)
                    {
                        e.BigW = tex.width; e.BigH = tex.height;
                        e.Big = tex.GetPixels32();
                        UnityEngine.Object.Destroy(tex);
                        e.Stage = 2;
                        Enqueue(e);
                        return;
                    }

                    Finish(e, tex);
                }
                else if (e.Stage == 3)
                {
                    var tex = new Texture2D(e.SmallW, e.SmallH, TextureFormat.RGBA32, false);
                    tex.SetPixels32(e.Small);
                    e.Small = null;
                    Finish(e, tex);
                }
            }
            catch (Exception ex)
            {
                e.Error = ex.GetType().Name + ": " + ex.Message;
                e.Stage = 5;
                _failed++;
            }
        }

        private static void Finish(Entry e, Texture2D tex)
        {
            tex.name = "Ac ui " + Path.GetFileName(e.Path);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(false, true); // no longer readable: frees the CPU copy
            e.Tex = tex;
            e.Stage = 4;
            _decoded++;
            if (!_firstReported)
            {
                _firstReported = true;
                AcLog.Info("Front: first menu picture decoded from the install (" + Path.GetFileName(e.Path) + ", " + tex.width + "x" + tex.height + ").");
            }
        }

        private static void Apply(BindReq b)
        {
            RawImage img = b.Image;
            if (img == null) return;
            Texture2D tex = b.Entry.Tex;
            img.texture = tex;
            SetAlpha(img, b.Color, b.Color.a);
            if (b.Fit && tex != null && tex.width > 0 && tex.height > 0)
            {
                float k = Mathf.Min(b.Size.x / tex.width, b.Size.y / tex.height);
                Vector2 s = new Vector2(tex.width * k, tex.height * k);
                img.rectTransform.sizeDelta = s;
                img.rectTransform.anchoredPosition = b.Pos + new Vector2((b.Size.x - s.x) * 0.5f, -(b.Size.y - s.y) * 0.5f);
            }

            Shown.Add(b);
        }

        private static void SetAlpha(RawImage img, Color c, float a)
        {
            img.color = new Color(c.r, c.g, c.b, a);
        }

        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void Report(Entry e)
        {
            if (Reported.Count < 10 && Reported.Add(e.Path)) AcLog.Warn("Front: a menu picture could not be read: " + e.Path + " (" + e.Error + ")");
        }

        /// <summary>Drops the oldest pictures that are not on screen when the cache is over <see cref="CacheSize"/>.</summary>
        private static void Prune()
        {
            for (int i = Shown.Count - 1; i >= 0; i--)
            {
                BindReq b = Shown[i];
                if (b.Image == null || b.Image.texture != b.Entry.Tex) Shown.RemoveAt(i);
                else b.Entry.LastUse = Time.unscaledTime;
            }

            if (Cache.Count <= CacheSize) return;
            var inUse = new HashSet<Entry>();
            for (int i = 0; i < Shown.Count; i++) inUse.Add(Shown[i].Entry);
            for (int i = 0; i < Pending.Count; i++) inUse.Add(Pending[i].Entry);
            var drop = new List<Entry>();
            foreach (var kv in Cache) if (!inUse.Contains(kv.Value) && (kv.Value.Stage == 4 || kv.Value.Stage == 5)) drop.Add(kv.Value);
            drop.Sort((a, b) => a.LastUse.CompareTo(b.LastUse));
            int n = Cache.Count - CacheSize;
            for (int i = 0; i < drop.Count && i < n; i++)
            {
                Entry e = drop[i];
                Cache.Remove(e.Key);
                if (e.Tex != null) UnityEngine.Object.Destroy(e.Tex);
                e.Tex = null;
            }
        }

        /// <summary>World closing: the worker stops, every picture is freed.</summary>
        public static void Shutdown()
        {
            _stop = true;
            Signal.Set();
            lock (Gate) { Work.Clear(); MainReady.Clear(); _mainReady = 0; }
            foreach (var kv in Cache) if (kv.Value.Tex != null) UnityEngine.Object.Destroy(kv.Value.Tex);
            Cache.Clear();
            Pending.Clear();
            Shown.Clear();
            _thread = null;
            _firstReported = false;
        }
    }
}
