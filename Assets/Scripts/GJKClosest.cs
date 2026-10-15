using UnityEngine;

public struct GJKHit
{
    public bool hit;
    public Vector3 point;      // contact point (on the mesh)
    public Vector3 normal;     // surface normal pointing AWAY from the mesh, towards the player
    public float distance;     // distance travelled along wishMove before contact (== |wishMove| when nothing is hit)
    public Vector3 closestA;   // closest point on player
    public Vector3 closestB;   // closest point on mesh
}

// Distance GJK + conservative-advancement shape cast + EPA based MTV.
//
// Conventions
//   CSO = mesh - player
//   p   = closest point of the CSO to the origin  (p == closestB - closestA)
//   p/|p| points from the player towards the mesh, so the surface normal facing the player is -p/|p|.
//
// Assumption: every MeshCollider is CONVEX (GJK only sees the convex hull of the vertices).
public class GJKClosest : MonoBehaviour
{
    const int   MAX_GJK_ITER    = 64;
    const int   MAX_CAST_ITER   = 32;
    const float OVERLAP_DIST_SQ = 1e-10f;  // |p|^2 below this counts as touching/overlapping
    const float CONTACT_DIST    = 1e-3f;   // shapes closer than this are "in contact"
    const float APPROACH_EPS    = 1e-4f;   // min cos between movement and contact normal to count as "moving into"
    const float NORMAL_MIN_DIST = 1e-2f;   // below this distance the closest-point direction is noisy

    struct Vertex { public Vector3 cso, a, b; }

    class Simplex
    {
        public Vertex[] v = new Vertex[4];
        public float[] l = new float[4];   // barycentric weights of the closest point
        public int count;

        public void CopyFrom(Simplex o)
        {
            for (int i = 0; i < 4; ++i) { v[i] = o.v[i]; l[i] = o.l[i]; }
            count = o.count;
        }
    }

    struct GJKResult
    {
        public bool overlap;
        public float distance;
        public Vector3 p;
        public Vector3 closestA, closestB;
    }

    // scratch data (not re-entrant, which is fine for single threaded Unity)
    readonly Simplex simplex = new Simplex();
    readonly Simplex tmp = new Simplex();
    readonly Simplex best = new Simplex();

    // ------------------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------------------

    // Distance / closest points between player box at playerPos and the mesh.
    public GJKHit GJKComplex(MeshCollider mesh, Vector3 playerHalfExtents, Vector3 playerPos)
    {
        GJKHit ret = new GJKHit();
        GJKResult r = Distance(mesh, playerHalfExtents, playerPos);
        if (r.overlap)
        {
            ret.hit = true;
            ret.distance = 0f;
            EPAResult e;
            if (RunEPA(mesh, playerHalfExtents, playerPos, out e))
            {
                ret.normal = e.normal;
                ret.closestA = e.pointA;
                ret.closestB = e.pointB;
                ret.point = e.pointB;
            }
            return ret;
        }
        ret.hit = false;
        ret.distance = r.distance;
        ret.normal = -r.p / r.distance;
        ret.closestA = r.closestA;
        ret.closestB = r.closestB;
        ret.point = r.closestB;
        return ret;
    }

    // Sweeps the box from startPos along wishMove with conservative advancement.
    // Contact while moving along / away from the surface is NOT a hit (old code reported a hit with
    // distance 0 for any touching surface, which is what glued the player to floors and walls).
    public GJKHit GJKShapeCast(MeshCollider mesh, Vector3 playerHalfExtents, Vector3 startPos, Vector3 wishMove)
    {
        GJKHit ret = new GJKHit();
        float wishDist = wishMove.magnitude;
        ret.distance = wishDist;
        if (wishDist < 1e-6f) return ret;

        Vector3 dir = wishMove / wishDist;

        float t = 0f;
        float prevT = 0f;
        Vector3 normal = Vector3.zero;
        bool haveNormal = false;

        for (int i = 0; i < MAX_CAST_ITER; ++i)
        {
            Vector3 x = startPos + dir * t;
            GJKResult r = Distance(mesh, playerHalfExtents, x);

            if (r.overlap)
            {
                if (i == 0)
                {
                    // started overlapping (or exactly touching): use EPA for the normal
                    EPAResult e;
                    Vector3 n = RunEPA(mesh, playerHalfExtents, x, out e) ? e.normal : -dir;
                    if (Vector3.Dot(dir, n) < -APPROACH_EPS)
                        return MakeHit(0f, n, e.pointA, e.pointB, wishDist);
                    return ret; // leaving or sliding, let the player move
                }
                // numerical overshoot: fall back to the last safe position
                if (!haveNormal) normal = -dir;
                return MakeHit(prevT, normal, r.closestA, r.closestB, wishDist);
            }

            float d = r.distance;
            Vector3 toMesh = r.p / d;
            if (d > NORMAL_MIN_DIST || !haveNormal)
            {
                normal = -toMesh;
                haveNormal = true;
            }

            float approach = Vector3.Dot(dir, toMesh);

            if (d < CONTACT_DIST)
            {
                if (approach > APPROACH_EPS)
                    return MakeHit(t, normal, r.closestA, r.closestB, wishDist);
                return ret; // touching, but not moving into it
            }

            if (approach <= 1e-6f) return ret; // moving away or parallel, will never hit this convex shape

            float step = d / approach;         // safe distance we can advance without touching
            if (t + step > wishDist) return ret;

            prevT = t;
            t += step;
        }

        // didn't converge: report a hit at the last safe position (caller already knows the sweep hull intersects)
        return MakeHit(t, haveNormal ? normal : -dir, startPos, startPos, wishDist);
    }

