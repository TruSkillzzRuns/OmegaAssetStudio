using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using OmegaAssetStudio.WinUI.Models;
using OmegaAssetStudio.WinUI.Modules.MFL.Models;
using OmegaAssetStudio.WinUI.Modules.MFL.Rendering;
using MflScene = OmegaAssetStudio.WinUI.Modules.MFL.Rendering.Scene;

namespace OmegaAssetStudio.WinUI.Modules.MFL;

public sealed class MFLViewModel : INotifyPropertyChanged
{
    private readonly MFLService service = new();
    private readonly DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private string meshAPath = string.Empty;
    private string meshBPath = string.Empty;
    private string selectedMeshAExportPath = string.Empty;
    private string selectedMeshBExportPath = string.Empty;
    private string additionalMeshPath = "Additional.mflmesh";
    private string exportPath = "UnifiedMesh";
    private string selectedSelectionMode = "Raycast";
    private string selectedBoneName = "Root";
    private string selectedSectionText = "0";
    private string rayOriginText = "0, 1, 5";
    private string rayDirectionText = "0, 0, -1";
    private string selectedExportFormat = "PSK";
    private string selectedPreviewFocus = "Auto";
    private string selectedActiveMeshKey = "MeshA";
    private string projectNameText = "DefaultProject";
    private string selectedProjectName = string.Empty;
    private string statusText = "Ready.";
    private string workspaceText = string.Empty;
    private string meshSummaryText = "Load Mesh A and Mesh B to begin.";
    private string selectionSummaryText = "No selection yet.";
    private string calibrationSummaryText = "No section calibration has been run yet.";
    private string pipelineSummaryText = "No pipeline has been run yet.";
    private string rebuildSummaryText = "No rebuild report yet.";
    private string exportSummaryText = "No export written yet.";
    private string validationSummaryText = "No validation report yet.";
    private string projectSummaryText = "No project loaded.";
    private string meshALoadStatus = "Mesh A not loaded.";
    private string meshBLoadStatus = "Mesh B not loaded.";
    private string viewportDiagnosticsText = "Viewport diagnostics not yet available.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> SelectionPreviewLines { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    public ObservableCollection<string> ValidationLines { get; } = [];

    public ObservableCollection<string> ValidationSnapshotLines { get; } = [];

    public ObservableCollection<string> CalibrationLines { get; } = [];

    public ObservableCollection<string> MeshALoadTraceLines { get; } = [];

    public ObservableCollection<string> MeshBLoadTraceLines { get; } = [];

    public ObservableCollection<MeshPreviewPoint> PreviewPoints { get; } = [];

    public ObservableCollection<string> AvailableProjects { get; } = [];

    public ObservableCollection<string> AvailableBones { get; } = [];

    public ObservableCollection<string> MeshAExportOptions { get; } = [];

    public ObservableCollection<string> MeshBExportOptions { get; } = [];

    public ObservableCollection<string> AvailableSelectionModes { get; } = ["Raycast", "Bone", "Section"];

    public ObservableCollection<string> AvailableExportFormats { get; } = ["PSK", "FBX"];

    public ObservableCollection<string> AvailablePreviewFocuses { get; } = ["Auto", "MeshA", "MeshB", "Extracted", "Fused", "Additional"];

    public ObservableCollection<string> AvailableSceneKeys { get; } = ["MeshA", "MeshB"];

    public ICommand LoadDemoPairCommand { get; }

    public ICommand LoadMeshACommand { get; }

    public ICommand LoadMeshBCommand { get; }

    public ICommand LoadAdditionalMeshCommand { get; }

    public ICommand SelectRegionCommand { get; }

    public ICommand ExtractRegionCommand { get; }

    public ICommand TransformRegionCommand { get; }

    public ICommand MergeRegionCommand { get; }

    public ICommand RebuildCommand { get; }

    public ICommand FuseCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand SaveProjectCommand { get; }

    public ICommand LoadProjectCommand { get; }

