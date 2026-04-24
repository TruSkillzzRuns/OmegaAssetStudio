using System.Numerics;
using System.Text.Json;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI.Modules.MFL.Engine;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;
using MflScene = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.Scene;
using MflCamera = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.Camera;
using MflMeshHitResult = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.MeshHitResult;
using MeshNode = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.MeshNode;
using MflMeshExporter = OmegaAssetStudio.WinUI.Modules.MFL.Engine.MeshExporter;

namespace OmegaAssetStudio.WinUI.Modules.MFL;

public sealed class MFLService
{
    public event EventHandler<string>? LogAdded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly string MflLogPath = RuntimeLogPaths.MflLogPath;

    private readonly MeshLoader meshLoader = new();
    private readonly UE3ToPreviewMeshConverter previewMeshConverter = new();
    private readonly MeshPreviewGameMaterialResolver previewMaterialResolver = new();
    private readonly RegionSelector regionSelector = new();
    private readonly RegionExtractor regionExtractor = new();
    private readonly MeshTransformer meshTransformer = new();
    private readonly MeshMerger meshMerger = new();
    private readonly MeshRebuilder meshRebuilder = new();
    private readonly MflMeshExporter meshExporter = new();
    private readonly SectionCalibrator sectionCalibrator = new();
    private readonly List<Mesh> additionalMeshes = [];
    private readonly List<string> logs = [];

    public MFLService(string? workspaceRoot = null)
    {
        WorkspaceRoot = workspaceRoot ?? Path.Combine(AppContext.BaseDirectory, "MFLWorkspace");
        Directory.CreateDirectory(WorkspaceRoot);
        Directory.CreateDirectory(MeshesFolder);
        Directory.CreateDirectory(ExportsFolder);
        Scene.ShowGroundPlane = false;
        Scene.GhostInactiveMesh = false;
        Scene.MeshNodeA.PropertyChanged += SceneNode_PropertyChanged;
        Scene.MeshNodeB.PropertyChanged += SceneNode_PropertyChanged;
        StatusMessage = "Ready.";
        AddLog($"MFL workspace initialized at {WorkspaceRoot}.");
    }

    public string WorkspaceRoot { get; }

    public string MeshesFolder => Path.Combine(WorkspaceRoot, "Meshes");

    public string ExportsFolder => Path.Combine(WorkspaceRoot, "Exports");

    public Mesh? MeshA { get; private set; }

    public Mesh? MeshB { get; private set; }

    public MflScene Scene { get; } = new();

    public MflCamera Camera { get; } = new();

    public IReadOnlyList<Mesh> AdditionalMeshes => additionalMeshes;

    public Mesh? ExtractedRegion { get; private set; }

    public Mesh? FusedMesh { get; private set; }

    public string PreviewFocus { get; private set; } = "Auto";

    public RegionSelectionResult? CurrentSelection { get; private set; }

    public RegionSelectionResult? TargetSelection { get; private set; }

    public string SelectionSourceMeshKey { get; private set; } = "MeshA";

    public string SelectionTargetMeshKey { get; private set; } = "MeshB";

    public MeshRebuildReport? LastRebuildReport { get; private set; }

    public MFLValidationReport? LastValidationReport { get; private set; }

    public string StatusMessage { get; private set; }

    public IReadOnlyList<string> LogEntries => logs;

    public string MeshALoadDiagnostics { get; private set; } = "Mesh A not loaded.";

    public string MeshBLoadDiagnostics { get; private set; } = "Mesh B not loaded.";

    public SectionCalibrationReport? MeshACalibration { get; private set; }

    public SectionCalibrationReport? MeshBCalibration { get; private set; }