    // Minimum translation vector that pushes the player out of the mesh.
    public bool ComputeMTV(MeshCollider mesh, Vector3 playerHalfExtents, Vector3 playerPos, out Vector3 mtv)
    {
        mtv = Vector3.zero;
        GJKResult r = Distance(mesh, playerHalfExtents, playerPos);
        if (!r.overlap) return false;

        EPAResult e;
        if (!RunEPA(mesh, playerHalfExtents, playerPos, out e)) return false;

        // CSO = mesh - player. Moving the player by +normal*depth shifts the CSO by -normal*depth,
        // which puts the origin exactly on the CSO boundary.
        mtv = e.normal * e.depth;
        return true;
    }

    // ------------------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------------------

    static GJKHit MakeHit(float dist, Vector3 normal, Vector3 a, Vector3 b, float wishDist)
    {
        GJKHit h = new GJKHit();
        h.hit = true;
        h.distance = Mathf.Min(dist, wishDist);
        h.normal = normal.normalized;
        h.closestA = a;
        h.closestB = b;
        h.point = b;
        return h;
    }

    static Vertex Support(Vector3 dir, MeshCollider mesh, Vector3 he, Vector3 pos)
    {
        Vertex v;
        v.b = GJKSupport.Mesh(dir, mesh);
        v.a = pos + GJKSupport.PlayerBox(-dir, he);
        v.cso = v.b - v.a;
        return v;
    }

    // Runs GJK. The final simplex stays in the `simplex` field (needed for EPA when overlap == true).
    GJKResult Distance(MeshCollider mesh, Vector3 he, Vector3 pos)
    {
        GJKResult r = new GJKResult();

        Vector3 dir = mesh.bounds.center - pos;
        if (dir.sqrMagnitude < 1e-12f) dir = Vector3.right;

        simplex.count = 1;
        simplex.v[0] = Support(dir, mesh, he, pos);

        Vector3 p;
        int iter = 0;
        while (true)
        {
            p = Solve(simplex);

            if (simplex.count == 4 || p.sqrMagnitude < OVERLAP_DIST_SQ)
            {
                r.overlap = true;
                return r;
            }

            if (++iter > MAX_GJK_ITER) break;

            float d2 = p.sqrMagnitude;
            Vertex w = Support(-p, mesh, he, pos);

            // p.(p - w) is how much closer the CSO could still get along -p
            float gap = d2 - Vector3.Dot(p, w.cso);
            if (gap <= d2 * 1e-4f + 1e-6f) break;

            bool duplicate = false;
            for (int i = 0; i < simplex.count; ++i)
                if ((simplex.v[i].cso - w.cso).sqrMagnitude < 1e-10f) { duplicate = true; break; }
            if (duplicate) break;

            simplex.v[simplex.count++] = w;
        }

        r.p = p;
        r.distance = p.magnitude;
        Vector3 A = Vector3.zero, B = Vector3.zero;
        for (int i = 0; i < simplex.count; ++i)
        {
            A += simplex.l[i] * simplex.v[i].a;
            B += simplex.l[i] * simplex.v[i].b;
        }
        r.closestA = A;
        r.closestB = B;
        return r;
    }

    bool RunEPA(MeshCollider mesh, Vector3 he, Vector3 pos, out EPAResult res)
    {
        int n = simplex.count;
        Vector3[] cso = new Vector3[n];
        Vector3[] ptA = new Vector3[n];
        Vector3[] ptB = new Vector3[n];
        for (int i = 0; i < n; ++i)
        {
            cso[i] = simplex.v[i].cso;
            ptA[i] = simplex.v[i].a;
            ptB[i] = simplex.v[i].b;
        }

        // plain supports: EPA flips the direction for A itself
        System.Func<Vector3, Vector3> supportA = dir => pos + GJKSupport.PlayerBox(dir, he);
        System.Func<Vector3, Vector3> supportB = dir => GJKSupport.Mesh(dir, mesh);

        res = EPA.Compute(cso, ptA, ptB, supportA, supportB);
        return res.valid;
    }

    // ---- closest point on simplex to the origin; reduces the simplex to the supporting feature ----

    Vector3 Solve(Simplex s)
    {
        switch (s.count)
        {
            case 1: s.l[0] = 1f; return s.v[0].cso;
            case 2: return SolveLine(s);
            case 3: return SolveTriangle(s);
            default: return SolveTetra(s);
        }
    }

