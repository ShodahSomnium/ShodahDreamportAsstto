using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// An AC AI spline (<c>ai/fast_lane.ai</c>, <c>ai/pit_lane.ai</c>) with a fast nearest-point / lap-progress lookup.
    ///
    /// Format v7 [VERIFIED: the 34 non-drift fast_lane + 34 pit_lane files parse exactly to EOF; ac_track_formats.md 5.1]:
    /// <c>int32 version (7), int32 pointCount, int32 lapTime (0), int32 sampleCount (0)</c>; pointCount x {float x, y, z (AC world),
    /// float cumulativeLength, int32 id}; <c>int32 payloadCount</c> + payloadCount x 18 floats (<see cref="Speed"/> ... <see cref="Grade"/>);
    /// <c>int32 hasGrid</c>; if set: float[3] max, float[3] min, int32 neighbours, float step, int32 Nx, Nx x {int32 Nz, Nz x {int32 n,
    /// int32[n] ids}} (cell = cx * Nz + cz). Other versions (drift's 12-byte v3 with 0 points) give an EMPTY spline (Count 0):
    /// fall back to the timing gates. ideal_line.ai is not used (v2 variants, some fail as v7).
    ///
    /// Positions are AC space (x_ac = -x_unity, z_ac = z_unity). Progress 0..1 = Length/lap length, 0 on the AC_TIME_0 gate
    /// [VERIFIED]; sections.ini / drs_zones.ini use the same scale.
    ///
    /// Lookups use OUR uniform grid (built at load, exact nearest via ring search, 3D distance so stacked track parts at different
    /// heights resolve) - the file's own grid is kept in <see cref="FileCells"/> for reference only (it missed 2/2000 on magione).
    /// <see cref="Progress(float, float, float, ref int)"/> tracks a car cheaply with a +-window around last frame's point.
    /// No allocation per lookup.
    /// </summary>
    public sealed class AcAiSpline
    {
        // payload field indices (18 floats per point)
        public const int Speed = 0, Gas = 1, Brake = 2, ObsoleteLatG = 3, Radius = 4, SideLeft = 5, SideRight = 6, Camber = 7,
            Direction = 8, NormalX = 9, NormalY = 10, NormalZ = 11, SegLength = 12, ForwardX = 13, ForwardY = 14, ForwardZ = 15,
            Tag = 16, Grade = 17, PayloadStride = 18;

        public int Version, Count, LapTime, SampleCount;
        /// <summary>xyz per point, AC world space.</summary>
        public float[] Pos = new float[0];
        /// <summary>Cumulative 3D distance from point 0 (m).</summary>
        public float[] Length = new float[0];
        public int[] Id = new int[0];
        public int PayloadCount;
        /// <summary>18 floats per point (null when absent). Speed m/s may be 0 in places (magione): never divide by it.</summary>
        public float[] Payload;
        public bool HasFileGrid;
        public float[] FileGridMax = new float[3], FileGridMin = new float[3];
        public int FileGridNeighbours, FileGridNx, FileGridNz;
        public float FileGridStep;
        public int[][] FileCells;
        public string Path;
        /// <summary>Bytes left unread after the grid (0 for every installed v7 file).</summary>
        public long TrailingBytes;

        /// <summary>Length of one lap including the closing gap last -> first point (m); for an open spline = <see cref="Total"/>.</summary>
        public float LapLength;
        /// <summary>True for a loop (fast lanes of circuits); false for pit lanes, drag strips and point-to-point roads.</summary>
        public bool IsClosed;
        public bool IsEmpty { get { return Count < 2; } }
        /// <summary>Cumulative length at the last point (the file's total, without the closing gap).</summary>
        public float Total { get { return Count > 0 ? Length[Count - 1] : 0f; } }

        // our lookup grid (XZ cells of GridStep m; per cell the point ids inside it)
        float _minX, _minZ, _step;
        int _nx, _nz;
        int[] _cellStart, _cellIds;

        /// <summary>Grid cell size in metres for our lookup grid.</summary>
        public const float GridStep = 10f; // ours

        public float X(int i) { return Pos[i * 3]; }
        public float Y(int i) { return Pos[i * 3 + 1]; }
        public float Z(int i) { return Pos[i * 3 + 2]; }
        public float P(int i, int field) { return Payload != null && i >= 0 && i < PayloadCount ? Payload[i * PayloadStride + field] : 0f; }

        public static AcAiSpline Read(string path)
        {
            using (var r = AcBinary.OpenFile(path, 1 << 16))
            {
                var s = Read(r);
                s.Path = path;
                return s;
            }
        }

        public static AcAiSpline Read(AcBinary r)
        {
            var s = new AcAiSpline();
            if (r.Length < 8) return s;
            s.Version = r.ReadInt32();
            int count = r.ReadInt32();
            if (s.Version != 7 || count <= 0 || r.Length < 16) return s; // drift: v3, 0 points
            s.LapTime = r.ReadInt32();
            s.SampleCount = r.ReadInt32();
            if ((long)count * 20 > r.Remaining) throw new InvalidDataException("ai: " + count + " points do not fit the file");
            s.Count = count;
            s.Pos = new float[count * 3];
            s.Length = new float[count];
            s.Id = new int[count];
            for (int i = 0; i < count; i++)
            {
                s.Pos[i * 3] = r.ReadSingle(); s.Pos[i * 3 + 1] = r.ReadSingle(); s.Pos[i * 3 + 2] = r.ReadSingle();
                s.Length[i] = r.ReadSingle(); s.Id[i] = r.ReadInt32();
            }
            if (r.Remaining >= 4)
            {
                int pc = r.ReadInt32();
                if (pc < 0 || (long)pc * PayloadStride * 4 > r.Remaining) throw new InvalidDataException("ai: payload count " + pc);
                s.PayloadCount = pc;
                s.Payload = new float[pc * PayloadStride];
                r.ReadFloats(s.Payload, 0, s.Payload.Length);
            }
            if (r.Remaining >= 4 && r.ReadInt32() != 0)
            {
                s.HasFileGrid = true;
                for (int k = 0; k < 3; k++) s.FileGridMax[k] = r.ReadSingle();
                for (int k = 0; k < 3; k++) s.FileGridMin[k] = r.ReadSingle();
                s.FileGridNeighbours = r.ReadInt32();
                s.FileGridStep = r.ReadSingle();
                s.FileGridNx = r.ReadInt32();
                if (s.FileGridNx < 0 || s.FileGridNx > 100000) throw new InvalidDataException("ai: grid nx " + s.FileGridNx);
                var cells = new List<int[]>();
                for (int x = 0; x < s.FileGridNx; x++)
                {
                    int nz = r.ReadInt32();
                    if (nz < 0 || nz > 100000) throw new InvalidDataException("ai: grid nz " + nz);
                    if (x == 0) s.FileGridNz = nz;
                    for (int z = 0; z < nz; z++)
                    {
                        int n = r.ReadInt32();
                        if (n < 0 || (long)n * 4 > r.Remaining) throw new InvalidDataException("ai: grid cell size " + n);
                        var a = new int[n];
                        r.ReadInts(a, 0, n);
                        cells.Add(a);
                    }
                }
                s.FileCells = cells.ToArray();
            }
            s.TrailingBytes = r.Remaining;
            s.Prepare();
            return s;
        }

        /// <summary>Builds from raw points (tests / other sources). Pos = xyz per point, AC space.</summary>
        public static AcAiSpline FromPoints(float[] pos)
        {
            var s = new AcAiSpline { Version = 7, Count = pos.Length / 3, Pos = pos };
            s.Length = new float[s.Count]; s.Id = new int[s.Count];
            for (int i = 1; i < s.Count; i++)
            {
                float dx = s.X(i) - s.X(i - 1), dy = s.Y(i) - s.Y(i - 1), dz = s.Z(i) - s.Z(i - 1);
                s.Length[i] = s.Length[i - 1] + (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                s.Id[i] = i;
            }
            s.Prepare();
            return s;
        }

        void Prepare()
        {
            if (Count < 2) return;
            float gx = X(0) - X(Count - 1), gy = Y(0) - Y(Count - 1), gz = Z(0) - Z(Count - 1);
            float gap = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz);
            float avg = Total / (Count - 1);
            // closed loop when the last point comes back near the first (monza: 1.55 m); pit lanes / drag / A2B are open
            IsClosed = gap <= Math.Max(20f, avg * 10f); // ours
            LapLength = IsClosed ? Total + gap : Total;
            if (LapLength <= 0f) LapLength = 1f;
            BuildGrid();
        }

        void BuildGrid()
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < Count; i++)
            {
                float x = X(i), z = Z(i);
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            _step = GridStep;
            _minX = minX; _minZ = minZ;
            _nx = Math.Max(1, (int)((maxX - minX) / _step) + 1);
            _nz = Math.Max(1, (int)((maxZ - minZ) / _step) + 1);
            var counts = new int[_nx * _nz + 1];
            var cellOf = new int[Count];
            for (int i = 0; i < Count; i++)
            {
                int cx = Clamp((int)((X(i) - _minX) / _step), _nx), cz = Clamp((int)((Z(i) - _minZ) / _step), _nz);
                int c = cx * _nz + cz;
                cellOf[i] = c;
                counts[c + 1]++;
            }
            for (int c = 0; c < _nx * _nz; c++) counts[c + 1] += counts[c];
            _cellStart = counts;
            _cellIds = new int[Count];
            var fill = new int[_nx * _nz];
            for (int i = 0; i < Count; i++) { int c = cellOf[i]; _cellIds[_cellStart[c] + fill[c]++] = i; }
        }

        static int Clamp(int v, int n) { return v < 0 ? 0 : v >= n ? n - 1 : v; }

        /// <summary>
        /// Exact nearest point to (x, y, z) in AC space by 3D distance (vertical weighted 4x so a bridge above does not win).
        /// -1 for an empty spline. Ring search over our grid; no allocation.
        /// </summary>
        public int Nearest(float x, float y, float z)
        {
            if (Count == 0) return -1;
            if (_cellStart == null) return NearestBrute(x, y, z);
            int cx = (int)Math.Floor((x - _minX) / _step), cz = (int)Math.Floor((z - _minZ) / _step);
            int best = -1; float bd = float.MaxValue;
            int maxRing = Math.Max(_nx, _nz) + Math.Max(Math.Abs(cx), Math.Abs(cz)) + 1;
            for (int ring = 0; ring <= maxRing; ring++)
            {
                // lower bound of the horizontal distance to any cell in this ring
                if (best >= 0)
                {
                    float lb = (ring - 1) * _step;
                    if (lb > 0 && lb * lb > bd) break;
                }
                for (int ix = cx - ring; ix <= cx + ring; ix++)
                {
                    if (ix < 0 || ix >= _nx) continue;
                    bool edgeX = ix == cx - ring || ix == cx + ring;
                    for (int iz = cz - ring; iz <= cz + ring; iz++)
                    {
                        if (!edgeX && iz != cz - ring && iz != cz + ring) continue;
                        if (iz < 0 || iz >= _nz) continue;
                        int c = ix * _nz + iz;
                        for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++) Test3(_cellIds[k], x, y, z, ref best, ref bd);
                    }
                }
            }
            return best;
        }

        int NearestBrute(float x, float y, float z)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < Count; i++) Test3(i, x, y, z, ref best, ref bd);
            return best;
        }

        void Test3(int i, float x, float y, float z, ref int best, ref float bd)
        {
            float dx = Pos[i * 3] - x, dy = Pos[i * 3 + 1] - y, dz = Pos[i * 3 + 2] - z;
            float d = dx * dx + dz * dz + 4f * dy * dy; // ours: vertical weight
            if (d < bd) { bd = d; best = i; }
        }

        /// <summary>Nearest point within +-<paramref name="window"/> points of <paramref name="hint"/> (wraps around the loop).</summary>
        public int NearestNear(float x, float y, float z, int hint, int window)
        {
            if (Count == 0) return -1;
            if (hint < 0 || hint >= Count || window * 2 + 1 >= Count) return Nearest(x, y, z);
            int best = -1; float bd = float.MaxValue;
            for (int d = -window; d <= window; d++)
            {
                int i = hint + d;
                if (i < 0) { if (!IsClosed) continue; i += Count; }
                else if (i >= Count) { if (!IsClosed) continue; i -= Count; }
                Test3(i, x, y, z, ref best, ref bd);
            }
            return best;
        }

        /// <summary>
        /// Lap progress 0..1 (0 = AC_TIME_0 line) of an AC-space position, projected on the segment around point <paramref name="i"/>.
        /// </summary>
        public float ProgressAt(float x, float z, int i)
        {
            if (Count < 2 || i < 0) return 0f;
            if (i >= Count) i = Count - 1;
            if (!IsClosed && i == Count - 1) i = Count - 2; // open spline: no closing segment
            // choose the segment (i-1 -> i) or (i -> i+1) the point projects onto
            int j = i + 1 >= Count ? 0 : i + 1;
            float t = SegT(i, j, x, z);
            if (t <= 0f)
            {
                int h = i - 1 < 0 ? (IsClosed ? Count - 1 : -1) : i - 1;
                float th = h >= 0 ? SegT(h, i, x, z) : 0f;
                if (h >= 0 && th < 1f && th > 0f) { i = h; j = h + 1 >= Count ? 0 : h + 1; t = th; }
                else t = 0f;
            }
            if (t > 1f) t = 1f;
            float segLen = j == 0 ? LapLength - Total : Length[j] - Length[i];
            float p = (Length[i] + t * segLen) / LapLength;
            if (IsClosed)
            {
                if (p >= 1f) p -= 1f;
                if (p < 0f) p += 1f;
            }
            else if (p > 1f) p = 1f;
            return p;
        }

        float SegT(int i, int j, float x, float z)
        {
            float ax = Pos[i * 3], az = Pos[i * 3 + 2], vx = Pos[j * 3] - ax, vz = Pos[j * 3 + 2] - az, l2 = vx * vx + vz * vz;
            return l2 > 1e-9f ? ((x - ax) * vx + (z - az) * vz) / l2 : 0f;
        }

        /// <summary>
        /// Fast progress lookup for a moving car (AC space). <paramref name="hint"/> = the point index found last frame (-1 to
        /// start: exact grid search); updated in place. A window of +-<paramref name="window"/> points is searched; when the result is
        /// farther than <paramref name="refindDistance"/> m (teleport / reset) the exact grid search runs instead.
        /// </summary>
        public float Progress(float x, float y, float z, ref int hint, int window = 40, float refindDistance = 25f) // ours: window, distance
        {
            if (Count < 2) { hint = -1; return 0f; }
            int i = hint >= 0 && hint < Count ? NearestNear(x, y, z, hint, window) : Nearest(x, y, z);
            if (i >= 0 && hint >= 0)
            {
                float dx = X(i) - x, dz = Z(i) - z;
                if (dx * dx + dz * dz > refindDistance * refindDistance) i = Nearest(x, y, z);
            }
            hint = i;
            return ProgressAt(x, z, i);
        }

        /// <summary>Progress 0..1 of point <paramref name="i"/>.</summary>
        public float PointProgress(int i) { return Count < 2 || i < 0 ? 0f : Length[Math.Min(i, Count - 1)] / LapLength; }

        /// <summary>Signed lateral offset (m) of an AC-space point from the line at point i: + = AC +X side (car LEFT of travel).</summary>
        public float LateralOffset(float x, float z, int i)
        {
            if (Count < 2 || i < 0) return 0f;
            int j = i + 1 >= Count ? 0 : i + 1;
            float fx = X(j) - X(i), fz = Z(j) - Z(i);
            float l = (float)Math.Sqrt(fx * fx + fz * fz);
            if (l < 1e-6f) return 0f;
            fx /= l; fz /= l;
            // left of travel in AC (right-handed, Y up): left = up x forward = (fz, 0, -fx)
            return (x - X(i)) * fz + (z - Z(i)) * -fx;
        }
    }
}
