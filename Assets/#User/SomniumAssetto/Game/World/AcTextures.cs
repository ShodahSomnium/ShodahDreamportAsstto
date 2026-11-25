using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Unity textures from worker-prepared AC textures (<see cref="AcTexPrep"/>). Uploaded LINEAR (raw stored values): AC shades in gamma space
    /// on the raw texels and our shaders do the same maths, turning the result into linear only at the end (Shaders/AcCommon.hlsl, the Halo
    /// recipe). DXT1 / DXT5 / BGRA32 / RGBA32 go up as they are with <c>LoadRawTextureData</c>; file row 0 (the image top) lands at Unity v = 0,
    /// which is what the kn5 uv (u, v_stored) expects (research C2/T4). PNG/JPG are decoded by ImageConversion (image top at v = 1) and
    /// then flipped so the same uv works. Main thread only. Neutral fallbacks: <see cref="White"/>, <see cref="FlatNormal"/>, <see cref="Black"/>.
    /// </summary>
    public static class AcTextures
    {
        static Texture2D _white, _black, _flat, _grey;

        public static Texture2D White { get { if (_white == null) _white = Solid(new Color32(255, 255, 255, 255), "Ac white"); return _white; } }
        public static Texture2D Black { get { if (_black == null) _black = Solid(new Color32(0, 0, 0, 0), "Ac black"); return _black; } }
        public static Texture2D Grey { get { if (_grey == null) _grey = Solid(new Color32(128, 128, 128, 255), "Ac grey"); return _grey; } }
        /// <summary>A flat tangent-space normal (0.5, 0.5, 1).</summary>
        public static Texture2D FlatNormal { get { if (_flat == null) _flat = Solid(new Color32(128, 128, 255, 255), "Ac flat normal"); return _flat; } }

        static Texture2D Solid(Color32 c, string name)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = name, hideFlags = HideFlags.DontSave };
            t.SetPixels32(new[] { c });
            t.Apply(false, true);
            return t;
        }

        /// <summary>True for the shared fallback textures (never destroyed by a scope).</summary>
        public static bool IsShared(Texture t) { return t != null && (t == _white || t == _black || t == _flat || t == _grey); }

        /// <summary>Anisotropic filtering level for uploaded textures. // ours</summary>
        public const int Aniso = 8;

        /// <summary>Uploads a prepared texture (null when it failed; one log line with the name).</summary>
        public static Texture2D Upload(AcTexPrep p, string where)
        {
            if (p == null) return null;
            if (p.Problem != null || p.Data == null)
            {
                AcLog.Warn("Textures: " + p.Name + (where != null ? " (" + where + ")" : "") + ": " + (p.Problem ?? "no data"));
                return null;
            }
            Texture2D t = null;
            try
            {
                if (p.Kind == AcTexKind.Png) return UploadImage(p, where);
                TextureFormat f;
                switch (p.Kind)
                {
                    case AcTexKind.DXT1: f = TextureFormat.DXT1; break;
                    case AcTexKind.DXT5: f = TextureFormat.DXT5; break;
                    case AcTexKind.BGRA32: f = TextureFormat.BGRA32; break;
                    default: f = TextureFormat.RGBA32; break;
                }
                t = new Texture2D(p.Width, p.Height, f, p.Mips, true)
                {
                    name = p.Name,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = p.Mips > 1 ? FilterMode.Trilinear : FilterMode.Bilinear,
                    anisoLevel = p.Mips > 1 ? Aniso : 1,
                };
                t.LoadRawTextureData(p.Data);
                t.Apply(false, true);
                return t;
            }
            catch (Exception e)
            {
                AcLog.Warn("Textures: " + p.Name + " (" + p.Kind + " " + p.Width + "x" + p.Height + " x" + p.Mips + (where != null ? ", " + where : "") + ") did not upload: " + e.Message);
                if (t != null) UnityEngine.Object.Destroy(t);
                return null;
            }
        }

        static Texture2D UploadImage(AcTexPrep p, string where)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { name = p.Name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = Aniso };
            if (!ImageConversion.LoadImage(t, p.Data, false))
            {
                UnityEngine.Object.Destroy(t);
                AcLog.Warn("Textures: " + p.Name + (where != null ? " (" + where + ")" : "") + ": PNG/JPG did not decode");
                return null;
            }
            // ImageConversion puts the image top at v = 1; kn5 uvs want it at v = 0 (like a raw DDS): flip the rows
            int w = t.width, h = t.height;
            Color32[] px = t.GetPixels32();
            var row = new Color32[w];
            for (int y = 0; y < h / 2; y++)
            {
                int a = y * w, b = (h - 1 - y) * w;
                Array.Copy(px, a, row, 0, w);
                Array.Copy(px, b, px, a, w);
                Array.Copy(row, 0, px, b, w);
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            p.Width = w; p.Height = h;
            return t;
        }

        /// <summary>Approximate GPU bytes of an uploaded texture (log lines).</summary>
        public static long BytesOf(AcTexPrep p)
        {
            if (p == null || p.Data == null) return 0;
            if (p.Kind == AcTexKind.Png) return (long)p.Width * p.Height * 4 * 4 / 3;
            return p.Data.LongLength;
        }
    }
}