    public ICommand ValidateProjectCommand { get; }

    public ICommand RefreshProjectsCommand { get; }

    public ICommand SelectPreviewVertexCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand CalibrateMeshACommand { get; }

    public ICommand CalibrateMeshBCommand { get; }

    public MflScene Scene => service.Scene;

    public Camera Camera => service.Camera;

    public async Task ImportMeshAAsync(string sourcePath)
    {
        await ExecuteSafelyAsync("Reading Mesh A UPK...", async () =>
        {
            IReadOnlyList<string> exports = await Task.Run(() => service.GetSkeletalMeshExports(sourcePath));
            MeshAPath = sourcePath;
            ReplaceCollection(MeshAExportOptions, exports);
            SelectedMeshAExportPath = exports.FirstOrDefault() ?? string.Empty;
            StatusText = exports.Count == 0
                ? $"Mesh A UPK loaded with no SkeletalMesh exports: {Path.GetFileName(sourcePath)}."
                : $"Mesh A UPK loaded: {Path.GetFileName(sourcePath)} ({exports.Count} SkeletalMesh export(s)).";
        });
    }

    public async Task ImportMeshBAsync(string sourcePath)
    {
        await ExecuteSafelyAsync("Reading Mesh B UPK...", async () =>
        {
            IReadOnlyList<string> exports = await Task.Run(() => service.GetSkeletalMeshExports(sourcePath));
            MeshBPath = sourcePath;
            ReplaceCollection(MeshBExportOptions, exports);
            SelectedMeshBExportPath = exports.FirstOrDefault() ?? string.Empty;
            StatusText = exports.Count == 0
                ? $"Mesh B UPK loaded with no SkeletalMesh exports: {Path.GetFileName(sourcePath)}."
                : $"Mesh B UPK loaded: {Path.GetFileName(sourcePath)} ({exports.Count} SkeletalMesh export(s)).";
        });
    }

    public void HandleViewportHit(MeshHitResult hit)
    {
        ExecuteSafely(() => service.ApplyViewportHit(hit));
    }

    public string MeshAPath
    {
        get => meshAPath;
        set => SetField(ref meshAPath, value);
    }

    public string MeshBPath
    {
        get => meshBPath;
        set => SetField(ref meshBPath, value);
    }

    public string SelectedMeshAExportPath
    {
        get => selectedMeshAExportPath;
        set => SetField(ref selectedMeshAExportPath, value);
    }

    public string SelectedMeshBExportPath
    {
        get => selectedMeshBExportPath;
        set => SetField(ref selectedMeshBExportPath, value);
    }

    public string AdditionalMeshPath
    {
        get => additionalMeshPath;
        set => SetField(ref additionalMeshPath, value);
    }

    public string ExportPath
    {
        get => exportPath;
        set => SetField(ref exportPath, value);
    }

    public string SelectedSelectionMode
    {
        get => selectedSelectionMode;
        set => SetField(ref selectedSelectionMode, value);
    }

    public string SelectedBoneName
    {
        get => selectedBoneName;
        set => SetField(ref selectedBoneName, value);
    }

    public string SelectedSectionText
    {
        get => selectedSectionText;
        set => SetField(ref selectedSectionText, value);
    }

    public string RayOriginText
    {
        get => rayOriginText;
        set => SetField(ref rayOriginText, value);
    }

    public string RayDirectionText
    {
        get => rayDirectionText;
        set => SetField(ref rayDirectionText, value);
    }

    public string SelectedExportFormat
    {
        get => selectedExportFormat;
        set => SetField(ref selectedExportFormat, value);
    }

    public string SelectedPreviewFocus
    {
        get => selectedPreviewFocus;
        set
        {
            if (SetField(ref selectedPreviewFocus, value))
            {
                service.SetPreviewFocus(selectedPreviewFocus);
                RefreshAll();
            }
        }
    }

