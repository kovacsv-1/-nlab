using UnityEngine;
using System.Collections.Generic;

public static class GJKDirCast
{
    const float EPSILON = 0.0001f;
    const float SMALL_EPSILON = 0.000001f;
    const int MAX_ITERATIONS = 64;

    /// <summary>
    /// Perform a GJK ray cast of shapeA (player) moving along 'dir' against shapeB (mesh).
    /// Both vertex lists must be in world space.
    /// Returns true if a hit occurs within the move, along with the hit distance, contact normal,
    /// and the contact point on shapeB.
    /// </summary>
    public static bool Cast(
        List<Vector3> shapeAVertices,
        List<Vector3> shapeBVertices,
        Vector3 dir,
        out float hitDistance,
        out Vector3 normal,
        out Vector3 pointOnB
    )
    {
        hitDistance = 0f;
        normal = Vector3.zero;
        pointOnB = Vector3.zero;

        // Movement direction must be meaningful
        if (dir.sqrMagnitude < 1e-8f) return false;

        // The ray we trace is actually in the opposite direction of movement
        // (because the CSO is built as B - A, and A moves toward B along dir)
        Vector3 direction = -dir;

        // ------- Initialisation: find a triangle that contains the ray -------
        // pick a search direction that isn't parallel to the ray
        Vector3 searchDirection = Vector3.Cross(direction, Vector3.up);
        if (searchDirection.sqrMagnitude < EPSILON)
            searchDirection = Vector3.Cross(direction, Vector3.forward);
        searchDirection = (searchDirection + direction) / 2f;

        // Create initial line simplex (two points)
        Simplex simplex1 = new Simplex();
        Simplex simplex2 = new Simplex();
        Simplex simplex = new Simplex();

        simplex1.count = 2;
        simplex1.a = Support(shapeAVertices, -searchDirection);
        simplex2.count = 2;
        simplex2.a = Support(shapeBVertices, searchDirection);
        simplex.count = 2;
        simplex.a = simplex2.a - simplex1.a;

        // Second point using a different search direction
        Vector3 searchNormal = Vector3.Cross(searchDirection, direction);
        searchDirection = searchNormal;
        searchDirection = (searchDirection + direction) / 2f;
        simplex1.b = Support(shapeAVertices, -searchDirection);
        simplex2.b = Support(shapeBVertices, searchDirection);
        simplex.b = simplex2.b - simplex1.b;

        float furthestDistance = 0f;
        Simplex furthestSimplex1 = new Simplex();

        // ---------- Loop 1: bring the ray inside a triangle ----------
        for (int iter = 0; iter < MAX_ITERATIONS; iter++)
        {
            if (simplex.count == 2)
            {
                Vector3 a = simplex.a, b = simplex.b;
                Vector3 ab = b - a;
                Vector3 abdir = Vector3.Cross(ab, a);
                if (abdir.sqrMagnitude < EPSILON)
                    return false;   // aligned with origin

                Vector3 abc = Vector3.Cross(abdir, ab);
                Vector3 triangleNormal = abc.normalized;
                float denom = Vector3.Dot(triangleNormal, direction);
                if (Mathf.Abs(denom) < EPSILON)
                    return false;

                Vector3 projected = Vector3.Dot(a, triangleNormal) / denom * direction;
                Vector3 projectedNormal = Vector3.Cross(ab, projected - a);

                if (projectedNormal.sqrMagnitude < SMALL_EPSILON)
                {
                    // projection falls on the line
                    float t = Vector3.Dot(projected - a, ab) / ab.sqrMagnitude;
                    if (t >= 0f && t <= 1f)
                    {
                        // add a third point to form a triangle
                        searchDirection = Vector3.Cross(triangleNormal, ab);
                        simplex1.count = 3;
                        simplex1.c = Support(shapeAVertices, -searchDirection);
                        simplex2.count = 3;
                        simplex2.c = Support(shapeBVertices, searchDirection);
                        simplex.count = 3;
                        simplex.c = simplex2.c - simplex1.c;

                        if (Vector3.Dot(Vector3.Cross(simplex.c - simplex.a, simplex.b - simplex.a), direction) > 0)
                        {
                            // swap to keep orientation
                            Swap(ref simplex.a, ref simplex.b);
                            Swap(ref simplex1.a, ref simplex1.b);
                            Swap(ref simplex2.a, ref simplex2.b);
                        }

                        // ready for loop 2
                        break;
                    }
                    else
                    {
                        searchDirection = Vector3.Cross(triangleNormal, ab);
                    }
                }
                else
                {
                    searchDirection = Vector3.Cross(projectedNormal, ab);
                }

                // add a third point
                simplex1.count = 3;
                simplex1.c = Support(shapeAVertices, -searchDirection);
                simplex2.count = 3;
                simplex2.c = Support(shapeBVertices, searchDirection);
                simplex.count = 3;
                simplex.c = simplex2.c - simplex1.c;

                if (Vector3.Dot(Vector3.Cross(simplex.c - simplex.a, simplex.b - simplex.a), direction) > 0)
                {
                    Swap(ref simplex.a, ref simplex.b);
                    Swap(ref simplex1.a, ref simplex1.b);
                    Swap(ref simplex2.a, ref simplex2.b);
                }
            }
            else if (simplex.count == 3)
            {
                Vector3 a = simplex.a, b = simplex.b, c = simplex.c;
                Vector3 abc = Vector3.Cross(b - a, c - a);
                if (abc.sqrMagnitude < EPSILON)
                    return false;   // degenerate triangle

                Vector3 triangleNormal = abc.normalized;
                float denom = Vector3.Dot(triangleNormal, direction);
                if (denom < EPSILON)
                    return false;   // triangle facing wrong way

                Vector3 projected = Vector3.Dot(a, triangleNormal) / denom * direction;
                Vector3 ab = b - a, bc = c - b, ca = a - c;

                // reduce triangle if the projection falls outside
                if (Vector3.Dot(Vector3.Cross(ab, triangleNormal), projected - a) > EPSILON)
                {
                    // keep line ba
                    simplex.a = b; simplex1.a = simplex1.b; simplex2.a = simplex2.b;
                    simplex.b = a; simplex1.b = simplex1.a; simplex2.b = simplex2.a;
                    simplex.count = 2; simplex1.count = 2; simplex2.count = 2;
                }
                else if (Vector3.Dot(Vector3.Cross(bc, triangleNormal), projected - b) > EPSILON)
                {
                    // keep line cb
                    simplex.a = c; simplex1.a = simplex1.c; simplex2.a = simplex2.c;
                    simplex.b = b; simplex1.b = simplex1.b; simplex2.b = simplex2.b;
                    simplex.count = 2; simplex1.count = 2; simplex2.count = 2;
                }
                else if (Vector3.Dot(Vector3.Cross(ca, triangleNormal), projected - c) > EPSILON)
                {
                    // keep line ac
                    simplex.a = a; simplex1.a = simplex1.a; simplex2.a = simplex2.a;
                    simplex.b = c; simplex1.b = simplex1.c; simplex2.b = simplex2.c;
                    simplex.count = 2; simplex1.count = 2; simplex2.count = 2;
                }
                else
                {
                    // projection is inside the triangle – we are ready
                    furthestDistance = Vector3.Dot(a, triangleNormal) / denom;
                    furthestSimplex1 = simplex1;
                    break;
                }
            }
        }

        // If we never found a containing triangle, abort
        if (simplex.count != 3) return false;

        // ---------- Loop 2: push the triangle forward until hit ----------
        for (int iter = 0; iter < MAX_ITERATIONS; iter++)
        {
            Vector3 a = simplex.a, b = simplex.b, c = simplex.c;
            Vector3 bcd = Vector3.Cross(b - a, c - a);

            if (bcd.sqrMagnitude < EPSILON)
            {
                // degenerate triangle – stop here
                hitDistance = furthestDistance;
                normal = GetBestNormal(furthestSimplex1, dir);
                pointOnB = GetPointOnB(furthestSimplex1, simplex2, dir); // could be improved
                return true;
            }

            Vector3 normalBcd = bcd.normalized;
            float denom = Vector3.Dot(normalBcd, direction);
            if (denom < EPSILON)
            {
                hitDistance = furthestDistance;
                normal = GetBestNormal(furthestSimplex1, dir);
                pointOnB = GetPointOnB(furthestSimplex1, simplex2, dir);
                return true;
            }

            // Get a new support point in the direction of the triangle normal
            searchDirection = bcd;
            Vector3 a1 = Support(shapeAVertices, -searchDirection);
            Vector3 a2 = Support(shapeBVertices, searchDirection);
            Vector3 aNew = a2 - a1;

            // Check if the new point is approximately on the triangle plane
            float planeDist = Vector3.Dot(normalBcd, aNew) - Vector3.Dot(normalBcd, a);
            if (Mathf.Abs(planeDist) < EPSILON)
            {
                hitDistance = furthestDistance;
                normal = GetBestNormal(furthestSimplex1, dir);
                pointOnB = GetPointOnB(furthestSimplex1, simplex2, dir);
                return true;
            }

            // Form three new triangles (abc, acd, adb) with the new point
            Vector3 abc = Vector3.Cross(b - aNew, c - aNew);
            Vector3 acd = Vector3.Cross(c - aNew, a - aNew);
            Vector3 adb = Vector3.Cross(a - aNew, b - aNew);

            // Test which triangle contains the ray projection
            float denomAbc = Vector3.Dot(abc.normalized, direction);
            float denomAcd = Vector3.Dot(acd.normalized, direction);
            float denomAdb = Vector3.Dot(adb.normalized, direction);

            // Project point aNew onto each triangle's plane along the ray
            bool insideAbc = false, insideAcd = false, insideAdb = false;
            if (denomAbc > 0)
            {
                Vector3 projAbc = Vector3.Dot(aNew, abc.normalized) / denomAbc * direction;
                insideAbc = Vector3.Dot(Vector3.Cross(aNew - b, abc.normalized), projAbc - b) >= -EPSILON
                         && Vector3.Dot(Vector3.Cross(c - aNew, abc.normalized), projAbc - c) >= -EPSILON;
            }
            if (denomAcd > 0)
            {
                Vector3 projAcd = Vector3.Dot(aNew, acd.normalized) / denomAcd * direction;
                insideAcd = Vector3.Dot(Vector3.Cross(aNew - c, acd.normalized), projAcd - c) >= -EPSILON
                         && Vector3.Dot(Vector3.Cross(a - aNew, acd.normalized), projAcd - a) >= -EPSILON;
            }
            if (denomAdb > 0)
            {
                Vector3 projAdb = Vector3.Dot(aNew, adb.normalized) / denomAdb * direction;
                insideAdb = Vector3.Dot(Vector3.Cross(aNew - a, adb.normalized), projAdb - a) >= -EPSILON
                         && Vector3.Dot(Vector3.Cross(b - aNew, adb.normalized), projAdb - b) >= -EPSILON;
            }

            if (insideAbc)
            {
                // new simplex: aNew, b, c
                simplex1 = new Simplex { a = a1, b = simplex1.b, c = simplex1.c, count = 3 };
                simplex2 = new Simplex { a = a2, b = simplex2.b, c = simplex2.c, count = 3 };
                simplex = new Simplex { a = aNew, b = b, c = c, count = 3 };
                furthestDistance = Vector3.Dot(aNew, abc.normalized) / denomAbc;
                furthestSimplex1 = simplex1;
            }
            else if (insideAcd)
            {
                // new simplex: aNew, c, a
                simplex1 = new Simplex { a = a1, b = simplex1.c, c = simplex1.a, count = 3 };
                simplex2 = new Simplex { a = a2, b = simplex2.c, c = simplex2.a, count = 3 };
                simplex = new Simplex { a = aNew, b = c, c = a, count = 3 };
                furthestDistance = Vector3.Dot(aNew, acd.normalized) / denomAcd;
                furthestSimplex1 = simplex1;
            }
            else if (insideAdb)
            {
                // new simplex: aNew, a, b
                simplex1 = new Simplex { a = a1, b = simplex1.a, c = simplex1.b, count = 3 };
                simplex2 = new Simplex { a = a2, b = simplex2.a, c = simplex2.b, count = 3 };
                simplex = new Simplex { a = aNew, b = a, c = b, count = 3 };
                furthestDistance = Vector3.Dot(aNew, adb.normalized) / denomAdb;
                furthestSimplex1 = simplex1;
            }
            else
            {
                // unexpected – abort
                hitDistance = furthestDistance;
                normal = GetBestNormal(furthestSimplex1, dir);
                pointOnB = GetPointOnB(furthestSimplex1, simplex2, dir);
                return true;
            }
        }

        // Exhausted iterations – use the last known distance
        hitDistance = furthestDistance;
        normal = GetBestNormal(furthestSimplex1, dir);
        pointOnB = GetPointOnB(furthestSimplex1, simplex2, dir);
        return true;
    }

