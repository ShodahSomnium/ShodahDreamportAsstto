using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// One kn5 mesh made ready on a worker thread (Unity-free): arrays already in Unity conventions (X mirrored, winding flipped,
    /// uv = (u, v_stored)), the vertex stream in the exact layout the Unity side declares, bounds, and what to do with it.
    /// </summary>
    public sealed class AcMeshPrep
    {
        public Kn5Node Node;
        /// <summary>Index of the kn5 in the layout (track) or 0 (single model).</summary>
        public int File;
        public Kn5Material Material;
        /// <summary>Draw it (renderable, not a marker cube, not hidden).</summary>
        public bool Render;
        /// <summary>Build a MeshCollider (physics mesh: digit-prefixed name, or every mesh with CollideRendered).</summary>
        public bool Collide;
        public bool Wall;
        public AcSurface Surface;
        public bool Skinned, HasTangents;
        public int VertexCount, IndexCount;
        /// <summary>Render stream: position3 normal3 [tangent4] uv2 (stride <see cref="Stride"/> floats). Null when not rendered.</summary>
        public float[] Vertices;
        public int Stride;
        /// <summary>Triangle list, winding flipped for Unity.</summary>
        public ushort[] Indices;
        /// <summary>Collider positions (x y z per vertex), only with <see cref="Collide"/>.</summary>
        public float[] ColliderPositions;
        /// <summary>Skinned: 4 weights + 4 bone indices per vertex (-1 = unused).</summary>
        public float[] SkinWeights;
        public int[] SkinBones;
        /// <summary>Bounds in the parent dummy's (Unity) space.</summary>
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public long Bytes
        {
            get
            {
                long b = 0;
                if (Vertices != null) b += Vertices.LongLength * 4;
                if (Indices != null) b += Indices.LongLength * 2;
                if (ColliderPositions != null) b += ColliderPositions.LongLength * 4;
                return b;
            }
        }
    }

    /// <summary>
    /// kn5 vertex block (stride 11: pos3 normal3 uv2 tangent3; skinned 19: + weights4 + boneIndices4) -> Unity arrays. Positions/normals:
    /// (-x, y, z); triangles (i0, i2, i1); uv as stored (DDS raw upload, Repeat). Tangents are NOT taken from the file (they point along
    /// -dP/du for most triangles, research 1.5): when the material has a normal map they are recomputed here from the mirrored positions and
    /// the uvs (Lengyel), w = handedness so that cross(N, T) * w = dP/dv. AC's normal maps are DirectX style (green = +v downwards in the
    /// image), and with uv = (u, v_stored) dP/dv IS that direction, so the shaders use n = T*x + B*y + N*z unflipped.
    /// </summary>
    public static class AcMeshPrepare
    {
        /// <summary>Fills <paramref name="p"/> from raw (unconverted) kn5 arrays. <paramref name="raw"/>/<paramref name="rawIdx"/> may be longer than needed.</summary>
        public static void Fill(AcMeshPrep p, Kn5Mesh m, float[] raw, ushort[] rawIdx, bool tangents)
        {
            int vc = m.VertexCount, ic = m.IndexCount - m.IndexCount % 3, st = m.Stride;
            p.VertexCount = vc; p.IndexCount = ic; p.Skinned = m.Skinned;
            var idx = new ushort[ic];
            for (int t = 0; t < ic; t += 3) { idx[t] = rawIdx[t]; idx[t + 1] = rawIdx[t + 2]; idx[t + 2] = rawIdx[t + 1]; }
            // guard against indices past the vertex count (corrupt data would crash the upload)
            for (int t = 0; t < ic; t++) if (idx[t] >= vc) idx[t] = 0;
            p.Indices = idx;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0, o = 0; i < vc; i++, o += st)
            {
                float x = -raw[o], y = raw[o + 1], z = raw[o + 2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            if (vc == 0) { minX = minY = minZ = maxX = maxY = maxZ = 0f; }
            p.MinX = minX; p.MinY = minY; p.MinZ = minZ; p.MaxX = maxX; p.MaxY = maxY; p.MaxZ = maxZ;

            if (p.Collide)
            {
                var cp = new float[vc * 3];
                for (int i = 0, o = 0; i < vc; i++, o += st) { cp[i * 3] = -raw[o]; cp[i * 3 + 1] = raw[o + 1]; cp[i * 3 + 2] = raw[o + 2]; }
                p.ColliderPositions = cp;
            }

            if (!p.Render) return;
            bool tan = tangents && !m.Skinned;
            int ds = tan ? 12 : 8;
            var v = new float[vc * ds];
            for (int i = 0, o = 0, d = 0; i < vc; i++, o += st, d += ds)
            {
                v[d] = -raw[o]; v[d + 1] = raw[o + 1]; v[d + 2] = raw[o + 2];
                float nx = -raw[o + 3], ny = raw[o + 4], nz = raw[o + 5];
                float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl > 1e-8f) { nx /= nl; ny /= nl; nz /= nl; } else { nx = 0f; ny = 1f; nz = 0f; }
                v[d + 3] = nx; v[d + 4] = ny; v[d + 5] = nz;
                int uvAt = tan ? 10 : 6;
                v[d + uvAt] = raw[o + 6]; v[d + uvAt + 1] = raw[o + 7];
            }
            if (tan) Tangents(v, vc, idx, ic);
            p.Vertices = v; p.Stride = ds; p.HasTangents = tan;

            if (m.Skinned)
            {
                var w = new float[vc * 4];
                var b = new int[vc * 4];
                for (int i = 0, o = 0; i < vc; i++, o += st)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float bi = raw[o + 15 + k], wt = raw[o + 11 + k];
                        b[i * 4 + k] = bi < 0f ? -1 : (int)(bi + 0.5f);
                        w[i * 4 + k] = bi < 0f ? 0f : wt;
                    }
                }
                p.SkinWeights = w; p.SkinBones = b;
            }
        }

        /// <summary>Per-vertex tangents (Lengyel) into the tangent slot (floats 6..9) of a stride-12 stream (pos 0-2, normal 3-5, uv 10-11).</summary>
        static void Tangents(float[] v, int vc, ushort[] idx, int ic)
        {
            var t1 = new float[vc * 3];
            var t2 = new float[vc * 3];
            for (int t = 0; t < ic; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                int A = a * 12, B = b * 12, C = c * 12;
                float e1x = v[B] - v[A], e1y = v[B + 1] - v[A + 1], e1z = v[B + 2] - v[A + 2];
                float e2x = v[C] - v[A], e2y = v[C + 1] - v[A + 1], e2z = v[C + 2] - v[A + 2];
                float du1 = v[B + 10] - v[A + 10], dv1 = v[B + 11] - v[A + 11];
                float du2 = v[C + 10] - v[A + 10], dv2 = v[C + 11] - v[A + 11];
                float det = du1 * dv2 - du2 * dv1;
                if (det > -1e-12f && det < 1e-12f) continue;
                float r = 1f / det;
                float sx = (e1x * dv2 - e2x * dv1) * r, sy = (e1y * dv2 - e2y * dv1) * r, sz = (e1z * dv2 - e2z * dv1) * r;
                float tx = (e2x * du1 - e1x * du2) * r, ty = (e2y * du1 - e1y * du2) * r, tz = (e2z * du1 - e1z * du2) * r;
                Add(t1, a, sx, sy, sz); Add(t1, b, sx, sy, sz); Add(t1, c, sx, sy, sz);
                Add(t2, a, tx, ty, tz); Add(t2, b, tx, ty, tz); Add(t2, c, tx, ty, tz);
            }
            for (int i = 0; i < vc; i++)
            {
                int d = i * 12;
                float nx = v[d + 3], ny = v[d + 4], nz = v[d + 5];
                float sx = t1[i * 3], sy = t1[i * 3 + 1], sz = t1[i * 3 + 2];
                float dot = nx * sx + ny * sy + nz * sz;
                sx -= nx * dot; sy -= ny * dot; sz -= nz * dot;
                float l = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
                if (l < 1e-8f)
                {
                    // no uv gradient: any vector perpendicular to the normal
                    if (Math.Abs(nx) < 0.9f) { sx = 0f; sy = nz; sz = -ny; } else { sx = -nz; sy = 0f; sz = nx; }
                    l = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
                    if (l < 1e-8f) { sx = 1f; sy = 0f; sz = 0f; l = 1f; }
                }
                sx /= l; sy /= l; sz /= l;
                // cross(N, T) (Unity's Vector3.Cross formula) against dP/dv decides w
                float cx = ny * sz - nz * sy, cy = nz * sx - nx * sz, cz = nx * sy - ny * sx;
                float w = cx * t2[i * 3] + cy * t2[i * 3 + 1] + cz * t2[i * 3 + 2] < 0f ? -1f : 1f;
                v[d + 6] = sx; v[d + 7] = sy; v[d + 8] = sz; v[d + 9] = w;
            }
        }

        static void Add(float[] a, int i, float x, float y, float z) { a[i * 3] += x; a[i * 3 + 1] += y; a[i * 3 + 2] += z; }
    }
}
