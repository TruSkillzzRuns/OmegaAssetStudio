using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;
using UpkManager.Models.UpkFile.Engine.Mesh;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Converters;

public sealed class MeshConversionResult
{
    public NeutralMesh Mesh { get; set; } = new();
    public NeutralSkeleton Skeleton { get; set; } = new();
}

public sealed class MeshConverter148ToNeutral
{
    public IReadOnlyList<MeshConversionResult> Convert(Upk148ExportTableEntry entry, Action<string>? log = null)
    {
        if (TryHydrate(entry, out USkeletalMesh? skeletalMesh, out UStaticMesh? staticMesh, log))
        {
            if (skeletalMesh is not null)
                return [ConvertSkeletalMesh(entry, skeletalMesh, log)];

            if (staticMesh is not null)
                return [ConvertStaticMesh(entry, staticMesh, log)];
        }

        return [];
    }

    private static bool TryHydrate(Upk148ExportTableEntry entry, out USkeletalMesh? skeletalMesh, out UStaticMesh? staticMesh, Action<string>? log)
    {
        skeletalMesh = null;
        staticMesh = null;

        if (UpkExportHydrator.TryHydrate(entry, out skeletalMesh, log, false) && skeletalMesh is not null)
            return true;

        if (UpkExportHydrator.TryHydrate(entry, out staticMesh, log, false) && staticMesh is not null)
            return true;

        return false;
    }

    private static MeshConversionResult ConvertSkeletalMesh(Upk148ExportTableEntry entry, USkeletalMesh skeletalMesh, Action<string>? log)
    {
        FStaticLODModel? lod = skeletalMesh.LODModels?.FirstOrDefault();
        NeutralMesh neutralMesh = new()
        {
            Name = string.IsNullOrWhiteSpace(entry.ObjectName) ? entry.PathName : entry.ObjectName,
            IsSkeletal = true,
            Skeleton = ConvertSkeleton(entry, skeletalMesh)
        };

        if (lod?.VertexBufferGPUSkin is not null)
        {
            foreach (GLVertex vertex in lod.VertexBufferGPUSkin.GetGLVertexData() ?? [])
            {
                neutralMesh.Vertices.Add(new NeutralVertex(
                    vertex.Position,
                    vertex.Normal,
                    vertex.Tangent,
                    vertex.Bitangent,
                    vertex.TexCoord,
                    BuildWeights(skeletalMesh, vertex)));
            }
        }

        if (lod is not null)
        {
            neutralMesh.Indices.AddRange(lod.MultiSizeIndexContainer?.IndexBuffer?.Select(index => (int)index) ?? []);
            neutralMesh.Sections.AddRange((lod.Sections ?? []).Select((section, index) =>
                new NeutralSection($"Section {index}", (int)section.MaterialIndex, (int)section.BaseIndex, (int)(section.NumTriangles * 3))));
            neutralMesh.Lods.Add(new NeutralLod(0, 0, neutralMesh.Vertices.Count, 0, neutralMesh.Indices.Count));
        }

        neutralMesh.MaterialSlots.AddRange((skeletalMesh.Materials ?? []).Select((material, index) =>
            new NeutralMaterialSlot(material?.ToString() ?? $"Material {index}", index, material?.ToString())));
        neutralMesh.Sockets.AddRange((skeletalMesh.Sockets ?? []).Where(socket => socket is not null).Select(socket =>
        {
            USkeletalMeshSocket? loadedSocket = socket.LoadObject<USkeletalMeshSocket>();
            return new NeutralSocket(
                loadedSocket?.SocketName?.Name ?? socket?.GetPathName() ?? string.Empty,
                loadedSocket?.BoneName?.Name ?? string.Empty,
                loadedSocket?.RelativeLocation?.ToVector3() ?? Vector3.Zero,
                loadedSocket?.RelativeRotation is not null
                    ? new Vector3(loadedSocket.RelativeRotation.Pitch, loadedSocket.RelativeRotation.Yaw, loadedSocket.RelativeRotation.Roll)
                    : Vector3.Zero,
                loadedSocket?.RelativeScale?.ToVector3() ?? Vector3.One);
        }));
        neutralMesh.Bounds = ToBounds(skeletalMesh.Bounds);
        log?.Invoke($"Converted skeletal mesh {entry.PathName} into {neutralMesh.Vertices.Count} vertices.");

        return new MeshConversionResult { Mesh = neutralMesh, Skeleton = neutralMesh.Skeleton ?? new NeutralSkeleton() };
    }