    // ------ Support function (max dot) ------
    static Vector3 Support(List<Vector3> vertices, Vector3 direction)
    {
        float bestDot = float.NegativeInfinity;
        Vector3 best = Vector3.zero;
        foreach (var v in vertices)
        {
            float d = Vector3.Dot(v, direction);
            if (d > bestDot) { bestDot = d; best = v; }
        }
        return best;
    }

    // ------ Normal from the final simplex of shape1 ------
    static Vector3 GetBestNormal(Simplex simplex, Vector3 rayDir)
    {
        if (simplex.count == 1) return rayDir.normalized;
        if (simplex.count == 2)
        {
            Vector3 ab = simplex.b - simplex.a;
            Vector3 n = Vector3.Cross(Vector3.Cross(ab, rayDir), ab);
            return n.normalized;
        }
        if (simplex.count == 3)
        {
            Vector3 n = Vector3.Cross(simplex.b - simplex.a, simplex.c - simplex.a);
            return n.normalized;
        }
        return rayDir.normalized;
    }

    // Approximate contact point on shapeB
    static Vector3 GetPointOnB(Simplex simplex1, Simplex simplex2, Vector3 dir)
    {
        // Use the last known support point from shape2 as the contact
        // In practice you’d compute the exact barycentric coords, but this is usually fine.
        return simplex2.a;
    }

    static void Swap(ref Vector3 a, ref Vector3 b) { Vector3 t = a; a = b; b = t; }

    struct Simplex
    {
        public Vector3 a, b, c;
        public int count;
    }
}