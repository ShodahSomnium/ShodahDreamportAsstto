using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [C, Car model] The car body hull from collider.kn5 (MODEL space, ac_car_formats.md C1): one convex MeshCollider per mesh on
    /// <see cref="AcLayers.Car"/>, under the model root, so P's Rigidbody (on the car root above) takes them as its compound body.
    /// Sanity check (C1): when the hull's centre is more than 0.5 m (horizontal) from the LOD_0 model centre the file is broken
    /// (ks_ford_mustang_2015: 5.3 m off - the only one of 86, harness <c>carparts</c>) and the LOD_0 bounding box is used instead.
    /// excludeLayers are left to P / S (AcLayers: walls share the Track layer 14 with the road, so a blanket GroundMask exclude would let
    /// cars through walls; the hull starts ~0.12 m above the model floor, C1, so it does not touch a flat road). Inertia is never touched here (P sets it after all colliders exist).
    /// </summary>
    public static class AcCarHull
    {
        /// <summary>Max horizontal distance between hull and model centre before the hull is rejected (m). From C1 / the brief.</summary>
        public const float MaxCentreOffset = 0.5f;

        /// <summary>Builds the hull colliders under <paramref name="modelRoot"/>. <paramref name="rejectedBy"/> = metres off when the box was used, else 0.</summary>
        public static Collider[] Build(AcCarJob job, Transform modelRoot, List<UnityEngine.Object> owned, out float rejectedBy)
        {
            rejectedBy = 0f;
            var list = new List<Collider>();
            if (job == null || modelRoot == null) return list.ToArray();
            var go = new GameObject("Hull");
            go.layer = AcLayers.Car;
            go.transform.SetParent(modelRoot, false);
            bool useBox = job.Hull.Count == 0;
            if (!useBox && job.HasHullBounds && job.HasModelBounds)
            {
                var d = job.HullBounds.center - job.ModelBounds.center;
                float off = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                if (off > MaxCentreOffset) { useBox = true; rejectedBy = off; }
            }
            if (!useBox)
            {
                for (int i = 0; i < job.Hull.Count; i++)
                {
                    var h = job.Hull[i];
                    var mesh = new Mesh { name = "Ac hull " + h.Name };
                    if (h.Vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.vertices = h.Vertices;
                    mesh.triangles = h.Triangles;
                    mesh.RecalculateBounds();
                    owned.Add(mesh);
                    var mc = go.AddComponent<MeshCollider>();
                    try { mc.convex = true; mc.sharedMesh = mesh; }
                    catch (Exception e) { AcLog.InfoOnce("Car: hull mesh " + h.Name + " not convex-able: " + e.Message); UnityEngine.Object.Destroy(mc); continue; }
                    list.Add(mc);
                }
                if (list.Count == 0) useBox = true;
            }
            if (useBox && job.HasModelBounds)
            {
                var box = go.AddComponent<BoxCollider>();
                var b = job.ModelBounds;
                float lift = Mathf.Min(0.12f, b.size.y * 0.25f); // ours: start at sill height like the real hulls (C1: ~0.12 m above the floor)
                box.center = b.center + new Vector3(0f, lift * 0.5f, 0f);
                box.size = b.size - new Vector3(0f, lift, 0f);
                list.Add(box);
            }
            return list.ToArray();
        }
    }
}
