using OmegaAssetStudio.Unreal.SkeletalMesh;

namespace OmegaAssetStudio.WinUI.Modules.Meshes.Import;

public sealed class MeshImporter
{
    private readonly MeshFbxParser parser = new();
    private readonly MeshLodBuilder lodBuilder = new();

    public SkeletalMesh ImportFbx(SkeletalMesh skeletalMesh, string fbxPath)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentException.ThrowIfNullOrWhiteSpace(fbxPath);

        FBXMeshData meshData = parser.Parse(fbxPath);
        ImportFbx(skeletalMesh, meshData);
        return skeletalMesh;
    }

    public SkeletalMesh ImportFbx(SkeletalMesh skeletalMesh, FBXMeshData meshData)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(meshData);

        IReadOnlyList<FBXLodData> lods = meshData.Lods.Count > 0
            ? meshData.Lods.OrderBy(item => item.LodIndex).ToList()
            : [new FBXLodData { LodIndex = 0, Name = "LOD0" }];

        foreach (FBXLodData lodData in lods)
            lodBuilder.BuildOrUpdateLod(skeletalMesh, meshData, lodData.LodIndex);

        skeletalMesh.RecalculateBounds();
        return skeletalMesh;
    }

    public async Task<SkeletalMesh> ImportFbxAsync(SkeletalMesh skeletalMesh, string fbxPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentException.ThrowIfNullOrWhiteSpace(fbxPath);

        FBXMeshData meshData = await parser.ParseAsync(fbxPath, cancellationToken).ConfigureAwait(false);
        return ImportFbx(skeletalMesh, meshData);
    }
}