    public IReadOnlyList<string> GetLoadTrace(string slotLabel)
    {
        string prefix = string.Equals(slotLabel, "Mesh B", StringComparison.OrdinalIgnoreCase) ? "Mesh B:" : "Mesh A:";
        return logs
            .Where(entry => entry.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<string> GetSkeletalMeshExports(string upkPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            return [];

        if (!File.Exists(upkPath))
            throw new FileNotFoundException($"UPK file '{upkPath}' was not found.", upkPath);

        return meshLoader.GetSkeletalMeshExports(upkPath);
    }

    public IReadOnlyList<MeshPreviewPoint> BuildPreviewPoints(int maxPoints = 256)
    {
        Mesh? mesh = GetFocusedPreviewMesh(PreviewFocus);
        if (mesh is null || mesh.Vertices.Count == 0)
            return [];

        float minX = mesh.Bounds.IsEmpty ? mesh.Vertices.Min(vertex => vertex.Position.X) : mesh.Bounds.Min.X;
        float maxX = mesh.Bounds.IsEmpty ? mesh.Vertices.Max(vertex => vertex.Position.X) : mesh.Bounds.Max.X;
        float minY = mesh.Bounds.IsEmpty ? mesh.Vertices.Min(vertex => vertex.Position.Y) : mesh.Bounds.Min.Y;
        float maxY = mesh.Bounds.IsEmpty ? mesh.Vertices.Max(vertex => vertex.Position.Y) : mesh.Bounds.Max.Y;
        float width = Math.Max(maxX - minX, 0.001f);
        float height = Math.Max(maxY - minY, 0.001f);

        HashSet<int> selectedVertices = CurrentSelection?.VertexIndices is null
            ? []
            : CurrentSelection.VertexIndices.ToHashSet();

        List<MeshPreviewPoint> points = [];
        int step = Math.Max(1, mesh.Vertices.Count / Math.Max(1, maxPoints));
        for (int index = 0; index < mesh.Vertices.Count; index += step)
        {
            Vertex vertex = mesh.Vertices[index];
            double left = ((vertex.Position.X - minX) / width) * 420.0;
            double top = (1.0 - ((vertex.Position.Y - minY) / height)) * 260.0;
            bool selected = selectedVertices.Contains(index);
            points.Add(new MeshPreviewPoint
            {
                Index = index,
                Label = index.ToString(),
                Left = left,
                Top = top,
                IsSelected = selected,
                FillBrush = new SolidColorBrush(selected ? Colors.OrangeRed : Colors.DeepSkyBlue),
                BorderBrush = new SolidColorBrush(selected ? Colors.White : Colors.Black)
            });
        }

        if (CurrentSelection is not null)
        {
            foreach (int vertexIndex in CurrentSelection.VertexIndices)
            {
                if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
                    continue;

                if (points.Any(point => point.Index == vertexIndex))
                    continue;

                Vertex vertex = mesh.Vertices[vertexIndex];
                double left = ((vertex.Position.X - minX) / width) * 420.0;
                double top = (1.0 - ((vertex.Position.Y - minY) / height)) * 260.0;
                points.Add(new MeshPreviewPoint
                {
                    Index = vertexIndex,
                    Label = vertexIndex.ToString(),
                    Left = left,
                    Top = top,
                    IsSelected = true,
                    FillBrush = new SolidColorBrush(Colors.OrangeRed),
                    BorderBrush = new SolidColorBrush(Colors.White)
                });
            }
        }

        return points
            .OrderBy(point => point.Index)
            .ToList();
    }

    public void SetPreviewFocus(string focus)
    {
        PreviewFocus = string.IsNullOrWhiteSpace(focus) ? "Auto" : focus;
        AddLog($"Preview focus set to {PreviewFocus}.");
    }

    public Mesh? GetFocusedPreviewMesh(string focus)
    {
        return focus switch
        {
            "MeshA" => MeshA,
            "MeshB" => MeshB,
            "Extracted" => ExtractedRegion,
            "Fused" => FusedMesh,
            "Additional" => additionalMeshes.FirstOrDefault(),
            _ => GetPreviewMesh()
        };
    }

    public IReadOnlyList<MeshValidationSummary> BuildValidationSummaries()
    {
        List<MeshValidationSummary> summaries = [];
        if (MeshA is not null)
            summaries.Add(CreateValidationSummary(MeshA));
        if (MeshB is not null)
            summaries.Add(CreateValidationSummary(MeshB));
        foreach (Mesh mesh in additionalMeshes)
            summaries.Add(CreateValidationSummary(mesh));
        if (ExtractedRegion is not null)
            summaries.Add(CreateValidationSummary(ExtractedRegion));
        if (FusedMesh is not null)
            summaries.Add(CreateValidationSummary(FusedMesh));
        return summaries;
    }

    public void LoadDemoPair()
    {
        MeshA = LoadOrCreate("MeshA.mflmesh", "MeshA", new Vector3(-1.4f, 0.0f, 0.0f));
        MeshB = LoadOrCreate("MeshB.mflmesh", "MeshB", new Vector3(1.4f, 0.0f, 0.0f));
        MeshACalibration = null;
        MeshBCalibration = null;
        additionalMeshes.Clear();
        ExtractedRegion = null;
        FusedMesh = null;
        CurrentSelection = null;
        LastRebuildReport = null;
        PreviewFocus = "Auto";
        Scene.SetActiveMesh("MeshA");
        Scene.GhostInactiveMesh = false;
        SyncScene();
        FrameCameraToLoadedMeshes();
        StatusMessage = "Demo pair loaded.";
        AddLog("Loaded demo pair into the isolated MFL workspace.");
    }

    public async Task<Mesh> LoadMeshAAsync(string upkPath, string? skeletalExportPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new InvalidOperationException("Select a UPK for Mesh A before loading.");

        MeshA = await LoadUpkMeshAsync("Mesh A", upkPath, skeletalExportPath, "MeshA", new Vector3(-1.2f, 0.0f, 0.0f)).ConfigureAwait(true);
        MeshACalibration = null;
        SetMeshALoadDiagnostic($"Mesh A loaded: {DescribeMesh(MeshA)}");
        SyncScene();
        FrameCameraToLoadedMeshes();
        StatusMessage = $"Mesh A loaded: {MeshA.Name}.";
        AddLog($"Loaded Mesh A from {upkPath}.");
        return MeshA;
    }

    public async Task<Mesh> LoadMeshBAsync(string upkPath, string? skeletalExportPath)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new InvalidOperationException("Select a UPK for Mesh B before loading.");

        MeshB = await LoadUpkMeshAsync("Mesh B", upkPath, skeletalExportPath, "MeshB", new Vector3(1.2f, 0.0f, 0.0f)).ConfigureAwait(true);
        MeshBCalibration = null;
        SetMeshBLoadDiagnostic($"Mesh B loaded: {DescribeMesh(MeshB)}");
        SyncScene();
        FrameCameraToLoadedMeshes();
        StatusMessage = $"Mesh B loaded: {MeshB.Name}.";
        AddLog($"Loaded Mesh B from {upkPath}.");
        return MeshB;
    }

    public void LoadAdditionalMesh(string relativePath)
    {
        Vector3 offset = new((additionalMeshes.Count + 1) * 1.75f, 0.0f, 0.0f);
        Mesh additional = LoadOrCreate(relativePath, $"Additional{additionalMeshes.Count + 1}", offset);
        additionalMeshes.Add(additional);
        SyncScene();
        FrameCameraToLoadedMeshes();
        StatusMessage = $"Additional mesh loaded: {additional.Name}.";
        AddLog($"Loaded additional mesh from {ResolveWorkspacePath(relativePath)}.");
    }

    public Mesh ImportMeshA(string sourcePath)
    {
        MeshA = ImportMesh(sourcePath, "MeshA");
        MeshACalibration = null;
        ClearDerivedState();
        SyncScene();
        FrameCameraToLoadedMeshes();
        StatusMessage = $"Imported Mesh A from {Path.GetFileName(sourcePath)}.";
        AddLog(StatusMessage);
        return MeshA;
    }

    public Mesh ImportMeshB(string sourcePath)
    {
        MeshB = ImportMesh(sourcePath, "MeshB");
        MeshBCalibration = null;
        ClearDerivedState();
        SyncScene();
        FrameCameraToLoadedMeshes();
        StatusMessage = $"Imported Mesh B from {Path.GetFileName(sourcePath)}.";
        AddLog(StatusMessage);
        return MeshB;
    }

    public SectionCalibrationReport CalibrateMeshASections()
    {
        Mesh? mesh = GetMeshByKey("MeshA");
        if (mesh is null)
            throw new InvalidOperationException("Load Mesh A before calibrating its sections.");

        MeshACalibration = sectionCalibrator.Calibrate(meshTransformer.AlignToSharedReferencePose(mesh), "MeshA");
        ApplyCalibrationToScene("MeshA");
        StatusMessage = MeshACalibration.SummaryText;
        AddLog($"Mesh A calibrated: {MeshACalibration.SummaryText}");
        RefreshSceneMeshState();
        return MeshACalibration;
    }

    public SectionCalibrationReport CalibrateMeshBSections()
    {
        Mesh? mesh = GetMeshByKey("MeshB");
        if (mesh is null)
            throw new InvalidOperationException("Load Mesh B before calibrating its sections.");

        MeshBCalibration = sectionCalibrator.Calibrate(meshTransformer.AlignToSharedReferencePose(mesh), "MeshB");
        ApplyCalibrationToScene("MeshB");
        StatusMessage = MeshBCalibration.SummaryText;
        AddLog($"Mesh B calibrated: {MeshBCalibration.SummaryText}");
        RefreshSceneMeshState();
        return MeshBCalibration;
    }

    public void SetActiveMesh(string meshKey)
    {
        Scene.SetActiveMesh(meshKey);
        UpdateSceneSelectionState();
        StatusMessage = $"Active mesh set to {Scene.ActiveMeshKey}.";
        AddLog(StatusMessage);
    }

    public void SetGhostInactiveMesh(bool enabled)
    {
        Scene.GhostInactiveMesh = enabled;
        UpdateSceneSelectionState();
        StatusMessage = enabled ? "Inactive mesh ghosting enabled." : "Inactive mesh ghosting disabled.";
        AddLog(StatusMessage);
    }

    public void ApplyViewportHit(MflMeshHitResult hit)
    {
        if (hit is null)
            return;

        SetActiveMesh(hit.MeshKey);
        Mesh? active = GetActiveMesh();
        if (active is null || hit.TriangleIndex < 0 || hit.TriangleIndex >= active.Triangles.Count)
        {
            StatusMessage = "Viewport selection was not valid.";
            AddLog(StatusMessage);
            return;
        }

        Mesh aligned = meshTransformer.AlignToSharedReferencePose(active);
        RegionSelectionResult? selection = regionSelector.SelectByTriangle(aligned, hit.TriangleIndex, hit.HitPoint);
        AddLog($"Viewport hit {hit.MeshKey} triangle {hit.TriangleIndex} -> section {selection?.SectionIndex ?? -1}, bone {selection?.BoneName ?? "none"}.");
        SelectionSourceMeshKey = Scene.ActiveMeshKey;
        SelectionTargetMeshKey = SelectionSourceMeshKey == "MeshB" ? "MeshA" : "MeshB";
        CurrentSelection = ApplyCalibrationToSelection(SelectionSourceMeshKey, selection);
        if (CurrentSelection is null)
        {
            StatusMessage = "Viewport selection was not valid.";
            AddLog(StatusMessage);
            return;
        }

        TargetSelection = BuildCorrespondingSelection(GetPassiveMesh(), CurrentSelection);
        AddLog($"Viewport selection mapped: source={DescribeSelection(CurrentSelection)}; target={DescribeSelection(TargetSelection)}.");
        Scene.SetHighlightedTriangles(
            SelectionSourceMeshKey,
            CurrentSelection.TriangleIndices,
            CurrentSelection.TriangleIndex,
            TargetSelection?.TriangleIndices,
            TargetSelection?.TriangleIndex ?? -1);
        StatusMessage = BuildSelectionStatus(SelectionSourceMeshKey, CurrentSelection, TargetSelection);
        AddLog(StatusMessage);
        RefreshSceneMeshState();
    }

    public RegionSelectionResult? SelectRegion(string mode, string? boneName, int sectionIndex, Vector3 rayOrigin, Vector3 rayDirection)
    {
        Mesh? source = GetActiveMesh();
        if (source is null)
        {
            StatusMessage = "Load an active mesh before selecting a region.";
            AddLog(StatusMessage);
            return null;
        }

        Mesh aligned = meshTransformer.AlignToSharedReferencePose(source);
        RegionSelectionResult? selection = mode switch
        {
            "Bone" when !string.IsNullOrWhiteSpace(boneName) => regionSelector.SelectByBone(aligned, boneName),
            "Section" => regionSelector.SelectBySection(aligned, sectionIndex),
            _ => regionSelector.SelectByRay(aligned, rayOrigin, rayDirection)
        };

        SelectionSourceMeshKey = Scene.ActiveMeshKey;
        SelectionTargetMeshKey = SelectionSourceMeshKey == "MeshB" ? "MeshA" : "MeshB";
        CurrentSelection = ApplyCalibrationToSelection(SelectionSourceMeshKey, selection);
        if (CurrentSelection is null)
        {
            StatusMessage = "Select a valid region on the active mesh.";
            AddLog(StatusMessage);
            return null;
        }

        TargetSelection = BuildCorrespondingSelection(GetPassiveMesh(), CurrentSelection);
        AddLog($"Region selection mapped: source={DescribeSelection(CurrentSelection)}; target={DescribeSelection(TargetSelection)}.");
        StatusMessage = BuildSelectionStatus(SelectionSourceMeshKey, CurrentSelection, TargetSelection);
        AddLog(StatusMessage);
        UpdateSceneSelectionState();
        return CurrentSelection;
    }

    public Mesh? ExtractSelectedRegion()
    {
        Mesh? sourceMesh = GetActiveMesh();
        if (sourceMesh is null || CurrentSelection is null)
        {
            StatusMessage = "Select a region from the active mesh before extracting.";
            AddLog(StatusMessage);
            return null;
        }

        ExtractedRegion = regionExtractor.Extract(meshTransformer.AlignToSharedReferencePose(sourceMesh), CurrentSelection);
        StatusMessage = $"Extracted {ExtractedRegion.Vertices.Count} vertices and {ExtractedRegion.Triangles.Count} triangles.";
        AddLog(StatusMessage);
        SyncScene();
        return ExtractedRegion;
    }

    public RegionSelectionResult? SelectPreviewVertex(int vertexIndex)
    {
        Mesh? mesh = GetPreviewMesh();
        if (mesh is null || vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
        {
            StatusMessage = "Select a valid vertex from the preview.";
            AddLog(StatusMessage);
            return null;
        }

        Vertex vertex = mesh.Vertices[vertexIndex];
        int dominantBone = DetermineDominantBone(mesh, vertexIndex);
        string boneName = dominantBone >= 0 && dominantBone < mesh.Bones.Count ? mesh.Bones[dominantBone].Name : string.Empty;
        List<int> triangleIndices = mesh.Triangles
            .Select((triangle, index) => (triangle, index))
            .Where(item => item.triangle.A == vertexIndex || item.triangle.B == vertexIndex || item.triangle.C == vertexIndex)
            .Select(item => item.index)
            .ToList();

        CurrentSelection = new RegionSelectionResult
        {
            Mode = "Vertex",
            TriangleIndex = triangleIndices.FirstOrDefault(-1),
            BoneIndex = dominantBone,
            BoneName = boneName,
            SectionIndex = vertex.SectionIndex,
            TriangleIndices = triangleIndices,
            VertexIndices = [vertexIndex],
            HitPoint = vertex.Position
        };

        StatusMessage = $"Selected vertex {vertexIndex} on {mesh.Name}.";
        AddLog(StatusMessage);
        return CurrentSelection;
    }

    public Mesh? TransformExtractedRegionToTarget()
    {
        Mesh? target = GetPassiveMesh();
        if (ExtractedRegion is null || target is null)
        {
            StatusMessage = "Load both meshes and extract a region before transforming.";
            AddLog(StatusMessage);
            return null;
        }

        Mesh source = meshTransformer.AlignToSharedReferencePose(ExtractedRegion);
        Mesh targetAligned = meshTransformer.AlignToSharedReferencePose(target);
        ExtractedRegion = meshTransformer.TransformToTargetSpace(source, targetAligned);
        StatusMessage = $"Region transformed into {target.Name} space.";
        AddLog(StatusMessage);
        SyncScene();
        return ExtractedRegion;
    }

    public Mesh? MergeIntoTarget()
    {
        Mesh? target = GetPassiveMesh();
        if (target is null || ExtractedRegion is null)
        {
            StatusMessage = "Load both meshes and transform a region before merging.";
            AddLog(StatusMessage);
            return null;
        }

        Mesh merged = meshMerger.Merge(meshTransformer.AlignToSharedReferencePose(target), ExtractedRegion);
        FusedMesh = merged;
        StatusMessage = $"Merged mesh now has {merged.Vertices.Count} vertices and {merged.Triangles.Count} triangles.";
        AddLog(StatusMessage);
        SyncScene();
        return FusedMesh;
    }

    public Mesh? RebuildMergedMesh()
    {
        Mesh? source = FusedMesh ?? GetPassiveMesh() ?? GetActiveMesh();
        if (source is null)
        {
            StatusMessage = "Load at least one mesh before rebuilding.";
            AddLog(StatusMessage);
            return null;
        }

        FusedMesh = meshRebuilder.Rebuild(source);
        LastRebuildReport = meshRebuilder.LastReport;
        StatusMessage = $"Rebuilt mesh: normals, tangents, UV seams, weights, LODs, sockets, and bounds.";
        AddLog($"{StatusMessage} Seam edges: {LastRebuildReport.SeamEdges}.");
        SyncScene();
        return FusedMesh;
    }

    public Mesh? FuseAll(string mode, string? boneName, int sectionIndex, Vector3 rayOrigin, Vector3 rayDirection)
    {
        Mesh? sourceMesh = GetActiveMesh();
        Mesh? targetMesh = GetPassiveMesh();
        if (sourceMesh is null || targetMesh is null)
        {
            StatusMessage = "Load both meshes before fusing.";
            AddLog(StatusMessage);
            return null;
        }

        Mesh left = meshTransformer.AlignToSharedReferencePose(sourceMesh);
        Mesh right = meshTransformer.AlignToSharedReferencePose(targetMesh);
        CurrentSelection = mode switch
        {
            "Bone" when !string.IsNullOrWhiteSpace(boneName) => regionSelector.SelectByBone(left, boneName),
            "Section" => regionSelector.SelectBySection(left, sectionIndex),
            _ => regionSelector.SelectByRay(left, rayOrigin, rayDirection)
        };

        ExtractedRegion = regionExtractor.Extract(left, CurrentSelection);
        Mesh transformed = meshTransformer.TransformToTargetSpace(ExtractedRegion, right);
        Mesh merged = meshMerger.Merge(right, transformed);

        foreach (Mesh extraMesh in additionalMeshes)
        {
            Mesh alignedExtra = meshTransformer.AlignToSharedReferencePose(extraMesh);
            merged = meshMerger.Merge(merged, meshTransformer.TransformToTargetSpace(alignedExtra, merged));
        }

        FusedMesh = meshRebuilder.Rebuild(merged);
        LastRebuildReport = meshRebuilder.LastReport;
        StatusMessage = $"Fusion complete. {FusedMesh.Vertices.Count} vertices, {FusedMesh.Triangles.Count} triangles.";
        AddLog(StatusMessage);
        SyncScene();
        return FusedMesh;
    }

    public string ExportFinalMesh(string format, string fileName)
    {
        Mesh? source = FusedMesh ?? MeshB ?? MeshA;
        if (source is null)
            throw new InvalidOperationException("Load a mesh before exporting.");

        Mesh rebuilt = meshRebuilder.Rebuild(source);
        LastRebuildReport = meshRebuilder.LastReport;
        string exportPath = ResolveExportPath(fileName, format);
        string writtenPath = string.Equals(format, "FBX", StringComparison.OrdinalIgnoreCase)
            ? meshExporter.ExportFbx(rebuilt, exportPath)
            : meshExporter.ExportPsk(rebuilt, exportPath);

        StatusMessage = $"Exported {Path.GetFileName(writtenPath)}.";
        AddLog(StatusMessage);
        return writtenPath;
    }

    public string SaveProject(MFLProjectState state)
    {
        string projectName = SanitizeName(state.ProjectName);
        string projectFolder = GetProjectFolder(projectName);
        string meshesFolder = Path.Combine(projectFolder, "Meshes");
        string exportsFolder = Path.Combine(projectFolder, "Exports");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(meshesFolder);
        Directory.CreateDirectory(exportsFolder);

        state.ProjectName = projectName;
        state.MeshAPath = SaveMeshIfAvailable(MeshA, projectFolder, meshesFolder, "MeshA");
        state.MeshBPath = SaveMeshIfAvailable(MeshB, projectFolder, meshesFolder, "MeshB");
        state.AdditionalMeshPaths = additionalMeshes
            .Select((mesh, index) => SaveMeshIfAvailable(mesh, projectFolder, meshesFolder, $"Additional{index + 1}"))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        state.ExtractedRegionPath = SaveMeshIfAvailable(ExtractedRegion, projectFolder, meshesFolder, "ExtractedRegion");
        state.FusedMeshPath = SaveMeshIfAvailable(FusedMesh, projectFolder, meshesFolder, "FusedMesh");
        state.SelectedMeshKey = Scene.ActiveMeshKey;
        state.MeshAVisible = Scene.MeshNodeA.IsVisible;
        state.MeshBVisible = Scene.MeshNodeB.IsVisible;
        state.MeshAWireframe = Scene.MeshNodeA.IsWireframe;
        state.MeshBWireframe = Scene.MeshNodeB.IsWireframe;
        state.GhostInactiveMesh = Scene.GhostInactiveMesh;
        state.ShowGroundPlane = Scene.ShowGroundPlane;
        state.CurrentSelection = CurrentSelection is null ? null : ToSelectionState(CurrentSelection);

        string projectPath = Path.Combine(projectFolder, "project.mflproject");
        File.WriteAllText(projectPath, JsonSerializer.Serialize(state, JsonOptions));
        CurrentProjectPath = projectPath;
        StatusMessage = $"Project saved: {projectName}.";
        AddLog(StatusMessage);
        return projectPath;
    }

    public IReadOnlyList<string> ListProjects()
    {
        string projectsFolder = Path.Combine(WorkspaceRoot, "Projects");
        if (!Directory.Exists(projectsFolder))
            return [];

        return Directory.EnumerateDirectories(projectsFolder)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public MFLProjectState LoadProject(string projectName)
    {
        string sanitizedName = SanitizeName(projectName);
        string projectPath = Path.Combine(GetProjectFolder(sanitizedName), "project.mflproject");
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project '{sanitizedName}' was not found.", projectPath);

        MFLProjectState? state = JsonSerializer.Deserialize<MFLProjectState>(File.ReadAllText(projectPath), JsonOptions);
        if (state is null)
            throw new InvalidDataException($"Project '{sanitizedName}' could not be parsed.");

        string projectFolder = GetProjectFolder(sanitizedName);

        MeshA = LoadProjectMesh(projectFolder, state.MeshAPath, "MeshA", new Vector3(-1.2f, 0.0f, 0.0f));
        MeshB = LoadProjectMesh(projectFolder, state.MeshBPath, "MeshB", new Vector3(1.2f, 0.0f, 0.0f));

        additionalMeshes.Clear();
        foreach (string additionalPath in state.AdditionalMeshPaths)
            additionalMeshes.Add(LoadProjectMesh(projectFolder, additionalPath, $"Additional{additionalMeshes.Count + 1}", new Vector3((additionalMeshes.Count + 1) * 1.75f, 0.0f, 0.0f)));

        ExtractedRegion = LoadOptionalProjectMesh(projectFolder, state.ExtractedRegionPath, "ExtractedRegion");
        FusedMesh = LoadOptionalProjectMesh(projectFolder, state.FusedMeshPath, "FusedMesh");
        CurrentSelection = state.CurrentSelection is null ? null : ToSelectionResult(state.CurrentSelection);
        LastRebuildReport = FusedMesh is null ? null : meshRebuilder.LastReport;
        CurrentProjectPath = projectPath;
        Scene.GhostInactiveMesh = state.GhostInactiveMesh;
        Scene.ShowGroundPlane = state.ShowGroundPlane;
        Scene.MeshNodeA.IsVisible = state.MeshAVisible;
        Scene.MeshNodeB.IsVisible = state.MeshBVisible;
        Scene.MeshNodeA.IsWireframe = state.MeshAWireframe;
        Scene.MeshNodeB.IsWireframe = state.MeshBWireframe;
        Scene.SetActiveMesh(state.SelectedMeshKey);
        SelectionSourceMeshKey = Scene.ActiveMeshKey;
        SelectionTargetMeshKey = SelectionSourceMeshKey == "MeshB" ? "MeshA" : "MeshB";
        SyncScene();

        StatusMessage = $"Project loaded: {sanitizedName}.";
        AddLog(StatusMessage);
        return state;
    }

    public MFLValidationReport ValidateWorkspace(MFLProjectState? state = null)
    {
        state ??= CaptureProjectState("Validation");

        MFLValidationReport report = new();
        ValidateMesh(report, MeshA, "Mesh A");
        ValidateMesh(report, MeshB, "Mesh B");
        foreach (Mesh additional in additionalMeshes)
            ValidateMesh(report, additional, additional.Name);

        if (MeshA is null)
        {
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "NO_MESH_A", Message = "Mesh A is not loaded.", Target = "Workspace" });
        }

        if (MeshB is null)
        {
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "NO_MESH_B", Message = "Mesh B is not loaded.", Target = "Workspace" });
        }

