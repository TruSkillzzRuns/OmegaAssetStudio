using UpkManager.Models.UpkFile.Engine.Mesh;

namespace OmegaAssetStudio.MeshImporter;

internal sealed partial class UE3LodBuilder
{
    private const int MaxAddressableVertexCount = ushort.MaxValue + 1;
    private readonly UE3VertexBuilder _vertexBuilder = new();
    private readonly UE3IndexBuilder _indexBuilder = new();

    public UE3LodModel Build(
        NeutralMesh mesh,
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        IReadOnlyList<SectionBuildInput> sectionInputs = NormalizeImportLayout(mesh, context, normalizedWeights);
        CaptureLayoutDiagnostics(mesh, context, sectionInputs);

        List<FSkelMeshSection> sections = [];
        List<FSkelMeshChunk> chunks = new(new FSkelMeshChunk[context.OriginalLod.Chunks.Count]);
        List<ushort> allIndices = [];
        List<FGPUSkinVertexBase> gpuVertices = [];
        List<FVertexInfluence> allInfluences = [];
        HashSet<int> activeBones = [];

        int globalVertexStart = 0;

        foreach (SectionBuildInput input in sectionInputs)
        {
            bool isNewSection = input.OriginalSectionIndex >= context.OriginalLod.Sections.Count;
            BuiltSectionData builtSection;
            int materialIndex;
            int chunkIndex;

            if (isNewSection)
            {
                builtSection = BuildNewSection(input, normalizedWeights, context, globalVertexStart);
                materialIndex = 0;
                chunkIndex = chunks.Count;
                chunks.Add(builtSection.Chunk);
            }
            else
            {
                FSkelMeshSection originalSection = context.OriginalLod.Sections[input.OriginalSectionIndex];
                builtSection = input.PreserveOriginal
                    ? BuildPreservedSection(originalSection, context, globalVertexStart)
                    : BuildImportedSection(input, normalizedWeights, context, globalVertexStart);
                materialIndex = originalSection.MaterialIndex;
                chunkIndex = originalSection.ChunkIndex;

                if (chunks[chunkIndex] != null)
                    throw new InvalidOperationException("Shared chunk sections are not supported by the importer yet.");

                chunks[chunkIndex] = builtSection.Chunk;
            }

            if (globalVertexStart + builtSection.VertexCount > MaxAddressableVertexCount)
            {
                throw new InvalidOperationException(
                    $"UE3 skeletal mesh vertex buffer exceeded {MaxAddressableVertexCount} vertices while rebuilding LOD {context.LodIndex}. " +
                    $"Current layout reached {globalVertexStart + builtSection.VertexCount} vertices. Reduce mesh complexity before UPK replacement.");
            }

            sections.Add(new FSkelMeshSection
            {
                MaterialIndex = (ushort)materialIndex,
                ChunkIndex = (ushort)chunkIndex,
                BaseIndex = (uint)allIndices.Count,
                NumTriangles = (uint)(builtSection.Indices.Count / 3),
                TriangleSorting = isNewSection ? (byte)0 : context.OriginalLod.Sections[input.OriginalSectionIndex].TriangleSorting
            });

            foreach (ushort boneIndex in builtSection.Chunk.BoneMap)
                activeBones.Add(boneIndex);

            allIndices.AddRange(builtSection.Indices);
            gpuVertices.AddRange(builtSection.GpuVertices);
            allInfluences.AddRange(builtSection.Influences);

            globalVertexStart += builtSection.VertexCount;
        }

        return new UE3LodModel(new FStaticLODModel
        {
            Sections = [.. sections],
            MultiSizeIndexContainer = new FMultiSizeIndexContainer
            {
                NeedsCPUAccess = true,
                DataTypeSize = 2,
                IndexBuffer = [.. allIndices.Select(static i => (uint)i)]
            },
            ActiveBoneIndices = [.. context.SortBonesByRequiredOrder(activeBones).Select(static i => (ushort)i)],
            Chunks = [.. chunks],
            NumVertices = (uint)gpuVertices.Count,
            RequiredBones = [.. context.RequiredBones],
            RawPointIndices = BuildRawPointIndices(context, gpuVertices.Count),
            NumTexCoords = (uint)context.NumTexCoords,
            VertexBufferGPUSkin = BuildVertexBuffer(context, gpuVertices),
            ColorVertexBuffer = context.HasVertexColors ? BuildColorBuffer(gpuVertices.Count) : null,
            VertexInfluences = [BuildInfluenceBuffer(allInfluences, sections, chunks, context)],
            AdjacencyMultiSizeIndexContainer = new FMultiSizeIndexContainer
            {
                NeedsCPUAccess = false,
                DataTypeSize = 2,
                IndexBuffer = []
            },
            Size = 0
        });
    }

    private sealed record SectionBuildInput(
        int OriginalSectionIndex,
        NeutralSection Section,
        IReadOnlyList<int> SourceVertexIndices,
        bool PreserveOriginal,
        IReadOnlyList<int> SourceImportedSectionIndices,
        string Behavior);
    private sealed record BuiltSectionData(FSkelMeshChunk Chunk, IReadOnlyList<ushort> Indices, IReadOnlyList<FGPUSkinVertexBase> GpuVertices, IReadOnlyList<FVertexInfluence> Influences, int VertexCount);

    /// <summary>
    /// Build a brand-new section when the imported mesh has more sections than the source LOD.
    /// The section gets its own chunk and uses the section's real weights to form a bone map.
    /// </summary>
    private BuiltSectionData BuildNewSection(
        SectionBuildInput input,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights,
        MeshImportContext context,
        int globalVertexStart)
    {
        NeutralSection importedSection = input.Section
            ?? throw new InvalidOperationException("New section data was missing.");

        HashSet<int> usedBones = [];
        for (int i = 0; i < input.SourceVertexIndices.Count; i++)
        {
            foreach (NormalizedWeight weight in normalizedWeights[input.SourceVertexIndices[i]])
            {
                if (weight.Weight > 0)
                    usedBones.Add(weight.BoneIndex);
            }
        }

        IReadOnlyList<int> chunkBoneMap = context.SortBonesByRequiredOrder(usedBones);
        if (chunkBoneMap.Count > byte.MaxValue)
            throw new InvalidOperationException("A UE3 chunk cannot reference more than 255 bones.");

        BuiltVertexData builtVertices = _vertexBuilder.Build(
            importedSection,
            normalizedWeights,
            context,
            chunkBoneMap,
            input.SourceVertexIndices,
            rebuildAsRigidChunk: false,
            preserveRigidVertices: false);
        ushort[] builtIndices = _indexBuilder.Build(importedSection, globalVertexStart, builtVertices.LocalIndexRemap);

        return new BuiltSectionData(
            new FSkelMeshChunk
            {
                BaseVertexIndex = (uint)globalVertexStart,
                RigidVertices = [.. builtVertices.RigidVertices],
                SoftVertices = [.. builtVertices.SoftVertices],
                BoneMap = [.. chunkBoneMap.Select(static x => (ushort)x)],
                NumRigidVertices = builtVertices.RigidVertices.Count,
                NumSoftVertices = builtVertices.SoftVertices.Count,
                MaxBoneInfluences = 4
            },
            builtIndices,
            builtVertices.GpuVertices,
            builtVertices.Influences,
            importedSection.Vertices.Count);
    }
}

