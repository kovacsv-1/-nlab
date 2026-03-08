using UnityEngine;
using System.Collections.Generic;
using System;

public class SweptTraces : MonoBehaviour
{
    // minkowski sum and GJK as explained here:
    // https://www.youtube.com/watch?v=Qupqu1xe7Io
    // well optimized, but doesn't give closest points/normals, currently used for double-checking boxcast results
    // and coded here:
    // https://github.com/kujukuju/KodaPhysics/blob/master/src/Gjk.jai


    //currently enough to check unswept traces, and collisions in swept hull, but not time of impact or closest point or separation

    //you can rotete direction by (opposite of) player rotation if player is rotated
    Vector3 SupportPlayerBox(Vector3 direction, Vector3 halfExtents) //bounds of axis-aligned bounding box, support to find the best vertex, doesn't take playertransform, just around 000; might replace wiht player meshcollider, so it works with other shapes
    {
        return new Vector3(
            direction.x >= 0 ? halfExtents.x : -halfExtents.x, //avoid centre of edges being returned as vertices
            direction.y >= 0 ? halfExtents.y : -halfExtents.y,
            direction.z >= 0 ? halfExtents.z : -halfExtents.z
        );
    }

    //get convex hull of all points playercollision covered during movement
    //if the wishdir points towards dir, the already maximum pushed by wishdir will be further, and still the maximum in pushed shape
    Vector3 SupportSweptBox(Vector3 direction, Vector3 halfExtents, Vector3 wishMove)
    {
        Vector3 unswept = SupportPlayerBox(direction, halfExtents);
        return Vector3.Dot(direction, wishMove) > 0f ? unswept + wishMove : unswept;
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

    Vector3 SupportMinkowskiDiff(Vector3 direction, MeshCollider meshCollider, Vector3 playerHalfExtents, Vector3 playerPos) //support function for signed minkowski sum, if replacing player with meshcollider, remove playerpos
    {
        Vector3 meshSupport = SupportMesh(direction, meshCollider);
        Vector3 boxSupport = playerPos + SupportPlayerBox(-direction, playerHalfExtents);
        return meshSupport - boxSupport;
    }

    Vector3 SupportSweptMinkowskiDiff(Vector3 direction, MeshCollider meshCollider, Vector3 playerHalfExtents, Vector3 playerPos, Vector3 wishMove) //support function for signed minkowski sum, if replacing player with meshcollider, remove playerpos
    {
        Vector3 meshSupport = SupportMesh(direction, meshCollider);
        Vector3 boxSupport = playerPos + SupportSweptBox(-direction, playerHalfExtents, wishMove);
        return meshSupport - boxSupport;
    }

    //start with any point
    //any point of A - any of B
    //support function computes a and b and a-b
    //call support in any direction to get a point
    //take the point and add it to list of points
    //new direction is origin - the point
    //loop (build a tetrahedron to enclose the origin)
    //  A = new point in direction
    //  if (A dot Direction < 0) -> no intersection, will never be one, return (as we always remain on this side of origin [since we started the other way, got 2 opposite sides of sum and still didn't cross an 0s])
    //  add A to list of points
    //  if DoSimplex(list of points, direction) -> if true, intersection found, return


    public bool GJKPointInside(MeshCollider mesh, Vector3 playerHalfExtents, Vector3 playerPos)
    {
        //random support point
        Vector3 dir = Vector3.one;
        Vector3 S = SupportMinkowskiDiff(dir, mesh, playerHalfExtents, playerPos);
        var simplex = new List<Vector3> { S };
        dir = -S; //vector from simplex point to origin

        for (int i = 0; i < 30; i++) //max iterations could be 20
        {
            Vector3 A = SupportMinkowskiDiff(dir, mesh, playerHalfExtents, playerPos);

            if (Vector3.Dot(A, dir) < 0) //no intersection
            {
                return false;
            }
            simplex.Add(A);
            if (DoSimplex(simplex, out simplex, out dir))
            {
                return true;
            }
        }
        return false;
    }

    public bool GJKSweptIntersects(MeshCollider mesh, Vector3 playerHalfExtents, Vector3 playerPos, Vector3 wishMove)
    {
        //random support point
        Vector3 dir = Vector3.one;
        Vector3 S = SupportSweptMinkowskiDiff(dir, mesh, playerHalfExtents, playerPos, wishMove);
        var simplex = new List<Vector3> { S };
        dir = -S; //vector from simplex point to origin

        for (int i = 0; i < 30; i++) //max iterations could be 40
        {
            Vector3 A = SupportSweptMinkowskiDiff(dir, mesh, playerHalfExtents, playerPos, wishMove);

            if (Vector3.Dot(A, dir) < 0) //no intersection
            {
                return false;
            }
            simplex.Add(A);
            if (DoSimplex(simplex, out simplex, out dir))
            {
                return true;
            }
        }
        return false;
    }

    //bool DoSimplex(list, dir)
    //case 1 -> add a point immediately, actually redundant as we add a new point before calling this and this always returns at least one point anyways
    //case 2 -> CAN BE REDUCED TO NO IF CHECKS, YOU KNOW THE POINT IS PAST THE ORIGIN; check where origin is, never return the point that was older, we checked the other direction, so it is either the new point (origin is farther and we cannot reach) or between (B was the older in the video)
    //  if (older point - newer point dot origin - newer point > 0) -> return list of points [newer, older] and Vector3 (older-newer) cross (origin-newer) cross (older-newer) as search direction
    //  else -> return [newer] as list of points and origin - newer as search direction
    //case 3 -> triangle case, one side of the trigangle is definitely on the wrong side of the origin, so we can ignore cases where the origin would be on that side (as our search direction to find the newest point was towards the origin); B and C are older points, A is new; ABC is (B - A) cross (C - A)
    //  if (ABC cross C - A dot origin - A > 0)
    //      if (C - A dot origin - A > 0) -> return [A, C]; C - A cross origin - A cross C - A
    //      else -> 
    //          if (B - A dot origin - A > 0) -> return [A, B] and B - A cross origin - A cross B - A
    //          else -> return [A] and origin - A
    //  else ->
    //      if (B - A cross ABC dot origin - A > 0) ->
    //          if (B - A dot origin - A > 0) -> return [A, B] and B - A cross origin - A cross B - A
    //          else -> return [A] and origin - A
    //      else ->
    //          if (ABC dot origin - A > 0) -> return [A, B, C] and ABC
    //          else -> return [A, C, B] and -ABC
    //case 4 -> way too many ifs... (6 nested, could be optimized)
    //  if ()

    bool DoSimplex(List<Vector3> simplex, out List<Vector3> newSimplex, out Vector3 dir)
    {
        newSimplex = new List<Vector3>();
        dir = Vector3.zero;
        switch (simplex.Count)
        {
            case 1:
                return PointSimplex(simplex, out newSimplex, out dir);
            case 2:
                return LineSimplex(simplex, out newSimplex, out dir);
            case 3:
                return TriangleSimplex(simplex, out newSimplex, out dir);
            case 4:
                return TetrahedronSimplex(simplex, out newSimplex, out dir);
            default:
                Debug.Log("huh?");
                break;
        }
        return false;
    }

    bool PointSimplex(List<Vector3> simplex, out List<Vector3> newSimplex, out Vector3 dir)
    {
        newSimplex = simplex;
        dir = -simplex[0];
        return false;
    }

    bool LineSimplex(List<Vector3> simplex, out List<Vector3> newSimplex, out Vector3 dir)
    {
        Vector3 B = simplex[0]; Vector3 A = simplex[1];
        Vector3 AB = B - A; Vector3 AO = -A;
        if (Vector3.Dot(AB, AO) > 0)
        {
            newSimplex = new List<Vector3> {A, B};
            dir = Vector3.Cross(Vector3.Cross(AB, AO), AB);
        }
        else
        {
            newSimplex = new List<Vector3> {A};
            dir = AO;
        }
        return false;
    }

    bool TriangleSimplex(List<Vector3> simplex, out List<Vector3> newSimplex, out Vector3 dir)
    {
        Vector3 A = simplex[2]; Vector3 B = simplex[1]; Vector3 C = simplex[0]; Vector3 ABC = Vector3.Cross(B - A, C - A); //vertices and normal of triangle
        Vector3 AC = C - A; Vector3 AO = -A; Vector3 AB = B - A;
        if (Vector3.Dot(Vector3.Cross(ABC, AC), AO) > 0)
        {
            if (Vector3.Dot(AC, AO) > 0)
            {
                newSimplex = new List<Vector3> {A, C};
                dir = Vector3.Cross(Vector3.Cross(AC, AO), AC);
            }
            else
            {
                if (Vector3.Dot(AB, AO) > 0)
                {
                    newSimplex = new List<Vector3> {A, B};
                    dir = Vector3.Cross(Vector3.Cross(AB, AO), AB);
                }
                else
                {
                    newSimplex = new List<Vector3> {A};
                    dir = AO;
                }
            }
        }
        else
        {
            if (Vector3.Dot(Vector3.Cross(AB, ABC), AO) > 0)
            {
                if (Vector3.Dot(AB, AO) > 0)
                {
                    newSimplex = new List<Vector3> {A, B};
                    dir = Vector3.Cross(Vector3.Cross(AB, AO), AB);
                }
                else
                {
                    newSimplex = new List<Vector3> {A};
                    dir = AO;
                }
            }
            else
            {
                if (Vector3.Dot(ABC, AO) > 0)
                {
                    newSimplex = new List<Vector3> {A, B, C};
                    dir = ABC;
                }
                else
                {
                    newSimplex = new List<Vector3> {A, C, B};
                    dir = -ABC;
                }
            }
        }
        return false;
    }

    bool TetrahedronSimplex(List<Vector3> simplex, out List<Vector3> newSimplex, out Vector3 dir)
    {
        Vector3 A = simplex[3]; Vector3 B = simplex[2]; Vector3 C = simplex[1]; Vector3 D = simplex[0];
        Vector3 AO = -A; Vector3 ABC = Vector3.Cross(B - A, C - A); Vector3 ACD = Vector3.Cross(C - A, D - A); Vector3 ADB = Vector3.Cross(D - A, B - A); Vector3 BCD = Vector3.Cross(C - B, D - B);
        if (Vector3.Dot(ABC, D - A) > 0) //if normals pointing inward, make them point outward, should be unnecessary based on how I calculate them
        {
            ABC = -ABC;
        }
        if (Vector3.Dot(ACD, B - A) > 0)
        {
            ACD = -ACD;
        }
        if (Vector3.Dot(ADB, C - A) > 0)
        {
            ADB = -ADB;
        }
        bool outsideABC = Vector3.Dot(ABC, AO) > 0; bool outsideACD = Vector3.Dot(ACD, AO) > 0; bool outsideADB = Vector3.Dot(ADB, AO) > 0; //bool outsideBCD = Vector3.Dot(BCD, -B) > 0;
        newSimplex = simplex;
        dir = AO;
        if (outsideABC)
        {
            simplex[0] = C; //remove D, as that is furthest
            simplex[1] = B; //shift all others
            simplex[2] = A; //keep A as newest
            simplex.RemoveAt(3);
            return TriangleSimplex(simplex, out newSimplex, out dir);
        }
        if (outsideACD)
        {
            simplex[0] = D; //these two remain the same and will be outoptimised but for readability they are here
            simplex[1] = C;
            simplex[2] = A; //shift A to B, as that is furthest, while keeping A as newest
            simplex.RemoveAt(3);
            return TriangleSimplex(simplex, out newSimplex, out dir);
        }
        if (outsideADB)
        {
            simplex[0] = B;
            simplex[1] = D;
            simplex[2] = A;
            simplex.RemoveAt(3);
            return TriangleSimplex(simplex, out newSimplex, out dir);
        }
        newSimplex = simplex;
        dir = AO;
        return true;
    }

    //support of a curve is "easy", for spheres you just take the center + radius * direction; any other curve that has a function can be derived along the direction to find a maximum

    //you can use swept collision, you can just make a support for the swept shape

    //shape expanded by x in all directions? just add x to the support function in the diirection of direction
}