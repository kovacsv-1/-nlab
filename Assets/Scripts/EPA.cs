using UnityEngine;
using System;
using System.Collections.Generic;

public struct EPAResult
{
    public bool valid;
    public Vector3 normal;   // CSO space: from origin to the closest boundary point (unit). Equals the surface normal facing the player.
    public float depth;      // penetration depth
    public Vector3 pointA;   // closest point on the player (world)
    public Vector3 pointB;   // closest point on the mesh (world)
}

// Expanding Polytope Algorithm.
// CSO convention: cso = B - A   (mesh - player).
//
// supportA(dir) / supportB(dir) are PLAIN supports: "furthest point of the shape in direction dir".
// The EPA itself takes care of the sign flip for A (CSO support in dir n = supportB(n) - supportA(-n)).
// (The old code passed a delegate that negated the direction too, so the sign was flipped twice.)
public static class EPA
{
    const int   MAX_ITER = 64;
    const float TOL      = 1e-3f;   // 1e-4 was below float noise at world coordinates of 100+
    const float EPS      = 1e-6f;

    struct Face
    {
        public int a, b, c;
        public Vector3 n;   // outward unit normal
        public float d;     // distance from origin along n
    }

    public static EPAResult Compute(
        Vector3[] csoPts,
        Vector3[] worldA,
        Vector3[] worldB,
        Func<Vector3, Vector3> supportA,
        Func<Vector3, Vector3> supportB)
    {
        EPAResult result = new EPAResult();
        if (csoPts == null) return result;

        List<Vector3> pts = new List<Vector3>(16);
        List<Vector3> ptA = new List<Vector3>(16);
        List<Vector3> ptB = new List<Vector3>(16);

        // 1. keep only affinely independent input points (GJK may end with a 1-3 point simplex when touching)
        for (int i = 0; i < csoPts.Length; ++i)
        {
            if (Independent(pts, csoPts[i]))
            {
                pts.Add(csoPts[i]); ptA.Add(worldA[i]); ptB.Add(worldB[i]);
            }
        }

        // 2. complete to a tetrahedron with extra support points
        if (!CompleteTetrahedron(pts, ptA, ptB, supportA, supportB)) return result;

        // Interior reference point used to orient faces. The origin can lie ON the boundary,
        // so the centroid is used instead of the origin.
        Vector3 interior = (pts[0] + pts[1] + pts[2] + pts[3]) * 0.25f;

        List<Face> faces = new List<Face>(64);
        AddFace(pts, faces, 0, 1, 2, interior);
        AddFace(pts, faces, 0, 1, 3, interior);
        AddFace(pts, faces, 0, 2, 3, interior);
        AddFace(pts, faces, 1, 2, 3, interior);

        int closestIdx = -1;

        for (int iter = 0; iter < MAX_ITER; ++iter)
        {
            // closest face to the origin
            closestIdx = -1;
            float closestDist = float.PositiveInfinity;
            for (int i = 0; i < faces.Count; ++i)
            {
                if (faces[i].d < closestDist) { closestDist = faces[i].d; closestIdx = i; }
            }
            if (closestIdx < 0) return result;

            Face cf = faces[closestIdx];

            // support in the face normal direction
            Vector3 sA = supportA(-cf.n);
            Vector3 sB = supportB(cf.n);
            Vector3 sup = sB - sA;
            float dSup = Vector3.Dot(sup, cf.n);

            // converged
            if (dSup - cf.d < TOL) return Finish(faces[closestIdx], pts, ptA, ptB);

            int newIdx = pts.Count;
            pts.Add(sup); ptA.Add(sA); ptB.Add(sB);

            // visible faces
            bool[] visible = new bool[faces.Count];
            int visCount = 0;
            for (int i = 0; i < faces.Count; ++i)
            {
                if (Vector3.Dot(faces[i].n, sup - pts[faces[i].a]) > EPS)
                {
                    visible[i] = true; visCount++;
                }
            }
            if (visCount == 0) return Finish(faces[closestIdx], pts, ptA, ptB);

            // horizon
            List<int[]> horizon = new List<int[]>();
            for (int i = 0; i < faces.Count; ++i)
            {
                if (!visible[i]) continue;
                AddHorizonEdge(horizon, faces[i].a, faces[i].b);
                AddHorizonEdge(horizon, faces[i].b, faces[i].c);
                AddHorizonEdge(horizon, faces[i].c, faces[i].a);
            }
            if (horizon.Count < 3) return Finish(faces[closestIdx], pts, ptA, ptB);

            // rebuild face list
            List<Face> newFaces = new List<Face>(faces.Count + horizon.Count);
            for (int i = 0; i < faces.Count; ++i)
                if (!visible[i]) newFaces.Add(faces[i]);
            faces = newFaces;

            foreach (int[] he in horizon)
                AddFace(pts, faces, he[0], he[1], newIdx, interior);

            if (faces.Count < 4) return result;
        }

        // Out of iterations: the closest face is still a good approximation, don't throw it away.
        closestIdx = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < faces.Count; ++i)
            if (faces[i].d < best) { best = faces[i].d; closestIdx = i; }
        if (closestIdx < 0) return result;
        return Finish(faces[closestIdx], pts, ptA, ptB);
    }

    // ---------------------------------------------------------------------------------

    static EPAResult Finish(Face f, List<Vector3> pts, List<Vector3> ptA, List<Vector3> ptB)
    {
        EPAResult r = new EPAResult();
        r.valid = true;
        r.normal = f.n;
        r.depth = Mathf.Max(0f, f.d);

        Vector3 pOnFace = f.n * f.d;
        Vector3 bary = Barycentric(pts[f.a], pts[f.b], pts[f.c], pOnFace);
        r.pointA = bary.x * ptA[f.a] + bary.y * ptA[f.b] + bary.z * ptA[f.c];
        r.pointB = bary.x * ptB[f.a] + bary.y * ptB[f.b] + bary.z * ptB[f.c];
        return r;
    }

    static bool Independent(List<Vector3> pts, Vector3 c)
    {
        switch (pts.Count)
        {
            case 0: return true;
            case 1: return (c - pts[0]).sqrMagnitude > 1e-8f;
            case 2: return Vector3.Cross(pts[1] - pts[0], c - pts[0]).sqrMagnitude > 1e-10f;
            case 3:
                return Mathf.Abs(Vector3.Dot(Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]), c - pts[0])) > 1e-6f;
            default: return false;
        }
    }

    static bool CompleteTetrahedron(
        List<Vector3> pts, List<Vector3> ptA, List<Vector3> ptB,
        Func<Vector3, Vector3> supportA, Func<Vector3, Vector3> supportB)
    {
        while (pts.Count < 4)
        {
            List<Vector3> dirs = new List<Vector3>(8);
            switch (pts.Count)
            {
                case 0:
                    dirs.Add(Vector3.right);
                    break;
                case 1:
                    dirs.Add(Vector3.right); dirs.Add(Vector3.left);
                    dirs.Add(Vector3.up);    dirs.Add(Vector3.down);
                    dirs.Add(Vector3.forward); dirs.Add(Vector3.back);
                    break;
                case 2:
                {
                    Vector3 d = pts[1] - pts[0];
                    Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                    foreach (Vector3 ax in axes)
                    {
                        Vector3 u = Vector3.Cross(d, ax);
                        if (u.sqrMagnitude > 1e-12f) { dirs.Add(u); dirs.Add(-u); }
                    }
                    break;
                }
                case 3:
                {
                    Vector3 n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
                    dirs.Add(n); dirs.Add(-n);
                    break;
                }
            }

            bool added = false;
            foreach (Vector3 dir in dirs)
            {
                Vector3 sA = supportA(-dir);
                Vector3 sB = supportB(dir);
                Vector3 s = sB - sA;
                if (Independent(pts, s))
                {
                    pts.Add(s); ptA.Add(sA); ptB.Add(sB);
                    added = true;
                    break;
                }
            }
            if (!added) return false;
        }
        return true;
    }

    // Faces are oriented outward using the interior point, so winding is consistent.
    static void AddFace(List<Vector3> pts, List<Face> faces, int i0, int i1, int i2, Vector3 interior)
    {
        Vector3 A = pts[i0], B = pts[i1], C = pts[i2];
        Vector3 n = Vector3.Cross(B - A, C - A);
        float len = n.magnitude;
        if (len < EPS) return;
        n /= len;
        if (Vector3.Dot(n, A - interior) < 0f)
        {
            n = -n;
            int t = i1; i1 = i2; i2 = t;
        }
        Face f = new Face();
        f.a = i0; f.b = i1; f.c = i2;
        f.n = n;
        f.d = Vector3.Dot(n, A);
        faces.Add(f);
    }

    static void AddHorizonEdge(List<int[]> horizon, int a, int b)
    {
        for (int i = 0; i < horizon.Count; ++i)
        {
            if (horizon[i][0] == b && horizon[i][1] == a)
            {
                horizon.RemoveAt(i);
                return;
            }
        }
        horizon.Add(new int[] { a, b });
    }

    static Vector3 Barycentric(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = Vector3.Dot(v0, v0);
        float d01 = Vector3.Dot(v0, v1);
        float d11 = Vector3.Dot(v1, v1);
        float d20 = Vector3.Dot(v2, v0);
        float d21 = Vector3.Dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denom) < 1e-12f) return new Vector3(1f, 0f, 0f);
        float inv = 1f / denom;
        float v = (d11 * d20 - d01 * d21) * inv;
        float w = (d00 * d21 - d01 * d20) * inv;
        return new Vector3(1f - v - w, v, w);
    }
}