    public string SelectedActiveMeshKey
    {
        get => selectedActiveMeshKey;
        set
        {
            if (SetField(ref selectedActiveMeshKey, value))
            {
                service.SetActiveMesh(selectedActiveMeshKey);
                RefreshAll();
            }
        }
    }

    public string ProjectNameText
    {
        get => projectNameText;
        set => SetField(ref projectNameText, value);
    }

    public string SelectedProjectName
    {
        get => selectedProjectName;
        set => SetField(ref selectedProjectName, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string WorkspaceText
    {
        get => workspaceText;
        private set => SetField(ref workspaceText, value);
    }

    public string MeshSummaryText
    {
        get => meshSummaryText;
        private set => SetField(ref meshSummaryText, value);
    }

    public string SelectionSummaryText
    {
        get => selectionSummaryText;
        private set => SetField(ref selectionSummaryText, value);
    }

    public string CalibrationSummaryText
    {
        get => calibrationSummaryText;
        private set => SetField(ref calibrationSummaryText, value);
    }

    public string PipelineSummaryText
    {
        get => pipelineSummaryText;
        private set => SetField(ref pipelineSummaryText, value);
    }

    public string RebuildSummaryText
    {
        get => rebuildSummaryText;
        private set => SetField(ref rebuildSummaryText, value);
    }

    public string ExportSummaryText
    {
        get => exportSummaryText;
        private set => SetField(ref exportSummaryText, value);
    }

    public string ValidationSummaryText
    {
        get => validationSummaryText;
        private set => SetField(ref validationSummaryText, value);
    }

    public string ProjectSummaryText
    {
        get => projectSummaryText;
        private set => SetField(ref projectSummaryText, value);
    }

    public string MeshALoadStatus
    {
        get => meshALoadStatus;
        private set => SetField(ref meshALoadStatus, value);
    }

    public string MeshBLoadStatus
    {
        get => meshBLoadStatus;
        private set => SetField(ref meshBLoadStatus, value);
    }

    public string ViewportDiagnosticsText
    {
        get => viewportDiagnosticsText;
        set => SetField(ref viewportDiagnosticsText, value);
    }

    public MFLViewModel()
    {
        service.LogAdded += Service_LogAdded;
        LoadDemoPairCommand = new RelayCommand(_ => ExecuteSafely(LoadDemoPair));
        LoadMeshACommand = new AsyncRelayCommand(async _ => await LoadMeshASelectedAsync().ConfigureAwait(true));
        LoadMeshBCommand = new AsyncRelayCommand(async _ => await LoadMeshBSelectedAsync().ConfigureAwait(true));
        LoadAdditionalMeshCommand = new RelayCommand(_ => ExecuteSafely(() => service.LoadAdditionalMesh(AdditionalMeshPath)));
        SelectRegionCommand = new RelayCommand(_ => ExecuteSafely(SelectRegion));
        ExtractRegionCommand = new RelayCommand(_ => ExecuteSafely(() => service.ExtractSelectedRegion()));
        TransformRegionCommand = new RelayCommand(_ => ExecuteSafely(() => service.TransformExtractedRegionToTarget()));
        MergeRegionCommand = new RelayCommand(_ => ExecuteSafely(() => service.MergeIntoTarget()));
        RebuildCommand = new RelayCommand(_ => ExecuteSafely(() => service.RebuildMergedMesh()));
        FuseCommand = new RelayCommand(_ => ExecuteSafely(Fuse));
        ExportCommand = new RelayCommand(_ => ExecuteSafely(Export));
        SaveProjectCommand = new RelayCommand(_ => ExecuteSafely(SaveProject));
        LoadProjectCommand = new RelayCommand(_ => ExecuteSafely(LoadProject));
        ValidateProjectCommand = new RelayCommand(_ => ExecuteSafely(ValidateProject));
        RefreshProjectsCommand = new RelayCommand(_ => ExecuteSafely(RefreshProjects));
        SelectPreviewVertexCommand = new RelayCommand(parameter => ExecuteSafely(() => SelectPreviewVertex(parameter)));
        ResetCommand = new RelayCommand(_ => ExecuteSafely(Reset));
        CalibrateMeshACommand = new RelayCommand(_ => ExecuteSafely(() => service.CalibrateMeshASections()));
        CalibrateMeshBCommand = new RelayCommand(_ => ExecuteSafely(() => service.CalibrateMeshBSections()));

        service.SetPreviewFocus(SelectedPreviewFocus);
        service.SetActiveMesh(SelectedActiveMeshKey);
        RefreshAll();
    }

    public async Task LoadMeshASelectedAsync()
    {
        await ExecuteSafelyAsync("Loading Mesh A...", async () =>
        {
            MeshALoadStatus = $"Mesh A load requested from {Path.GetFileName(MeshAPath)}.";
            Mesh mesh = await service.LoadMeshAAsync(MeshAPath, SelectedMeshAExportPath).ConfigureAwait(true);
            StatusText = $"Mesh A loaded: {mesh.Name}.";
            MeshALoadStatus = service.MeshALoadDiagnostics;
        }).ConfigureAwait(true);
    }

    public async Task LoadMeshBSelectedAsync()
    {
        await ExecuteSafelyAsync("Loading Mesh B...", async () =>
        {
            MeshBLoadStatus = $"Mesh B load requested from {Path.GetFileName(MeshBPath)}.";
            Mesh mesh = await service.LoadMeshBAsync(MeshBPath, SelectedMeshBExportPath).ConfigureAwait(true);
            StatusText = $"Mesh B loaded: {mesh.Name}.";
            MeshBLoadStatus = service.MeshBLoadDiagnostics;
        }).ConfigureAwait(true);
    }

    public async Task HandleWorkspaceLaunchAsync(WorkspaceLaunchContext context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.UpkPath))
            return;

        string targetMeshSlot = string.IsNullOrWhiteSpace(context.TargetMeshSlot) ? "MeshA" : context.TargetMeshSlot;
        if (string.Equals(targetMeshSlot, "MeshB", System.StringComparison.OrdinalIgnoreCase))
        {
            await ImportMeshBAsync(context.UpkPath).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(context.ExportPath))
                SelectedMeshBExportPath = context.ExportPath;
            SelectedActiveMeshKey = "MeshB";
            await LoadMeshBSelectedAsync().ConfigureAwait(true);
            return;
        }

