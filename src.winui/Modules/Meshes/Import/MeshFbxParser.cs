using System.Globalization;
using System.Numerics;
using System.Text;

namespace OmegaAssetStudio.WinUI.Modules.Meshes.Import;

public sealed class MeshFbxParser
{
    public FBXMeshData Parse(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("FBX file not found.", filePath);

        if (IsBinaryFbx(filePath))
            throw new NotSupportedException("Binary FBX files are not supported by the in-repo parser.");

        string text = File.ReadAllText(filePath);
        FbxNode root = FbxAsciiParser.Parse(text);
        return FBXMeshDataBuilder.Build(root, filePath);
    }

    public Task<FBXMeshData> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Parse(filePath);
        }, cancellationToken);
    }

    private static bool IsBinaryFbx(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] header = new byte[Math.Min(32, (int)stream.Length)];
        int read = stream.Read(header, 0, header.Length);
        if (read <= 0)
            return false;

        string text = Encoding.ASCII.GetString(header, 0, read);
        return text.Contains("Kaydara FBX Binary", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FBXMeshData
{
    public string SourcePath { get; set; } = string.Empty;
    public List<FBXLodData> Lods { get; } = new();
    public List<FBXSectionData> Sections { get; } = new();
    public List<FBXVertexData> Vertices { get; } = new();
    public List<int> Indices { get; } = new();
    public List<FBXBoneWeightData> BoneWeights { get; } = new();
    public List<string> MaterialNames { get; } = new();
}

public sealed class FBXLodData
{
    public int LodIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<int> SectionIndices { get; } = new();
    public int VertexStart { get; set; }
    public int VertexCount { get; set; }
    public int IndexStart { get; set; }
    public int IndexCount { get; set; }
}

public sealed class FBXSectionData
{
    public int LodIndex { get; set; }
    public int SectionIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MaterialIndex { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public int VertexStart { get; set; }
    public int VertexCount { get; set; }
    public int IndexStart { get; set; }
    public int IndexCount { get; set; }
    public List<int> BoneIndices { get; } = new();
}

public sealed class FBXVertexData
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public Vector4 Tangent { get; set; }
    public Vector2 UV { get; set; }
    public Vector4 Color { get; set; } = Vector4.One;
    public int SourceControlPointIndex { get; set; }
    public int SourcePolygonIndex { get; set; }
    public int SourceCornerIndex { get; set; }
}

public sealed class FBXBoneWeightData
{
    public int VertexIndex { get; set; }
    public int BoneIndex { get; set; }
    public string BoneName { get; set; } = string.Empty;
    public float Weight { get; set; }
}

internal static class FBXMeshDataBuilder
{
    public static FBXMeshData Build(FbxNode root, string sourcePath)
    {
        FBXMeshData meshData = new()
        {
            SourcePath = sourcePath
        };

        Dictionary<string, int> materialNameToIndex = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> objectIdToName = BuildObjectNameLookup(root);
        Dictionary<int, string> objectIdToType = BuildObjectTypeLookup(root);
        Dictionary<int, List<ClusterWeight>> clusterWeights = BuildClusterWeights(root, objectIdToName);
        Dictionary<int, int> objectIdToBoneIndex = BuildBoneIndexLookup(root, objectIdToName, objectIdToType);

        List<FbxGeometry> geometries = BuildGeometries(root);
        foreach (FbxGeometry geometry in geometries)
        {
            int lodIndex = geometry.LodIndex;
            FBXLodData lod = GetOrCreateLod(meshData, lodIndex, geometry.Name);

            int lodVertexStart = meshData.Vertices.Count;
            int lodIndexStart = meshData.Indices.Count;

            List<FBXSectionData> sections = BuildSections(meshData, geometry, lodIndex, materialNameToIndex, objectIdToBoneIndex);
            foreach (FBXSectionData section in sections)
            {
                int sectionIndex = meshData.Sections.Count;
                section.SectionIndex = sectionIndex;
                meshData.Sections.Add(section);
                lod.SectionIndices.Add(sectionIndex);
            }

            int geometryVertexCount = meshData.Vertices.Count - lodVertexStart;
            int geometryIndexCount = meshData.Indices.Count - lodIndexStart;

            lod.VertexStart = lodVertexStart;
            lod.VertexCount = geometryVertexCount;
            lod.IndexStart = lodIndexStart;
            lod.IndexCount = geometryIndexCount;

            AppendBoneWeights(meshData, geometry, clusterWeights, objectIdToBoneIndex, lodVertexStart);
        }

        if (meshData.Lods.Count == 0)
        {
            FBXLodData fallback = new()
            {
                LodIndex = 0,
                Name = "LOD0"
            };
            meshData.Lods.Add(fallback);
        }

        return meshData;
    }

    private static List<FbxGeometry> BuildGeometries(FbxNode root)
    {
        List<FbxGeometry> geometries = new();
        foreach (FbxNode geometryNode in root.FindDescendants("Geometry"))
        {
            string? typeName = geometryNode.GetArgument(2);
            if (!string.Equals(typeName, "Mesh", StringComparison.OrdinalIgnoreCase))
                continue;

            string geometryName = geometryNode.GetArgument(1) ?? geometryNode.Name;
            geometries.Add(FbxGeometry.Parse(geometryNode, geometryName));
        }

        return geometries;
    }

    private static FBXLodData GetOrCreateLod(FBXMeshData meshData, int lodIndex, string lodName)
    {
        FBXLodData? lod = meshData.Lods.FirstOrDefault(item => item.LodIndex == lodIndex);
        if (lod != null)
            return lod;

        lod = new FBXLodData
        {
            LodIndex = lodIndex,
            Name = lodName
        };
        meshData.Lods.Add(lod);
        return lod;
    }

    private static List<FBXSectionData> BuildSections(
        FBXMeshData meshData,
        FbxGeometry geometry,
        int lodIndex,
        Dictionary<string, int> materialNameToIndex,
        Dictionary<int, int> objectIdToBoneIndex)
    {
        Dictionary<int, SectionBuilderState> states = new();
        int polygonCount = geometry.PolygonVertexCounts.Count;
        int cornerCursor = 0;
        int emittedCornerIndex = 0;

        for (int polygonIndex = 0; polygonIndex < polygonCount; polygonIndex++)
        {
            int cornerCount = geometry.PolygonVertexCounts[polygonIndex];
            if (cornerCount < 3)
            {
                cornerCursor += cornerCount;
                continue;
            }

            int materialIndex = geometry.GetMaterialIndexForPolygon(polygonIndex);
            string materialName = geometry.GetMaterialNameForPolygon(polygonIndex);
            if (!materialNameToIndex.ContainsKey(materialName))
                materialNameToIndex[materialName] = materialNameToIndex.Count;

            SectionBuilderState state = GetSectionState(states, materialIndex, materialName, lodIndex);
            state.VertexStart ??= meshData.Vertices.Count;
            state.IndexStart ??= meshData.Indices.Count;

            for (int tri = 1; tri < cornerCount - 1; tri++)
            {
                int a = geometry.PolygonVertexIndices[cornerCursor];
                int b = geometry.PolygonVertexIndices[cornerCursor + tri];
                int c = geometry.PolygonVertexIndices[cornerCursor + tri + 1];

                int emittedA = AddVertex(meshData, geometry, polygonIndex, emittedCornerIndex, a);
                emittedCornerIndex++;
                int emittedB = AddVertex(meshData, geometry, polygonIndex, emittedCornerIndex, b);
                emittedCornerIndex++;
                int emittedC = AddVertex(meshData, geometry, polygonIndex, emittedCornerIndex, c);
                emittedCornerIndex++;

                meshData.Indices.Add(emittedA);
                meshData.Indices.Add(emittedB);
                meshData.Indices.Add(emittedC);
                state.TriangleCount++;

                AddBoneIndicesFromControlPoint(state, geometry, a, objectIdToBoneIndex);
                AddBoneIndicesFromControlPoint(state, geometry, b, objectIdToBoneIndex);
                AddBoneIndicesFromControlPoint(state, geometry, c, objectIdToBoneIndex);
            }

            cornerCursor += cornerCount;
        }

        List<FBXSectionData> sections = new();
        foreach (SectionBuilderState state in states.Values.OrderBy(item => item.MaterialIndex))
        {
            FBXSectionData section = new()
            {
                LodIndex = lodIndex,
                Name = state.MaterialName,
                MaterialIndex = state.MaterialIndex,
                MaterialName = state.MaterialName,
                VertexStart = state.VertexStart ?? meshData.Vertices.Count,
                VertexCount = meshData.Vertices.Count - (state.VertexStart ?? meshData.Vertices.Count),
                IndexStart = state.IndexStart ?? meshData.Indices.Count,
                IndexCount = state.TriangleCount * 3
            };
            section.BoneIndices.AddRange(state.BoneIndices.OrderBy(value => value).Distinct());
            sections.Add(section);
        }

        return sections;
    }

    private static int AddVertex(FBXMeshData meshData, FbxGeometry geometry, int polygonIndex, int cornerIndex, int controlPointIndex)
    {
        FBXVertexData vertex = new()
        {
            SourceControlPointIndex = controlPointIndex,
            SourcePolygonIndex = polygonIndex,
            SourceCornerIndex = cornerIndex,
            Position = geometry.GetPosition(controlPointIndex),
            Normal = geometry.GetNormal(controlPointIndex, polygonIndex, cornerIndex),
            Tangent = geometry.GetTangent(controlPointIndex, polygonIndex, cornerIndex),
            UV = geometry.GetUV(controlPointIndex, polygonIndex, cornerIndex),
            Color = geometry.GetColor(controlPointIndex, polygonIndex, cornerIndex)
        };

        meshData.Vertices.Add(vertex);
        return meshData.Vertices.Count - 1;
    }

    private static void AddBoneIndicesFromControlPoint(
        SectionBuilderState state,
        FbxGeometry geometry,
        int controlPointIndex,
        Dictionary<int, int> objectIdToBoneIndex)
    {
        foreach (ClusterWeight weight in geometry.GetWeightsForControlPoint(controlPointIndex))
        {
            if (weight.Weight <= 0.0f)
                continue;

            if (weight.BoneObjectId.HasValue && objectIdToBoneIndex.TryGetValue(weight.BoneObjectId.Value, out int boneIndex))
            {
                if (!state.BoneIndices.Contains(boneIndex))
                    state.BoneIndices.Add(boneIndex);
            }
        }
    }

    private static void AppendBoneWeights(
        FBXMeshData meshData,
        FbxGeometry geometry,
        Dictionary<int, List<ClusterWeight>> clusterWeights,
        Dictionary<int, int> objectIdToBoneIndex,
        int vertexBaseIndex)
    {
        for (int controlPointIndex = 0; controlPointIndex < geometry.ControlPointCount; controlPointIndex++)
        {
            if (!clusterWeights.TryGetValue(controlPointIndex, out List<ClusterWeight>? weights) || weights.Count == 0)
                continue;

            float totalWeight = weights.Sum(item => Math.Max(0.0f, item.Weight));
            if (totalWeight <= 0.0f)
                continue;

            foreach (ClusterWeight weight in weights.OrderByDescending(item => item.Weight).Take(4))
            {
                if (weight.Weight <= 0.0f)
                    continue;

                if (!weight.BoneObjectId.HasValue)
                    continue;

                if (!objectIdToBoneIndex.TryGetValue(weight.BoneObjectId.Value, out int boneIndex))
                    continue;

                meshData.BoneWeights.Add(new FBXBoneWeightData
                {
                    VertexIndex = vertexBaseIndex + controlPointIndex,
                    BoneIndex = boneIndex,
                    BoneName = weight.BoneName,
                    Weight = weight.Weight / totalWeight
                });
            }
        }
    }

    private static SectionBuilderState GetSectionState(
        Dictionary<int, SectionBuilderState> states,
        int materialIndex,
        string materialName,
        int lodIndex)
    {
        if (states.TryGetValue(materialIndex, out SectionBuilderState? state))
            return state;

        state = new SectionBuilderState
        {
            MaterialIndex = materialIndex,
            MaterialName = materialName,
            LodIndex = lodIndex
        };
        states[materialIndex] = state;
        return state;
    }

    private static Dictionary<string, int> BuildBoneNameLookup(FbxNode root)
    {
        Dictionary<string, int> boneNames = new(StringComparer.OrdinalIgnoreCase);
        int boneIndex = 0;
        foreach (FbxNode model in root.FindDescendants("Model"))
        {
            string typeName = model.GetArgument(2) ?? string.Empty;
            if (!string.Equals(typeName, "LimbNode", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(typeName, "Null", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(typeName, "Root", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = model.GetArgument(1) ?? model.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!boneNames.ContainsKey(name))
                boneNames[name] = boneIndex++;
        }

        return boneNames;
    }

    private static Dictionary<int, int> BuildBoneIndexLookup(FbxNode root, Dictionary<int, string> objectIdToName, Dictionary<int, string> objectIdToType)
    {
        Dictionary<string, int> boneNames = BuildBoneNameLookup(root);
        Dictionary<int, int> lookup = new();
        foreach (KeyValuePair<int, string> item in objectIdToName)
        {
            if (!objectIdToType.TryGetValue(item.Key, out string? typeName))
                continue;

            if (!string.Equals(typeName, "LimbNode", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(typeName, "Null", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(typeName, "Root", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (boneNames.TryGetValue(item.Value, out int boneIndex))
                lookup[item.Key] = boneIndex;
        }

        return lookup;
    }

    private static Dictionary<int, List<ClusterWeight>> BuildClusterWeights(FbxNode root, Dictionary<int, string> objectIdToName)
    {
        Dictionary<int, List<ClusterWeight>> weightsByControlPoint = new();
        Dictionary<int, int> clusterToBoneObjectId = new();

        foreach (FbxNode connection in root.FindDescendants("C"))
        {
            string? relationType = connection.GetArgument(0);
            if (!string.Equals(relationType, "OO", StringComparison.OrdinalIgnoreCase))
                continue;

            int? childId = connection.GetArgumentAsInt(1);
            int? parentId = connection.GetArgumentAsInt(2);
            if (childId.HasValue && parentId.HasValue)
                clusterToBoneObjectId[childId.Value] = parentId.Value;
        }

        foreach (FbxNode deformers in root.FindDescendants("Deformer"))
        {
            string deformType = deformers.GetArgument(2) ?? string.Empty;
            if (!string.Equals(deformType, "Cluster", StringComparison.OrdinalIgnoreCase))
                continue;

            int? clusterObjectId = deformers.GetArgumentAsInt(0);
            if (!clusterObjectId.HasValue)
                continue;

            int? boneObjectId = clusterToBoneObjectId.TryGetValue(clusterObjectId.Value, out int mappedBoneId) ? mappedBoneId : null;
            string boneName = boneObjectId.HasValue && objectIdToName.TryGetValue(boneObjectId.Value, out string? mappedName) ? mappedName : string.Empty;

            List<int> indexes = ParseIntArray(FindArrayValue(deformers, "Indexes"));
            List<float> weightValues = ParseFloatArray(FindArrayValue(deformers, "Weights"));
            int limit = Math.Min(indexes.Count, weightValues.Count);
            for (int i = 0; i < limit; i++)
            {
                int controlPointIndex = indexes[i];
                float weight = weightValues[i];
                if (!weightsByControlPoint.TryGetValue(controlPointIndex, out List<ClusterWeight>? clusterWeights))
                {
                    clusterWeights = new List<ClusterWeight>();
                    weightsByControlPoint[controlPointIndex] = clusterWeights;
                }

                clusterWeights.Add(new ClusterWeight
                {
                    BoneObjectId = boneObjectId,
                    BoneName = boneName,
                    Weight = weight
                });
            }
        }

        return weightsByControlPoint;
    }

    private static Dictionary<int, string> BuildObjectNameLookup(FbxNode root)
    {
        Dictionary<int, string> lookup = new();
        foreach (FbxNode node in root.FindDescendants("Model").Concat(root.FindDescendants("Geometry")).Concat(root.FindDescendants("Deformer")))
        {
            int? objectId = node.GetArgumentAsInt(0);
            if (!objectId.HasValue)
                continue;

            string name = node.GetArgument(1) ?? node.Name;
            if (!lookup.ContainsKey(objectId.Value))
                lookup[objectId.Value] = name;
        }

        return lookup;
    }

    private static Dictionary<int, string> BuildObjectTypeLookup(FbxNode root)
    {
        Dictionary<int, string> lookup = new();
        foreach (FbxNode node in root.FindDescendants("Model").Concat(root.FindDescendants("Geometry")).Concat(root.FindDescendants("Deformer")))
        {
            int? objectId = node.GetArgumentAsInt(0);
            if (!objectId.HasValue)
                continue;

            string typeName = node.GetArgument(2) ?? string.Empty;
            if (!lookup.ContainsKey(objectId.Value))
                lookup[objectId.Value] = typeName;
        }

        return lookup;
    }

    private static string FindArrayValue(FbxNode node, string key)
    {
        FbxNode? arrayNode = node.FindDescendant(key);
        if (arrayNode == null)
            return string.Empty;

        if (arrayNode.Properties.TryGetValue("a", out string? value))
            return value;

        if (arrayNode.Properties.TryGetValue(key, out value))
            return value;

        return string.Empty;
    }

    private static List<int> ParseIntArray(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<int>();

        string[] tokens = value.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        List<int> result = new(tokens.Length);
        foreach (string token in tokens)
        {
            string cleaned = token.Trim();
            if (cleaned.Length == 0)
                continue;

            if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                result.Add(parsed);
        }

        return result;
    }

    private static List<float> ParseFloatArray(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<float>();

        string[] tokens = value.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        List<float> result = new(tokens.Length);
        foreach (string token in tokens)
        {
            string cleaned = token.Trim();
            if (cleaned.Length == 0)
                continue;

            if (float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                result.Add(parsed);
        }

        return result;
    }

    private static List<Vector3> ParseVector3Array(string value)
    {
        List<float> values = ParseFloatArray(value);
        List<Vector3> result = new(values.Count / 3);
        for (int i = 0; i + 2 < values.Count; i += 3)
            result.Add(new Vector3(values[i], values[i + 1], values[i + 2]));

        return result;
    }

    private static List<Vector2> ParseVector2Array(string value)
    {
        List<float> values = ParseFloatArray(value);
        List<Vector2> result = new(values.Count / 2);
        for (int i = 0; i + 1 < values.Count; i += 2)
            result.Add(new Vector2(values[i], values[i + 1]));

        return result;
    }

    private static FbxNode FindNodeWithArray(FbxNode node, string name)
    {
        FbxNode? direct = node.FindDescendant(name);
        return direct ?? new FbxNode(name);
    }

    private sealed class SectionBuilderState
    {
        public int MaterialIndex { get; set; }
        public string MaterialName { get; set; } = string.Empty;
        public int LodIndex { get; set; }
        public int? VertexStart { get; set; }
        public int? IndexStart { get; set; }
        public int TriangleCount { get; set; }
        public List<int> BoneIndices { get; } = new();
    }

    private sealed class ClusterWeight
    {
        public int? BoneObjectId { get; set; }
        public string BoneName { get; set; } = string.Empty;
        public float Weight { get; set; }
    }

    private sealed class FbxGeometry
    {
        private readonly Dictionary<int, List<ClusterWeight>> _weightsByControlPoint;
        private readonly Dictionary<int, int> _polygonMaterialIndices;
        private readonly Dictionary<int, string> _materialNamesByIndex;
        private readonly List<Vector3> _positions;
        private readonly List<Vector3> _normals;
        private readonly List<int> _normalIndices;
        private readonly string _normalMapping;
        private readonly string _normalReference;
        private readonly List<Vector2> _uvs;
        private readonly List<int> _uvIndices;
        private readonly string _uvMapping;
        private readonly string _uvReference;
        private readonly List<Vector4> _colors;
        private readonly List<int> _colorIndices;
        private readonly string _colorMapping;
        private readonly string _colorReference;

        private FbxGeometry(
            string name,
            int lodIndex,
            List<Vector3> positions,
            List<int> polygonVertexIndices,
            List<int> polygonVertexCounts,
            Dictionary<int, List<ClusterWeight>> weightsByControlPoint,
            Dictionary<int, int> polygonMaterialIndices,
            Dictionary<int, string> materialNamesByIndex,
            List<Vector3> normals,
            List<int> normalIndices,
            string normalMapping,
            string normalReference,
            List<Vector2> uvs,
            List<int> uvIndices,
            string uvMapping,
            string uvReference,
            List<Vector4> colors,
            List<int> colorIndices,
            string colorMapping,
            string colorReference)
        {
            Name = name;
            LodIndex = lodIndex;
            _positions = positions;
            PolygonVertexIndices = polygonVertexIndices;
            PolygonVertexCounts = polygonVertexCounts;
            _weightsByControlPoint = weightsByControlPoint;
            _polygonMaterialIndices = polygonMaterialIndices;
            _materialNamesByIndex = materialNamesByIndex;
            _normals = normals;
            _normalIndices = normalIndices;
            _normalMapping = normalMapping;
            _normalReference = normalReference;
            _uvs = uvs;
            _uvIndices = uvIndices;
            _uvMapping = uvMapping;
            _uvReference = uvReference;
            _colors = colors;
            _colorIndices = colorIndices;
            _colorMapping = colorMapping;
            _colorReference = colorReference;
        }

        public string Name { get; }
        public int LodIndex { get; }
        public List<int> PolygonVertexIndices { get; }
        public List<int> PolygonVertexCounts { get; }
        public int ControlPointCount => _positions.Count;

        public static FbxGeometry Parse(FbxNode geometryNode, string name)
        {
            int lodIndex = ExtractLodIndex(name);
            List<Vector3> positions = ParseVector3Array(FindArrayValue(geometryNode, "Vertices"));
            List<int> polygonIndices = ParseIntArray(FindArrayValue(geometryNode, "PolygonVertexIndex"));
            List<int> polygonVertexCounts = BuildPolygonCounts(polygonIndices);

            Dictionary<int, List<ClusterWeight>> weightsByControlPoint = new();
            Dictionary<int, int> polygonMaterialIndices = ParsePolygonMaterials(geometryNode);
            Dictionary<int, string> materialNamesByIndex = ParseMaterialNames(geometryNode);
            ParseLayerElementVectors(geometryNode, "LayerElementNormal", out List<Vector3> normals, out List<int> normalIndices, out string normalMapping, out string normalReference);
            ParseLayerElementVectors2(geometryNode, "LayerElementUV", out List<Vector2> uvs, out List<int> uvIndices, out string uvMapping, out string uvReference);
            ParseLayerElementVectors4(geometryNode, "LayerElementColor", out List<Vector4> colors, out List<int> colorIndices, out string colorMapping, out string colorReference);

            return new FbxGeometry(
                name,
                lodIndex,
                positions,
                polygonIndices,
                polygonVertexCounts,
                weightsByControlPoint,
                polygonMaterialIndices,
                materialNamesByIndex,
                normals,
                normalIndices,
                normalMapping,
                normalReference,
                uvs,
                uvIndices,
                uvMapping,
                uvReference,
                colors,
                colorIndices,
                colorMapping,
                colorReference);
        }

        public Vector3 GetPosition(int controlPointIndex)
        {
            if (controlPointIndex >= 0 && controlPointIndex < _positions.Count)
                return _positions[controlPointIndex];

            return Vector3.Zero;
        }

        public Vector3 GetNormal(int controlPointIndex, int polygonIndex, int cornerIndex)
        {
            if (_normals.Count == 0)
                return Vector3.UnitY;

            int resolvedIndex = ResolveLayerIndex(controlPointIndex, polygonIndex, cornerIndex, _normalMapping, _normalReference, _normalIndices, _normals.Count);
            if (resolvedIndex >= 0 && resolvedIndex < _normals.Count)
                return _normals[resolvedIndex];

            return Vector3.UnitY;
        }

        public Vector4 GetTangent(int controlPointIndex, int polygonIndex, int cornerIndex)
        {
            Vector3 normal = GetNormal(controlPointIndex, polygonIndex, cornerIndex);
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitZ));
            if (tangent.LengthSquared() < 0.0001f)
                tangent = Vector3.UnitX;

            return new Vector4(tangent, 1.0f);
        }

        public Vector2 GetUV(int controlPointIndex, int polygonIndex, int cornerIndex)
        {
            if (_uvs.Count == 0)
                return Vector2.Zero;

            int resolvedIndex = ResolveLayerIndex(controlPointIndex, polygonIndex, cornerIndex, _uvMapping, _uvReference, _uvIndices, _uvs.Count);
            if (resolvedIndex >= 0 && resolvedIndex < _uvs.Count)
                return _uvs[resolvedIndex];

            return Vector2.Zero;
        }

        public Vector4 GetColor(int controlPointIndex, int polygonIndex, int cornerIndex)
        {
            if (_colors.Count == 0)
                return Vector4.One;

            int resolvedIndex = ResolveLayerIndex(controlPointIndex, polygonIndex, cornerIndex, _colorMapping, _colorReference, _colorIndices, _colors.Count);
            if (resolvedIndex >= 0 && resolvedIndex < _colors.Count)
                return _colors[resolvedIndex];

            return Vector4.One;
        }

        public IReadOnlyList<ClusterWeight> GetWeightsForControlPoint(int controlPointIndex)
        {
            if (_weightsByControlPoint.TryGetValue(controlPointIndex, out List<ClusterWeight>? weights))
                return weights;

            return Array.Empty<ClusterWeight>();
        }

        public int GetMaterialIndexForPolygon(int polygonIndex)
        {
            if (_polygonMaterialIndices.TryGetValue(polygonIndex, out int materialIndex))
                return materialIndex;

            return 0;
        }

        public string GetMaterialNameForPolygon(int polygonIndex)
        {
            int materialIndex = GetMaterialIndexForPolygon(polygonIndex);
            if (_materialNamesByIndex.TryGetValue(materialIndex, out string? name) && !string.IsNullOrWhiteSpace(name))
                return name;

            return $"Material_{materialIndex}";
        }

        private static int ResolveLayerIndex(
            int controlPointIndex,
            int polygonIndex,
            int cornerIndex,
            string mapping,
            string reference,
            List<int> indices,
            int valueCount)
        {
            if (valueCount == 0)
                return -1;

            if (string.Equals(mapping, "ByControlPoint", StringComparison.OrdinalIgnoreCase))
                return Math.Clamp(controlPointIndex, 0, valueCount - 1);

            if (string.Equals(mapping, "ByPolygonVertex", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(reference, "Direct", StringComparison.OrdinalIgnoreCase))
                    return Math.Clamp(cornerIndex, 0, valueCount - 1);

                if (string.Equals(reference, "IndexToDirect", StringComparison.OrdinalIgnoreCase) && indices.Count > cornerIndex)
                    return Math.Clamp(indices[cornerIndex], 0, valueCount - 1);
            }

            if (string.Equals(mapping, "ByPolygon", StringComparison.OrdinalIgnoreCase))
                return Math.Clamp(polygonIndex, 0, valueCount - 1);

            return Math.Clamp(controlPointIndex, 0, valueCount - 1);
        }

        private static List<int> BuildPolygonCounts(List<int> polygonIndices)
        {
            List<int> counts = new();
            int current = 0;
            foreach (int index in polygonIndices)
            {
                current++;
                if (index < 0)
                {
                    counts.Add(current);
                    current = 0;
                }
            }

            if (current > 0)
                counts.Add(current);

            return counts;
        }

        private static Dictionary<int, int> ParsePolygonMaterials(FbxNode geometryNode)
        {
            Dictionary<int, int> polygonMaterials = new();
            FbxNode? layerElement = geometryNode.FindDescendant("LayerElementMaterial");
            if (layerElement == null)
                return polygonMaterials;

            string mapping = layerElement.GetPropertyValue("MappingInformationType");
            string reference = layerElement.GetPropertyValue("ReferenceInformationType");
            List<int> materials = ParseIntArray(FindArrayValue(layerElement, "Materials"));
            List<int> indices = ParseIntArray(FindArrayValue(layerElement, "MaterialsIndex"));
            if (indices.Count == 0)
                indices = ParseIntArray(FindArrayValue(layerElement, "MaterialIndex"));

            if (materials.Count == 0)
                materials = indices;

            int polygonIndex = 0;
            int cursor = 0;
            foreach (int count in BuildPolygonCounts(ParseIntArray(FindArrayValue(geometryNode, "PolygonVertexIndex"))))
            {
                int materialIndex = 0;
                if (string.Equals(mapping, "ByPolygon", StringComparison.OrdinalIgnoreCase) && materials.Count > polygonIndex)
                    materialIndex = materials[polygonIndex];
                else if (string.Equals(reference, "IndexToDirect", StringComparison.OrdinalIgnoreCase) && indices.Count > polygonIndex)
                    materialIndex = indices[polygonIndex];
                else if (materials.Count > polygonIndex)
                    materialIndex = materials[polygonIndex];

                polygonMaterials[polygonIndex] = materialIndex;
                polygonIndex++;
                cursor += count;
            }

            return polygonMaterials;
        }

        private static Dictionary<int, string> ParseMaterialNames(FbxNode geometryNode)
        {
            Dictionary<int, string> names = new();
            FbxNode? layerElement = geometryNode.FindDescendant("LayerElementMaterial");
            if (layerElement == null)
                return names;

            List<string> materialNames = ParseStringArray(FindArrayValue(layerElement, "Materials"));
            for (int i = 0; i < materialNames.Count; i++)
            {
                if (!names.ContainsKey(i))
                    names[i] = materialNames[i];
            }

            return names;
        }

        private static void ParseLayerElementVectors(
            FbxNode geometryNode,
            string nodeName,
            out List<Vector3> values,
            out List<int> indices,
            out string mapping,
            out string reference)
        {
            values = new List<Vector3>();
            indices = new List<int>();
            mapping = string.Empty;
            reference = string.Empty;

            FbxNode? layerElement = geometryNode.FindDescendant(nodeName);
            if (layerElement == null)
                return;

            mapping = layerElement.GetPropertyValue("MappingInformationType");
            reference = layerElement.GetPropertyValue("ReferenceInformationType");
            values = ParseVector3Array(FindArrayValue(layerElement, "Normals"));
            if (values.Count == 0)
                values = ParseVector3Array(FindArrayValue(layerElement, "NormalsIndex"));
            indices = ParseIntArray(FindArrayValue(layerElement, "NormalsIndex"));
            if (indices.Count == 0)
                indices = ParseIntArray(FindArrayValue(layerElement, "NormalsIndices"));
        }

        private static void ParseLayerElementVectors2(
            FbxNode geometryNode,
            string nodeName,
            out List<Vector2> values,
            out List<int> indices,
            out string mapping,
            out string reference)
        {
            values = new List<Vector2>();
            indices = new List<int>();
            mapping = string.Empty;
            reference = string.Empty;

            FbxNode? layerElement = geometryNode.FindDescendant(nodeName);
            if (layerElement == null)
                return;

            mapping = layerElement.GetPropertyValue("MappingInformationType");
            reference = layerElement.GetPropertyValue("ReferenceInformationType");
            values = ParseVector2Array(FindArrayValue(layerElement, "UV"));
            indices = ParseIntArray(FindArrayValue(layerElement, "UVIndex"));
            if (indices.Count == 0)
                indices = ParseIntArray(FindArrayValue(layerElement, "UVIndices"));
        }

        private static void ParseLayerElementVectors4(
            FbxNode geometryNode,
            string nodeName,
            out List<Vector4> values,
            out List<int> indices,
            out string mapping,
            out string reference)
        {
            values = new List<Vector4>();
            indices = new List<int>();
            mapping = string.Empty;
            reference = string.Empty;

            FbxNode? layerElement = geometryNode.FindDescendant(nodeName);
            if (layerElement == null)
                return;

            mapping = layerElement.GetPropertyValue("MappingInformationType");
            reference = layerElement.GetPropertyValue("ReferenceInformationType");
            List<float> flatValues = ParseFloatArray(FindArrayValue(layerElement, "Colors"));
            for (int i = 0; i + 3 < flatValues.Count; i += 4)
                values.Add(new Vector4(flatValues[i], flatValues[i + 1], flatValues[i + 2], flatValues[i + 3]));

            indices = ParseIntArray(FindArrayValue(layerElement, "ColorIndex"));
            if (indices.Count == 0)
                indices = ParseIntArray(FindArrayValue(layerElement, "ColorIndices"));
        }

        private static List<string> ParseStringArray(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<string>();

            List<string> result = new();
            StringBuilder current = new();
            bool insideQuote = false;
            foreach (char ch in value)
            {
                if (ch == '"')
                {
                    insideQuote = !insideQuote;
                    continue;
                }

                if (ch == ',' && !insideQuote)
                {
                    string text = current.ToString().Trim();
                    if (text.Length > 0)
                        result.Add(text);
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            string last = current.ToString().Trim();
            if (last.Length > 0)
                result.Add(last);

            return result;
        }

        private static int ExtractLodIndex(string name)
        {
            ReadOnlySpan<char> span = name.AsSpan();
            for (int i = 0; i < span.Length - 3; i++)
            {
                if ((span[i] == 'L' || span[i] == 'l')
                    && (span[i + 1] == 'O' || span[i + 1] == 'o')
                    && (span[i + 2] == 'D' || span[i + 2] == 'd'))
                {
                    int start = i + 3;
                    int end = start;
                    while (end < span.Length && char.IsDigit(span[end]))
                        end++;

                    if (end > start && int.TryParse(span.Slice(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lodIndex))
                        return lodIndex;
                }
            }

            return 0;
        }
    }
}

internal sealed class FbxAsciiParser
{
    public static FbxNode Parse(string text)
    {
        FbxNode root = new("Root");
        Stack<FbxNode> stack = new();
        stack.Push(root);

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (string rawLine in lines)
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            int closingCount = CountLeadingClosings(line);
            for (int i = 0; i < closingCount && stack.Count > 1; i++)
                stack.Pop();

            line = line.TrimStart('}');
            line = line.Trim();
            if (line.Length == 0)
                continue;

            bool opensBlock = line.EndsWith("{", StringComparison.Ordinal);
            if (opensBlock)
                line = line[..^1].Trim();

            if (line.Length == 0)
                continue;

            if (TryParseProperty(line, out string propertyName, out string propertyValue))
            {
                stack.Peek().Properties[propertyName] = propertyValue;
            }
            else
            {
                FbxNode node = ParseNode(line);
                stack.Peek().Children.Add(node);
                if (opensBlock)
                    stack.Push(node);
            }
        }

        return root;
    }

    private static FbxNode ParseNode(string line)
    {
        int colonIndex = line.IndexOf(':');
        if (colonIndex < 0)
            return new FbxNode(line);

        string name = line[..colonIndex].Trim();
        string remainder = line[(colonIndex + 1)..].Trim();
        List<string> arguments = SplitArguments(remainder);

        FbxNode node = new(name);
        node.Arguments.AddRange(arguments);
        return node;
    }

    private static bool TryParseProperty(string line, out string name, out string value)
    {
        int colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
        {
            name = string.Empty;
            value = string.Empty;
            return false;
        }

        name = line[..colonIndex].Trim();
        value = line[(colonIndex + 1)..].Trim();
        return true;
    }

    private static string StripComment(string line)
    {
        bool insideQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
                insideQuote = !insideQuote;
            else if (ch == ';' && !insideQuote)
                return line[..i];
        }

        return line;
    }

    private static int CountLeadingClosings(string line)
    {
        int count = 0;
        foreach (char ch in line)
        {
            if (ch != '}')
                break;

            count++;
        }

        return count;
    }

    private static List<string> SplitArguments(string value)
    {
        List<string> result = new();
        StringBuilder current = new();
        bool insideQuote = false;
        int bracketDepth = 0;

        foreach (char ch in value)
        {
            if (ch == '"')
            {
                insideQuote = !insideQuote;
                continue;
            }

            if (!insideQuote)
            {
                if (ch == '{' || ch == '(' || ch == '[')
                    bracketDepth++;
                else if (ch == '}' || ch == ')' || ch == ']')
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                else if (ch == ',' && bracketDepth == 0)
                {
                    string token = current.ToString().Trim();
                    if (token.Length > 0)
                        result.Add(token);
                    current.Clear();
                    continue;
                }
            }

            current.Append(ch);
        }

        string last = current.ToString().Trim();
        if (last.Length > 0)
            result.Add(last);

        return result;
    }
}

internal sealed class FbxNode
{
    public FbxNode(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public List<string> Arguments { get; } = new();
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FbxNode> Children { get; } = new();

    public string? GetArgument(int index)
    {
        if (index < 0 || index >= Arguments.Count)
            return null;

        return Unquote(Arguments[index]);
    }

    public int? GetArgumentAsInt(int index)
    {
        string? argument = GetArgument(index);
        if (argument == null)
            return null;

        if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return parsed;

        return null;
    }

    public string GetPropertyValue(string key)
    {
        if (Properties.TryGetValue(key, out string? value))
            return Unquote(value);

        return string.Empty;
    }

    public FbxNode? FindDescendant(string name)
    {
        foreach (FbxNode child in Children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                return child;

            FbxNode? descendant = child.FindDescendant(name);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    public IEnumerable<FbxNode> FindDescendants(string name)
    {
        foreach (FbxNode child in Children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                yield return child;

            foreach (FbxNode descendant in child.FindDescendants(name))
                yield return descendant;
        }
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];

        return value;
    }
}

