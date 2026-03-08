using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public struct GJKHit
{
    public bool hit;
    public Vector3 point;      // contact point
    public Vector3 normal;     // collision normal
    public float distance;     // distance along wishMove
    public Vector3 closestA;   // closest point on player
    public Vector3 closestB;   // closest point on mesh
}

public class GJKClosest : MonoBehaviour
{
    //based on https://github.com/kujukuju/KodaPhysics/blob/master/src/GjkComplex.jai
    //gets distance of objects and closest point on both (+ normals)
    //could do it along wishdir if solved in 2d (points projected onto a plane perpendicular to the direction)
    //then make solvetetrahedron always step towards -dir, if it encapsulates origin, we started inside, otherwise find the point closest to origin along -dir to find contact
    //if encapsulates origin then started inside and make tetrahedron step in the wishdir direction, to hopefully find first exit point
    //this is likely one of the following:
    // https://github.com/kujukuju/KodaPhysics/blob/master/src/GjkDir.jai
    // https://github.com/kujukuju/KodaPhysics/blob/master/src/GjkBullet.jai
    //also, can use EPA algorithm to resolve being stuck in a mesh, but might just nudge the player around, that is how source works 
    //might not be needed at all

    private float EPSILON = 0.0000001f; //seems good enough, play around with preciseness

    struct SimplexSolve {
        public Vector3 p;
        public Vector3 dir;
    }

    class ComplexSimplex {  //could just be a matrix
        public Vector3 a; public Vector3 a1; public Vector3 a2;
        public Vector3 b; public Vector3 b1; public Vector3 b2;
        public Vector3 c; public Vector3 c1; public Vector3 c2;
        public Vector3 d; public Vector3 d1; public Vector3 d2;
        public int count;

        //Vector3[] a_all = new Vector3[3];
        //Vector3[] b_all = new Vector3[3];
        //Vector3[] c_all = new Vector3[3];
        //Vector3[] d_all = new Vector3[3];

        public ComplexSimplex Copy()
        {
            ComplexSimplex ret = new ComplexSimplex();
            ret.a = a; ret.a1 = a1; ret.a2 = a2;
            ret.b = b; ret.b1 = b1; ret.b2 = b2;
            ret.c = c; ret.c1 = c1; ret.c2 = c2;
            ret.d = d; ret.d1 = d1; ret.d2 = d2;
            ret.count = count;
            //Array.Copy(a_all, ret.a_all, 3);
            //Array.Copy(b_all, ret.b_all, 3);
            //Array.Copy(c_all, ret.c_all, 3);
            //Array.Copy(d_all, ret.d_all, 3);
            return ret;
        }
    }

    Vector3 SupportPlayerBox(Vector3 direction, Vector3 halfExtents) //bounds of axis-aligned bounding box, support to find the best vertex, doesn't take playertransform, just around 000; might replace wiht player meshcollider, so it works with other shapes
    {
        return new Vector3(
            direction.x >= 0 ? halfExtents.x : -halfExtents.x, //avoid centre of edges being returned as vertices
            direction.y >= 0 ? halfExtents.y : -halfExtents.y,
            direction.z >= 0 ? halfExtents.z : -halfExtents.z
        );
    }