    static Vector3 Keep1(Simplex s, Vertex a)
    {
        s.v[0] = a; s.count = 1; s.l[0] = 1f;
        return a.cso;
    }

    static void Keep2(Simplex s, Vertex a, Vertex b, float la, float lb)
    {
        s.v[0] = a; s.v[1] = b; s.count = 2; s.l[0] = la; s.l[1] = lb;
    }

    Vector3 SolveLine(Simplex s)
    {
        Vertex va = s.v[0], vb = s.v[1];
        Vector3 a = va.cso, b = vb.cso;
        Vector3 ab = b - a;
        float den = Vector3.Dot(ab, ab);
        if (den < 1e-14f) return Keep1(s, va);

        float t = -Vector3.Dot(a, ab) / den;
        if (t <= 0f) return Keep1(s, va);
        if (t >= 1f) return Keep1(s, vb);

        s.count = 2;
        s.l[0] = 1f - t; s.l[1] = t;
        return a + ab * t;
    }

    // Ericson, Real-Time Collision Detection, ClosestPtPointTriangle with p = origin
    Vector3 SolveTriangle(Simplex s)
    {
        Vertex va = s.v[0], vb = s.v[1], vc = s.v[2];
        Vector3 a = va.cso, b = vb.cso, c = vc.cso;
        Vector3 ab = b - a, ac = c - a;

        float d1 = -Vector3.Dot(ab, a);
        float d2 = -Vector3.Dot(ac, a);
        if (d1 <= 0f && d2 <= 0f) return Keep1(s, va);

        float d3 = -Vector3.Dot(ab, b);
        float d4 = -Vector3.Dot(ac, b);
        if (d3 >= 0f && d4 <= d3) return Keep1(s, vb);

        float vcr = d1 * d4 - d3 * d2;
        if (vcr <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            Keep2(s, va, vb, 1f - v, v);
            return a + ab * v;
        }

        float d5 = -Vector3.Dot(ab, c);
        float d6 = -Vector3.Dot(ac, c);
        if (d6 >= 0f && d5 <= d6) return Keep1(s, vc);

        float vbr = d5 * d2 - d1 * d6;
        if (vbr <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            Keep2(s, va, vc, 1f - w, w);
            return a + ac * w;
        }

        float var_ = d3 * d6 - d5 * d4;
        if (var_ <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            Keep2(s, vb, vc, 1f - w, w);
            return b + (c - b) * w;
        }

        float sum = var_ + vbr + vcr;
        if (Mathf.Abs(sum) < 1e-20f)
        {
            // degenerate (collinear) triangle: drop the newest vertex
            s.count = 2;
            return SolveLine(s);
        }

        float inv = 1f / sum;
        float vv = vbr * inv, ww = vcr * inv;
        s.l[0] = 1f - vv - ww; s.l[1] = vv; s.l[2] = ww;
        return a + ab * vv + ac * ww;
    }

    static readonly int[,] TetraFaces = { { 0, 1, 2, 3 }, { 0, 1, 3, 2 }, { 0, 2, 3, 1 }, { 1, 2, 3, 0 } };

    Vector3 SolveTetra(Simplex s)
    {
        Vector3 a = s.v[0].cso, b = s.v[1].cso, c = s.v[2].cso, d = s.v[3].cso;
        Vector3 ab = b - a, ac = c - a, ad = d - a;

        float vol = Vector3.Dot(Vector3.Cross(ab, ac), ad);
        float scale = ab.magnitude * ac.magnitude * ad.magnitude;
        if (Mathf.Abs(vol) <= 1e-6f * scale + 1e-20f)
        {
            // flat tetrahedron: drop the newest vertex
            s.count = 3;
            return SolveTriangle(s);
        }

        bool anyOutside = false;
        float bestD2 = float.PositiveInfinity;
        Vector3 bestP = Vector3.zero;

        for (int f = 0; f < 4; ++f)
        {
            int i = TetraFaces[f, 0], j = TetraFaces[f, 1], k = TetraFaces[f, 2], o = TetraFaces[f, 3];
            Vector3 pi = s.v[i].cso;
            Vector3 n = Vector3.Cross(s.v[j].cso - pi, s.v[k].cso - pi);
            float sideOpp = Vector3.Dot(n, s.v[o].cso - pi);
            float sideOrg = Vector3.Dot(n, -pi);
            if (sideOrg * sideOpp >= 0f) continue; // origin on the inner side of this face

            anyOutside = true;
            tmp.count = 3;
            tmp.v[0] = s.v[i]; tmp.v[1] = s.v[j]; tmp.v[2] = s.v[k];
            Vector3 q = SolveTriangle(tmp);
            float d2 = q.sqrMagnitude;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestP = q;
                best.CopyFrom(tmp);
            }
        }

        if (!anyOutside)
        {
            s.count = 4;      // origin inside the tetrahedron
            return Vector3.zero;
        }

        s.CopyFrom(best);
        return bestP;
    }
}
