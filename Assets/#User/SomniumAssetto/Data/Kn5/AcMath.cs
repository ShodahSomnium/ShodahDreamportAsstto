using System;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>A plain 3-vector in whatever space the caller says (Unity-free; the Unity side converts to Vector3).</summary>
    public struct AcVec3
    {
        public float X, Y, Z;
        public AcVec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static AcVec3 operator +(AcVec3 a, AcVec3 b) { return new AcVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static AcVec3 operator -(AcVec3 a, AcVec3 b) { return new AcVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static AcVec3 operator *(AcVec3 a, float s) { return new AcVec3(a.X * s, a.Y * s, a.Z * s); }
        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public AcVec3 Normalized { get { float l = Length; return l > 1e-12f ? new AcVec3(X / l, Y / l, Z / l) : new AcVec3(0, 0, 0); } }
        public static float Dot(AcVec3 a, AcVec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static AcVec3 Cross(AcVec3 a, AcVec3 b) { return new AcVec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }

        /// <summary>AC (right-handed, +X = car left) -> Unity (left-handed, +X = right): negate X. Same formula both ways.</summary>
        public AcVec3 MirrorX { get { return new AcVec3(-X, Y, Z); } }

        public static AcVec3 FromArray(float[] a, int o = 0)
        {
            if (a == null || a.Length < o + 3) return new AcVec3(a != null && a.Length > o ? a[o] : 0f, a != null && a.Length > o + 1 ? a[o + 1] : 0f, 0f);
            return new AcVec3(a[o], a[o + 1], a[o + 2]);
        }

        public override string ToString() { return "(" + X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," + Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," + Z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")"; }
    }

    /// <summary>Quaternion (Hamilton, x y z w) as stored in ksanim v2.</summary>
    public struct AcQuat
    {
        public float X, Y, Z, W;
        public AcQuat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static readonly AcQuat Identity = new AcQuat(0, 0, 0, 1);

        /// <summary>
        /// AC quaternion -> Unity quaternion components after the X mirror: (x, -y, -z, w) [VERIFIED convention, ac_car_formats 1.5].
        /// </summary>
        public AcQuat MirrorX { get { return new AcQuat(X, -Y, -Z, W); } }
    }

    /// <summary>
    /// A 4x4 matrix exactly as AC stores it: 16 floats ROW-MAJOR with the ROW-VECTOR convention (p' = p * M), translation in
    /// elements [12],[13],[14], [15] = 1. Rows 0/1/2 are the local X/Y/Z axes expressed in the parent space (AC: X = car LEFT,
    /// Y = up, Z = forward). World = local * parentLocal * ... * rootLocal.
    ///
    /// <b>AC -> Unity conversion</b> (verified on cars and tracks, see ac_car_formats.md 1.5 / C1 and ac_track_formats.md 4.2):
    /// <list type="bullet">
    /// <item>positions, normals, tangents: (x, y, z) -> (-x, y, z) (<see cref="AcVec3.MirrorX"/>);</item>
    /// <item>triangles: swap indices 1 and 2 of every triangle (the X mirror flips the winding);</item>
    /// <item>matrices: U = S * transpose(M) * S with S = diag(-1, 1, 1, 1) (<see cref="ToUnity"/>): Unity column-vector matrix whose
    /// column 3 is the localPosition (-tx, ty, tz);</item>
    /// <item>quaternions: (x, y, z, w) -> (x, -y, -z, w);</item>
    /// <item>spawn markers: use only the horizontal part of the node's world +Z (<see cref="HeadingDeg"/>), positions raycast down
    /// (markers float 0-7.7 m above the road).</item>
    /// </list>
    /// </summary>
    public struct AcMat4
    {
        public float M0, M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11, M12, M13, M14, M15;

        public static readonly AcMat4 Identity = FromRowMajor(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }, 0);

        public static AcMat4 FromRowMajor(float[] m, int o = 0)
        {
            var r = new AcMat4();
            if (m == null || m.Length < o + 16) return Identity;
            r.M0 = m[o]; r.M1 = m[o + 1]; r.M2 = m[o + 2]; r.M3 = m[o + 3];
            r.M4 = m[o + 4]; r.M5 = m[o + 5]; r.M6 = m[o + 6]; r.M7 = m[o + 7];
            r.M8 = m[o + 8]; r.M9 = m[o + 9]; r.M10 = m[o + 10]; r.M11 = m[o + 11];
            r.M12 = m[o + 12]; r.M13 = m[o + 13]; r.M14 = m[o + 14]; r.M15 = m[o + 15];
            return r;
        }

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return M0; case 1: return M1; case 2: return M2; case 3: return M3;
                    case 4: return M4; case 5: return M5; case 6: return M6; case 7: return M7;
                    case 8: return M8; case 9: return M9; case 10: return M10; case 11: return M11;
                    case 12: return M12; case 13: return M13; case 14: return M14; default: return M15;
                }
            }
        }

        public float[] ToArray()
        {
            return new[] { M0, M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11, M12, M13, M14, M15 };
        }

        /// <summary>a * b (row-vector convention: apply a first, then b). World = Mul(local, parentWorld).</summary>
        public static AcMat4 Mul(AcMat4 a, AcMat4 b)
        {
            var r = new AcMat4();
            r.M0 = a.M0 * b.M0 + a.M1 * b.M4 + a.M2 * b.M8 + a.M3 * b.M12;
            r.M1 = a.M0 * b.M1 + a.M1 * b.M5 + a.M2 * b.M9 + a.M3 * b.M13;
            r.M2 = a.M0 * b.M2 + a.M1 * b.M6 + a.M2 * b.M10 + a.M3 * b.M14;
            r.M3 = a.M0 * b.M3 + a.M1 * b.M7 + a.M2 * b.M11 + a.M3 * b.M15;
            r.M4 = a.M4 * b.M0 + a.M5 * b.M4 + a.M6 * b.M8 + a.M7 * b.M12;
            r.M5 = a.M4 * b.M1 + a.M5 * b.M5 + a.M6 * b.M9 + a.M7 * b.M13;
            r.M6 = a.M4 * b.M2 + a.M5 * b.M6 + a.M6 * b.M10 + a.M7 * b.M14;
            r.M7 = a.M4 * b.M3 + a.M5 * b.M7 + a.M6 * b.M11 + a.M7 * b.M15;
            r.M8 = a.M8 * b.M0 + a.M9 * b.M4 + a.M10 * b.M8 + a.M11 * b.M12;
            r.M9 = a.M8 * b.M1 + a.M9 * b.M5 + a.M10 * b.M9 + a.M11 * b.M13;
            r.M10 = a.M8 * b.M2 + a.M9 * b.M6 + a.M10 * b.M10 + a.M11 * b.M14;
            r.M11 = a.M8 * b.M3 + a.M9 * b.M7 + a.M10 * b.M11 + a.M11 * b.M15;
            r.M12 = a.M12 * b.M0 + a.M13 * b.M4 + a.M14 * b.M8 + a.M15 * b.M12;
            r.M13 = a.M12 * b.M1 + a.M13 * b.M5 + a.M14 * b.M9 + a.M15 * b.M13;
            r.M14 = a.M12 * b.M2 + a.M13 * b.M6 + a.M14 * b.M10 + a.M15 * b.M14;
            r.M15 = a.M12 * b.M3 + a.M13 * b.M7 + a.M14 * b.M11 + a.M15 * b.M15;
            return r;
        }

        public AcVec3 Translation { get { return new AcVec3(M12, M13, M14); } }
        public AcVec3 AxisX { get { return new AcVec3(M0, M1, M2); } }
        public AcVec3 AxisY { get { return new AcVec3(M4, M5, M6); } }
        public AcVec3 AxisZ { get { return new AcVec3(M8, M9, M10); } }

        /// <summary>Transforms a point (row vector * M).</summary>
        public AcVec3 TransformPoint(AcVec3 p)
        {
            return new AcVec3(p.X * M0 + p.Y * M4 + p.Z * M8 + M12, p.X * M1 + p.Y * M5 + p.Z * M9 + M13, p.X * M2 + p.Y * M6 + p.Z * M10 + M14);
        }

        public AcVec3 TransformDirection(AcVec3 d)
        {
            return new AcVec3(d.X * M0 + d.Y * M4 + d.Z * M8, d.X * M1 + d.Y * M5 + d.Z * M9, d.X * M2 + d.Y * M6 + d.Z * M10);
        }

        /// <summary>Determinant of the 3x3 rotation/scale part (&lt; 0 = mirrored: bake into vertices instead of TRS).</summary>
        public float Determinant3
        {
            get { return M0 * (M5 * M10 - M6 * M9) - M1 * (M4 * M10 - M6 * M8) + M2 * (M4 * M9 - M5 * M8); }
        }

        /// <summary>
        /// The Unity matrix (column-vector convention) as 16 floats in Unity's <c>Matrix4x4[row, col]</c> order, index row*4+col:
        /// U = S * transpose(M) * S, S = diag(-1,1,1,1). Assign with <c>u[r, c] = a[r * 4 + c]</c>; then
        /// localPosition = column 3 = (a[3], a[7], a[11]) = (-tx, ty, tz), rotation = u.rotation, scale = u.lossyScale.
        /// </summary>
        public float[] ToUnity()
        {
            var u = new float[16];
            // transpose
            u[0] = M0; u[1] = M4; u[2] = M8; u[3] = M12;
            u[4] = M1; u[5] = M5; u[6] = M9; u[7] = M13;
            u[8] = M2; u[9] = M6; u[10] = M10; u[11] = M14;
            u[12] = M3; u[13] = M7; u[14] = M11; u[15] = M15;
            // S*U*S: negate entries where exactly one of (row, col) is 0
            u[1] = -u[1]; u[2] = -u[2]; u[3] = -u[3];
            u[4] = -u[4]; u[8] = -u[8]; u[12] = -u[12];
            return u;
        }

        /// <summary>Unity localPosition of this AC local matrix: (-tx, ty, tz).</summary>
        public AcVec3 UnityPosition { get { return new AcVec3(-M12, M13, M14); } }

        /// <summary>
        /// Unity rotation quaternion (x, y, z, w) of this AC matrix after the X mirror (scale removed by normalising the axes).
        /// </summary>
        public AcQuat UnityRotation
        {
            get
            {
                // Unity basis vectors (columns of U): X' = mirror(rowX) with sign: S*R^T*S columns
                var x = new AcVec3(M0, -M1, -M2).Normalized;   // column 0 of U: (u00,u10,u20) = (M0,-M1,-M2)
                var y = new AcVec3(-M4, M5, M6).Normalized;    // column 1: (u01,u11,u21) = (-M4, M5, M6)
                var z = new AcVec3(-M8, M9, M10).Normalized;   // column 2: (-M8, M9, M10)
                return FromBasis(x, y, z);
            }
        }

        /// <summary>Quaternion from an orthonormal basis given as columns (x, y, z), column-vector convention.</summary>
        public static AcQuat FromBasis(AcVec3 x, AcVec3 y, AcVec3 z)
        {
            float m00 = x.X, m10 = x.Y, m20 = x.Z, m01 = y.X, m11 = y.Y, m21 = y.Z, m02 = z.X, m12 = z.Y, m22 = z.Z;
            float tr = m00 + m11 + m22;
            float qx, qy, qz, qw;
            if (tr > 0f)
            {
                float s = (float)Math.Sqrt(tr + 1f) * 2f;
                qw = 0.25f * s; qx = (m21 - m12) / s; qy = (m02 - m20) / s; qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                qw = (m21 - m12) / s; qx = 0.25f * s; qy = (m01 + m10) / s; qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                qw = (m02 - m20) / s; qx = (m01 + m10) / s; qy = 0.25f * s; qz = (m12 + m21) / s;
            }
            else
            {
                float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                qw = (m10 - m01) / s; qx = (m02 + m20) / s; qy = (m12 + m21) / s; qz = 0.25f * s;
            }
            return new AcQuat(qx, qy, qz, qw);
        }

        /// <summary>Axis lengths (scale) of the local axes.</summary>
        public AcVec3 Scale { get { return new AcVec3(AxisX.Length, AxisY.Length, AxisZ.Length); } }

        /// <summary>
        /// Heading of the node's +Z projected on the ground, in degrees, in UNITY yaw terms (0 = +Z, positive = towards Unity +X,
        /// i.e. towards AC -X). Works for rolled markers (silverstone1967 AC_START_n have X = world up). NaN when +Z is vertical.
        /// </summary>
        public float HeadingDeg
        {
            get
            {
                float zx = -M8, zz = M10; // Unity-space horizontal Z axis
                if (zx * zx + zz * zz < 1e-10f) return float.NaN;
                return (float)(Math.Atan2(zx, zz) * 180.0 / Math.PI);
            }
        }

        /// <summary>Builds the AC row-vector matrix of a ksanim v2 frame (Hamilton quaternion, translation, scale).</summary>
        public static AcMat4 FromTrs(AcQuat q, AcVec3 t, AcVec3 s)
        {
            float x = q.X, y = q.Y, z = q.Z, w = q.W;
            float n = x * x + y * y + z * z + w * w;
            if (n > 1e-12f && Math.Abs(n - 1f) > 1e-6f) { float inv = 1f / (float)Math.Sqrt(n); x *= inv; y *= inv; z *= inv; w *= inv; }
            // column-vector rotation matrix R; row-vector form is R^T (rows = rotated axes)
            float r00 = 1 - 2 * (y * y + z * z), r01 = 2 * (x * y - z * w), r02 = 2 * (x * z + y * w);
            float r10 = 2 * (x * y + z * w), r11 = 1 - 2 * (x * x + z * z), r12 = 2 * (y * z - x * w);
            float r20 = 2 * (x * z - y * w), r21 = 2 * (y * z + x * w), r22 = 1 - 2 * (x * x + y * y);
            var m = new AcMat4();
            m.M0 = r00 * s.X; m.M1 = r10 * s.X; m.M2 = r20 * s.X; m.M3 = 0;
            m.M4 = r01 * s.Y; m.M5 = r11 * s.Y; m.M6 = r21 * s.Y; m.M7 = 0;
            m.M8 = r02 * s.Z; m.M9 = r12 * s.Z; m.M10 = r22 * s.Z; m.M11 = 0;
            m.M12 = t.X; m.M13 = t.Y; m.M14 = t.Z; m.M15 = 1;
            return m;
        }

        /// <summary>Inverse of an affine matrix (rotation/scale + translation).</summary>
        public AcMat4 InverseAffine()
        {
            float det = Determinant3;
            if (Math.Abs(det) < 1e-20f) return Identity;
            float id = 1f / det;
            var r = new AcMat4();
            r.M0 = (M5 * M10 - M6 * M9) * id; r.M1 = (M2 * M9 - M1 * M10) * id; r.M2 = (M1 * M6 - M2 * M5) * id;
            r.M4 = (M6 * M8 - M4 * M10) * id; r.M5 = (M0 * M10 - M2 * M8) * id; r.M6 = (M2 * M4 - M0 * M6) * id;
            r.M8 = (M4 * M9 - M5 * M8) * id; r.M9 = (M1 * M8 - M0 * M9) * id; r.M10 = (M0 * M5 - M1 * M4) * id;
            r.M12 = -(M12 * r.M0 + M13 * r.M4 + M14 * r.M8);
            r.M13 = -(M12 * r.M1 + M13 * r.M5 + M14 * r.M9);
            r.M14 = -(M12 * r.M2 + M13 * r.M6 + M14 * r.M10);
            r.M15 = 1;
            return r;
        }
    }

    /// <summary>Static helpers for converting kn5 vertex/index data to Unity conventions in place (pure math, worker-thread safe).</summary>
    public static class AcConvert
    {
        /// <summary>
        /// Negates X of position, normal and tangent of every vertex in an interleaved kn5 block (stride 11 static / 19 skinned:
        /// pos 0-2, normal 3-5, uv 6-7, tangent 8-10). UVs are left as stored (uv = (u, v_stored) with DDS raw upload, Repeat wrap).
        /// </summary>
        public static void MirrorVerticesX(float[] v, int vertexCount, int stride)
        {
            for (int i = 0, o = 0; i < vertexCount; i++, o += stride)
            {
                v[o] = -v[o];
                v[o + 3] = -v[o + 3];
                v[o + 8] = -v[o + 8];
            }
        }

        /// <summary>Swaps indices 1 and 2 of every triangle (winding fix after the X mirror).</summary>
        public static void FlipWinding(ushort[] idx, int indexCount)
        {
            for (int t = 0; t + 2 < indexCount; t += 3) { ushort a = idx[t + 1]; idx[t + 1] = idx[t + 2]; idx[t + 2] = a; }
        }
    }
}