    Vector3 SupportMesh(Vector3 direction, MeshCollider meshCollider) //bounds of mesh collider, returns world-space vertices
    {
        Transform meshTransform = meshCollider.transform;
        Mesh mesh = meshCollider.sharedMesh;
        Vector3[] vertices = mesh.vertices; // local space

        float bestDot = float.NegativeInfinity;
        Vector3 bestVertex = Vector3.zero;

        foreach (Vector3 localVertex in vertices)
        {
            Vector3 worldVertex = meshTransform.TransformPoint(localVertex);
            float dot = Vector3.Dot(direction, worldVertex);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestVertex = worldVertex;
            }
        }
        return bestVertex;
    }

    // returns whether or not the shapes intersect, the simplex, and the distance between shapes if allowed to resolve completely, this has some shapecast logic it returns, but shouldn't be used for that
    public GJKHit GJKComplex(MeshCollider mesh, Vector3 playerHalfExtents, Vector3 playerPos, Vector3 wishMove) { //bool, ComplexSimplex, Vector3, Vector3 is what this should return
        ComplexSimplex simplex = new ComplexSimplex();
        Vector3 direction = wishMove;
        simplex.a1 = SupportPlayerBox(-direction, playerHalfExtents) + playerPos;
        simplex.a2 = SupportMesh(direction, mesh);
        simplex.a = simplex.a2 - simplex.a1;
        simplex.count = 1;
        ComplexSimplex bestSimplex = new ComplexSimplex();
        float bestSupportDifference = 0f;
        Vector3 bestP = Vector3.zero;
        GJKHit ret = new GJKHit();
        SimplexSolve closests = new SimplexSolve();
        Vector3 separation = Vector3.zero;
        // if one triangle is unnecessarily huge, epsilon might not be large enough to check directional values against each other if its almost exactly touching
        for (int i = 0; i < 30; ++i)
        { //30 iterations of loop to converge

            // solving direction from cross products gives much more accuracy than resolving from p
            // resolving from p can result in infinite loops when epsilon is too small to check directional differences
            SimplexSolve p_dir = new SimplexSolve();
            switch (simplex.count)
            {
                case 1:
                    p_dir = PointSimplex(ref simplex);
                    break;
                case 2:
                    p_dir = LineSimplex(ref simplex);
                    break;
                case 3:
                    p_dir = TriangleSimplex(ref simplex);
                    break;
                case 4:
                    // current_simplex := simplex;
                    p_dir = TetrahedronSimplex(ref simplex);
                    // if p == .{} {
                    //     #if GJK_DEBUG {
                    //         step_debug(simplex, v1, v2, p, .{0, 1000, 0}, true);
                    //     }
                    //     return true, current_simplex, .{}, .{};
                    // }
                    break;
                default:
                    ret.hit = true;
                    ret.distance = 0f;
                    return ret;
            }

            // checking if distance squared from p to 0 is less than epsilon rather than comparing it to 0
            // because I'm not entirely sure how it would work together with the tetrahedron cross product
            // check and the support point directional check below
            if (p_dir.p.magnitude < EPSILON)
            {
                ret.hit = true;
                ret.distance = 0f;
                return ret;
            }

            Vector3 support1 = SupportPlayerBox(-p_dir.dir, playerHalfExtents) + playerPos;
            Vector3 support2 = SupportMesh(p_dir.dir, mesh);;
            Vector3 support = support2 - support1;

            // if p is more than or equivalently extremal in the direction of the origin, we can't find a better support point
            // scaled_epsilon := support.magnitude * EPSILON;
            // print("% >= %\n", dir_strength(p, direction), dir_strength(support, direction));
            float p_dirStrength = GetDirStrength(p_dir.p, p_dir.dir);
            float support_dirStrength = GetDirStrength(support, p_dir.dir) - EPSILON;
            /*if (Vector3.Dot(support, p_dir.dir) < 0)*/ if (p_dirStrength >= support_dirStrength - EPSILON)
            {
                closests = GetLocalPoints(simplex, p_dir.p); //speed
                separation = closests.dir - closests.p;
                ret.hit = false;
                ret.closestA = closests.p;
                ret.closestB = closests.dir;
                //if (separation.sqrMagnitude < EPSILON)
                //{
                //    ret.normal = Vector3.zero;
                //}
                //else
                //{
                    ret.normal = separation.normalized; // p_dir.dir.normalized
                //} 
                //ret.distance = Vector3.Dot(separation, wishMove.normalized);
                ret.distance = separation.magnitude;
                ret.point = closests.dir;
                return ret;
            }

            // this solves the case where the objects are both very large and infinitely close to the each other (but not within epsilon)
            // when the loop exits itll return the best simplex anyways
            if (support_dirStrength - p_dirStrength > bestSupportDifference)
            {
                bestSupportDifference = support_dirStrength - p_dirStrength;
                bestSimplex = simplex.Copy();
                bestP = p_dir.p;
            }

            simplex.d = simplex.c; simplex.d1 = simplex.c1; simplex.d2 = simplex.c2;
            simplex.c = simplex.b; simplex.c1 = simplex.b1; simplex.c2 = simplex.b2;
            simplex.b = simplex.a; simplex.b1 = simplex.a1; simplex.b2 = simplex.a2;
            simplex.count += 1;

            simplex.a1 = support1;
            simplex.a2 = support2;
            simplex.a = support;
        }

        closests = GetLocalPoints(bestSimplex, bestP); //use struct for speed
        separation = closests.dir - closests.p;
        ret.hit = false;
        ret.closestA = closests.p;
        ret.closestB = closests.dir;
        //if (separation.sqrMagnitude < EPSILON)
        //{
            //ret.normal = Vector3.zero;
        //}
        //else
        //{
            ret.normal = separation.normalized; //p_dir.dir.normalized
        //} 
        //ret.distance = Vector3.Dot(separation, wishMove.normalized);
        ret.distance = separation.magnitude;
        ret.point = closests.dir;
        return ret;
    }

    public GJKHit GJKShapeCast(MeshCollider mesh, Vector3 playerHalfExtents, Vector3 playerPos, Vector3 wishMove)
    {
        Vector3 dir = wishMove.normalized;
        float remaining = wishMove.magnitude;

        Vector3 x = playerPos;

        GJKHit ret = new GJKHit();

        for (int step = 0; step < 20; step++)
        {
            // Run your existing closest-distance GJK
            GJKHit gjk = GJKComplex(mesh, playerHalfExtents, x, wishMove);

            // overlap
            if (gjk.hit || gjk.distance < EPSILON)
            {
                ret.hit = true;
                ret.distance = (x - playerPos).magnitude;
                ret.normal = gjk.normal;
                ret.point = gjk.point;
                return ret;
            }

            float denom = Vector3.Dot(gjk.normal, dir);

            // moving away
            if (denom <= EPSILON)
            {
                ret.hit = false;
                return ret;
            }

            float stepDist = gjk.distance / denom;

            if (stepDist > remaining)
            {
                ret.hit = false;
                return ret;
            }

            // advance
            x += dir * stepDist;
            remaining -= stepDist;

            if (remaining <= EPSILON)
            {
                ret.hit = false;
                return ret;
            }
        }

        ret.hit = false;
        return ret;
    }

    /*List<List<Vector3>> ResolveSimplex(ComplexSimplex complex) { //gets points on original shapes that contribute to found point on difference
        List<Vector3> simplex1 = new List<Vector3>();
        simplex1.Add(complex.a1);
        List<Vector3> simplex2 = new List<Vector3>();
        simplex2.Add(complex.a2);
        // not sure if first is necessary but it makes sense to me that vertex c and a could overlap
        Vector3 first1 = simplex1[0];
        Vector3 first2 = simplex2[0];
        Vector3 last1 = simplex1[0];
        Vector3 last2 = simplex2[0];
        for (int i = 1; i < complex.count; ++i)
        {
            Vector3 current1 = first1;
            Vector3 current2 = first2;

            switch (i)
            {
                case 1:
                    current1 = complex.b1;
                    current2 = complex.b2;
                    break;
                case 2:
                    current1 = complex.c1;
                    current2 = complex.c2;
                    break;
                case 3:
                    current1 = complex.d1;
                    current2 = complex.d2;
                    break;
            }
            if (current1 != first1 && current1 != last1) //this is weird, last is never updated, checking contains might be more secure
            {
                simplex1.Add(current1);
            }
            if (current2 != first2 && current2 != last2)
            {
                simplex2.Add(current2);
            }
        }

        return new List<List<Vector3>> {simplex1, simplex2};
    }*/

    //simplex cases
    SimplexSolve PointSimplex(ref ComplexSimplex simplex)
    {
        SimplexSolve ret = new SimplexSolve();
        ret.p = simplex.a;
        ret.dir = -simplex.a;
        return ret;
    }

    SimplexSolve LineSimplex(ref ComplexSimplex simplex)
    {
        Vector3 ab = simplex.b - simplex.a; Vector3 ao = -simplex.a;
        if (Vector3.Dot(ab, ao) > 0)
        {
            SimplexSolve ret = new SimplexSolve();
            ret.p = simplex.a + Vector3.Dot(ao, ab) / Vector3.Dot(ab, ab) * ab;
            ret.dir = Vector3.Cross(Vector3.Cross(ab, ao), ab);
            return ret;
        }
        else
        {
            simplex.count = 1;
            SimplexSolve ret = new SimplexSolve();
            ret.p = simplex.a;
            ret.dir = ao;
            return ret;
        }
    }

    SimplexSolve TriangleSimplex(ref ComplexSimplex simplex)
    {
        Vector3 ab = simplex.b - simplex.a;
        Vector3 ac = simplex.c - simplex.a;
        Vector3 abc = Vector3.Cross(ab, ac);
        Vector3 ao = -simplex.a;
        if (Vector3.Dot(Vector3.Cross(abc, ac), ao) > 0)
        {
            if (Vector3.Dot(ac, ao) > 0)
            {
                // the origin is nearest to the line ac
                // simplex c remains as c
                simplex.b = simplex.c;
                simplex.b1 = simplex.c1;
                simplex.b2 = simplex.c2;
                simplex.count = 2;
                float t = Vector3.Dot(ao, ac) / Vector3.Dot(ac, ac);
                SimplexSolve ret = new SimplexSolve();
                ret.p = simplex.a + t * ac;
                ret.dir = Vector3.Cross(Vector3.Cross(ac, ao), ac);
                return ret;
            }
            else
            {
                if (Vector3.Dot(ab, ao) > 0)
                {
                    // the origin is nearest to the line ab
                    simplex.count = 2;
                    float t = Vector3.Dot(ao, ab) / Vector3.Dot(ab, ab);
                    SimplexSolve ret = new SimplexSolve();
                    ret.p = simplex.a;
                    ret.dir = ao;
                    return ret;
                }
                else
                {
                    // the origin is nearest to the point a
                    simplex.count = 1;
                    SimplexSolve ret = new SimplexSolve();
                    ret.p = simplex.a;
                    ret.dir = ao;
                    return ret;
                }
            }
        }
        else
        {
            if (Vector3.Dot(Vector3.Cross(ab, abc), ao) > 0)
            {
                if (Vector3.Dot(ab, ao) > 0)
                {
                    // the origin is nearest to the line ab
                    simplex.count = 2;
                    float t = Vector3.Dot(ao, ab) / Vector3.Dot(ab, ab);
                    SimplexSolve ret = new SimplexSolve();
                    ret.p = simplex.a + t * ab;
                    ret.dir = Vector3.Cross(Vector3.Cross(ab, ao), ab);
                    return ret;
                }
                else
                {
                    // the origin is nearest to the point a
                    simplex.count = 1;
                    SimplexSolve ret = new SimplexSolve();
                    ret.p = simplex.a;
                    ret.dir = ao;
                    return ret;
                }
            }
            else
            {
                if (Vector3.Dot(abc, ao) > 0)
                {
                    // the origin is nearest to the triangle abc
                    Vector3 bo = -simplex.b;
                    Vector3 co = -simplex.c;
                    float d1 = Vector3.Dot(ab, ao);
                    float d2 = Vector3.Dot(ac, ao);
                    float d3 = Vector3.Dot(ab, bo);
                    float d4 = Vector3.Dot(ac, bo);
                    float d5 = Vector3.Dot(ab, co);
                    float d6 = Vector3.Dot(ac, co);
                    float va = d3 * d6 - d5 * d4;
                    float vb = d5 * d2 - d1 * d6;
                    float vc = d1 * d4 - d3 * d2;
                    float denom = 1f / (va + vb + vc);
                    float v = vb * denom;
                    float w = vc * denom;
                    SimplexSolve ret = new SimplexSolve();
                    ret.p = simplex.a + ab * v + ac * w;
                    ret.dir = abc;
                    return ret;
                }
                else
                {
                    // the origin is nearest to the triangle acb
                    Vector3 tmp = simplex.b; Vector3 tmp1 = simplex.b1; Vector3 tmp2 = simplex.b2;
                    simplex.b = simplex.c; simplex.b1 = simplex.c1; simplex.b2 = simplex.c2;
                    simplex.c = tmp; simplex.c1 = tmp1; simplex.c2 = tmp2;
                    Vector3 bo = -simplex.b;
                    Vector3 co = -simplex.c;
                    float d1 = Vector3.Dot(ac, ao);
                    float d2 = Vector3.Dot(ab, ao);
                    float d3 = Vector3.Dot(ac, co);
                    float d4 = Vector3.Dot(ab, co);
                    float d5 = Vector3.Dot(ac, bo);
                    float d6 = Vector3.Dot(ab, bo);
                    float va = d3 * d6 - d5 * d4;
                    float vb = d5 * d2 - d1 * d6;
                    float vc = d1 * d4 - d3 * d2;
                    float denom = 1f / (va + vb + vc);
                    float v = vb * denom;
                    float w = vc * denom;
                    SimplexSolve ret = new SimplexSolve();
                    ret.p = simplex.a + ab * v + ac * w;
                    ret.dir = -abc;
                    return ret;
                }
            }
        }
    }

    SimplexSolve TetrahedronSimplex(ref ComplexSimplex simplex)
    {
        Vector3 abc = Vector3.Cross(simplex.b - simplex.a, simplex.c - simplex.a);
        Vector3 acd = Vector3.Cross(simplex.c - simplex.a, simplex.d - simplex.a);
        Vector3 adb = Vector3.Cross(simplex.d - simplex.a, simplex.b - simplex.a);
        Vector3 ao = -simplex.a;
        SimplexSolve ret = new SimplexSolve();
        /*if (Vector3.Dot(abc, simplex.d - simplex.a) > 0) //if normals pointing inward, make them point outward, should be unnecessary based on how I calculate them
        {
            abc = -abc;
        }
        if (Vector3.Dot(acd, simplex.b - simplex.a) > 0)
        {
            acd = -acd;
        }
        if (Vector3.Dot(adb, simplex.c - simplex.a) > 0)
        {
            adb = -adb;
        }*/
        bool outsideabc = Vector3.Dot(abc, ao) > 0;
        bool outsideacd = Vector3.Dot(acd, ao) > 0;
        bool outsideadb = Vector3.Dot(adb, ao) > 0;
        if (!outsideabc && !outsideacd && !outsideadb)
        {
            // the origin is inside the simplex
            ret.p = Vector3.zero;
            ret.dir = Vector3.zero;
            return ret;
        }
        if (outsideabc && !outsideacd && !outsideadb)
        {
            // the origin falls somewhere near abc
            simplex.count = 3;
            return TriangleSimplex(ref simplex);
        }
        if (!outsideabc && outsideacd && !outsideadb)
        {
            // the origin falls somewhere near acd
            simplex.b = simplex.c; simplex.b1 = simplex.c1; simplex.b2 = simplex.c2;
            simplex.c = simplex.d; simplex.c1 = simplex.d1; simplex.c2 = simplex.d2;
            simplex.count = 3;
            return TriangleSimplex(ref simplex);
        }
        if (!outsideabc && !outsideacd && outsideadb)
        {
            // the origin falls somewhere near adb
            simplex.c = simplex.b; simplex.c1 = simplex.b1; simplex.c2 = simplex.b2;
            simplex.b = simplex.d; simplex.b1 = simplex.d1; simplex.b2 = simplex.d2;
            simplex.count = 3;
            return TriangleSimplex(ref simplex);
        }
        // the origin potentially falls on multiple triangles
        ComplexSimplex simplex_abc = simplex.Copy();
        simplex_abc.count = 3;
        ComplexSimplex simplex_acd = simplex.Copy();
        simplex_acd.b = simplex_acd.c; simplex_acd.b1 = simplex_acd.c1; simplex_acd.b2 = simplex_acd.c2;
        simplex_acd.c = simplex_acd.d; simplex_acd.c1 = simplex_acd.d1; simplex_acd.c2 = simplex_acd.d2;
        simplex_acd.count = 3;
        ComplexSimplex simplex_adb = simplex.Copy();
        simplex_adb.c = simplex_adb.b; simplex_adb.c1 = simplex_adb.b1; simplex_adb.c2 = simplex_adb.b2;
        simplex_adb.b = simplex_adb.d; simplex_adb.b1 = simplex_adb.d1; simplex_adb.b2 = simplex_adb.d2;
        simplex_adb.count = 3;
        SimplexSolve abcsolved = TriangleSimplex(ref simplex_abc);
        SimplexSolve acdsolved = TriangleSimplex(ref simplex_acd);
        SimplexSolve adbsolved = TriangleSimplex(ref simplex_adb);
        float abc_d2 = Vector3.Dot(abcsolved.p, abcsolved.p);
        float acd_d2 = Vector3.Dot(acdsolved.p, acdsolved.p);
        float adb_d2 = Vector3.Dot(adbsolved.p, adbsolved.p);
        if (abc_d2 <= acd_d2 && abc_d2 <= adb_d2)
        {
            simplex = simplex_abc;
            return abcsolved;
        }
        else if (acd_d2 <= abc_d2 && acd_d2 <= adb_d2)
        {
            simplex = simplex_acd;
            return acdsolved;
        }
        else if (adb_d2 <= abc_d2 && adb_d2 <= acd_d2)
        {
            simplex = simplex_adb;
            return adbsolved;
        }
        // the origin isn't outside of any plane, so it's inside the tretrahedron
        ret = new SimplexSolve();
        ret.p = Vector3.zero;
        ret.dir = Vector3.zero;
        return ret;
    }

    float GetDirStrength(Vector3 v, Vector3 direction)
    {
        return v.x * direction.x + v.y * direction.y + v.z * direction.z;
    }

    SimplexSolve GetLocalPoints(ComplexSimplex simplex, Vector3 p) //return type struct instead of List<> for speed
    {
        Vector3 cartesian1;
        Vector3 cartesian2;
        SimplexSolve ret = new SimplexSolve();
        switch (simplex.count)
        {
            case 1:
                ret.p = simplex.a1; ret.dir = simplex.a2;
                return ret;
            case 2:
                float fbarycentric = fConvertBarycentric(simplex.a, simplex.b, p);
                cartesian1 = ConvertCartesian(simplex.a1, simplex.b1, fbarycentric);
                cartesian2 = ConvertCartesian(simplex.a2, simplex.b2, fbarycentric);
                ret.p = cartesian1; ret.dir = cartesian2;
                return ret;
            case 3:
                Vector3 v3barycentric = v3ConvertBarycentric(simplex.a, simplex.b, simplex.c, p);
                cartesian1 = ConvertCartesian(simplex.a1, simplex.b1, simplex.c1, v3barycentric);
                cartesian2 = ConvertCartesian(simplex.a2, simplex.b2, simplex.c2, v3barycentric);
                ret.p = cartesian1; ret.dir = cartesian2;
                return ret;
            default:
                ret.p = Vector3.zero; ret.dir = Vector3.zero;
                return ret;
        }
    }

    Vector3 v3ConvertBarycentric(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        Vector3 abc = Vector3.Cross(b - a, c - a);
        float nu;
        float nv;
        float ood;
        float x = Math.Abs(abc.x);
        float y = Math.Abs(abc.y);
        float z = Math.Abs(abc.z);
        if (x >= y && x >= z)
        {
            // x is the largest so project onto the yz plane
            nu = Get2DTriangleArea(p.y, p.z, b.y, b.z, c.y, c.z);
            nv = Get2DTriangleArea(p.y, p.z, c.y, c.z, a.y, a.z);
            ood = 1f / abc.x;
        }
        else if (y >= x && y >= z)
        {
            // y is the largest so project onto the xz plane
            nu = Get2DTriangleArea(p.x, p.z, b.x, b.z, c.x, c.z);
            nv = Get2DTriangleArea(p.x, p.z, c.x, c.z, a.x, a.z);
            ood = 1f / -abc.y;
        }
        else
        {
            // z is the largest so project onto the xy plane
            nu = Get2DTriangleArea(p.x, p.y, b.x, b.y, c.x, c.y);
            nv = Get2DTriangleArea(p.x, p.y, c.x, c.y, a.x, a.y);
            ood = 1f / abc.z;
        }
        Vector3 ret = new Vector3();
        ret.x = nu * ood;
        ret.y = nv * ood;
        ret.z = 1f - ret.x - ret.y;
        return ret;
    }

    float Get2DTriangleArea(float x1, float y1, float x2, float y2, float x3, float y3)
    {
        return (x1 - x2) * (y2 - y3) - (x2 - x3) * (y1 - y2);
    }

    Vector3 ConvertCartesian(Vector3 a, Vector3 b, Vector3 c, Vector3 barycentric)
    {
        Vector3 ret = new Vector3();
        ret.x = a.x * barycentric.x + b.x * barycentric.y + c.x * barycentric.z;
        ret.y = a.y * barycentric.x + b.y * barycentric.y + c.y * barycentric.z;
        ret.z = a.z * barycentric.x + b.z * barycentric.y + c.z * barycentric.z;
        return ret;
    }

    float fConvertBarycentric(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        Vector3 ap = p - a;
        return Vector3.Dot(ap, ab) / Vector3.Dot(ab, ab);
    }

    Vector3 ConvertCartesian(Vector3 a, Vector3 b, float barycentric)
    {
        Vector3 ret = new Vector3();
        ret.x = a.x + barycentric * (b.x - a.x);
        ret.y = a.y + barycentric * (b.y - a.y);
        ret.z = a.z + barycentric * (b.z - a.z);
        return ret;
    }
}
