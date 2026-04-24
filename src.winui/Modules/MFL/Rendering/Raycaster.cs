using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;
using OmegaAssetStudio.WinUI.Modules.MFL.Viewport;
using Windows.Foundation;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Rendering;

public sealed class Raycaster
{
    public bool TryHitTest(Scene scene, Camera camera, Point point, double viewportWidth, double viewportHeight, out MeshHitResult? hitResult)
    {
        hitResult = null;
        if (scene is null || camera is null || viewportWidth <= 0 || viewportHeight <= 0)
            return false;

        MeshHitResult? bestHit = null;
        float bestDistance = float.MaxValue;

        foreach (MeshNode node in scene.Nodes)
        {
            if (!node.IsVisible || node.Mesh is null)
                continue;

            MeshHitResult? hit = HitTestMesh(scene, camera, node, point, viewportWidth, viewportHeight);
            if (hit is null || hit.Distance >= bestDistance)
                continue;

            bestDistance = hit.Distance;
            bestHit = hit;
        }

        hitResult = bestHit;
        return hitResult is not null;
    }

    private static MeshHitResult? HitTestMesh(Scene scene, Camera camera, MeshNode node, Point point, double viewportWidth, double viewportHeight)
    {
        if (node.Mesh is null)
            return null;

        ViewportRay ray = camera.CreateRay(point, viewportWidth, viewportHeight);
        Mesh mesh = node.Mesh;
        Matrix4x4 world = node.WorldTransform;
        float closest = float.MaxValue;
        int closestTriangle = -1;
        Vector3 hitPoint = Vector3.Zero;

        for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
        {
            Triangle triangle = mesh.Triangles[triangleIndex];
            if (triangle.A < 0 || triangle.B < 0 || triangle.C < 0
                || triangle.A >= mesh.Vertices.Count
                || triangle.B >= mesh.Vertices.Count
                || triangle.C >= mesh.Vertices.Count)
            {
                continue;
            }

            Vector3 a = Vector3.Transform(mesh.Vertices[triangle.A].Position, world);
            Vector3 b = Vector3.Transform(mesh.Vertices[triangle.B].Position, world);
            Vector3 c = Vector3.Transform(mesh.Vertices[triangle.C].Position, world);

            if (TryIntersectTriangle(ray.Origin, ray.Direction, a, b, c, out float distance, out Vector3 triangleHit) && distance < closest)
            {
                closest = distance;
                closestTriangle = triangleIndex;
                hitPoint = triangleHit;
            }
        }

        if (closestTriangle < 0)
            return null;

        return new MeshHitResult
        {
            MeshKey = ReferenceEquals(node, scene.MeshNodeB) ? "MeshB" : "MeshA",
            TriangleIndex = closestTriangle,
            VertexIndex = -1,
            HitPoint = hitPoint,
            Distance = closest
        };
    }

    private static bool TryIntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 hitPoint)
    {
        distance = 0.0f;
        hitPoint = Vector3.Zero;
        const float epsilon = 0.000001f;
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 h = Vector3.Cross(direction, edge2);
        float det = Vector3.Dot(edge1, h);

        if (det > -epsilon && det < epsilon)
            return false;

        float invDet = 1.0f / det;
        Vector3 s = origin - a;
        float u = invDet * Vector3.Dot(s, h);
        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = invDet * Vector3.Dot(direction, q);
        if (v < 0.0f || u + v > 1.0f)
            return false;

        float t = invDet * Vector3.Dot(edge2, q);
        if (t <= epsilon)
            return false;

        distance = t;
        hitPoint = origin + direction * t;
        return true;
    }
}

