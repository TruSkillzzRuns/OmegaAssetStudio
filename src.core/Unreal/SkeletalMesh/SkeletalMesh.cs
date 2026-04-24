using System.Numerics;

namespace OmegaAssetStudio.Unreal.SkeletalMesh;

public class SkeletalMesh
{
    public string Name { get; set; } = string.Empty;

    public List<SkeletalMeshLodModel> LODModels { get; } = [];

    public List<string> Materials { get; } = [];

    public Vector3 BoundsMin { get; set; } = Vector3.Zero;

    public Vector3 BoundsMax { get; set; } = Vector3.Zero;

    public void RecalculateBounds()
    {
        bool hasValue = false;
        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;

        foreach (SkeletalMeshLodModel lodModel in LODModels)
        {
            foreach (var vertex in lodModel.VertexBuffer.Vertices)
            {
                if (!hasValue)
                {
                    min = vertex.Position;
                    max = vertex.Position;
                    hasValue = true;
                    continue;
                }

                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }
        }

        BoundsMin = hasValue ? min : Vector3.Zero;
        BoundsMax = hasValue ? max : Vector3.Zero;
    }
}

