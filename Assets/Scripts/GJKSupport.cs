using UnityEngine;
using System.Collections.Generic;

// Support functions shared by GJK, EPA and SweptTraces.
public static class GJKSupport
{
    // Axis-aligned box support around the origin.
    public static Vector3 PlayerBox(Vector3 direction, Vector3 halfExtents)
    {
        return new Vector3(
            direction.x >= 0 ? halfExtents.x : -halfExtents.x,
            direction.y >= 0 ? halfExtents.y : -halfExtents.y,
            direction.z >= 0 ? halfExtents.z : -halfExtents.z
        );
    }

    // Swept box (hull of box + box moved by wishMove).
    public static Vector3 SweptPlayerBox(Vector3 direction, Vector3 halfExtents, Vector3 wishMove)
    {
        Vector3 unswept = PlayerBox(direction, halfExtents);
        return Vector3.Dot(direction, wishMove) > 0f ? unswept + wishMove : unswept;
    }

    // ---- Mesh support with caching ------------------------------------------------
    // The old version called mesh.vertices (allocates + copies the whole array) and
    // TransformPoint for every vertex on EVERY support call. GJK calls support dozens of
    // times per query, and the shape cast runs GJK dozens of times, so this was extremely slow.
    // Here the local vertices are fetched once, and world vertices are only recomputed
    // when the collider's localToWorld matrix changes.
    class MeshCache
    {
        public Mesh mesh;
        public Vector3[] local;
        public Vector3[] world;
        public Matrix4x4 matrix;
        public bool valid;
    }

    static readonly Dictionary<MeshCollider, MeshCache> cache = new Dictionary<MeshCollider, MeshCache>();

    static Vector3[] GetWorldVertices(MeshCollider mc)
    {
        MeshCache c;
        if (!cache.TryGetValue(mc, out c))
        {
            c = new MeshCache();
            cache[mc] = c;
        }

        Mesh mesh = mc.sharedMesh;
        if (c.local == null || c.mesh != mesh)
        {
            c.mesh = mesh;
            c.local = mesh.vertices;               // requires Read/Write enabled on the mesh
            c.world = new Vector3[c.local.Length];
            c.valid = false;
        }

        Matrix4x4 m = mc.transform.localToWorldMatrix;
        if (!c.valid || m != c.matrix)
        {
            for (int i = 0; i < c.local.Length; ++i)
                c.world[i] = m.MultiplyPoint3x4(c.local[i]);
            c.matrix = m;
            c.valid = true;
        }
        return c.world;
    }

    // MeshCollider support in world space (convex hull of the mesh).
    public static Vector3 Mesh(Vector3 direction, MeshCollider meshCollider)
    {
        Vector3[] verts = GetWorldVertices(meshCollider);

        float bestDot = float.NegativeInfinity;
        Vector3 best = Vector3.zero;
        for (int i = 0; i < verts.Length; ++i)
        {
            Vector3 v = verts[i];
            float dot = direction.x * v.x + direction.y * v.y + direction.z * v.z;
            if (dot > bestDot) { bestDot = dot; best = v; }
        }
        return best;
    }
}
