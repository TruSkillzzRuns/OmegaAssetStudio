using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Types;
using UpkManager.Repository;

namespace OmegaAssetStudio.WinUI.Modules.MFL.Engine;

public sealed class MeshLoader
{
    private const int MaxSocketsToImport = 32;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly record struct MeshVertexKey(int SourceIndex, int SectionIndex, int MaterialSlotIndex);
    public sealed record LoadedMeshPackage(Mesh Mesh, USkeletalMesh SkeletalMesh, string SkeletalMeshPath);

    public Mesh Load(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".upk", StringComparison.OrdinalIgnoreCase))
            return LoadUpk(path);

        string text = File.ReadAllText(path);

        MeshDocument? document = TryReadJsonDocument(text);
        if (document is null)
            document = TryReadStructuredDocument(text, path);

        if (document is null)
            throw new InvalidDataException($"Mesh document '{path}' could not be parsed.");

        return FromDocument(document, path);
    }

    public Mesh LoadUpk(string path, string? preferredExportPath = null, Action<string>? log = null)
    {
        return LoadUpkPackage(path, preferredExportPath, log).Mesh;
    }

    public LoadedMeshPackage LoadUpkPackage(string path, string? preferredExportPath = null, Action<string>? log = null)
    {
        UpkFileRepository repository = new();
        UnrealHeader header = repository.LoadUpkFile(path).GetAwaiter().GetResult();
        header.ReadHeaderAsync(null).GetAwaiter().GetResult();

        UnrealExportTableEntry? selectedExport = null;
        USkeletalMesh? skeletalMesh = null;
        int bestScore = -1;
        foreach (UnrealExportTableEntry export in header.ExportTable)
        {
            if (!TryResolveSkeletalMesh(header, export, out USkeletalMesh? candidate, log))
                continue;
            if (candidate is null)
                continue;

            int candidateScore;
            try
            {
                log?.Invoke($"Scoring skeletal export {export.GetPathName()}.");
                candidateScore = ScoreSkeletalMesh(export, candidate, preferredExportPath);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Skipping skeletal export {export.GetPathName()} while scoring: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (candidateScore > bestScore)
            {
                bestScore = candidateScore;
                selectedExport = export;
                skeletalMesh = candidate;
            }
        }

        if (selectedExport is null || skeletalMesh is null)
            throw new InvalidDataException($"No SkeletalMesh export could be resolved from '{path}'.");

        log?.Invoke($"Loaded UPK skeletal mesh {selectedExport.GetPathName()} from {path}.");
        LogRawSkeletalMeshLayout(skeletalMesh, selectedExport.GetPathName(), log);
        try
        {
            Mesh mesh = ConvertSkeletalMesh(skeletalMesh, selectedExport.GetPathName(), path, log);
            LogConvertedLayout(mesh, selectedExport.GetPathName(), log);
            return new LoadedMeshPackage(mesh, skeletalMesh, selectedExport.GetPathName());
        }
        catch (Exception ex)
        {
            log?.Invoke($"Failed to convert skeletal mesh {selectedExport.GetPathName()}: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    public IReadOnlyList<string> GetSkeletalMeshExports(string path)
    {
        UpkFileRepository repository = new();
        UnrealHeader header = repository.LoadUpkFile(path).GetAwaiter().GetResult();
        header.ReadHeaderAsync(null).GetAwaiter().GetResult();

        List<string> exports = [];
        foreach (UnrealExportTableEntry export in header.ExportTable)
        {
            if (!TryResolveSkeletalMesh(header, export, out _, null))
                continue;

            exports.Add(export.GetPathName());
        }

        return exports
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Save(string path, Mesh mesh)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        MeshDocument document = ToDocument(mesh, path);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public Mesh CreateDemoMesh(string name, Vector3 offset)
    {
        Mesh mesh = new()
        {
            Name = name,
            SourcePath = string.Empty
        };

        mesh.Bones.AddRange([
            new Bone { Name = "Root", ParentIndex = -1, BindPosition = offset, Length = 1.0f },
            new Bone { Name = "Spine", ParentIndex = 0, BindPosition = offset + new Vector3(0.0f, 0.9f, 0.0f), Length = 0.6f },
            new Bone { Name = "Head", ParentIndex = 1, BindPosition = offset + new Vector3(0.0f, 1.55f, 0.0f), Length = 0.35f },
            new Bone { Name = "LeftArm", ParentIndex = 1, BindPosition = offset + new Vector3(-0.55f, 1.0f, 0.0f), Length = 0.6f },
            new Bone { Name = "RightArm", ParentIndex = 1, BindPosition = offset + new Vector3(0.55f, 1.0f, 0.0f), Length = 0.6f },
            new Bone { Name = "LeftLeg", ParentIndex = 0, BindPosition = offset + new Vector3(-0.18f, -0.75f, 0.0f), Length = 0.9f },
            new Bone { Name = "RightLeg", ParentIndex = 0, BindPosition = offset + new Vector3(0.18f, -0.75f, 0.0f), Length = 0.9f }
        ]);

        mesh.MaterialSlots.Add(new MaterialSlot { Index = 0, Name = "BodyMaterial", MaterialPath = "/Game/Materials/BodyMaterial" });
        mesh.UVSets.Add(new UVSet { Name = "UV0", ChannelIndex = 0 });

        AddBoxPart(mesh, offset + new Vector3(-0.32f, 0.0f, -0.18f), offset + new Vector3(0.32f, 0.95f, 0.18f), 1, 0, 0);
        AddBoxPart(mesh, offset + new Vector3(-0.18f, 0.95f, -0.15f), offset + new Vector3(0.18f, 1.45f, 0.15f), 2, 1, 0);
        AddBoxPart(mesh, offset + new Vector3(-0.72f, 0.45f, -0.12f), offset + new Vector3(-0.32f, 0.95f, 0.12f), 3, 2, 0);
        AddBoxPart(mesh, offset + new Vector3(0.32f, 0.45f, -0.12f), offset + new Vector3(0.72f, 0.95f, 0.12f), 4, 3, 0);
        AddBoxPart(mesh, offset + new Vector3(-0.22f, -0.85f, -0.13f), offset + new Vector3(-0.04f, 0.0f, 0.13f), 5, 4, 0);
        AddBoxPart(mesh, offset + new Vector3(0.04f, -0.85f, -0.13f), offset + new Vector3(0.22f, 0.0f, 0.13f), 6, 5, 0);

        mesh.LODGroups.Add(new LODGroup
        {
            LevelIndex = 0,
            ScreenSize = 1.0f,
            TriangleIndices = Enumerable.Range(0, mesh.Triangles.Count).ToList()
        });

        mesh.Sockets.Add(new Socket
        {
            Name = "WeaponSocket",
            BoneName = "RightArm",
            BoneIndex = 4,
            Position = offset + new Vector3(0.8f, 0.85f, 0.0f),
            Rotation = Quaternion.Identity
        });

        mesh.Sockets.Add(new Socket
        {
            Name = "BackSocket",
            BoneName = "Spine",
            BoneIndex = 1,
            Position = offset + new Vector3(0.0f, 1.0f, -0.16f),
            Rotation = Quaternion.Identity
        });

        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddBoxPart(Mesh mesh, Vector3 min, Vector3 max, int boneIndex, int sectionIndex, int materialSlotIndex)
    {
        int vertexOffset = mesh.Vertices.Count;
        Vector3[] corners =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z),
            new(min.X, max.Y, max.Z)
        ];

        Vector2[] uv =
        [
            new(0.0f, 0.0f),
            new(1.0f, 0.0f),
            new(1.0f, 1.0f),
            new(0.0f, 1.0f),
            new(0.0f, 0.0f),
            new(1.0f, 0.0f),
            new(1.0f, 1.0f),
            new(0.0f, 1.0f)
        ];

        for (int i = 0; i < corners.Length; i++)
        {
            List<BoneWeight> weights =
            [
                new BoneWeight { BoneIndex = boneIndex, BoneName = mesh.Bones[boneIndex].Name, Weight = 0.8f },
                new BoneWeight { BoneIndex = 0, BoneName = mesh.Bones[0].Name, Weight = 0.2f }
            ];

            mesh.Vertices.Add(new Vertex
            {
                Position = corners[i],
                Normal = Vector3.Zero,
                Tangent = Vector3.UnitX,
                Bitangent = Vector3.UnitZ,
                SectionIndex = sectionIndex,
                MaterialSlotIndex = materialSlotIndex,
                UVs = [uv[i]],
                Weights = weights
            });

            mesh.UVSets[0].Coordinates.Add(uv[i]);
        }

        int[][] faces =
        [
            [0, 2, 1], [0, 3, 2],
            [4, 5, 6], [4, 6, 7],
            [0, 1, 5], [0, 5, 4],
            [1, 2, 6], [1, 6, 5],
            [2, 3, 7], [2, 7, 6],
            [3, 0, 4], [3, 4, 7]
        ];

        foreach (int[] face in faces)
        {
            mesh.Triangles.Add(new Triangle
            {
                A = vertexOffset + face[0],
                B = vertexOffset + face[1],
                C = vertexOffset + face[2],
                MaterialSlotIndex = materialSlotIndex,
                SectionIndex = sectionIndex,
                LodIndex = 0
            });
        }
    }

    private static Mesh FromDocument(MeshDocument document, string sourcePath)
    {
        Mesh mesh = new()
        {
            Name = document.Name,
            SourcePath = string.IsNullOrWhiteSpace(document.SourcePath) ? sourcePath : document.SourcePath
        };

        mesh.Bones.AddRange(document.Bones.Select(item => new Bone
        {
            Name = item.Name,
            ParentIndex = item.ParentIndex,
            BindPosition = ReadVector3(item.BindPosition),
            BindRotation = ReadQuaternion(item.BindRotation),
            Length = item.Length
        }));

        mesh.MaterialSlots.AddRange(document.MaterialSlots.Select(item => new MaterialSlot
        {
            Index = item.Index,
            Name = item.Name,
            MaterialPath = item.MaterialPath
        }));

        mesh.UVSets.AddRange(document.UVSets.Select(item => new UVSet
        {
            ChannelIndex = item.ChannelIndex,
            Name = item.Name,
            Coordinates = item.Coordinates.Select(ReadVector2).ToList()
        }));

        mesh.Vertices.AddRange(document.Vertices.Select(item => new Vertex
        {
            Position = ReadVector3(item.Position),
            Normal = ReadVector3(item.Normal, Vector3.UnitY),
            Tangent = ReadVector3(item.Tangent, Vector3.UnitX),
            Bitangent = ReadVector3(item.Bitangent, Vector3.UnitZ),
            SectionIndex = item.SectionIndex,
            MaterialSlotIndex = item.MaterialSlotIndex,
            UVs = item.UVs.Select(ReadVector2).ToList(),
            Weights = item.Weights.Select(weight => new BoneWeight
            {
                BoneIndex = weight.BoneIndex,
                BoneName = weight.BoneName,
                Weight = weight.Weight
            }).ToList()
        }));

        mesh.Triangles.AddRange(document.Triangles.Select(item => new Triangle
        {
            A = item.A,
            B = item.B,
            C = item.C,
            MaterialSlotIndex = item.MaterialSlotIndex,
            SectionIndex = item.SectionIndex,
            LodIndex = item.LodIndex
        }));

        mesh.LODGroups.AddRange(document.LODGroups.Select(item => new LODGroup
        {
            LevelIndex = item.LevelIndex,
            ScreenSize = item.ScreenSize,
            TriangleIndices = item.TriangleIndices.ToList()
        }));

        mesh.Sockets.AddRange(document.Sockets.Select(item => new Socket
        {
            Name = item.Name,
            BoneName = item.BoneName,
            BoneIndex = item.BoneIndex,
            Position = ReadVector3(item.Position),
            Rotation = ReadQuaternion(item.Rotation)
        }));

        mesh.Bounds = new BoundingBox
        {
            Min = ReadVector3(document.BoundingBox.Min),
            Max = ReadVector3(document.BoundingBox.Max)
        };

        mesh.RecalculateBounds();
        return mesh;
    }

    private static MeshDocument? TryReadJsonDocument(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<MeshDocument>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryResolveSkeletalMesh(UnrealHeader header, UnrealExportTableEntry export, out USkeletalMesh? skeletalMesh, Action<string>? log)
    {
        skeletalMesh = null;
        string exportName = export.GetPathName() ?? "<unnamed>";
        string className = export.ClassReferenceNameIndex?.Name ?? "(unknown)";

        try
        {
            if (!IsPotentialSkeletalMeshClass(className))
                return false;

            log?.Invoke($"Inspecting export {exportName} [{className}].");

            if (export.UnrealObject is null)
            {
                log?.Invoke($"Reading export object {exportName}.");
                header.ReadExportObjectAsync(export, null).GetAwaiter().GetResult();
                log?.Invoke($"Parsing export {exportName}.");
                export.ParseUnrealObject(false, false).GetAwaiter().GetResult();
            }

            if (export.UnrealObject is IUnrealObject { UObject: USkeletalMesh directMesh })
            {
                log?.Invoke($"Resolved direct SkeletalMesh {exportName}.");
                skeletalMesh = directMesh;
                return true;
            }

            if (export.UnrealObject is IUnrealObject { UObject: USkeletalMeshComponent component })
            {
                USkeletalMesh? referenced = component.SkeletalMesh?.LoadObject<USkeletalMesh>();
                if (referenced is not null)
                {
                    log?.Invoke($"Resolved SkeletalMeshComponent {exportName} -> referenced SkeletalMesh.");
                    skeletalMesh = referenced;
                    return true;
                }

                log?.Invoke($"SkeletalMeshComponent {exportName} did not reference a loadable SkeletalMesh.");
            }

            log?.Invoke($"Export {exportName} [{className}] was not a resolved SkeletalMesh.");
        }
        catch (Exception ex)
        {
            log?.Invoke($"Failed to resolve {exportName} [{className}]: {ex.GetType().Name}: {ex.Message}");
        }

        return false;
    }

    private static bool IsPotentialSkeletalMeshClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        string normalized = className.Trim().ToLowerInvariant();
        if (normalized.Contains("skeletalmeshsocket"))
            return false;

        return normalized.Contains("skeletalmesh");
    }

    private static int ScoreSkeletalMesh(UnrealExportTableEntry export, USkeletalMesh skeletalMesh, string? preferredExportPath)
    {
        int vertexCount = skeletalMesh.LODModels is null || skeletalMesh.LODModels.Count == 0 || skeletalMesh.LODModels[0].VertexBufferGPUSkin is null
            ? 0
            : skeletalMesh.LODModels[0].VertexBufferGPUSkin.VertexData.Count();
        int triangleCount = skeletalMesh.LODModels is null || skeletalMesh.LODModels.Count == 0 || skeletalMesh.LODModels[0].MultiSizeIndexContainer is null
            ? 0
            : skeletalMesh.LODModels[0].MultiSizeIndexContainer.IndexBuffer.Count() / 3;
        int boneCount = skeletalMesh.RefSkeleton is null ? 0 : skeletalMesh.RefSkeleton.Count();
        int score = (vertexCount * 4) + (triangleCount * 2) + boneCount;

        if (!string.IsNullOrWhiteSpace(preferredExportPath) &&
            string.Equals(export.GetPathName(), preferredExportPath, StringComparison.OrdinalIgnoreCase))
        {
            score += 1_000_000;
        }

        return score;
    }

    private static Mesh ConvertSkeletalMesh(USkeletalMesh skeletalMesh, string meshName, string sourcePath, Action<string>? log)
    {
        Mesh mesh = new()
        {
            Name = meshName,
            SourcePath = sourcePath
        };

        mesh.Bones.AddRange(skeletalMesh.RefSkeleton is not null ? BuildBones(skeletalMesh.RefSkeleton) : []);
        mesh.MaterialSlots.AddRange(BuildMaterials(skeletalMesh));
        if (skeletalMesh.Sockets is not null && skeletalMesh.Sockets.Count > 0)
        {
            if (skeletalMesh.Sockets.Count <= MaxSocketsToImport)
            {
                mesh.Sockets.AddRange(BuildSockets(skeletalMesh, log));
            }
            else
            {
                log?.Invoke($"Skipping {skeletalMesh.Sockets.Count} sockets during initial import to keep the viewport responsive.");
            }
        }

        if (skeletalMesh.LODModels is null || skeletalMesh.LODModels.Count == 0)
        {
            mesh.RecalculateBounds();
            return mesh;
        }

        FStaticLODModel lod = skeletalMesh.LODModels[0];
        if (lod.VertexBufferGPUSkin is null || lod.MultiSizeIndexContainer is null)
        {
            mesh.RecalculateBounds();
            return mesh;
        }

        FGPUSkinVertexBase[] sourceVertices = [.. lod.VertexBufferGPUSkin.VertexData];
        uint[] sourceIndices = [.. lod.MultiSizeIndexContainer.IndexBuffer];
        int uvChannelCount = Math.Max(1, checked((int)lod.VertexBufferGPUSkin.NumTexCoords));

        Dictionary<MeshVertexKey, int> vertexMap = [];
        if (lod.Sections is not null && lod.Sections.Count > 0)
        {
            log?.Invoke($"Converting LOD0 by section: sections={lod.Sections.Count}, chunks={lod.Chunks?.Count ?? 0}.");
            for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
            {
                FSkelMeshSection section = lod.Sections[sectionIndex];
                int sectionStart = Math.Max(0, checked((int)section.BaseIndex));
                int sectionTriangleCount = Math.Max(0, checked((int)section.NumTriangles));
                int sectionEnd = Math.Min(sourceIndices.Length, sectionStart + (sectionTriangleCount * 3));
                if (sectionTriangleCount == 0 || sectionStart >= sectionEnd)
                {
                    log?.Invoke($"Skipping section {sectionIndex}: MaterialIndex={section.MaterialIndex}, ChunkIndex={section.ChunkIndex}, BaseIndex={section.BaseIndex}, Triangles={section.NumTriangles}.");
                    continue;
                }

                FSkelMeshChunk? sectionChunk = GetChunkForSection(lod, sectionIndex);
                log?.Invoke($"Converting section {sectionIndex}: MaterialIndex={section.MaterialIndex}, ChunkIndex={section.ChunkIndex}, BaseIndex={section.BaseIndex}, Triangles={section.NumTriangles}, Range=[{sectionStart},{sectionEnd}).");
                for (int triangleIndex = sectionStart; triangleIndex + 2 < sectionEnd; triangleIndex += 3)
                {
                    int materialSlotIndex = section.MaterialIndex;
                    int indexA = checked((int)sourceIndices[triangleIndex]);
                    int indexB = checked((int)sourceIndices[triangleIndex + 1]);
                    int indexC = checked((int)sourceIndices[triangleIndex + 2]);

                    int mappedA = GetOrAddVertex(mesh, vertexMap, sourceVertices, lod, uvChannelCount, indexA, sectionIndex, materialSlotIndex, sectionChunk, log);
                    int mappedB = GetOrAddVertex(mesh, vertexMap, sourceVertices, lod, uvChannelCount, indexB, sectionIndex, materialSlotIndex, sectionChunk, log);
                    int mappedC = GetOrAddVertex(mesh, vertexMap, sourceVertices, lod, uvChannelCount, indexC, sectionIndex, materialSlotIndex, sectionChunk, log);

                    mesh.Triangles.Add(new Triangle
                    {
                        A = mappedA,
                        B = mappedB,
                        C = mappedC,
                        MaterialSlotIndex = materialSlotIndex,
                        SectionIndex = sectionIndex,
                        LodIndex = 0
                    });
                }
            }
        }

        if (mesh.Triangles.Count == 0)
        {
            log?.Invoke("Section-aware conversion yielded no triangles; falling back to triangle-range conversion.");
            vertexMap.Clear();
            for (int triangleIndex = 0; triangleIndex + 2 < sourceIndices.Length; triangleIndex += 3)
            {
                int sectionIndex = GetSectionIndex(lod, triangleIndex / 3);
                int materialSlotIndex = GetMaterialSlotIndex(lod, triangleIndex / 3);
                FSkelMeshChunk? sectionChunk = GetChunkForSection(lod, sectionIndex);
                int indexA = checked((int)sourceIndices[triangleIndex]);
                int indexB = checked((int)sourceIndices[triangleIndex + 1]);
                int indexC = checked((int)sourceIndices[triangleIndex + 2]);

                int mappedA = GetOrAddVertex(mesh, vertexMap, sourceVertices, lod, uvChannelCount, indexA, sectionIndex, materialSlotIndex, sectionChunk, log);
                int mappedB = GetOrAddVertex(mesh, vertexMap, sourceVertices, lod, uvChannelCount, indexB, sectionIndex, materialSlotIndex, sectionChunk, log);
                int mappedC = GetOrAddVertex(mesh, vertexMap, sourceVertices, lod, uvChannelCount, indexC, sectionIndex, materialSlotIndex, sectionChunk, log);

                mesh.Triangles.Add(new Triangle
                {
                    A = mappedA,
                    B = mappedB,
                    C = mappedC,
                    MaterialSlotIndex = materialSlotIndex,
                    SectionIndex = sectionIndex,
                    LodIndex = 0
                });
            }
        }

        mesh.LODGroups.Add(new LODGroup
        {
            LevelIndex = 0,
            ScreenSize = 1.0f,
            TriangleIndices = Enumerable.Range(0, mesh.Triangles.Count).ToList()
        });

        mesh.RecalculateBounds();
        return mesh;
    }

    private static void LogRawSkeletalMeshLayout(USkeletalMesh skeletalMesh, string exportName, Action<string>? log)
    {
        if (skeletalMesh.LODModels is null || skeletalMesh.LODModels.Count == 0)
        {
            log?.Invoke($"Raw skeletal mesh {exportName}: no LOD models.");
            return;
        }

        FStaticLODModel lod = skeletalMesh.LODModels[0];
        int sectionCount = lod.Sections?.Count ?? 0;
        int chunkCount = lod.Chunks?.Count ?? 0;
        int vertexCount = lod.VertexBufferGPUSkin is null ? 0 : lod.VertexBufferGPUSkin.VertexData.Count();
        int indexCount = lod.MultiSizeIndexContainer is null ? 0 : lod.MultiSizeIndexContainer.IndexBuffer.Count();

        log?.Invoke($"Raw skeletal mesh {exportName}: LOD0 sections={sectionCount}, chunks={chunkCount}, vertices={vertexCount}, indices={indexCount}.");

        if (lod.Sections is not null)
        {
            for (int i = 0; i < lod.Sections.Count; i++)
            {
                FSkelMeshSection section = lod.Sections[i];
                log?.Invoke($"Raw section {i}: MaterialIndex={section.MaterialIndex}, ChunkIndex={section.ChunkIndex}, Triangles={section.NumTriangles}, BaseIndex={section.BaseIndex}.");
            }
        }

        if (lod.Chunks is not null)
        {
            for (int i = 0; i < lod.Chunks.Count; i++)
            {
                FSkelMeshChunk? chunk = lod.Chunks[i];
                if (chunk is null)
                {
                    log?.Invoke($"Raw chunk {i}: null.");
                    continue;
                }

                string boneMap = string.Join(",", chunk.BoneMap.Select(static bone => bone.ToString()));
                log?.Invoke($"Raw chunk {i}: BaseVertexIndex={chunk.BaseVertexIndex}, Rigid={chunk.NumRigidVertices}, Soft={chunk.NumSoftVertices}, BoneMap=[{boneMap}].");
            }
        }
    }

    private static void LogConvertedLayout(Mesh mesh, string exportName, Action<string>? log)
    {
        int sectionCount = mesh.Triangles.Select(triangle => triangle.SectionIndex).Distinct().Count();
        int materialCount = mesh.Triangles.Select(triangle => triangle.MaterialSlotIndex).Distinct().Count();
        log?.Invoke($"Converted skeletal mesh {exportName}: vertices={mesh.Vertices.Count}, triangles={mesh.Triangles.Count}, sections={sectionCount}, materials={materialCount}.");

        foreach (var sectionGroup in mesh.Triangles
                     .GroupBy(triangle => triangle.SectionIndex)
                     .OrderBy(group => group.Key))
        {
            int sectionMaterial = sectionGroup.Select(triangle => triangle.MaterialSlotIndex).FirstOrDefault();
            log?.Invoke($"Converted section {sectionGroup.Key}: triangles={sectionGroup.Count()}, material={sectionMaterial}.");
        }
    }

    private static IReadOnlyList<Bone> BuildBones(UArray<FMeshBone> skeleton)
    {
        List<Bone> bones = new(skeleton.Count);
        for (int i = 0; i < skeleton.Count; i++)
        {
            FMeshBone sourceBone = skeleton[i];
            Quaternion rotation = sourceBone.BonePos.Orientation.ToQuaternion();
            Vector3 position = sourceBone.BonePos.Position.ToVector3();

            bones.Add(new Bone
            {
                Name = sourceBone.Name?.Name ?? $"Bone_{i}",
                ParentIndex = sourceBone.ParentIndex,
                BindPosition = ConvertPosition(position),
                BindRotation = ConvertQuaternion(rotation),
                Length = 1.0f
            });
        }

        for (int i = 0; i < bones.Count; i++)
        {
            int parentIndex = bones[i].ParentIndex;
            if (parentIndex < 0 || parentIndex >= bones.Count)
                continue;

            float length = Vector3.Distance(bones[i].BindPosition, bones[parentIndex].BindPosition);
            bones[parentIndex].Length = Math.Max(bones[parentIndex].Length, length);
        }

        return bones;
    }

    private static IReadOnlyList<MaterialSlot> BuildMaterials(USkeletalMesh skeletalMesh)
    {
        List<MaterialSlot> materials = [];
        if (skeletalMesh.Materials is null)
            return materials;

        for (int i = 0; i < skeletalMesh.Materials.Count; i++)
        {
            string materialPath = skeletalMesh.Materials[i]?.GetPathName() ?? string.Empty;
            materials.Add(new MaterialSlot
            {
                Index = i,
                Name = !string.IsNullOrWhiteSpace(materialPath) ? materialPath : $"Material_{i}",
                MaterialPath = materialPath
            });
        }

        return materials;
    }

    private static IReadOnlyList<Socket> BuildSockets(USkeletalMesh skeletalMesh, Action<string>? log)
    {
        List<Socket> sockets = [];
        if (skeletalMesh.Sockets is null)
            return sockets;

        for (int i = 0; i < skeletalMesh.Sockets.Count; i++)
        {
            try
            {
                var socketEntry = skeletalMesh.Sockets[i];
                if (socketEntry is null)
                {
                    log?.Invoke($"Skipping socket entry {i}: entry was null.");
                    continue;
                }

                string socketPath = socketEntry.GetPathName() ?? string.Empty;
                USkeletalMeshSocket? socket = socketEntry.LoadObject<USkeletalMeshSocket>();
                if (socket is null)
                {
                    log?.Invoke($"Skipping socket entry {i} ({socketPath}): socket object was null.");
                    continue;
                }

                Vector3 position = Vector3.Zero;
                if (socket.RelativeLocation is not null)
                    position = ConvertPosition(socket.RelativeLocation.ToVector3());

                Quaternion rotation = Quaternion.Identity;
                if (socket.RelativeRotation is not null)
                    rotation = ConvertQuaternion(CreateQuaternion(socket.RelativeRotation));

                sockets.Add(new Socket
                {
                    Name = !string.IsNullOrWhiteSpace(socketPath) ? socketPath : socket.SocketName?.Name ?? $"Socket_{i}",
                    BoneName = socket.BoneName?.Name ?? string.Empty,
                    BoneIndex = 0,
                    Position = position,
                    Rotation = rotation
                });
            }
            catch (Exception ex)
            {
                log?.Invoke($"Skipping socket entry {i}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return sockets;
    }

    private static int GetOrAddVertex(
        Mesh mesh,
        Dictionary<MeshVertexKey, int> vertexMap,
        FGPUSkinVertexBase[] sourceVertices,
        FStaticLODModel lod,
        int uvChannelCount,
        int sourceIndex,
        int sectionIndex,
        int materialSlotIndex,
        FSkelMeshChunk? sectionChunk,
        Action<string>? log)
    {
        MeshVertexKey key = new(sourceIndex, sectionIndex, materialSlotIndex);
        if (vertexMap.TryGetValue(key, out int mappedIndex))
            return mappedIndex;

        FGPUSkinVertexBase vertex = sourceVertices[sourceIndex];
        Vector3 position = ConvertPosition(lod.VertexBufferGPUSkin.GetVertexPosition(vertex));
        Vector3 normal = ConvertDirection(GLVertex.SafeNormal(vertex.TangentZ));
        Vector3 tangent = ConvertDirection(GLVertex.SafeNormal(vertex.TangentX));
        Vector3 bitangent = ConvertDirection(GLVertex.ComputeBitangent(normal, tangent, vertex.TangentZ));

        List<Vector2> uvs = [];
        int availableUvCount = GetAvailableUvCount(vertex, uvChannelCount);
        for (int uvIndex = 0; uvIndex < availableUvCount; uvIndex++)
            uvs.Add(vertex.GetVector2(uvIndex));
        if (uvs.Count == 0)
            uvs.Add(Vector2.Zero);

        List<BoneWeight> weights = [];
        for (int influenceIndex = 0; influenceIndex < 4; influenceIndex++)
        {
            byte localBoneIndex = vertex.InfluenceBones[influenceIndex];
            byte weight = vertex.InfluenceWeights[influenceIndex];
            if (weight == 0)
                continue;

            int boneIndex = localBoneIndex;
            if (sectionChunk?.BoneMap is not null && localBoneIndex < sectionChunk.BoneMap.Count)
            {
                boneIndex = sectionChunk.BoneMap[localBoneIndex];
            }
            else if (sectionChunk?.BoneMap is not null)
            {
                log?.Invoke($"Vertex {sourceIndex} influence bone {localBoneIndex} exceeded chunk bone map size {sectionChunk.BoneMap.Count}; preserving local index.");
            }

            string boneName = boneIndex >= 0 && boneIndex < mesh.Bones.Count ? mesh.Bones[boneIndex].Name : $"Bone_{boneIndex}";
            weights.Add(new BoneWeight
            {
                BoneIndex = boneIndex,
                BoneName = boneName,
                Weight = weight / 255.0f
            });
        }

        int newIndex = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex
        {
            Position = position,
            Normal = NormalizeOrUnitY(normal),
            Tangent = NormalizeOrUnitX(tangent),
            Bitangent = NormalizeOrUnitZ(bitangent),
            SectionIndex = sectionIndex,
            MaterialSlotIndex = materialSlotIndex,
            UVs = uvs,
            Weights = weights
        });

        vertexMap[key] = newIndex;
        return newIndex;
    }

    private static FSkelMeshChunk? GetChunkForSection(FStaticLODModel lod, int sectionIndex)
    {
        if (lod.Sections is null || lod.Chunks is null)
            return null;

        if (sectionIndex < 0 || sectionIndex >= lod.Sections.Count)
            return null;

        int chunkIndex = lod.Sections[sectionIndex].ChunkIndex;
        if (chunkIndex < 0 || chunkIndex >= lod.Chunks.Count)
            return null;

        return lod.Chunks[chunkIndex];
    }

    private static int GetSectionIndex(FStaticLODModel lod, int triangleIndex)
    {
        if (lod.Sections is null || lod.Sections.Count == 0)
            return 0;

        for (int i = 0; i < lod.Sections.Count; i++)
        {
            FSkelMeshSection section = lod.Sections[i];
            int start = checked((int)section.BaseIndex / 3);
            int end = start + checked((int)section.NumTriangles);
            if (triangleIndex >= start && triangleIndex < end)
                return i;
        }

        return 0;
    }

    private static int GetMaterialSlotIndex(FStaticLODModel lod, int triangleIndex)
    {
        if (lod.Sections is null || lod.Sections.Count == 0)
            return 0;

        int sectionIndex = GetSectionIndex(lod, triangleIndex);
        if (sectionIndex < 0 || sectionIndex >= lod.Sections.Count)
            return 0;

        return lod.Sections[sectionIndex].MaterialIndex;
    }


    private static Vector3 ConvertPosition(Vector3 source) => source;
    private static Vector3 ConvertDirection(Vector3 source) => source;
    private static Quaternion ConvertQuaternion(Quaternion source) => source;
    private static Quaternion CreateQuaternion(FRotator rotator)
    {
        float pitch = rotator.Pitch * (MathF.PI / 32768.0f);
        float yaw = rotator.Yaw * (MathF.PI / 32768.0f);
        float roll = rotator.Roll * (MathF.PI / 32768.0f);
        return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
    }
    private static Vector3 NormalizeOrUnitY(Vector3 value) => value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : Vector3.UnitY;
    private static Vector3 NormalizeOrUnitX(Vector3 value) => value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : Vector3.UnitX;
    private static Vector3 NormalizeOrUnitZ(Vector3 value) => value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : Vector3.UnitZ;
    private static int GetAvailableUvCount(FGPUSkinVertexBase vertex, int requestedUvCount)
    {
        return vertex switch
        {
            FGPUSkinVertexFloat16Uvs32Xyz float16Packed => Math.Min(requestedUvCount, float16Packed.UVs?.Length ?? 0),
            FGPUSkinVertexFloat16Uvs float16 => Math.Min(requestedUvCount, float16.UVs?.Length ?? 0),
            FGPUSkinVertexFloat32Uvs32Xyz float32Packed => Math.Min(requestedUvCount, float32Packed.UVs?.Length ?? 0),
            FGPUSkinVertexFloat32Uvs float32 => Math.Min(requestedUvCount, float32.UVs?.Length ?? 0),
            _ => Math.Max(1, requestedUvCount)
        };
    }

    private static MeshDocument? TryReadStructuredDocument(string text, string sourcePath)
    {
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("Mesh Fusion Lab ", StringComparison.OrdinalIgnoreCase))
            return null;

        MeshDocument document = new()
        {
            SourcePath = sourcePath
        };

        string section = string.Empty;
        UVSetDocument? currentUvSet = null;

        foreach (string rawLine in lines.Skip(1))
        {
            if (rawLine.StartsWith('[') && rawLine.EndsWith(']'))
            {
                section = rawLine[1..^1];
                currentUvSet = null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.Contains("Mesh Fusion Lab ", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (section)
            {
                case "":
                    ParseHeaderLine(document, rawLine);
                    break;
                case "Bones":
                    ParseBoneLine(document, rawLine);
                    break;
                case "Vertices":
                    ParseVertexLine(document, rawLine);
                    break;
                case "Triangles":
                    ParseTriangleLine(document, rawLine);
                    break;
                case "Materials":
                    ParseMaterialLine(document, rawLine);
                    break;
                case "UVSets":
                    currentUvSet = ParseUvSetLine(document, rawLine, currentUvSet);
                    break;
                case "LODs":
                    ParseLodLine(document, rawLine);
                    break;
                case "Sockets":
                    ParseSocketLine(document, rawLine);
                    break;
            }
        }

        if (document.Bones.Count == 0 && document.Vertices.Count == 0 && document.Triangles.Count == 0)
            return null;

        return document;
    }

    private static void ParseHeaderLine(MeshDocument document, string line)
    {
        if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
        {
            document.Name = line["Name:".Length..].Trim();
        }
        else if (line.StartsWith("Source:", StringComparison.OrdinalIgnoreCase))
        {
            document.SourcePath = line["Source:".Length..].Trim();
        }
    }

    private static void ParseBoneLine(MeshDocument document, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return;

        string remainder = line[(colon + 1)..].Trim();
        string[] parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return;

        document.Bones.Add(new BoneDocument
        {
            Name = parts[0],
            ParentIndex = ParseIntToken(remainder, "Parent=", -1),
            BindPosition = WriteVector3(ParseVector3Token(remainder, "Pos=", Vector3.Zero)),
            BindRotation = WriteQuaternion(ParseQuaternionToken(remainder, "Rot=", Quaternion.Identity)),
            Length = 1.0f
        });
    }

    private static void ParseVertexLine(MeshDocument document, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return;

        string remainder = line[(colon + 1)..].Trim();
        document.Vertices.Add(new VertexDocument
        {
            Position = WriteVector3(ParseVector3Token(remainder, "P=", Vector3.Zero)),
            Normal = WriteVector3(ParseVector3Token(remainder, "N=", Vector3.UnitY)),
            Tangent = WriteVector3(ParseVector3Token(remainder, "T=", Vector3.UnitX)),
            Bitangent = WriteVector3(ParseVector3Token(remainder, "B=", Vector3.UnitZ)),
            Weights = ParseWeights(remainder)
        });
    }

    private static void ParseTriangleLine(MeshDocument document, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return;

        string remainder = line[(colon + 1)..].Trim();
        string[] indices = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (indices.Length == 0)
            return;

        string[] triangleIndices = indices[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (triangleIndices.Length < 3)
            return;

        document.Triangles.Add(new TriangleDocument
        {
            A = ParseInt(triangleIndices[0], 0),
            B = ParseInt(triangleIndices[1], 0),
            C = ParseInt(triangleIndices[2], 0),
            MaterialSlotIndex = ParseIntToken(remainder, "Material=", 0),
            SectionIndex = ParseIntToken(remainder, "Section=", 0),
            LodIndex = ParseIntToken(remainder, "LOD=", 0)
        });
    }

    private static void ParseMaterialLine(MeshDocument document, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return;

        string remainder = line[(colon + 1)..].Trim();
        string[] split = remainder.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        document.MaterialSlots.Add(new MaterialSlotDocument
        {
            Index = ParseInt(line[..colon], 0),
            Name = split.Length > 0 ? split[0] : string.Empty,
            MaterialPath = split.Length > 1 ? split[1] : string.Empty
        });
    }

    private static UVSetDocument ParseUvSetLine(MeshDocument document, string line, UVSetDocument? current)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return current ?? new UVSetDocument { ChannelIndex = 0, Name = "UV0" };

        string remainder = line[(colon + 1)..].Trim();
        if (!remainder.StartsWith('('))
        {
            current = new UVSetDocument
            {
                ChannelIndex = ParseInt(line[..colon], document.UVSets.Count),
                Name = remainder
            };
            document.UVSets.Add(current);
            return current;
        }

        current ??= new UVSetDocument
        {
            ChannelIndex = document.UVSets.Count,
            Name = $"UV{document.UVSets.Count}"
        };

        current.Coordinates.Add(WriteVector2(ParseVector2(remainder)));
        if (!document.UVSets.Contains(current))
            document.UVSets.Add(current);

        return current;
    }

    private static void ParseLodLine(MeshDocument document, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return;

        string remainder = line[(colon + 1)..].Trim();
        document.LODGroups.Add(new LodGroupDocument
        {
            LevelIndex = ParseInt(line[..colon], document.LODGroups.Count),
            ScreenSize = ParseFloatToken(remainder, "Screen=", 1.0f),
            TriangleIndices = ParseTriangleListToken(remainder, "Triangles=")
        });
    }

    private static void ParseSocketLine(MeshDocument document, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return;

        string name = line[..colon].Trim();
        string remainder = line[(colon + 1)..].Trim();
        document.Sockets.Add(new SocketDocument
        {
            Name = name,
            BoneName = ParseStringToken(remainder, "Bone="),
            BoneIndex = ParseIntToken(remainder, "Index=", -1),
            Position = WriteVector3(ParseVector3Token(remainder, "Pos=", Vector3.Zero)),
            Rotation = WriteQuaternion(ParseQuaternionToken(remainder, "Rot=", Quaternion.Identity))
        });
    }

    private static List<BoneWeightDocument> ParseWeights(string line)
    {
        string payload = ParseStringToken(line, "Weights=");
        if (string.IsNullOrWhiteSpace(payload))
            return [];

        List<BoneWeightDocument> weights = [];
        foreach (string token in payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = token.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
                continue;

            weights.Add(new BoneWeightDocument
            {
                BoneName = parts[0],
                BoneIndex = ParseInt(parts[1], -1),
                Weight = ParseFloat(parts[2], 0.0f)
            });
        }

        return weights;
    }

    private static string ParseStringToken(string source, string token)
    {
        return ParseTokenValue(source, token);
    }

    private static Vector3 ParseVector3Token(string source, string token, Vector3 fallback)
    {
        string value = ParseTokenValue(source, token);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return ParseVector3(value, fallback);
    }

    private static Quaternion ParseQuaternionToken(string source, string token, Quaternion fallback)
    {
        string value = ParseTokenValue(source, token);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return ParseQuaternion(value, fallback);
    }

    private static int ParseIntToken(string source, string token, int fallback)
    {
        string value = ParseStringToken(source, token);
        return ParseInt(value, fallback);
    }

    private static float ParseFloatToken(string source, string token, float fallback)
    {
        string value = ParseStringToken(source, token);
        return ParseFloat(value, fallback);
    }

    private static List<int> ParseTriangleListToken(string source, string token)
    {
        string value = ParseStringToken(source, token);
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => ParseInt(item, 0))
            .ToList();
    }

    private static MeshDocument ToDocument(Mesh mesh, string sourcePath)
    {
        return new MeshDocument
        {
            Name = mesh.Name,
            SourcePath = string.IsNullOrWhiteSpace(mesh.SourcePath) ? sourcePath : mesh.SourcePath,
            Bones = mesh.Bones.Select(bone => new BoneDocument
            {
                Name = bone.Name,
                ParentIndex = bone.ParentIndex,
                BindPosition = WriteVector3(bone.BindPosition),
                BindRotation = WriteQuaternion(bone.BindRotation),
                Length = bone.Length
            }).ToList(),
            MaterialSlots = mesh.MaterialSlots.Select(slot => new MaterialSlotDocument
            {
                Index = slot.Index,
                Name = slot.Name,
                MaterialPath = slot.MaterialPath
            }).ToList(),
            UVSets = mesh.UVSets.Select(set => new UVSetDocument
            {
                ChannelIndex = set.ChannelIndex,
                Name = set.Name,
                Coordinates = set.Coordinates.Select(WriteVector2).ToList()
            }).ToList(),
            Vertices = mesh.Vertices.Select(vertex => new VertexDocument
            {
                Position = WriteVector3(vertex.Position),
                Normal = WriteVector3(vertex.Normal),
                Tangent = WriteVector3(vertex.Tangent),
                Bitangent = WriteVector3(vertex.Bitangent),
                SectionIndex = vertex.SectionIndex,
                MaterialSlotIndex = vertex.MaterialSlotIndex,
                UVs = vertex.UVs.Select(WriteVector2).ToList(),
                Weights = vertex.Weights.Select(weight => new BoneWeightDocument
                {
                    BoneIndex = weight.BoneIndex,
                    BoneName = weight.BoneName,
                    Weight = weight.Weight
                }).ToList()
            }).ToList(),
            Triangles = mesh.Triangles.Select(triangle => new TriangleDocument
            {
                A = triangle.A,
                B = triangle.B,
                C = triangle.C,
                MaterialSlotIndex = triangle.MaterialSlotIndex,
                SectionIndex = triangle.SectionIndex,
                LodIndex = triangle.LodIndex
            }).ToList(),
            LODGroups = mesh.LODGroups.Select(group => new LodGroupDocument
            {
                LevelIndex = group.LevelIndex,
                ScreenSize = group.ScreenSize,
                TriangleIndices = group.TriangleIndices.ToList()
            }).ToList(),
            Sockets = mesh.Sockets.Select(socket => new SocketDocument
            {
                Name = socket.Name,
                BoneName = socket.BoneName,
                BoneIndex = socket.BoneIndex,
                Position = WriteVector3(socket.Position),
                Rotation = WriteQuaternion(socket.Rotation)
            }).ToList(),
            BoundingBox = new BoundingBoxDocument
            {
                Min = WriteVector3(mesh.Bounds.Min),
                Max = WriteVector3(mesh.Bounds.Max)
            }
        };
    }

    private static Vector3 ReadVector3(float[]? values, Vector3 fallback = default)
    {
        if (values is null || values.Length < 3)
            return fallback;

        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector2 ReadVector2(float[]? values)
    {
        if (values is null || values.Length < 2)
            return Vector2.Zero;

        return new Vector2(values[0], values[1]);
    }

    private static Quaternion ReadQuaternion(float[]? values)
    {
        if (values is null || values.Length < 4)
            return Quaternion.Identity;

        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    private static float[] WriteVector3(Vector3 value) => [value.X, value.Y, value.Z];

    private static float[] WriteVector2(Vector2 value) => [value.X, value.Y];

    private static float[] WriteQuaternion(Quaternion value) => [value.X, value.Y, value.Z, value.W];

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out int value) ? value : fallback;
    }

    private static float ParseFloat(string text, float fallback)
    {
        return float.TryParse(text, out float value) ? value : fallback;
    }

    private static Vector2 ParseVector2(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('('))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith(')'))
            trimmed = trimmed[..^1];

        string[] parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return Vector2.Zero;

        return new Vector2(ParseFloat(parts[0], 0.0f), ParseFloat(parts[1], 0.0f));
    }

    private static Vector3 ParseVector3(string text, Vector3 fallback)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('('))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith(')'))
            trimmed = trimmed[..^1];

        string[] parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return fallback;

        return new Vector3(ParseFloat(parts[0], fallback.X), ParseFloat(parts[1], fallback.Y), ParseFloat(parts[2], fallback.Z));
    }

    private static Quaternion ParseQuaternion(string text, Quaternion fallback)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('('))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith(')'))
            trimmed = trimmed[..^1];

        string[] parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
            return fallback;

        return new Quaternion(
            ParseFloat(parts[0], fallback.X),
            ParseFloat(parts[1], fallback.Y),
            ParseFloat(parts[2], fallback.Z),
            ParseFloat(parts[3], fallback.W));
    }

    private static string ParseTokenValue(string source, string token)
    {
        int start = source.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        start += token.Length;
        if (start >= source.Length)
            return string.Empty;

        if (source[start] == '(')
        {
            int end = source.IndexOf(')', start);
            if (end < 0)
                end = source.Length - 1;
            return source[start..(end + 1)].Trim();
        }

        int space = source.IndexOf(' ', start);
        if (space < 0)
            space = source.Length;

        return source[start..space].Trim();
    }

    private sealed class MeshDocument
    {
        public string Name { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public List<VertexDocument> Vertices { get; set; } = [];
        public List<TriangleDocument> Triangles { get; set; } = [];
        public List<BoneDocument> Bones { get; set; } = [];
        public List<MaterialSlotDocument> MaterialSlots { get; set; } = [];
        public List<UVSetDocument> UVSets { get; set; } = [];
        public List<LodGroupDocument> LODGroups { get; set; } = [];
        public List<SocketDocument> Sockets { get; set; } = [];
        public BoundingBoxDocument BoundingBox { get; set; } = new();
    }

    private sealed class VertexDocument
    {
        public float[] Position { get; set; } = [];
        public float[] Normal { get; set; } = [];
        public float[] Tangent { get; set; } = [];
        public float[] Bitangent { get; set; } = [];
        public int SectionIndex { get; set; }
        public int MaterialSlotIndex { get; set; }
        public List<float[]> UVs { get; set; } = [];
        public List<BoneWeightDocument> Weights { get; set; } = [];
    }

    private sealed class TriangleDocument
    {
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public int MaterialSlotIndex { get; set; }
        public int SectionIndex { get; set; }
        public int LodIndex { get; set; }
    }

    private sealed class BoneDocument
    {
        public string Name { get; set; } = string.Empty;
        public int ParentIndex { get; set; } = -1;
        public float[] BindPosition { get; set; } = [];
        public float[] BindRotation { get; set; } = [];
        public float Length { get; set; } = 1.0f;
    }

    private sealed class BoneWeightDocument
    {
        public int BoneIndex { get; set; } = -1;
        public string BoneName { get; set; } = string.Empty;
        public float Weight { get; set; }
    }

    private sealed class MaterialSlotDocument
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string MaterialPath { get; set; } = string.Empty;
    }

    private sealed class UVSetDocument
    {
        public string Name { get; set; } = string.Empty;
        public int ChannelIndex { get; set; }
        public List<float[]> Coordinates { get; set; } = [];
    }

    private sealed class LodGroupDocument
    {
        public int LevelIndex { get; set; }
        public float ScreenSize { get; set; } = 1.0f;
        public List<int> TriangleIndices { get; set; } = [];
    }

    private sealed class SocketDocument
    {
        public string Name { get; set; } = string.Empty;
        public string BoneName { get; set; } = string.Empty;
        public int BoneIndex { get; set; } = -1;
        public float[] Position { get; set; } = [];
        public float[] Rotation { get; set; } = [];
    }

    private sealed class BoundingBoxDocument
    {
        public float[] Min { get; set; } = [];
        public float[] Max { get; set; } = [];
    }
}