    private static MeshConversionResult ConvertStaticMesh(Upk148ExportTableEntry entry, UStaticMesh staticMesh, Action<string>? log)
    {
        NeutralMesh neutralMesh = new()
        {
            Name = string.IsNullOrWhiteSpace(entry.ObjectName) ? entry.PathName : entry.ObjectName,
            IsSkeletal = false
        };

        if (staticMesh.LODModels is not null && staticMesh.LODModels.Count > 0)
        {
            FStaticMeshRenderData lod = staticMesh.LODModels[0];
            foreach (GLVertex vertex in lod.GetGLVertexData() ?? [])
            {
                neutralMesh.Vertices.Add(new NeutralVertex(
                    vertex.Position,
                    vertex.Normal,
                    vertex.Tangent,
                    vertex.Bitangent,
                    vertex.TexCoord,
                    []));
            }

            neutralMesh.Indices.AddRange(lod.IndexBuffer?.Indices?.Select(index => (int)index) ?? []);
            neutralMesh.Sections.Add(new NeutralSection("StaticSection0", 0, 0, neutralMesh.Indices.Count));
            neutralMesh.Lods.Add(new NeutralLod(0, 0, neutralMesh.Vertices.Count, 0, neutralMesh.Indices.Count));
        }

        neutralMesh.Bounds = ToBounds(staticMesh.Bounds);
        neutralMesh.MaterialSlots.Add(new NeutralMaterialSlot(entry.PathName, 0, entry.PathName));
        log?.Invoke($"Converted static mesh {entry.PathName} into {neutralMesh.Vertices.Count} vertices.");
        return new MeshConversionResult { Mesh = neutralMesh, Skeleton = new NeutralSkeleton { Name = neutralMesh.Name } };
    }

    private static NeutralSkeleton ConvertSkeleton(Upk148ExportTableEntry entry, USkeletalMesh skeletalMesh)
    {
        NeutralSkeleton skeleton = new() { Name = entry.PathName };
        for (int index = 0; index < skeletalMesh.RefSkeleton.Count; index++)
        {
            FMeshBone bone = skeletalMesh.RefSkeleton[index];
            Quaternion rotation = bone.BonePos.Orientation.ToQuaternion();
            Vector3 position = bone.BonePos.Position.ToVector3();
            skeleton.Bones.Add(new NeutralBone(
                bone.Name.Name,
                bone.ParentIndex,
                position,
                rotation,
                Vector3.One));
            skeleton.Links.Add(new NeutralSkeletonLink(
                bone.Name.Name,
                index,
                Matrix4x4.CreateFromQuaternion(rotation)));
        }
        return skeleton;
    }

    private static NeutralBoneWeight[] BuildWeights(USkeletalMesh skeletalMesh, GLVertex vertex)
    {
        static NeutralBoneWeight MakeWeight(USkeletalMesh mesh, byte boneIndex, byte weight)
        {
            string boneName = boneIndex < mesh.RefSkeleton.Count ? mesh.RefSkeleton[boneIndex].Name.Name : $"Bone_{boneIndex}";
            return new NeutralBoneWeight(boneName, boneIndex, weight / 255.0f);
        }

        return
        [
            MakeWeight(skeletalMesh, vertex.Bone0, vertex.Weight0),
            MakeWeight(skeletalMesh, vertex.Bone1, vertex.Weight1),
            MakeWeight(skeletalMesh, vertex.Bone2, vertex.Weight2),
            MakeWeight(skeletalMesh, vertex.Bone3, vertex.Weight3)
        ];
    }

    private static NeutralBoundingBox ToBounds(UpkManager.Models.UpkFile.Core.FBoxSphereBounds bounds)
    {
        Vector3 min = bounds.Origin.ToVector3() - bounds.BoxExtent.ToVector3();
        Vector3 max = bounds.Origin.ToVector3() + bounds.BoxExtent.ToVector3();
        return new NeutralBoundingBox(min, max);
    }
}