        if (MeshA is not null && MeshB is not null)
        {
            if (MeshA.Bones.Count == 0 || MeshB.Bones.Count == 0)
            {
                report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Error, Code = "MISSING_BONES", Message = "One of the loaded meshes has no bones.", Target = "Skeleton" });
            }

            if (!string.Equals(MeshA.Bones.FirstOrDefault()?.Name, MeshB.Bones.FirstOrDefault()?.Name, StringComparison.OrdinalIgnoreCase))
            {
                report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "ROOT_MISMATCH", Message = "Mesh A and Mesh B do not appear to share the same root bone.", Target = "Skeleton" });
            }
        }

        if (CurrentSelection is null)
        {
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Info, Code = "NO_SELECTION", Message = "No region is currently selected.", Target = "Selection" });
        }
        else if (CurrentSelection.VertexIndices.Count == 0 && CurrentSelection.TriangleIndices.Count == 0)
        {
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "EMPTY_SELECTION", Message = "The active selection does not contain any triangles or vertices.", Target = "Selection" });
        }

        if (FusedMesh is null)
        {
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Info, Code = "NO_FUSED_MESH", Message = "No fused mesh has been built yet.", Target = "Fusion" });
        }

        if (!string.Equals(state.SelectedExportFormat, "PSK", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(state.SelectedExportFormat, "FBX", StringComparison.OrdinalIgnoreCase))
        {
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Error, Code = "BAD_EXPORT_FORMAT", Message = "The export format must be PSK or FBX.", Target = "Export" });
        }

        if (string.IsNullOrWhiteSpace(state.ExportPath))
        {
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "EMPTY_EXPORT_PATH", Message = "The export path is empty.", Target = "Export" });
        }

        LastValidationReport = report;
        StatusMessage = $"Validation complete: {report.SummaryText}";
        AddLog(StatusMessage);
        return report;
    }

    public MFLProjectState CaptureProjectState(string projectName)
    {
        return new MFLProjectState
        {
            ProjectName = projectName,
            MeshAPath = MeshA?.SourcePath ?? string.Empty,
            MeshBPath = MeshB?.SourcePath ?? string.Empty,
            AdditionalMeshPaths = additionalMeshes.Select(mesh => mesh.SourcePath).ToList(),
            ExtractedRegionPath = ExtractedRegion?.SourcePath ?? string.Empty,
            FusedMeshPath = FusedMesh?.SourcePath ?? string.Empty,
            ExportPath = string.Empty,
            SelectedSelectionMode = CurrentSelection?.Mode ?? "Raycast",
            SelectedBoneName = CurrentSelection?.BoneName ?? string.Empty,
            SelectedSectionText = CurrentSelection?.SectionIndex.ToString() ?? "0",
            CurrentSelection = CurrentSelection is null ? null : ToSelectionState(CurrentSelection)
        };
    }

    public string DescribeMesh(Mesh? mesh)
    {
        if (mesh is null)
            return "Not loaded.";

        string sectionSummary = DescribeSectionBreakdown(mesh);
        return $"{mesh.Name} | Verts: {mesh.Vertices.Count} | Tris: {mesh.Triangles.Count} | Bones: {mesh.Bones.Count} | Materials: {mesh.MaterialSlots.Count} | LODs: {mesh.LODGroups.Count} | Sections: {mesh.Triangles.Select(triangle => triangle.SectionIndex).Distinct().Count()} [{sectionSummary}] | Bounds: {FormatVector(mesh.Bounds.Center)}";
    }

    public IReadOnlyList<string> GetActiveBoneNames()
    {
        return GetActiveMesh()?.Bones.Select(bone => bone.Name).ToList() ?? [];
    }

    public string DescribeSelection(RegionSelectionResult? selection)
    {
        if (selection is null)
            return "No selection yet.";

        string boneName = string.IsNullOrWhiteSpace(selection.BoneName) ? "none" : selection.BoneName;
        return $"{selection.Mode} | Bone: {boneName} | Section: {selection.SectionIndex} | Triangles: {selection.TriangleIndices.Count} | Vertices: {selection.VertexIndices.Count}";
    }

    public string DescribeSelectionPair()
    {
        if (CurrentSelection is null && TargetSelection is null)
            return "No selection yet.";

        List<string> lines = [];
        if (CurrentSelection is not null)
            lines.Add($"Source: {DescribeSelection(CurrentSelection)}");
        if (TargetSelection is not null)
            lines.Add($"Target: {DescribeSelection(TargetSelection)}");
        return string.Join(Environment.NewLine, lines);
    }

    public IReadOnlyList<string> BuildSelectionPreviewLines(int maxLines = 16)
    {
        Mesh? source = CurrentSelection is null
            ? GetActiveMesh() ?? GetPreviewMesh()
            : Scene.GetNode(SelectionSourceMeshKey)?.Mesh ?? GetActiveMesh() ?? GetPreviewMesh();
        if (CurrentSelection is null || source is null)
            return [];

        List<string> lines = [];
        foreach (int vertexIndex in CurrentSelection.VertexIndices.Take(maxLines))
        {
            if (vertexIndex < 0 || vertexIndex >= source.Vertices.Count)
                continue;

            Vertex vertex = source.Vertices[vertexIndex];
            string weights = string.Join(", ", vertex.Weights.Select(weight => $"{weight.BoneName}:{weight.Weight:0.###}"));
            lines.Add($"{vertexIndex}: {FormatVector(vertex.Position)} | {weights}");
        }

        return lines;
    }

    public IReadOnlyList<string> BuildMeshRows()
    {
        List<string> rows = [];
        rows.Add($"Workspace: {WorkspaceRoot}");
        rows.Add($"Mesh A: {DescribeMesh(MeshA)}");
        rows.Add($"Mesh B: {DescribeMesh(MeshB)}");
        rows.Add($"Selected Region: {DescribeSelection(CurrentSelection)}");
        if (TargetSelection is not null)
            rows.Add($"Target Region: {DescribeSelection(TargetSelection)}");
        rows.Add($"Extracted Region: {DescribeMesh(ExtractedRegion)}");
        rows.Add($"Fused Mesh: {DescribeMesh(FusedMesh)}");
        rows.Add($"Additional Meshes: {additionalMeshes.Count}");
        if (LastRebuildReport is not null)
            rows.Add($"Last Rebuild: {LastRebuildReport.VertexCount} verts, {LastRebuildReport.TriangleCount} tris, {LastRebuildReport.SeamEdges} seam edges");

        return rows;
    }

    private Mesh LoadOrCreate(string relativePath, string fallbackName, Vector3 offset)
    {
        string path = ResolveWorkspacePath(relativePath);
        Mesh mesh = File.Exists(path)
            ? meshLoader.Load(path)
            : meshLoader.CreateDemoMesh(fallbackName, offset);

        mesh.Name = string.IsNullOrWhiteSpace(mesh.Name) ? fallbackName : mesh.Name;
        mesh.SourcePath = path;
        meshLoader.Save(path, mesh);
        return mesh;
    }

    private Mesh LoadWorkspaceMeshOrImport(string relativePath, string fallbackName, Vector3 offset)
    {
        string resolvedPath = ResolveWorkspacePath(relativePath);
        if (File.Exists(resolvedPath))
            return LoadAlignedMesh(resolvedPath, fallbackName, false, offset);

        string? importedPath = FindLatestImportedMeshPath(fallbackName);
        if (!string.IsNullOrWhiteSpace(importedPath) && File.Exists(importedPath))
            return LoadAlignedMesh(importedPath, fallbackName, false, offset);

        return meshLoader.CreateDemoMesh(fallbackName, offset);
    }

    private async Task<Mesh> LoadUpkMeshAsync(string slotLabel, string upkPath, string? skeletalExportPath, string fallbackName, Vector3 offset)
    {
        string exportLabel = string.IsNullOrWhiteSpace(skeletalExportPath) ? "(auto)" : skeletalExportPath;
        string currentStage = "validating selection";
        try
        {
            string selectedName = Path.GetFileName(upkPath);
            SetLoadDiagnostic(slotLabel, $"{slotLabel} {currentStage}: {selectedName} / {exportLabel}");

            currentStage = "resolving skeletal mesh";
            MeshLoader.LoadedMeshPackage package = await Task.Run(() => meshLoader.LoadUpkPackage(upkPath, skeletalExportPath, message => AddLog($"{slotLabel}: {message}"))).ConfigureAwait(true);
            Mesh mesh = package.Mesh;
            mesh.Name = string.IsNullOrWhiteSpace(mesh.Name) ? fallbackName : mesh.Name;
            mesh.SourcePath = upkPath;
            SetLoadDiagnostic(slotLabel, $"{slotLabel} {currentStage}: {mesh.Name} with {mesh.Vertices.Count} verts, {mesh.Triangles.Count} tris, {mesh.Bones.Count} bones.");

            await BuildPreviewMeshAsync(slotLabel, upkPath, package.SkeletalMesh, mesh, offset).ConfigureAwait(true);

            SetLoadDiagnostic(slotLabel, $"{slotLabel} viewport sync ready: {mesh.Name} loaded without importer-side rotation.");
            return mesh;
        }
        catch (Exception ex)
        {
            string message = $"{slotLabel} load failed while {currentStage}:{Environment.NewLine}{ex}";
            SetLoadDiagnostic(slotLabel, message);
            AddLog(message);
            throw new InvalidOperationException(message, ex);
        }
    }

    private async Task BuildPreviewMeshAsync(string slotLabel, string upkPath, UpkManager.Models.UpkFile.Engine.Mesh.USkeletalMesh skeletalMesh, Mesh mesh, Vector3 offset)
    {
        SetLoadDiagnostic(slotLabel, $"{slotLabel} building textured preview mesh...");
        MeshPreviewMesh previewMesh = await Task.Run(() => previewMeshConverter.Convert(skeletalMesh, 0, message => AddLog($"{slotLabel}: {message}"))).ConfigureAwait(true);
        await previewMaterialResolver.ApplyToSectionsAsync(upkPath, skeletalMesh, previewMesh, message => AddLog($"{slotLabel}: {message}")).ConfigureAwait(true);
        previewMesh.Name = mesh.Name;
        previewMesh.Center += offset;
        previewMesh.Radius = Math.Max(1.0f, previewMesh.Radius);
        AddLog($"{slotLabel}: Preview mesh ready: {previewMesh.Vertices.Count} verts, {previewMesh.Indices.Count / 3:N0} tris, {previewMesh.Bones.Count} bones, {previewMesh.Sections.Count} sections, radius {previewMesh.Radius:0.##}.");
        if (string.Equals(slotLabel, "Mesh A", StringComparison.OrdinalIgnoreCase))
            Scene.MeshNodeA.BasePreviewMesh = previewMesh;
        else
            Scene.MeshNodeB.BasePreviewMesh = previewMesh;
    }

    private Mesh LoadAlignedMesh(string path, string fallbackName, bool saveBack, Vector3 offset)
    {
        Mesh mesh = meshLoader.Load(path);
        mesh.Name = string.IsNullOrWhiteSpace(mesh.Name) ? fallbackName : mesh.Name;
        mesh.SourcePath = path;
        if (saveBack)
            meshLoader.Save(path, mesh);
        return mesh;
    }

    private string? FindLatestImportedMeshPath(string fallbackName)
    {
        string importsFolder = Path.Combine(WorkspaceRoot, "Imports");
        if (!Directory.Exists(importsFolder))
            return null;

        string prefix = $"{SanitizeName(fallbackName)}_";
        return Directory.EnumerateFiles(importsFolder, $"{prefix}*.mflmesh")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private Mesh? GetPreviewMesh()
    {
        return FusedMesh ?? ExtractedRegion ?? MeshB ?? MeshA;
    }

    private Mesh? GetActiveMesh()
    {
        return string.Equals(Scene.ActiveMeshKey, "MeshB", StringComparison.OrdinalIgnoreCase) ? MeshB : MeshA;
    }

    private Mesh? GetPassiveMesh()
    {
        return string.Equals(Scene.ActiveMeshKey, "MeshB", StringComparison.OrdinalIgnoreCase) ? MeshA : MeshB;
    }

    private Mesh ImportMesh(string sourcePath, string fallbackName)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Mesh file '{sourcePath}' was not found.", sourcePath);

        string importsFolder = Path.Combine(WorkspaceRoot, "Imports");
        Directory.CreateDirectory(importsFolder);

        string importedName = $"{SanitizeName(fallbackName)}_{Path.GetFileNameWithoutExtension(sourcePath)}.mflmesh";
        string importedPath = Path.Combine(importsFolder, importedName);

        Mesh mesh = meshLoader.Load(sourcePath);
        mesh.Name = string.IsNullOrWhiteSpace(mesh.Name) ? fallbackName : mesh.Name;
        mesh.SourcePath = importedPath;
        meshLoader.Save(importedPath, mesh);
        return mesh;
    }

    private Mesh LoadProjectMesh(string projectFolder, string relativePath, string fallbackName, Vector3 offset)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return meshLoader.CreateDemoMesh(fallbackName, offset);

        string path = ResolveProjectRelativePath(projectFolder, relativePath);
        if (!File.Exists(path))
            return meshLoader.CreateDemoMesh(fallbackName, offset);

        Mesh mesh = meshLoader.Load(path);
        mesh.SourcePath = path;
        return mesh;
    }

    private Mesh? LoadOptionalProjectMesh(string projectFolder, string relativePath, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        string path = ResolveProjectRelativePath(projectFolder, relativePath);
        if (!File.Exists(path))
            return null;

        Mesh mesh = meshLoader.Load(path);
        mesh.SourcePath = path;
        return mesh;
    }

    private string SaveMeshIfAvailable(Mesh? mesh, string projectFolder, string projectMeshesFolder, string fileName)
    {
        if (mesh is null)
            return string.Empty;

        string meshPath = Path.Combine(projectMeshesFolder, $"{fileName}.mflmesh");
        meshLoader.Save(meshPath, mesh);
        return Path.GetRelativePath(projectFolder, meshPath);
    }

    private string ResolveProjectRelativePath(string projectFolder, string relativePath)
    {
        string projectRoot = Path.GetFullPath(projectFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string combined = Path.GetFullPath(Path.Combine(projectFolder, relativePath));
        if (!combined.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MFL may only access its own workspace.");

        return combined;
    }

    private string GetProjectFolder(string projectName)
    {
        string projectFolder = Path.Combine(WorkspaceRoot, "Projects", projectName);
        string resolved = Path.GetFullPath(projectFolder);
        string root = Path.GetFullPath(WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MFL may only access its own workspace.");

        return resolved;
    }

    private static string SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "DefaultProject";

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Where(character => !invalid.Contains(character)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "DefaultProject" : sanitized.Trim();
    }

    public string CurrentProjectPath { get; private set; } = string.Empty;

    private static void ValidateMesh(MFLValidationReport report, Mesh? mesh, string target)
    {
        if (mesh is null)
            return;

        if (mesh.Vertices.Count == 0)
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Error, Code = "EMPTY_VERTICES", Message = "The mesh has no vertices.", Target = target });

        if (mesh.Triangles.Count == 0)
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "EMPTY_TRIANGLES", Message = "The mesh has no triangles.", Target = target });

        if (mesh.Bones.Count == 0)
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "EMPTY_BONES", Message = "The mesh has no bones.", Target = target });

        if (mesh.MaterialSlots.Count == 0)
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "EMPTY_MATERIALS", Message = "The mesh has no material slots.", Target = target });

        if (mesh.Bounds.IsEmpty)
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "EMPTY_BOUNDS", Message = "The mesh bounding box has not been calculated.", Target = target });

        int invalidTriangles = mesh.Triangles.Count(triangle => triangle.A < 0 || triangle.B < 0 || triangle.C < 0 || triangle.A >= mesh.Vertices.Count || triangle.B >= mesh.Vertices.Count || triangle.C >= mesh.Vertices.Count);
        if (invalidTriangles > 0)
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Error, Code = "INVALID_TRIANGLES", Message = $"The mesh contains {invalidTriangles} triangle(s) with invalid vertex indices.", Target = target });

        int orphanWeights = mesh.Vertices.Count(vertex => vertex.Weights.Any(weight => weight.BoneIndex < 0 || weight.BoneIndex >= mesh.Bones.Count));
        if (orphanWeights > 0)
            report.Issues.Add(new MFLValidationIssue { Severity = MFLValidationSeverity.Warning, Code = "ORPHAN_WEIGHTS", Message = $"The mesh contains {orphanWeights} vertex/weight entries that reference missing bones.", Target = target });
    }

    private static MeshValidationSummary CreateValidationSummary(Mesh mesh)
    {
        return new MeshValidationSummary
        {
            MeshName = mesh.Name,
            InvalidTriangleCount = mesh.Triangles.Count(triangle => triangle.A < 0 || triangle.B < 0 || triangle.C < 0 || triangle.A >= mesh.Vertices.Count || triangle.B >= mesh.Vertices.Count || triangle.C >= mesh.Vertices.Count),
            InvalidWeightCount = mesh.Vertices.Count(vertex => vertex.Weights.Any(weight => weight.Weight <= 0.0f || weight.BoneIndex < 0 || weight.BoneIndex >= mesh.Bones.Count)),
            MissingBoneReferenceCount = mesh.Vertices.Sum(vertex => vertex.Weights.Count(weight => weight.BoneIndex < 0 || weight.BoneIndex >= mesh.Bones.Count)),
            HasBounds = !mesh.Bounds.IsEmpty
        };
    }

    private static int DetermineDominantBone(Mesh mesh, int vertexIndex)
    {
        if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count)
            return -1;

        Dictionary<int, float> totals = [];
        foreach (BoneWeight weight in mesh.Vertices[vertexIndex].Weights)
        {
            if (weight.BoneIndex < 0)
                continue;

            totals.TryGetValue(weight.BoneIndex, out float current);
            totals[weight.BoneIndex] = current + weight.Weight;
        }

        return totals.Count == 0 ? -1 : totals.OrderByDescending(entry => entry.Value).First().Key;
    }

    private static RegionSelectionState ToSelectionState(RegionSelectionResult selection)
    {
        return new RegionSelectionState
        {
            Mode = selection.Mode,
            TriangleIndex = selection.TriangleIndex,
            BoneIndex = selection.BoneIndex,
            BoneName = selection.BoneName,
            SectionIndex = selection.SectionIndex,
            HitPoint = selection.HitPoint,
            TriangleIndices = selection.TriangleIndices.ToList(),
            VertexIndices = selection.VertexIndices.ToList()
        };
    }

    private static RegionSelectionResult ToSelectionResult(RegionSelectionState state)
    {
        return new RegionSelectionResult
        {
            Mode = state.Mode,
            TriangleIndex = state.TriangleIndex,
            BoneIndex = state.BoneIndex,
            BoneName = state.BoneName,
            SectionIndex = state.SectionIndex,
            HitPoint = state.HitPoint,
            TriangleIndices = state.TriangleIndices.ToList(),
            VertexIndices = state.VertexIndices.ToList()
        };
    }

    private string ResolveWorkspacePath(string path)
    {
        string combined = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(MeshesFolder, path));
        string root = Path.GetFullPath(WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MFL may only access its own workspace.");

        return combined;
    }

    private void ClearDerivedState()
    {
        CurrentSelection = null;
        TargetSelection = null;
        SelectionSourceMeshKey = "MeshA";
        SelectionTargetMeshKey = "MeshB";
        ExtractedRegion = null;
        FusedMesh = null;
        LastRebuildReport = null;
        LastValidationReport = null;
        UpdateSceneSelectionState();
    }

    private string ResolveExportPath(string fileName, string format)
    {
        string safeFileName = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(fileName) ? "MFL_FusedMesh" : fileName);
        string extension = string.Equals(format, "FBX", StringComparison.OrdinalIgnoreCase) ? ".fbx" : ".psk";
        string combined = Path.GetFullPath(Path.Combine(ExportsFolder, safeFileName + extension));
        string root = Path.GetFullPath(WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MFL may only export inside its own workspace.");

        return combined;
    }

    private void AddLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        logs.Add(line);
        LogAdded?.Invoke(this, line);
        try
        {
            File.AppendAllText(MflLogPath, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void SetLoadDiagnostic(string slotLabel, string message)
    {
        if (string.Equals(slotLabel, "Mesh B", StringComparison.OrdinalIgnoreCase))
        {
            MeshBLoadDiagnostics = message;
        }
        else
        {
            MeshALoadDiagnostics = message;
        }
    }

    private void SetMeshALoadDiagnostic(string message)
    {
        MeshALoadDiagnostics = message;
    }

    private void SetMeshBLoadDiagnostic(string message)
    {
        MeshBLoadDiagnostics = message;
    }

    private void SyncScene()
    {
        Scene.AttachMeshes(MeshA, MeshB);
        LayoutLoadedMeshes();
        RefreshPreviewMeshes();
        UpdateSceneSelectionState();
    }

    private Mesh? GetMeshByKey(string meshKey)
    {
        return string.Equals(meshKey, "MeshB", StringComparison.OrdinalIgnoreCase) ? MeshB : MeshA;
    }

    private void LayoutLoadedMeshes()
    {
        const float forwardFacingRotationDegrees = 90.0f;

        if (MeshA is not null && MeshB is not null)
        {
            Scene.MeshNodeA.Position = new Vector3(-50.0f, 0.0f, 0.0f);
            Scene.MeshNodeB.Position = new Vector3(50.0f, 0.0f, 0.0f);
            Scene.MeshNodeA.RotationDegrees = new Vector3(0.0f, 0.0f, forwardFacingRotationDegrees);
            Scene.MeshNodeB.RotationDegrees = new Vector3(0.0f, 0.0f, forwardFacingRotationDegrees);
            return;
        }

        if (MeshA is not null)
        {
            Scene.MeshNodeA.Position = new Vector3(-50.0f, 0.0f, 0.0f);
            Scene.MeshNodeA.RotationDegrees = new Vector3(0.0f, 0.0f, forwardFacingRotationDegrees);
            return;
        }

        if (MeshB is not null)
        {
            Scene.MeshNodeB.Position = new Vector3(50.0f, 0.0f, 0.0f);
            Scene.MeshNodeB.RotationDegrees = new Vector3(0.0f, 0.0f, forwardFacingRotationDegrees);
        }
    }

    private void FrameCameraToLoadedMeshes()
    {
        BoundingBox combined = BoundingBox.Empty;
        bool hasBounds = false;

        void IncludeMesh(Mesh? mesh, Matrix4x4 world)
        {
            if (mesh is null || mesh.Bounds.IsEmpty)
                return;

            BoundingBox worldBounds = TransformBounds(mesh.Bounds, world);
            if (worldBounds.IsEmpty)
                return;

            combined.Include(worldBounds.Min);
            combined.Include(worldBounds.Max);
            hasBounds = true;
        }

        IncludeMesh(MeshA, Scene.MeshNodeA.WorldTransform);
        IncludeMesh(MeshB, Scene.MeshNodeB.WorldTransform);
        IncludeMesh(ExtractedRegion, Matrix4x4.Identity);
        IncludeMesh(FusedMesh, Matrix4x4.Identity);
        foreach (Mesh mesh in additionalMeshes)
            IncludeMesh(mesh, Matrix4x4.Identity);

        if (!hasBounds)
            return;

        Camera.FocusOnBounds(combined);
        AddLog($"Camera framed to loaded meshes at {FormatVector(combined.Center)}.");
    }

    private static BoundingBox TransformBounds(BoundingBox bounds, Matrix4x4 world)
    {
        if (bounds.IsEmpty)
            return BoundingBox.Empty;

        Vector3[] corners =
        [
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z)
        ];

        BoundingBox transformed = BoundingBox.Empty;
        foreach (Vector3 corner in corners)
            transformed.Include(Vector3.Transform(corner, world));

        return transformed;
    }

    private void SceneNode_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null ||
            e.PropertyName == nameof(Scene.MeshNodeA.Position) ||
            e.PropertyName == nameof(Scene.MeshNodeA.RotationDegrees) ||
            e.PropertyName == nameof(Scene.MeshNodeA.Scale) ||
            e.PropertyName == nameof(Scene.MeshNodeA.Mesh) ||
            e.PropertyName == nameof(Scene.MeshNodeA.BasePreviewMesh) ||
            e.PropertyName == nameof(Scene.MeshNodeA.IsVisible) ||
            e.PropertyName == nameof(Scene.MeshNodeB.Position) ||
            e.PropertyName == nameof(Scene.MeshNodeB.RotationDegrees) ||
            e.PropertyName == nameof(Scene.MeshNodeB.Scale) ||
            e.PropertyName == nameof(Scene.MeshNodeB.Mesh) ||
            e.PropertyName == nameof(Scene.MeshNodeB.BasePreviewMesh) ||
            e.PropertyName == nameof(Scene.MeshNodeB.IsVisible))
        {
            RefreshPreviewMeshes();
        }
    }

    private void RefreshPreviewMeshes()
    {
        Scene.MeshNodeA.PreviewMesh = TransformPreviewMesh(Scene.MeshNodeA.BasePreviewMesh, Scene.MeshNodeA.WorldTransform);
        Scene.MeshNodeB.PreviewMesh = TransformPreviewMesh(Scene.MeshNodeB.BasePreviewMesh, Scene.MeshNodeB.WorldTransform);
    }

    private static MeshPreviewMesh? TransformPreviewMesh(MeshPreviewMesh? source, Matrix4x4 world)
    {
        if (source is null)
            return null;

        MeshPreviewMesh transformed = new()
        {
            Name = source.Name
        };

        foreach (MeshPreviewSection section in source.Sections)
        {
            transformed.Sections.Add(new MeshPreviewSection
            {
                Index = section.Index,
                MaterialIndex = section.MaterialIndex,
                BaseIndex = section.BaseIndex,
                IndexCount = section.IndexCount,
                Color = section.Color,
                Name = section.Name,
                GameMaterial = section.GameMaterial
            });
        }

        foreach (uint index in source.Indices)
            transformed.Indices.Add(index);

        foreach (MeshPreviewBone bone in source.Bones)
        {
            MeshPreviewBone transformedBone = new()
            {
                Name = bone.Name,
                ParentIndex = bone.ParentIndex,
                LocalTransform = bone.LocalTransform * world,
                OffsetMatrix = bone.OffsetMatrix,
                GlobalTransform = bone.GlobalTransform * world
            };
            transformed.Bones.Add(transformedBone);
        }

        for (int i = 0; i < source.Vertices.Count; i++)
        {
            MeshPreviewVertex vertex = source.Vertices[i];
            transformed.Vertices.Add(new MeshPreviewVertex
            {
                Position = vertex.Position,
                Normal = vertex.Normal,
                Tangent = vertex.Tangent,
                Bitangent = vertex.Bitangent,
                Uv = vertex.Uv,
                Bone0 = vertex.Bone0,
                Bone1 = vertex.Bone1,
                Bone2 = vertex.Bone2,
                Bone3 = vertex.Bone3,
                Weight0 = vertex.Weight0,
                Weight1 = vertex.Weight1,
                Weight2 = vertex.Weight2,
                Weight3 = vertex.Weight3,
                SectionIndex = vertex.SectionIndex
            });
        }

        transformed.Center = source.Center;
        transformed.Radius = Math.Max(1.0f, source.Radius);
        transformed.UvSeamLines.AddRange(source.UvSeamLines);
        return transformed;
    }

    private void RefreshSceneMeshState()
    {
        UpdateSceneSelectionState();
    }

    private void UpdateSceneSelectionState()
    {
        MeshNode active = Scene.GetNode(SelectionSourceMeshKey) ?? Scene.MeshNodeA;
        MeshNode passive = Scene.GetNode(SelectionTargetMeshKey) ?? (ReferenceEquals(active, Scene.MeshNodeA) ? Scene.MeshNodeB : Scene.MeshNodeA);

        if (CurrentSelection is null || active.Mesh is null)
        {
            active.HighlightedTriangleIndices = [];
            active.SelectedTriangleIndex = -1;
            active.HighlightedSectionIndex = -1;
            passive.HighlightedTriangleIndices = [];
            passive.SelectedTriangleIndex = -1;
            passive.HighlightedSectionIndex = -1;
            return;
        }

        active.HighlightedTriangleIndices = CurrentSelection.TriangleIndices.ToList();
        active.SelectedTriangleIndex = CurrentSelection.TriangleIndex;
        active.HighlightedSectionIndex = CurrentSelection.SectionIndex;
        if (TargetSelection is not null)
        {
            passive.HighlightedTriangleIndices = TargetSelection.TriangleIndices.ToList();
            passive.SelectedTriangleIndex = TargetSelection.TriangleIndex;
            passive.HighlightedSectionIndex = TargetSelection.SectionIndex;
        }
        else
        {
            passive.HighlightedTriangleIndices = [];
            passive.SelectedTriangleIndex = -1;
            passive.HighlightedSectionIndex = -1;
        }
    }

    private string BuildSelectionStatus(string sourceMeshKey, RegionSelectionResult? sourceSelection, RegionSelectionResult? targetSelection)
    {
        if (sourceSelection is null)
            return "No selection yet.";

        string source = DescribeSelection(sourceSelection);
        if (targetSelection is null)
            return $"{sourceMeshKey}: {source}";

        return $"{sourceMeshKey}: {source}{Environment.NewLine}Target: {DescribeSelection(targetSelection)}";
    }

    private RegionSelectionResult? BuildCorrespondingSelection(Mesh? passiveMesh, RegionSelectionResult? sourceSelection)
    {
        if (passiveMesh is null || sourceSelection is null)
            return null;

        Mesh passiveAligned = meshTransformer.AlignToSharedReferencePose(passiveMesh);
        string sourceMeshKey = SelectionSourceMeshKey;
        string targetMeshKey = SelectionTargetMeshKey;

        if (sourceSelection.SectionIndex >= 0)
        {
            RegionSelectionResult bySection = GetCalibrationForMeshKey(sourceMeshKey) is SectionCalibrationReport sourceCalibration
                && GetCalibrationForMeshKey(targetMeshKey) is SectionCalibrationReport targetCalibration
                ? regionSelector.SelectBySection(passiveAligned, sourceCalibration.ResolveMatchingSectionIndex(sourceSelection.SectionIndex, targetCalibration))
                : regionSelector.SelectBySection(passiveAligned, sourceSelection.SectionIndex);

            if (bySection.TriangleIndices.Count > 0 || bySection.VertexIndices.Count > 0)
                return ApplyCalibrationToSelection(targetMeshKey, bySection);
        }

        if (!string.IsNullOrWhiteSpace(sourceSelection.BoneName))
        {
            RegionSelectionResult byBone = regionSelector.SelectByBone(passiveAligned, sourceSelection.BoneName);
            if (byBone.TriangleIndices.Count > 0 || byBone.VertexIndices.Count > 0)
                return ApplyCalibrationToSelection(targetMeshKey, byBone);
        }

        if (sourceSelection.TriangleIndex >= 0)
        {
            RegionSelectionResult byTriangle = regionSelector.SelectByTriangle(passiveAligned, sourceSelection.TriangleIndex, sourceSelection.HitPoint);
            if (byTriangle.TriangleIndices.Count > 0 || byTriangle.VertexIndices.Count > 0)
                return ApplyCalibrationToSelection(targetMeshKey, byTriangle);
        }

        return null;
    }

    private SectionCalibrationReport? GetCalibrationForMeshKey(string meshKey)
    {
        return string.Equals(meshKey, "MeshB", StringComparison.OrdinalIgnoreCase) ? MeshBCalibration : MeshACalibration;
    }

    private void ApplyCalibrationToScene(string meshKey)
    {
        Mesh? mesh = GetMeshByKey(meshKey);
        if (mesh is null)
            return;

        SectionCalibrationReport? calibration = GetCalibrationForMeshKey(meshKey);
        if (calibration is null)
            return;

        if (Scene.GetNode(meshKey) is not MeshNode node)
            return;

        if (node.HighlightedSectionIndex >= 0)
        {
            RegionSelectionResult? highlighted = new()
            {
                SectionIndex = node.HighlightedSectionIndex
            };
            highlighted = ApplyCalibrationToSelection(meshKey, highlighted);
            if (highlighted is not null)
            {
                node.HighlightedSectionIndex = highlighted.SectionIndex;
            }
        }
    }

    private RegionSelectionResult? ApplyCalibrationToSelection(string meshKey, RegionSelectionResult? selection)
    {
        if (selection is null)
            return null;

        SectionCalibrationReport? calibration = GetCalibrationForMeshKey(meshKey);
        if (calibration is null || selection.SectionIndex < 0)
            return selection;

        if (!calibration.TryGetEntry(selection.SectionIndex, out SectionCalibrationEntry? entry) || entry is null)
            return selection;

        selection.BoneIndex = entry.RepresentativeBoneIndex;
        selection.BoneName = entry.RepresentativeBoneName;
        return selection;
    }

    public IReadOnlyList<string> BuildCalibrationLines()
    {
        List<string> lines = [];
        AppendCalibrationLines(lines, MeshACalibration);
        AppendCalibrationLines(lines, MeshBCalibration);
        if (lines.Count == 0)
            lines.Add("No section calibration has been run yet.");
        return lines;
    }

    private static void AppendCalibrationLines(ICollection<string> lines, SectionCalibrationReport? report)
    {
        if (report is null)
            return;

        lines.Add($"{report.MeshName}: {report.Entries.Count} section(s) calibrated.");
        foreach (SectionCalibrationEntry entry in report.Entries.Take(5))
        {
            lines.Add($"  Section {entry.SectionIndex} -> {entry.RepresentativeBoneName} [{entry.BoneGroup}]");
        }
    }

    private static string DescribeSectionBreakdown(Mesh mesh)
    {
        List<string> parts = [];
        foreach (var group in mesh.Triangles
                     .GroupBy(triangle => triangle.SectionIndex)
                     .OrderBy(group => group.Key))
        {
            int material = group.Select(triangle => triangle.MaterialSlotIndex).FirstOrDefault();
            parts.Add($"S{group.Key}:{group.Count()} tris/M{material}");
        }

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private static string FormatVector(Vector3 value) => $"({value.X:0.##}, {value.Y:0.##}, {value.Z:0.##})";
}

