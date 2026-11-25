using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>One kn5 of a track layout and where it goes (AC space; all files share one world space).</summary>
    public sealed class AcTrackModel
    {
        public string File;            // file name as written (e.g. "2.kn5")
        public string FullPath;        // resolved against the track folder
        public float[] Position = new float[3];
        /// <summary>ROTATION as written: [0] = yaw about Y in degrees (Unity: Euler(0, rot[0], 0)); [1],[2] always 0 (meaning unknown).</summary>
        public float[] Rotation = new float[3];
        public bool Exists;
        public bool IsDynamic;         // from a [DYNAMIC_OBJECT_n] section (random flying objects; often missing files)
        public AcIniSection Section;   // the source section (for DYNAMIC_OBJECT extra keys)
    }

    /// <summary>
    /// <c>models.ini</c> / <c>models_&lt;layout&gt;.ini</c>: the kn5 files of a layout in load order [VERIFIED on all 21 track folders].
    /// Only <c>[MODEL_n]</c> sections are the track; <c>[DYNAMIC_OBJECT_n]</c> list optional random props (files often absent: skip
    /// unless present). Duplicate file names are loaded once (ks_highlands layout_drift lists 9.kn5 twice). When the ini is missing
    /// (trento-bondone) the layout is just <c>&lt;track&gt;.kn5</c>. Non-zero transforms exist only on 100.kn5 (start-light gantry).
    /// </summary>
    public sealed class AcModelsIni
    {
        public string Path;            // null when the ini was missing
        public readonly List<AcTrackModel> Models = new List<AcTrackModel>();
        public readonly List<AcTrackModel> Dynamic = new List<AcTrackModel>();

        public bool AllExist
        {
            get { for (int i = 0; i < Models.Count; i++) if (!Models[i].Exists) return false; return Models.Count > 0; }
        }

        /// <summary>Models file of a layout: models.ini (single layout, <paramref name="layoutId"/> "") or models_&lt;layout&gt;.ini.</summary>
        public static string PathFor(string trackDir, string layoutId)
        {
            return System.IO.Path.Combine(trackDir, string.IsNullOrEmpty(layoutId) ? "models.ini" : "models_" + layoutId + ".ini");
        }

        public static AcModelsIni Load(string trackDir, string layoutId)
        {
            string path = PathFor(trackDir, layoutId);
            var res = new AcModelsIni();
            var ini = File.Exists(path) ? AcIni.Load(path) : null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ini != null)
            {
                res.Path = path;
                for (int i = 0; i < ini.Order.Count; i++)
                {
                    var s = ini.Order[i];
                    bool model = s.Name.StartsWith("MODEL_", StringComparison.OrdinalIgnoreCase);
                    bool dyn = s.Name.StartsWith("DYNAMIC_OBJECT", StringComparison.OrdinalIgnoreCase);
                    if (!model && !dyn) continue;
                    string file = (s.Get("FILE", "") ?? "").Trim();
                    if (file.Length == 0) continue;
                    if (model && !seen.Add(file)) continue;
                    var m = new AcTrackModel
                    {
                        File = file,
                        FullPath = System.IO.Path.Combine(trackDir, file),
                        IsDynamic = dyn,
                        Section = s,
                    };
                    var p = s.GetFloats("POSITION"); if (p != null) for (int k = 0; k < 3 && k < p.Length; k++) m.Position[k] = p[k];
                    var r = s.GetFloats("ROTATION"); if (r != null) for (int k = 0; k < 3 && k < r.Length; k++) m.Rotation[k] = r[k];
                    m.Exists = File.Exists(m.FullPath);
                    (dyn ? res.Dynamic : res.Models).Add(m);
                }
            }
            else
            {
                string id = System.IO.Path.GetFileName(trackDir.TrimEnd('/', '\\'));
                var m = new AcTrackModel { File = id + ".kn5", FullPath = System.IO.Path.Combine(trackDir, id + ".kn5") };
                m.Exists = File.Exists(m.FullPath);
                res.Models.Add(m);
            }
            return res;
        }
    }
}
