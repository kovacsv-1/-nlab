using UnityEngine;
using System.Collections.Generic;

public struct EPAResult
{
    public bool valid;
    public Vector3 normal;   // CSO-tér: origóból a legközelebbi határpont felé (egységvektor)
    public float depth;      // penetrációs mélység
    public Vector3 pointA;   // legközelebbi pont a playeren (world)
    public Vector3 pointB;   // legközelebbi pont a meshen (world)
}

// Expanding Polytope Algorithm
// Teljesen független a GJK-tól: 4 CSO pontot és két support delegate-et kap.
// A CSO konvenció: cso = supportB - supportA  (mesh - player).
public static class EPA
{
    const int   MAX_ITER = 64;
    const float TOL      = 1e-4f;
    const float EPS      = 1e-7f;

    // csoPts: 4 kezdeti CSO pont (a GJK záró szimplexe), tartalmazza az origót
    // worldA: a hozzájuk tartozó player world pontok
    // worldB: a hozzájuk tartozó mesh world pontok
    // supportA(dir): player support egy adott irányban (world)
    // supportB(dir): mesh support egy adott irányban (world)
    public static EPAResult Compute(
        Vector3[] csoPts,
        Vector3[] worldA,
        Vector3[] worldB,
        System.Func<Vector3, Vector3> supportA,
        System.Func<Vector3, Vector3> supportB)
    {
        EPAResult result = new EPAResult();
        result.valid = false;

        if (csoPts == null || csoPts.Length < 4) return result;

        // Munka listák (bővíthetők)
        List<Vector3> pts  = new List<Vector3>(csoPts);
        List<Vector3> ptA  = new List<Vector3>(worldA);
        List<Vector3> ptB  = new List<Vector3>(worldB);

        // Degenerált tetraéder ellenőrzés
        Vector3 ab = pts[1] - pts[0];
        Vector3 ac = pts[2] - pts[0];
        Vector3 ad = pts[3] - pts[0];
        if (Mathf.Abs(Vector3.Dot(Vector3.Cross(ab, ac), ad)) < EPS)
            return result;

        // Kezdeti 4 lap
        List<int[]> faces = new List<int[]>(4);
        AddFace(pts, faces, 0, 1, 2);
        AddFace(pts, faces, 0, 1, 3);
        AddFace(pts, faces, 0, 2, 3);
        AddFace(pts, faces, 1, 2, 3);

        for (int iter = 0; iter < MAX_ITER; ++iter)
        {
            // 1. Legközelebbi lap az origóhoz
            int     closestIdx    = -1;
            float   closestDist   = float.PositiveInfinity;
            Vector3 closestNormal = Vector3.zero;

            for (int i = 0; i < faces.Count; ++i)
            {
                Vector3 A = pts[faces[i][0]];
                Vector3 B = pts[faces[i][1]];
                Vector3 C = pts[faces[i][2]];
                Vector3 nrm = Vector3.Cross(B - A, C - A);
                float len = nrm.magnitude;
                if (len < EPS) continue;
                nrm /= len;
                float d = Vector3.Dot(nrm, A);
                if (d < 0f) { nrm = -nrm; d = -d; }  // kifelé nézzen
                if (d < closestDist)
                {
                    closestDist   = d;
                    closestIdx    = i;
                    closestNormal = nrm;
                }
            }
            if (closestIdx < 0) return result;

            // 2. Support a lap normálisa irányában
            Vector3 supportA_ = supportA(-closestNormal);
            Vector3 supportB_ = supportB( closestNormal);
            Vector3 support   = supportB_ - supportA_;
            float dSupport    = Vector3.Dot(support, closestNormal);

            // 3. Konvergencia
            if (dSupport - closestDist < TOL)
            {
                result.valid  = true;
                result.normal = closestNormal;
                result.depth  = closestDist;

                int[] f = faces[closestIdx];
                Vector3 A = pts[f[0]], B = pts[f[1]], C = pts[f[2]];
                Vector3 pOnFace = closestNormal * closestDist;
                Vector3 bary = Barycentric(A, B, C, pOnFace);

                result.pointA = bary.x * ptA[f[0]] + bary.y * ptA[f[1]] + bary.z * ptA[f[2]];
                result.pointB = bary.x * ptB[f[0]] + bary.y * ptB[f[1]] + bary.z * ptB[f[2]];
                return result;
            }

            // 4. Polytope bővítése
            int newIdx = pts.Count;
            pts.Add(support);
            ptA.Add(supportA_);
            ptB.Add(supportB_);

            // Látható lapok
            List<int> visible = new List<int>();
            for (int i = 0; i < faces.Count; ++i)
            {
                Vector3 A = pts[faces[i][0]];
                Vector3 B = pts[faces[i][1]];
                Vector3 C = pts[faces[i][2]];
                Vector3 nrm = Vector3.Cross(B - A, C - A);
                float len = nrm.magnitude;
                if (len < EPS) continue;
                nrm /= len;
                if (Vector3.Dot(nrm, A) < 0f) nrm = -nrm;
                if (Vector3.Dot(nrm, support - A) > EPS) visible.Add(i);
            }
            if (visible.Count == 0) return result;

            // Horizont kör
            List<int[]> horizon = new List<int[]>();
            foreach (int vi in visible)
            {
                int[] f = faces[vi];
                AddHorizonEdge(horizon, f[0], f[1]);
                AddHorizonEdge(horizon, f[1], f[2]);
                AddHorizonEdge(horizon, f[2], f[0]);
            }
            if (horizon.Count < 3) return result;

            // Látható lapok törlése
            visible.Sort();
            for (int i = visible.Count - 1; i >= 0; --i)
                faces.RemoveAt(visible[i]);

            // Új lapok
            foreach (int[] he in horizon)
                AddFace(pts, faces, he[0], he[1], newIdx);
        }

        return result;
    }

    // --- segédfüggvények ---

    static void AddFace(List<Vector3> pts, List<int[]> faces, int i0, int i1, int i2)
    {
        Vector3 A = pts[i0], B = pts[i1], C = pts[i2];
        Vector3 nrm = Vector3.Cross(B - A, C - A);
        if (Vector3.Dot(nrm, A) < 0f) { int t = i1; i1 = i2; i2 = t; }
        faces.Add(new int[] { i0, i1, i2 });
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

    // Barycentrikus koordináták egy háromszögben (p a síkban van)
    static Vector3 Barycentric(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = Vector3.Dot(v0, v0);
        float d01 = Vector3.Dot(v0, v1);
        float d11 = Vector3.Dot(v1, v1);
        float d20 = Vector3.Dot(v2, v0);
        float d21 = Vector3.Dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denom) < EPS) return new Vector3(1f, 0f, 0f);
        float invDenom = 1f / denom;
        float v = (d11 * d20 - d01 * d21) * invDenom;
        float w = (d00 * d21 - d01 * d20) * invDenom;
        float u = 1f - v - w;
        return new Vector3(u, v, w);
    }
}