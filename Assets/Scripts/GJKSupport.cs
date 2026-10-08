using UnityEngine;

// A GJK és az EPA által közösen használt support függvények.
// A delegate-ek ezekre mutatnak majd, így az EPA független a konkrét alakzattól.
public static class GJKSupport
{
    // Tengelyre igazított doboz support-ja, origó körül.
    // direction: a support keresési iránya (world)
    // halfExtents: a doboz félméretei
    public static Vector3 PlayerBox(Vector3 direction, Vector3 halfExtents)
    {
        return new Vector3(
            direction.x >= 0 ? halfExtents.x : -halfExtents.x,
            direction.y >= 0 ? halfExtents.y : -halfExtents.y,
            direction.z >= 0 ? halfExtents.z : -halfExtents.z
        );
    }

    // MeshCollider support-ja world-space-ben.
    public static Vector3 Mesh(Vector3 direction, MeshCollider meshCollider)
    {
        Transform meshTransform = meshCollider.transform;
        Mesh mesh = meshCollider.sharedMesh;
        Vector3[] vertices = mesh.vertices;

        float bestDot = float.NegativeInfinity;
        Vector3 bestVertex = Vector3.zero;

        foreach (Vector3 localVertex in vertices)
        {
            Vector3 worldVertex = meshTransform.TransformPoint(localVertex);
            float dot = Vector3.Dot(direction, worldVertex);
            if (dot > bestDot) { bestDot = dot; bestVertex = worldVertex; }
        }
        return bestVertex;
    }

    // Swept doboz support-ja (a GJK swept változatához).
    public static Vector3 SweptPlayerBox(Vector3 direction, Vector3 halfExtents, Vector3 wishMove)
    {
        Vector3 unswept = PlayerBox(direction, halfExtents);
        return Vector3.Dot(direction, wishMove) > 0f ? unswept + wishMove : unswept;
    }
}