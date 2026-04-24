using System.Collections.Generic;
using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;

public sealed class NeutralSkeleton
{
    public string Name { get; set; } = string.Empty;
    public List<NeutralBone> Bones { get; } = [];
    public List<NeutralSkeletonLink> Links { get; } = [];
}

public sealed record NeutralBone(string Name, int ParentIndex, Vector3 Position, Quaternion Rotation, Vector3 Scale);

public sealed record NeutralSkeletonLink(string BoneName, int BoneIndex, Matrix4x4 ReferenceTransform);