        await ImportMeshAAsync(context.UpkPath).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(context.ExportPath))
            SelectedMeshAExportPath = context.ExportPath;
        SelectedActiveMeshKey = "MeshA";
        await LoadMeshASelectedAsync().ConfigureAwait(true);
    }

    private void SelectRegion()
    {
        Vector3 rayOrigin = ParseVector3(RayOriginText, new Vector3(0.0f, 1.0f, 5.0f));
        Vector3 rayDirection = ParseVector3(RayDirectionText, new Vector3(0.0f, 0.0f, -1.0f));
        if (rayDirection == Vector3.Zero)
            rayDirection = new Vector3(0.0f, 0.0f, -1.0f);

        int sectionIndex = ParseInt(SelectedSectionText, 0);
        service.SelectRegion(SelectedSelectionMode, SelectedBoneName, sectionIndex, rayOrigin, rayDirection);
        SelectionSummaryText = service.DescribeSelectionPair();
    }

    private void Fuse()
    {
        Vector3 rayOrigin = ParseVector3(RayOriginText, new Vector3(0.0f, 1.0f, 5.0f));
        Vector3 rayDirection = ParseVector3(RayDirectionText, new Vector3(0.0f, 0.0f, -1.0f));
        if (rayDirection == Vector3.Zero)
            rayDirection = new Vector3(0.0f, 0.0f, -1.0f);

        int sectionIndex = ParseInt(SelectedSectionText, 0);
        service.FuseAll(SelectedSelectionMode, SelectedBoneName, sectionIndex, rayOrigin, rayDirection);
        SelectionSummaryText = service.DescribeSelection(service.CurrentSelection);
    }

    private void Export()
    {
        service.ExportFinalMesh(SelectedExportFormat, ExportPath);
        ExportSummaryText = $"Exported {Path.GetFileNameWithoutExtension(ExportPath)} as {SelectedExportFormat}.";
    }

    private void SaveProject()
    {
        service.SaveProject(BuildProjectState());
        ValidationSummaryText = service.LastValidationReport?.SummaryText ?? "No validation report yet.";
        RefreshProjects();
    }

    private void LoadProject()
    {
        string project = string.IsNullOrWhiteSpace(SelectedProjectName) ? ProjectNameText : SelectedProjectName;
        MFLProjectState state = service.LoadProject(project);
        ApplyProjectState(state);
    }

    private void ValidateProject()
    {
        MFLValidationReport report = service.ValidateWorkspace(BuildProjectState());
        ValidationSummaryText = report.SummaryText;
        ReplaceCollection(ValidationLines, report.Issues.Select(issue => $"[{issue.Severity}] {issue.Target}: {issue.Message}"));
    }

    private void SelectPreviewVertex(object? parameter)
    {
        if (!TryReadInt(parameter, out int vertexIndex))
            return;

        service.SelectPreviewVertex(vertexIndex);
        RefreshAll();
    }

    private void Reset()
    {
        LoadDemoPair();
        AdditionalMeshPath = "Additional.mflmesh";
        ExportPath = "UnifiedMesh";
        SelectedProjectName = string.Empty;
        SelectedPreviewFocus = "Auto";
        SelectedActiveMeshKey = "MeshA";
        ViewportDiagnosticsText = "Viewport diagnostics not yet available.";
        RefreshProjects();
    }

    private void LoadDemoPair()
    {
        service.LoadDemoPair();
        MeshAPath = string.Empty;
        MeshBPath = string.Empty;
        SelectedMeshAExportPath = string.Empty;
        SelectedMeshBExportPath = string.Empty;
        ReplaceCollection(MeshAExportOptions, []);
        ReplaceCollection(MeshBExportOptions, []);
        StatusText = service.StatusMessage;
    }

    private void ExecuteSafely(Action action)
    {
        try
        {
            action();
            StatusText = service.StatusMessage;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            MeshALoadStatus = service.MeshALoadDiagnostics;
            MeshBLoadStatus = service.MeshBLoadDiagnostics;
        }

        RefreshAll();
    }

    private async Task ExecuteSafelyAsync(string loadingMessage, Func<Task> action)
    {
        try
        {
            StatusText = loadingMessage;
            await action();
            StatusText = service.StatusMessage;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            MeshALoadStatus = service.MeshALoadDiagnostics;
            MeshBLoadStatus = service.MeshBLoadDiagnostics;
        }

        RefreshAll();
    }

    private void RefreshAll()
    {
        WorkspaceText = service.WorkspaceRoot;
        MeshSummaryText = string.Join(Environment.NewLine, service.BuildMeshRows());
        SelectionSummaryText = service.DescribeSelection(service.CurrentSelection);
        PipelineSummaryText = string.Join(Environment.NewLine, service.BuildMeshRows());
        RebuildSummaryText = service.LastRebuildReport is null
            ? "No rebuild report yet."
            : $"Vertices: {service.LastRebuildReport.VertexCount}{Environment.NewLine}Triangles: {service.LastRebuildReport.TriangleCount}{Environment.NewLine}Bones: {service.LastRebuildReport.BoneCount}{Environment.NewLine}Sockets: {service.LastRebuildReport.SocketCount}{Environment.NewLine}LODs: {service.LastRebuildReport.LodCount}{Environment.NewLine}Seam Edges: {service.LastRebuildReport.SeamEdges}";
        ExportSummaryText = service.FusedMesh is null ? "No export written yet." : $"Final mesh ready: {service.FusedMesh.Name}";
        ValidationSummaryText = service.LastValidationReport?.SummaryText ?? "No validation report yet.";
        ProjectSummaryText = string.IsNullOrWhiteSpace(service.CurrentProjectPath) ? "No project loaded." : service.CurrentProjectPath;
        MeshALoadStatus = service.MeshALoadDiagnostics;
        MeshBLoadStatus = service.MeshBLoadDiagnostics;
        CalibrationSummaryText = string.Join(Environment.NewLine, service.BuildCalibrationLines());

        ReplaceCollection(SelectionPreviewLines, service.BuildSelectionPreviewLines());
        ReplaceCollection(LogLines, service.LogEntries);
        ReplaceCollection(ValidationLines, service.LastValidationReport?.Issues.Select(issue => $"[{issue.Severity}] {issue.Target}: {issue.Message}") ?? []);
        ReplaceCollection(ValidationSnapshotLines, service.BuildValidationSummaries().Select(summary => $"{summary.MeshName}: InvalidTris={summary.InvalidTriangleCount} InvalidWeights={summary.InvalidWeightCount} MissingRefs={summary.MissingBoneReferenceCount} Bounds={(summary.HasBounds ? "Yes" : "No")}"));
        ReplaceCollection(MeshALoadTraceLines, service.GetLoadTrace("Mesh A"));
        ReplaceCollection(MeshBLoadTraceLines, service.GetLoadTrace("Mesh B"));
        ReplaceCollection(PreviewPoints, service.BuildPreviewPoints());
        ReplaceCollection(AvailableProjects, service.ListProjects());
        ReplaceCollection(AvailableBones, service.GetActiveBoneNames());

        if (AvailableBones.Count > 0 && !AvailableBones.Contains(SelectedBoneName))
            SelectedBoneName = AvailableBones[0];
        else if (AvailableBones.Count == 0)
            SelectedBoneName = string.Empty;

        if (AvailableProjects.Count > 0 && string.IsNullOrWhiteSpace(SelectedProjectName))
            SelectedProjectName = AvailableProjects[0];
    }

    private void RefreshProjects()
    {
        ReplaceCollection(AvailableProjects, service.ListProjects());
        if (AvailableProjects.Count > 0 && !AvailableProjects.Contains(SelectedProjectName))
            SelectedProjectName = AvailableProjects[0];
    }

    private void Service_LogAdded(object? sender, string line)
    {
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            AppendLiveLogLine(line);
            return;
        }

        dispatcherQueue.TryEnqueue(() => AppendLiveLogLine(line));
    }

    private void AppendLiveLogLine(string line)
    {
        AddUniqueLine(LogLines, line);
        if (line.Contains("Mesh A:", StringComparison.OrdinalIgnoreCase))
            AddUniqueLine(MeshALoadTraceLines, line);
        if (line.Contains("Mesh B:", StringComparison.OrdinalIgnoreCase))
            AddUniqueLine(MeshBLoadTraceLines, line);

        StatusText = service.StatusMessage;
        MeshALoadStatus = service.MeshALoadDiagnostics;
        MeshBLoadStatus = service.MeshBLoadDiagnostics;
    }

    private MFLProjectState BuildProjectState()
    {
        return new MFLProjectState
        {
            ProjectName = ProjectNameText,
            MeshAPath = MeshAPath,
            MeshAUpkPath = MeshAPath,
            MeshAExportPath = SelectedMeshAExportPath,
            MeshBPath = MeshBPath,
            MeshBUpkPath = MeshBPath,
            MeshBExportPath = SelectedMeshBExportPath,
            AdditionalMeshPaths = string.IsNullOrWhiteSpace(AdditionalMeshPath) ? [] : [AdditionalMeshPath],
            ExportPath = ExportPath,
            SelectedSelectionMode = SelectedSelectionMode,
            SelectedBoneName = SelectedBoneName,
            SelectedSectionText = SelectedSectionText,
            RayOriginText = RayOriginText,
            RayDirectionText = RayDirectionText,
            SelectedExportFormat = SelectedExportFormat,
            SelectedPreviewFocus = SelectedPreviewFocus,
            SelectedMeshKey = SelectedActiveMeshKey,
            ShowGroundPlane = Scene.ShowGroundPlane,
            CurrentSelection = service.CurrentSelection is null ? null : new RegionSelectionState
            {
                Mode = service.CurrentSelection.Mode,
                TriangleIndex = service.CurrentSelection.TriangleIndex,
                BoneIndex = service.CurrentSelection.BoneIndex,
                BoneName = service.CurrentSelection.BoneName,
                SectionIndex = service.CurrentSelection.SectionIndex,
                HitPoint = service.CurrentSelection.HitPoint,
                TriangleIndices = service.CurrentSelection.TriangleIndices.ToList(),
                VertexIndices = service.CurrentSelection.VertexIndices.ToList()
            }
        };
    }

    private void ApplyProjectState(MFLProjectState state)
    {
        ProjectNameText = state.ProjectName;
        SelectedProjectName = state.ProjectName;
        MeshAPath = string.IsNullOrWhiteSpace(state.MeshAUpkPath) ? state.MeshAPath : state.MeshAUpkPath;
        MeshBPath = string.IsNullOrWhiteSpace(state.MeshBUpkPath) ? state.MeshBPath : state.MeshBUpkPath;
        SelectedMeshAExportPath = state.MeshAExportPath;
        SelectedMeshBExportPath = state.MeshBExportPath;
        ReplaceCollection(MeshAExportOptions, string.IsNullOrWhiteSpace(state.MeshAExportPath) ? [] : [state.MeshAExportPath]);
        ReplaceCollection(MeshBExportOptions, string.IsNullOrWhiteSpace(state.MeshBExportPath) ? [] : [state.MeshBExportPath]);
        AdditionalMeshPath = state.AdditionalMeshPaths.FirstOrDefault() ?? "Additional.mflmesh";
        ExportPath = state.ExportPath;
        SelectedSelectionMode = state.SelectedSelectionMode;
        SelectedBoneName = state.SelectedBoneName;
        SelectedSectionText = state.SelectedSectionText;
        RayOriginText = state.RayOriginText;
        RayDirectionText = state.RayDirectionText;
        SelectedExportFormat = state.SelectedExportFormat;
        SelectedPreviewFocus = string.IsNullOrWhiteSpace(state.SelectedPreviewFocus) ? "Auto" : state.SelectedPreviewFocus;
        SelectedActiveMeshKey = string.IsNullOrWhiteSpace(state.SelectedMeshKey) ? "MeshA" : state.SelectedMeshKey;
        Scene.ShowGroundPlane = state.ShowGroundPlane;
        RefreshProjects();
        RefreshAll();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (T value in values)
            collection.Add(value);
    }

    private static void AddUniqueLine(ObservableCollection<string> collection, string line)
    {
        if (collection.Count > 0 && string.Equals(collection[^1], line, StringComparison.Ordinal))
            return;

        collection.Add(line);
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out int value) ? value : fallback;
    }

    private static bool TryReadInt(object? parameter, out int value)
    {
        if (parameter is int direct)
        {
            value = direct;
            return true;
        }

        if (parameter is string text && int.TryParse(text, out value))
            return true;

        value = 0;
        return false;
    }

    private static Vector3 ParseVector3(string text, Vector3 fallback)
    {
        string[] parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            return fallback;

        if (float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y) && float.TryParse(parts[2], out float z))
            return new Vector3(x, y, z);

        return fallback;
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> execute;

        public RelayCommand(Action<object?> execute)
        {
            this.execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }

    private sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> execute;

        public AsyncRelayCommand(Func<object?, Task> execute)
        {
            this.execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter) => await execute(parameter);
    }
}

