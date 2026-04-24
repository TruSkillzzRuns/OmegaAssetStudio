using System;
using System.Numerics;
using OmegaAssetStudio.MeshPreview;
using UpkManager.Models.UpkFile.Engine.Mesh;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Rendering;

public sealed class MaterialPreviewMesh : IDisposable
{
    public string Name { get; set; } = string.Empty;

    public string SourceUpkPath { get; set; } = string.Empty;

    public string SourceMeshExportPath { get; set; } = string.Empty;

    internal MeshPreviewMesh NativeMesh { get; }

    private MaterialPreviewMesh(MeshPreviewMesh nativeMesh)
    {
        NativeMesh = nativeMesh;
    }

    public static MaterialPreviewMesh CreateSphere(string name = "MaterialPreviewSphere", float radius = 1.0f, int slices = 24, int stacks = 16)
    {
        throw new InvalidOperationException("MaterialEditor does not use the sphere fallback mesh.");
    }

    public static MaterialPreviewMesh CreatePlane(string name = "MaterialPreviewPlane", float width = 2.0f, float height = 2.0f)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        MeshPreviewMesh previewMesh = new()
        {
            Name = name
        };

        previewMesh.Vertices.Add(new MeshPreviewVertex
        {
            Position = new Vector3(-halfWidth, -halfHeight, 0.0f),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            Bitangent = Vector3.UnitY,
            Uv = new Vector2(0.0f, 1.0f),
            Bone0 = 0,
            Weight0 = 1.0f,
            SectionIndex = 0
        });
        previewMesh.Vertices.Add(new MeshPreviewVertex
        {
            Position = new Vector3(halfWidth, -halfHeight, 0.0f),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            Bitangent = Vector3.UnitY,
            Uv = new Vector2(1.0f, 1.0f),
            Bone0 = 0,
            Weight0 = 1.0f,
            SectionIndex = 0
        });
        previewMesh.Vertices.Add(new MeshPreviewVertex
        {
            Position = new Vector3(halfWidth, halfHeight, 0.0f),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            Bitangent = Vector3.UnitY,
            Uv = new Vector2(1.0f, 0.0f),
            Bone0 = 0,
            Weight0 = 1.0f,
            SectionIndex = 0
        });
        previewMesh.Vertices.Add(new MeshPreviewVertex
        {
            Position = new Vector3(-halfWidth, halfHeight, 0.0f),
            Normal = Vector3.UnitZ,
            Tangent = Vector3.UnitX,
            Bitangent = Vector3.UnitY,
            Uv = new Vector2(0.0f, 0.0f),
            Bone0 = 0,
            Weight0 = 1.0f,
            SectionIndex = 0
        });

        previewMesh.Indices.Add(0);
        previewMesh.Indices.Add(1);
        previewMesh.Indices.Add(2);
        previewMesh.Indices.Add(0);
        previewMesh.Indices.Add(2);
        previewMesh.Indices.Add(3);

        previewMesh.Sections.Add(new MeshPreviewSection
        {
            Index = 0,
            MaterialIndex = 0,
            BaseIndex = 0,
            IndexCount = 6,
            Name = "PreviewPlane",
            Color = PreviewPalette.ColorForIndex(0),
            GameMaterial = new MeshPreviewGameMaterial
            {
                Enabled = true,
                DiffuseColor = new Vector3(0.65f, 0.65f, 0.65f),
                SpecularColor = Vector3.One
            }
        });
        previewMesh.Bones.Add(new MeshPreviewBone
        {
            Name = "Root",
            ParentIndex = -1,
            LocalTransform = Matrix4x4.Identity,
            GlobalTransform = Matrix4x4.Identity,
            OffsetMatrix = Matrix4x4.Identity
        });
        previewMesh.Center = Vector3.Zero;
        previewMesh.Radius = MathF.Max(width, height) * 0.75f;
        return new MaterialPreviewMesh(previewMesh)
        {
            Name = name
        };
    }

    public static MaterialPreviewMesh CreateFromSkeletalMesh(USkeletalMesh skeletalMesh, string name, int lodIndex = 0, Action<string>? log = null)
    {
        if (skeletalMesh is null)
            throw new ArgumentNullException(nameof(skeletalMesh));

        UE3ToPreviewMeshConverter converter = new();
        MeshPreviewMesh previewMesh = converter.Convert(skeletalMesh, lodIndex, log);
        previewMesh.Name = name;
        return new MaterialPreviewMesh(previewMesh) { Name = name };
    }

    internal void ApplyMaterial(MeshPreviewGameMaterial material)
    {
        if (NativeMesh.Sections.Count == 0)
            return;

        foreach (MeshPreviewSection section in NativeMesh.Sections)
            section.GameMaterial = material;
    }

    public void Upload()
    {
    }

    public void Dispose() => NativeMesh.Dispose();

}

