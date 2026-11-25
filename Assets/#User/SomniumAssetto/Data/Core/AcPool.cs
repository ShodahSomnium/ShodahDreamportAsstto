using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// A tiny thread-safe array pool (Unity-free; System.Buffers.ArrayPool is not guaranteed on the upload server).
    /// Arrays are bucketed by power-of-two capacity, so a rented array is usually LONGER than asked: always carry the real
    /// element count next to it (the kn5 reader does: <see cref="Kn5Mesh.VertexCount"/>, <see cref="Kn5Mesh.IndexCount"/>).
    /// Used for the bulk vertex/index blocks of big track kn5 files so loading layout after layout does not churn the GC.
    /// </summary>
    public static class AcPool<T>
    {
        const int MinBits = 10;         // ours: 1024 elements smallest bucket
        const int MaxBits = 22;         // ours: 4M elements largest pooled array (biggest track mesh ~22k verts x 11 floats)
        const int PerBucket = 32;       // ours: arrays kept per size class
        static readonly Stack<T[]>[] Buckets = new Stack<T[]>[MaxBits + 1];
        static readonly object Lock = new object();

        static int BitsFor(int n)
        {
            int b = MinBits;
            while (b < 31 && (1 << b) < n) b++;
            return b;
        }

        /// <summary>An array with Length &gt;= <paramref name="minLength"/> (contents undefined).</summary>
        public static T[] Rent(int minLength)
        {
            if (minLength <= 0) return new T[0];
            int bits = BitsFor(minLength);
            if (bits > MaxBits) return new T[minLength];
            lock (Lock)
            {
                var st = Buckets[bits];
                if (st != null && st.Count > 0) return st.Pop();
            }
            return new T[1 << bits];
        }

        /// <summary>Gives an array back. Arrays not created by <see cref="Rent"/> (non power-of-two length) are dropped.</summary>
        public static void Return(T[] a)
        {
            if (a == null || a.Length == 0) return;
            int len = a.Length;
            if ((len & (len - 1)) != 0) return;
            int bits = BitsFor(len);
            if (bits < MinBits || bits > MaxBits) return;
            lock (Lock)
            {
                var st = Buckets[bits];
                if (st == null) { st = new Stack<T[]>(); Buckets[bits] = st; }
                if (st.Count < PerBucket) st.Push(a);
            }
        }

        /// <summary>Drops every pooled array (e.g. after a track unload).</summary>
        public static void Clear()
        {
            lock (Lock) { for (int i = 0; i < Buckets.Length; i++) Buckets[i] = null; }
        }
    }
}
