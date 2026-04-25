using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using OmegaAssetStudio.MeshImporter;
using OmegaAssetStudio.Model;
using OmegaAssetStudio.MeshPreview;
using OmegaAssetStudio.WinUI;
using OmegaAssetStudio.WinUI.Models;
using OmegaAssetStudio.WinUI.Modules.Workflows;
using OmegaAssetStudio.WinUI.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls.Primitives;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices.WindowsRuntime;
using WinRT.Interop;
using System.Threading.Tasks;
using Point = global::Windows.Foundation.Point;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class MeshPage : Page
{
    private const string PreviewView = "preview";
    private const string ExporterView = "exporter";
    private const string ImporterView = "importer";
    private const string SectionsView = "sections";
    private const float DefaultPreviewYaw = 28.0f;
    private const float DefaultPreviewPitch = 12.0f;
    private const float DefaultPreviewZoom = 1.45f;

    private enum MeshExportKind
    {
        Skeletal,
        Static
    }

    private readonly UpkFileRepository repository = new();
    private readonly Dictionary<string, UnrealExportTableEntry> meshExports = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeshExportKind> meshExportKinds = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, USkeletalMesh> resolvedMeshCache = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UStaticMesh> resolvedStaticMeshCache = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> resolvedMeshSourcePaths = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly List<string> allMeshPaths = [];
    private readonly FbxToPreviewMeshConverter fbxToPreviewMeshConverter = new();
    private readonly UE3ToPreviewMeshConverter ue3ToPreviewMeshConverter = new();
    private readonly MeshPreviewGameMaterialResolver gameMaterialResolver = new();
    private readonly MeshPreviewSoftwareRenderer previewRenderer = new();
    private MeshPreviewD3D11Renderer d3dPreviewRenderer = new();
    private bool previewPointerCaptured;
    private bool previewPanMode;
    private Point lastPreviewPointerPoint;
    private readonly MeshPreviewCamera previewCamera = new();
    private readonly MeshPreviewScene previewScene = new();
    private static readonly string MeshErrorLogPath = RuntimeLogPaths.MeshErrorLogPath;
    private UnrealHeader? currentHeader;
    private WorkspaceLaunchContext? lastContext;
    private string currentView = PreviewView;
    private string currentFbxPath = string.Empty;
    private USkeletalMesh? currentSkeletalMesh;
    private UStaticMesh? currentStaticMesh;
    private UnrealExportTableEntry? currentExport;
    private MeshPreviewMesh? currentPreviewMesh;
    private object? currentPreviewSourceMesh;
    private int currentPreviewLodIndex = -1;
    private string exportFbxPath = string.Empty;
    private string importFbxPath = string.Empty;
    private bool previewControlsInitialized;
    private bool previewSurfaceLoaded;
    private bool previewRenderInProgress;
    private bool previewRenderPending;
    private bool previewSurfaceRetryPending;
    private bool loadingMeshInspector;
    private bool suppressPreviewSettingChanges;
    private bool suppressSessionWrites;

    public string? CurrentUpkPath => currentHeader?.FullFilename;
    public string? SelectedMeshPath => MeshComboBox.SelectedItem as string ?? currentExport?.GetPathName();

    public ObservableCollection<string> StatusRows { get; } = [];
    public ObservableCollection<string> MeshSummaryRows { get; } = [];
    public ObservableCollection<string> LodSummaryRows { get; } = [];
    public ObservableCollection<string> MaterialRows { get; } = [];
    public ObservableCollection<string> BoneRows { get; } = [];
    public ObservableCollection<string> ExporterRows { get; } = [];
    public ObservableCollection<string> ImporterRows { get; } = [];
    public ObservableCollection<string> SectionRows { get; } = [];
    public ObservableCollection<string> ChunkRows { get; } = [];
    public ObservableCollection<string> SectionDetailRows { get; } = [];

    public MeshPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        TryLoadPackageIndex();
        d3dPreviewRenderer.RenderCompleted += D3dPreviewRenderer_RenderCompleted;
        InitializePreviewControls();
        Unloaded += MeshPage_Unloaded;
        suppressSessionWrites = true;
        ApplySessionState();
        suppressSessionWrites = false;
        StatusRows.Add("Open a UPK or use Objects handoff to start browsing mesh exports.");
        MeshSummaryRows.Add("Select a mesh export to inspect it here.");
        PreviewStatusText.Text = "Open a mesh export to generate the first native WinUI preview slice.";
        SetActiveView(PreviewView);
    }

    private void TryLoadPackageIndex()
    {
        try
        {
            string indexPath = Path.Combine(AppContext.BaseDirectory, "Data", "mh152.mpk");
            if (File.Exists(indexPath))
            {
                repository.LoadPackageIndex(indexPath);
                App.WriteDiagnosticsLog("Mesh.PackageIndex", $"Loaded package index: {indexPath}");
            }
            else
            {
                App.WriteDiagnosticsLog("Mesh.PackageIndex", $"Package index not found: {indexPath}");
            }
        }
        catch (System.Exception ex)
        {
            App.WriteDiagnosticsLog("Mesh.PackageIndex", $"Failed to load package index: {ex.Message}");
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        WorkspaceSessionStore.MeshWorkspaceSession session = WorkspaceSessionStore.Mesh;
        if (currentHeader is null && !string.IsNullOrWhiteSpace(session.UpkPath) && File.Exists(session.UpkPath))
        {
            CurrentPathText.Text = session.UpkPath;
            await LoadUpkAsync(session.UpkPath, session.ExportPath).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(session.ViewMode))
                SetActiveView(session.ViewMode);
            return;
        }

        if (e.Parameter is string workspaceMode)
        {
            SetWorkspaceMode(workspaceMode);
            return;
        }

        if (e.Parameter is not WorkspaceLaunchContext context)
            return;

        lastContext = context;
        if (!string.IsNullOrWhiteSpace(context.UpkPath) &&
            (currentHeader is null || !string.Equals(currentHeader.FullFilename, context.UpkPath, System.StringComparison.OrdinalIgnoreCase)))
        {
            await LoadUpkAsync(context.UpkPath, context.ExportPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(context.ExportPath))
            SelectMesh(context.ExportPath);

        SaveSessionState();
    }

    private async void OpenUpkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        await LoadUpkAsync(file.Path);
    }

    private async void LoadFbxButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".fbx");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        await LoadFbxAsync(file.Path).ConfigureAwait(true);
    }

    private async void UseHandoffButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (lastContext is null || string.IsNullOrWhiteSpace(lastContext.UpkPath))
            return;

        await LoadUpkAsync(lastContext.UpkPath, lastContext.ExportPath);
    }

    private void IncludeStaticMeshesCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        ApplyMeshFilter(MeshComboBox.SelectedItem as string);
    }

    private void IncludeStaticMeshesCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        ApplyMeshFilter(MeshComboBox.SelectedItem as string);
    }

    private async Task LoadUpkAsync(string upkPath, string? preferredExportPath = null)
    {
        try
        {
            App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", $"Start: {upkPath} preferred={preferredExportPath ?? "(none)"}");
            CurrentPathText.Text = "Loading...";
            StatusRows.Clear();
            StatusRows.Add($"Loading mesh exports from {upkPath}");
            ClearInspectorCollections();
            MeshSummaryRows.Add("Reading UPK header and export table...");
            MeshComboBox.ItemsSource = null;
            LodComboBox.ItemsSource = null;
            meshExports.Clear();
            meshExportKinds.Clear();
            resolvedMeshCache.Clear();
            resolvedStaticMeshCache.Clear();
            resolvedMeshSourcePaths.Clear();
            allMeshPaths.Clear();
            currentExport = null;
            currentSkeletalMesh = null;
            currentStaticMesh = null;
            previewScene.SetUe3Mesh(null);
            currentPreviewMesh = null;
            currentPreviewSourceMesh = null;
            currentPreviewLodIndex = -1;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = "Loading mesh package...";

            UnrealHeader header = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
            await header.ReadHeaderAsync(null).ConfigureAwait(true);
            currentHeader = header;
            CurrentPathText.Text = upkPath;

            List<string> names = [];
            int skeletalCount = 0;
            int staticCount = 0;
            foreach (UnrealExportTableEntry export in header.ExportTable)
            {
                string className = export.ClassReferenceNameIndex?.Name ?? string.Empty;
                if (IsSkeletalMeshClass(className))
                {
                    RegisterMeshExport(export, MeshExportKind.Skeletal, names);
                    skeletalCount++;
                    continue;
                }

                if (IsStaticMeshClass(className))
                {
                    RegisterMeshExport(export, MeshExportKind.Static, names);
                    staticCount++;
                }
            }

            if (skeletalCount == 0)
            {
                App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", "No class-name SkeletalMesh exports found; scanning parsed exports for USkeletalMesh objects.");
                foreach (UnrealExportTableEntry export in header.ExportTable)
                {
                    try
                    {
                        if (export.UnrealObject is null)
                        {
                            await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
                            await export.ParseUnrealObject(false, false).ConfigureAwait(true);
                        }

                        if (export.UnrealObject is not IUnrealObject { UObject: USkeletalMesh })
                            continue;

                        string path = export.GetPathName();
                        if (meshExports.ContainsKey(path))
                            continue;

                        RegisterMeshExport(export, MeshExportKind.Skeletal, names);
                        skeletalCount++;
                    }
                    catch (System.Exception ex)
                    {
                        App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", $"Fallback export scan skipped {export.GetPathName()}: {ex.Message}");
                    }
                }
            }

            if (IncludeStaticMeshesCheckBox.IsChecked == true && staticCount == 0)
            {
                App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", "No class-name StaticMesh exports found; scanning parsed exports for UStaticMesh objects.");
                foreach (UnrealExportTableEntry export in header.ExportTable)
                {
                    try
                    {
                        if (export.UnrealObject is null)
                        {
                            await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
                            await export.ParseUnrealObject(false, false).ConfigureAwait(true);
                        }

                        if (export.UnrealObject is not IUnrealObject { UObject: UStaticMesh })
                            continue;

                        string path = export.GetPathName();
                        if (meshExports.ContainsKey(path))
                            continue;

                        RegisterMeshExport(export, MeshExportKind.Static, names);
                        staticCount++;
                    }
                    catch (System.Exception ex)
                    {
                        App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", $"Static mesh fallback skipped {export.GetPathName()}: {ex.Message}");
                    }
                }
            }

            if (names.Count == 0)
            {
                App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", "No direct SkeletalMesh exports resolved; scanning SkeletalMeshComponent references.");
                foreach (UnrealExportTableEntry export in header.ExportTable)
                {
                    try
                    {
                        if (export.UnrealObject is null)
                        {
                            await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
                            await export.ParseUnrealObject(false, false).ConfigureAwait(true);
                        }

                        if (export.UnrealObject is not IUnrealObject { UObject: USkeletalMeshComponent component })
                            continue;

                        USkeletalMesh? referencedMesh = component.SkeletalMesh?.LoadObject<USkeletalMesh>();
                        if (referencedMesh is null)
                            continue;

                        string componentPath = export.GetPathName();
                        string? resolvedPath = component.SkeletalMesh?.GetPathName();
                        string displayPath = string.IsNullOrWhiteSpace(resolvedPath)
                            ? componentPath
                            : resolvedPath;

                        if (meshExports.ContainsKey(displayPath))
                            continue;

                        RegisterMeshExport(export, MeshExportKind.Skeletal, names, displayPath);
                        resolvedMeshCache[displayPath] = referencedMesh;
                        resolvedMeshSourcePaths[displayPath] = componentPath;
                        App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", $"Resolved component-backed mesh: {displayPath} via {componentPath}");
                    }
                    catch (System.Exception ex)
                    {
                        App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", $"Component mesh fallback skipped {export.GetPathName()}: {ex.Message}");
                    }
                }
            }

            allMeshPaths.AddRange(names);
            RecentUpkSession.RecordUpk(upkPath, "mesh", preferredExportPath, title: Path.GetFileName(upkPath), summary: $"Mesh workspace load: SkeletalMesh exports={skeletalCount:N0}, StaticMesh exports={staticCount:N0}");
            ApplyMeshFilter(preferredExportPath);
            StatusRows.Clear();
            StatusRows.Add($"UPK: {upkPath}");
            StatusRows.Add($"Found {skeletalCount:N0} SkeletalMesh exports.");
            if (staticCount > 0)
                StatusRows.Add($"Found {staticCount:N0} StaticMesh exports.");
            StatusRows.Add("Preview rendering host is not ported into WinUI yet, but live mesh/LOD data is.");
            StatusRows.Add("Use Preview / Exporter / Importer / Sections to move through the workspace.");
            SaveSessionState();

            if (!string.IsNullOrWhiteSpace(preferredExportPath) && meshExports.ContainsKey(preferredExportPath))
            {
                MeshComboBox.SelectedItem = preferredExportPath;
            }
            else if (names.Count > 0)
            {
                MeshComboBox.SelectedIndex = 0;
            }
            else
            {
                InspectorTitle.Text = "Mesh Workspace";
                InspectorSubtitle.Text = "No SkeletalMesh exports were found in the selected package.";
                ClearInspectorCollections();
                MeshSummaryRows.Add("No SkeletalMesh exports found.");
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
                PreviewStatusText.Text = "No SkeletalMesh exports were found in this package.";
            }

            App.WriteDiagnosticsLog("Mesh.LoadUpkAsync", $"Completed: {upkPath} exports={names.Count:N0}");
            SaveSessionState();
        }
        catch (System.Exception ex)
        {
            LogMeshError("LoadUpkAsync", ex);
            StatusRows.Clear();
            StatusRows.Add($"UPK mesh load failed while reading {Path.GetFileName(upkPath)}: {ex.Message}");
            StatusRows.Add("See the mesh error log on the desktop for the full stack trace.");
            InspectorTitle.Text = "Mesh Workspace";
            InspectorSubtitle.Text = "The selected UPK could not be loaded.";
            ClearInspectorCollections();
            MeshSummaryRows.Add($"UPK mesh load failed while reading {Path.GetFileName(upkPath)}: {ex.Message}");
            MeshSummaryRows.Add("See the mesh error log on the desktop for the full stack trace.");
            meshExports.Clear();
            resolvedMeshCache.Clear();
            resolvedMeshSourcePaths.Clear();
            allMeshPaths.Clear();
            currentHeader = null;
            currentExport = null;
            currentSkeletalMesh = null;
            currentPreviewMesh = null;
            currentPreviewSourceMesh = null;
            currentPreviewLodIndex = -1;
            MeshComboBox.ItemsSource = null;
            LodComboBox.ItemsSource = null;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = $"UPK mesh load failed while reading {Path.GetFileName(upkPath)}: 0x{ex.HResult:X8} {ex.Message}";
            SaveSessionState();
        }
    }

    private async Task LoadFbxAsync(string fbxPath)
    {
        try
        {
            StatusRows.Add($"Loading FBX mesh from {fbxPath}");
            PreviewStatusText.Text = $"Loading FBX mesh from {fbxPath}...";

            MeshPreviewMesh mesh = await Task.Run(() => fbxToPreviewMeshConverter.Convert(fbxPath, message => DispatcherQueue.TryEnqueue(() => StatusRows.Add(message)))).ConfigureAwait(true);

            currentFbxPath = fbxPath;
            previewScene.SetFbxMesh(mesh);
            currentPreviewMesh = previewScene.FbxMesh;
            currentPreviewSourceMesh = null;
            currentPreviewLodIndex = -1;

            PopulatePreviewControlChoices();
            RefreshPreviewSectionChoices();
            RefreshWorkflowRows();

            if (previewScene.Ue3Mesh is null)
                previewCamera.Reset(mesh.Center, MathF.Max(1.0f, mesh.Radius));
            else
                SyncPreviewCamera(mesh);

            ApplyCameraToControls();
            StatusRows.Add($"Loaded FBX mesh: {fbxPath}");
            await RenderCurrentPreviewAsync().ConfigureAwait(true);
            SaveSessionState();
        }
        catch (System.Exception ex)
        {
            StatusRows.Add($"FBX mesh load failed while reading {Path.GetFileName(fbxPath)}: {ex.Message}");
            PreviewStatusText.Text = $"FBX mesh load failed while reading {Path.GetFileName(fbxPath)}: 0x{ex.HResult:X8} {ex.Message}";
            SaveSessionState();
        }
    }

    private async void MeshComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MeshComboBox.SelectedItem is not string meshPath)
            return;

        if (!meshExports.TryGetValue(meshPath, out var export) || export is null)
            return;

        try
        {
            App.WriteDiagnosticsLog("Mesh.Selection", $"Start: {meshPath}");
            await PopulateMeshInspectorAsync(meshPath, export);
            SaveSessionState();
        }
        catch (System.Exception ex)
        {
            LogMeshError("MeshComboBox_SelectionChanged", ex);
            StatusRows.Add($"Mesh selection failed while inspecting {meshPath}: {ex.Message}");
            PreviewStatusText.Text = $"Mesh selection failed while inspecting {meshPath}: 0x{ex.HResult:X8} {ex.Message}";
            MeshSummaryRows.Clear();
            MeshSummaryRows.Add("Failed to load the selected mesh export.");
            MeshSummaryRows.Add("See the mesh error log on the desktop for the full stack trace.");
            currentExport = null;
            currentSkeletalMesh = null;
            currentStaticMesh = null;
            currentPreviewMesh = null;
            currentPreviewSourceMesh = null;
            currentPreviewLodIndex = -1;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void LodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingMeshInspector)
            return;

        if (MeshComboBox.SelectedItem is not string meshPath)
            return;

        if (!TryResolveSelectedMesh(meshPath, out var export, out MeshExportKind kind, out var skeletalMesh, out var staticMesh))
            return;

        if (export is null)
            return;

        try
        {
            if (kind == MeshExportKind.Static)
            {
                if (staticMesh is null)
                    return;

                PopulateLodRows(export, staticMesh);
            }
            else if (skeletalMesh is not null)
            {
                PopulateLodRows(export, skeletalMesh);
            }

            SaveSessionState();
        }
        catch (System.Exception ex)
        {
            StatusRows.Add($"LOD refresh failed while updating {meshPath}: {ex.Message}");
            PreviewStatusText.Text = $"LOD refresh failed while updating {meshPath}: 0x{ex.HResult:X8} {ex.Message}";
        }
    }

    private async Task PopulateMeshInspectorAsync(string meshPath, UnrealExportTableEntry export)
    {
        try
        {
            App.WriteDiagnosticsLog("Mesh.Inspector", $"Start: {meshPath}");
            loadingMeshInspector = true;
            InspectorTitle.Text = meshPath.Split('.').LastOrDefault() ?? export.ObjectNameIndex?.Name ?? export.GetPathName();
            InspectorSubtitle.Text = meshPath;
            ClearInspectorCollections();
            MeshSummaryRows.Add("Parsing selected mesh export...");

            if (!TryResolveSelectedMesh(meshPath, out UnrealExportTableEntry? resolvedExport, out MeshExportKind kind, out USkeletalMesh? skeletalMesh, out UStaticMesh? staticMesh))
            {
                ClearInspectorCollections();
                MeshSummaryRows.Add("Selected export did not resolve as a supported mesh type.");
                LodComboBox.ItemsSource = null;
                return;
            }

            if (resolvedExport is null)
            {
                ClearInspectorCollections();
                MeshSummaryRows.Add("Selected export could not be resolved.");
                LodComboBox.ItemsSource = null;
                return;
            }

            export = resolvedExport;
            currentExport = resolvedExport;
            currentSkeletalMesh = skeletalMesh;
            currentStaticMesh = staticMesh;

            int lodCount = kind == MeshExportKind.Static
                ? staticMesh?.LODModels?.Count ?? 0
                : skeletalMesh?.LODModels?.Count ?? 0;

            LodComboBox.ItemsSource = Enumerable.Range(0, lodCount)
                .Select(index => $"LOD{index}")
                .ToList();
            LodComboBox.SelectedIndex = lodCount > 0 ? 0 : -1;

            MeshSummaryRows.Clear();
            MeshSummaryRows.Add($"Export Path: {meshPath}");
            MeshSummaryRows.Add($"Source Export: {resolvedMeshSourcePaths.GetValueOrDefault(meshPath, export.GetPathName())}");
            MeshSummaryRows.Add($"Class: {export.ClassReferenceNameIndex?.Name ?? "Unknown"}");
            MeshSummaryRows.Add($"Outer: {export.OuterReferenceNameIndex?.Name ?? "(root)"}");
            MeshSummaryRows.Add($"Serial Size: {export.SerialDataSize:N0}");
            if (kind == MeshExportKind.Static && staticMesh is not null)
            {
                int elementCount = staticMesh.LODModels?.Count > 0 ? staticMesh.LODModels[0].Elements?.Count ?? 0 : 0;
                MeshSummaryRows.Add($"Material Elements: {elementCount}");
                MeshSummaryRows.Add($"LOD Models: {staticMesh.LODModels?.Count ?? 0}");
                MeshSummaryRows.Add($"Light Map Coordinate Index: {staticMesh.LightMapCoordinateIndex}");
                MeshSummaryRows.Add($"Light Map Resolution: {staticMesh.LightMapResolution}");
                MeshSummaryRows.Add($"Has Simplified: {staticMesh.bHasBeenSimplified}");

                PopulateStaticMaterials(staticMesh);
                PopulateStaticBones(staticMesh);
                PopulatePreviewControlChoices(null);
                PopulateWorkflowRows(export, staticMesh);
                PopulateLodRows(export, staticMesh);
                await RenderPreviewAsync(staticMesh, LodComboBox.SelectedIndex).ConfigureAwait(true);
            }
            else if (skeletalMesh is not null)
            {
                MeshSummaryRows.Add($"Materials: {skeletalMesh.Materials?.Count ?? 0}");
                MeshSummaryRows.Add($"Sockets: {skeletalMesh.Sockets?.Count ?? 0}");
                MeshSummaryRows.Add($"LOD Models: {skeletalMesh.LODModels?.Count ?? 0}");
                MeshSummaryRows.Add($"Ref Skeleton Bones: {skeletalMesh.RefSkeleton?.Count ?? 0}");
                MeshSummaryRows.Add($"Skeletal Depth: {skeletalMesh.SkeletalDepth}");
                MeshSummaryRows.Add($"Clothing Assets: {skeletalMesh.ClothingAssets?.Count ?? 0}");

                PopulateMaterials(skeletalMesh);
                PopulateBones(skeletalMesh);
                PopulatePreviewControlChoices(skeletalMesh);
                PopulateWorkflowRows(export, skeletalMesh);
                PopulateLodRows(export, skeletalMesh);
                await RenderPreviewAsync(skeletalMesh, LodComboBox.SelectedIndex).ConfigureAwait(true);
            }

            App.WriteDiagnosticsLog("Mesh.Inspector", $"Completed: {meshPath}");
        }
        catch (System.Exception ex)
        {
            LogMeshError("PopulateMeshInspectorAsync", ex);
            ClearInspectorCollections();
            MeshSummaryRows.Add($"Mesh inspection failed while parsing {meshPath}: {ex.Message}");
            MeshSummaryRows.Add("See the mesh error log on the desktop for the full stack trace.");
            LodComboBox.ItemsSource = null;
            currentExport = null;
            currentSkeletalMesh = null;
            currentStaticMesh = null;
            currentPreviewMesh = null;
            currentPreviewSourceMesh = null;
            currentPreviewLodIndex = -1;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = $"Mesh inspection failed while parsing {meshPath}: 0x{ex.HResult:X8} {ex.Message}";
        }
        finally
        {
            loadingMeshInspector = false;
        }
    }

    private bool TryResolveSelectedMesh(string meshPath, out UnrealExportTableEntry? export, out MeshExportKind kind, out USkeletalMesh? skeletalMesh, out UStaticMesh? staticMesh)
    {
        export = null;
        kind = MeshExportKind.Skeletal;
        skeletalMesh = null;
        staticMesh = null;

        if (!meshExports.TryGetValue(meshPath, out export) || export is null)
            return false;

        if (!meshExportKinds.TryGetValue(meshPath, out kind))
            kind = MeshExportKind.Skeletal;

        if (resolvedMeshCache.TryGetValue(meshPath, out skeletalMesh) && skeletalMesh is not null)
            return true;

        if (resolvedStaticMeshCache.TryGetValue(meshPath, out staticMesh) && staticMesh is not null)
            return true;

        try
        {
            if (export.UnrealObject is null)
            {
                if (currentHeader is not null)
                    currentHeader.ReadExportObjectAsync(export, null).GetAwaiter().GetResult();

                export.ParseUnrealObject(false, false).GetAwaiter().GetResult();
            }

            if (export.UnrealObject is IUnrealObject { UObject: USkeletalMesh mesh })
            {
                kind = MeshExportKind.Skeletal;
                skeletalMesh = mesh;
                resolvedMeshCache[meshPath] = mesh;
                resolvedMeshSourcePaths[meshPath] = export.GetPathName();
                return true;
            }

            if (export.UnrealObject is IUnrealObject { UObject: USkeletalMeshComponent component })
            {
                USkeletalMesh? referencedMesh = component.SkeletalMesh?.LoadObject<USkeletalMesh>();
                if (referencedMesh is not null)
                {
                    skeletalMesh = referencedMesh;
                    resolvedMeshCache[meshPath] = referencedMesh;
                    resolvedMeshSourcePaths[meshPath] = export.GetPathName();
                    return true;
                }
            }

            if (export.UnrealObject is IUnrealObject { UObject: UStaticMesh staticMeshObject })
            {
                kind = MeshExportKind.Static;
                staticMesh = staticMeshObject;
                resolvedStaticMeshCache[meshPath] = staticMeshObject;
                resolvedMeshSourcePaths[meshPath] = export.GetPathName();
                return true;
            }
        }
        catch (System.Exception ex)
        {
            App.WriteDiagnosticsLog("Mesh.Resolve", $"Failed to resolve mesh {meshPath}: {ex.Message}");
        }

        return false;
    }

    private void PopulateLodRows(UnrealExportTableEntry export, USkeletalMesh skeletalMesh)
    {
        LodSummaryRows.Clear();
        SectionRows.Clear();
        ChunkRows.Clear();
        SectionDetailRows.Clear();

        if (LodComboBox.SelectedIndex < 0 || skeletalMesh.LODModels is null || LodComboBox.SelectedIndex >= skeletalMesh.LODModels.Count)
            return;

        FStaticLODModel lod = skeletalMesh.LODModels[LodComboBox.SelectedIndex];
        LodSummaryRows.Add($"Export Path: {export.GetPathName()}");
        LodSummaryRows.Add($"LOD Index: {LodComboBox.SelectedIndex}");
        LodSummaryRows.Add($"Sections: {lod.Sections?.Count ?? 0}");
        LodSummaryRows.Add($"Chunks: {lod.Chunks?.Count ?? 0}");
        LodSummaryRows.Add($"Vertices: {lod.NumVertices:N0}");
        LodSummaryRows.Add($"Indices: {lod.MultiSizeIndexContainer?.IndexBuffer?.Count ?? 0:N0}");
        LodSummaryRows.Add($"Active Bones: {lod.ActiveBoneIndices?.Count ?? 0}");
        LodSummaryRows.Add($"Required Bones: {lod.RequiredBones?.Length ?? 0}");
        LodSummaryRows.Add($"TexCoords: {lod.NumTexCoords}");
        LodSummaryRows.Add($"LOD Size: {lod.Size:N0}");
        LodSummaryRows.Add($"Color Vertices: {lod.ColorVertexBuffer?.Colors?.Count() ?? 0}");

        if (lod.Sections is not null)
        {
            for (int index = 0; index < lod.Sections.Count; index++)
            {
                FSkelMeshSection section = lod.Sections[index];
                SectionRows.Add($"Section {index}: Material={section.MaterialIndex}, Chunk={section.ChunkIndex}, Triangles={section.NumTriangles}, BaseIndex={section.BaseIndex}");
            }
        }

        RefreshPreviewSectionChoices();

        if (lod.Chunks is not null)
        {
            for (int index = 0; index < lod.Chunks.Count; index++)
            {
                FSkelMeshChunk chunk = lod.Chunks[index];
                ChunkRows.Add($"Chunk {index}: BaseVertex={chunk.BaseVertexIndex}, Rigid={chunk.NumRigidVertices}, Soft={chunk.NumSoftVertices}, MaxInfluences={chunk.MaxBoneInfluences}, BoneMap={chunk.BoneMap?.Count ?? 0}");
            }
        }

        if (SectionRows.Count > 0)
        {
            SectionRowsList.SelectedIndex = 0;
        }
        else if (ChunkRows.Count > 0)
            ChunkRowsList.SelectedIndex = 0;

        _ = RenderPreviewAsync(skeletalMesh, LodComboBox.SelectedIndex);
    }

    private void PopulateLodRows(UnrealExportTableEntry export, UStaticMesh staticMesh)
    {
        LodSummaryRows.Clear();
        SectionRows.Clear();
        ChunkRows.Clear();
        SectionDetailRows.Clear();

        if (LodComboBox.SelectedIndex < 0 || staticMesh.LODModels is null || LodComboBox.SelectedIndex >= staticMesh.LODModels.Count)
            return;

        FStaticMeshRenderData lod = staticMesh.LODModels[LodComboBox.SelectedIndex];
        LodSummaryRows.Add($"Export Path: {export.GetPathName()}");
        LodSummaryRows.Add($"LOD Index: {LodComboBox.SelectedIndex}");
        LodSummaryRows.Add($"Elements: {lod.Elements?.Count ?? 0}");
        LodSummaryRows.Add($"Vertices: {lod.NumVertices:N0}");
        LodSummaryRows.Add($"Indices: {lod.IndexBuffer?.Indices?.Count ?? 0:N0}");
        LodSummaryRows.Add($"TexCoords: {lod.VertexBuffer?.NumTexCoords ?? 0}");
        LodSummaryRows.Add($"LOD Size: {lod.NumVertices:N0}");

        if (lod.Elements is not null)
        {
            for (int index = 0; index < lod.Elements.Count; index++)
            {
                FStaticMeshElement element = lod.Elements[index];
                SectionRows.Add($"Element {index}: MaterialIndex={element.MaterialIndex}, FirstIndex={element.FirstIndex}, Triangles={element.NumTriangles}, MinVertex={element.MinVertexIndex}, MaxVertex={element.MaxVertexIndex}");
            }
        }

        if (SectionRows.Count == 0)
            SectionRows.Add("No static mesh elements were found for this LOD.");

        RefreshPreviewSectionChoices();
        if (SectionRows.Count > 0)
            SectionRowsList.SelectedIndex = 0;

        _ = RenderPreviewAsync(staticMesh, LodComboBox.SelectedIndex);
    }

    private void SelectMesh(string exportPath)
    {
        if (MeshComboBox.ItemsSource is not IEnumerable<string> items)
            return;

        string? match = items.FirstOrDefault(item => string.Equals(item, exportPath, System.StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            MeshComboBox.SelectedItem = match;
    }

    private void RegisterMeshExport(UnrealExportTableEntry export, MeshExportKind kind, List<string> names, string? pathOverride = null)
    {
        string path = pathOverride ?? export.GetPathName();
        if (string.IsNullOrWhiteSpace(path))
            return;

        meshExports[path] = export;
        meshExportKinds[path] = kind;
        resolvedMeshSourcePaths[path] = pathOverride ?? path;
        names.Add(path);
    }

    private static bool IsSkeletalMeshClass(string className)
    {
        return string.Equals(className, nameof(USkeletalMesh), System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "SkeletalMesh", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStaticMeshClass(string className)
    {
        return string.Equals(className, nameof(UStaticMesh), System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "StaticMesh", System.StringComparison.OrdinalIgnoreCase);
    }

    private void PopulateMaterials(USkeletalMesh skeletalMesh)
    {
        MaterialRows.Clear();

        if (skeletalMesh.Materials is null || skeletalMesh.Materials.Count == 0)
        {
            MaterialRows.Add("No material bindings found.");
            return;
        }

        for (int index = 0; index < skeletalMesh.Materials.Count; index++)
        {
            FObject? material = skeletalMesh.Materials[index];
            if (material is null)
            {
                MaterialRows.Add($"Material {index}: (null)");
                continue;
            }

            string name = !string.IsNullOrWhiteSpace(material.GetPathName()) ? material.GetPathName() : material.Name ?? "(unnamed material)";
            MaterialRows.Add($"Material {index}: {name}");
        }
    }

    private void PopulateStaticMaterials(UStaticMesh staticMesh)
    {
        MaterialRows.Clear();

        if (staticMesh.LODModels is null || staticMesh.LODModels.Count == 0 || staticMesh.LODModels[0].Elements is null || staticMesh.LODModels[0].Elements.Count == 0)
        {
            MaterialRows.Add("No static mesh material elements found.");
            return;
        }

        FStaticMeshRenderData lod = staticMesh.LODModels[0];
        for (int index = 0; index < lod.Elements.Count; index++)
        {
            FStaticMeshElement element = lod.Elements[index];
            string materialName = element.Material?.GetPathName() ?? element.Material?.Name ?? $"Material {index}";
            MaterialRows.Add($"Element {index}: {materialName}");
        }
    }

    private void PopulateBones(USkeletalMesh skeletalMesh)
    {
        BoneRows.Clear();

        int socketCount = skeletalMesh.Sockets?.Count ?? 0;
        int boneCount = skeletalMesh.RefSkeleton?.Count ?? 0;
        BoneRows.Add($"Sockets: {socketCount}");
        BoneRows.Add($"Ref Bones: {boneCount}");

        if (skeletalMesh.Sockets is not null)
        {
            for (int index = 0; index < System.Math.Min(6, skeletalMesh.Sockets.Count); index++)
            {
                FObject socket = skeletalMesh.Sockets[index];
                string name = !string.IsNullOrWhiteSpace(socket.GetPathName()) ? socket.GetPathName() : socket.Name ?? $"Socket {index}";
                BoneRows.Add($"Socket {index}: {name}");
            }
        }

        if (skeletalMesh.RefSkeleton is not null)
        {
            for (int index = 0; index < System.Math.Min(8, skeletalMesh.RefSkeleton.Count); index++)
            {
                FMeshBone bone = skeletalMesh.RefSkeleton[index];
                BoneRows.Add($"Bone {index}: {bone.Name} (Parent {bone.ParentIndex})");
            }
        }
    }

    private void PopulateStaticBones(UStaticMesh staticMesh)
    {
        BoneRows.Clear();
        BoneRows.Add("Static mesh: no skeletal bones.");
        BoneRows.Add($"LOD models: {staticMesh.LODModels?.Count ?? 0}");
    }

    private void PopulateWorkflowRows(UnrealExportTableEntry export, USkeletalMesh skeletalMesh)
    {
        ExporterRows.Clear();
        ImporterRows.Clear();

        ExporterRows.Add($"Target UPK: {currentHeader?.FullFilename ?? "(none)"}");
        ExporterRows.Add($"Selected mesh: {export.GetPathName()}");
        ExporterRows.Add($"Selected LOD: {System.Math.Max(0, LodComboBox.SelectedIndex)}");
        ExporterRows.Add($"Available LODs: {skeletalMesh.LODModels?.Count ?? 0}");
        ExporterRows.Add($"FBX Output: {(string.IsNullOrWhiteSpace(exportFbxPath) ? "(not selected)" : exportFbxPath)}");
        ExporterRows.Add("Next step: port the real FBX export backend from the WinForms exporter panel.");
        ExporterRows.Add("Goal: preserve the familiar UPK -> SkeletalMesh -> LOD -> FBX workflow.");

        ImporterRows.Add($"Target UPK: {currentHeader?.FullFilename ?? "(none)"}");
        ImporterRows.Add($"Selected mesh: {export.GetPathName()}");
        ImporterRows.Add($"Selected LOD: {System.Math.Max(0, LodComboBox.SelectedIndex)}");
        ImporterRows.Add($"Replace-ready LODs: {skeletalMesh.LODModels?.Count ?? 0}");
        ImporterRows.Add($"FBX Source: {(string.IsNullOrWhiteSpace(importFbxPath) ? "(not selected)" : importFbxPath)}");
        ImporterRows.Add($"Replace All LODs: {(ReplaceAllLodsCheckBox.IsChecked == true ? "Yes" : "No")}");
        ImporterRows.Add("Next step: port the real FBX import backend, logs, and rebuild path from the WinForms importer panel.");
        ImporterRows.Add("Goal: keep the live diagnostics and rebuild status visible inside this WinUI workspace.");
    }

    private void PopulateWorkflowRows(UnrealExportTableEntry export, UStaticMesh staticMesh)
    {
        ExporterRows.Clear();
        ImporterRows.Clear();

        ExporterRows.Add($"Target UPK: {currentHeader?.FullFilename ?? "(none)"}");
        ExporterRows.Add($"Selected mesh: {export.GetPathName()}");
        ExporterRows.Add($"Selected LOD: {System.Math.Max(0, LodComboBox.SelectedIndex)}");
        ExporterRows.Add($"Available LODs: {staticMesh.LODModels?.Count ?? 0}");
        ExporterRows.Add($"FBX Output: {(string.IsNullOrWhiteSpace(exportFbxPath) ? "(not selected)" : exportFbxPath)}");
        ExporterRows.Add("Static mesh preview is enabled through the mesh workspace export picker.");

        ImporterRows.Add($"Target UPK: {currentHeader?.FullFilename ?? "(none)"}");
        ImporterRows.Add($"Selected mesh: {export.GetPathName()}");
        ImporterRows.Add($"Selected LOD: {System.Math.Max(0, LodComboBox.SelectedIndex)}");
        ImporterRows.Add($"Replace-ready LODs: {staticMesh.LODModels?.Count ?? 0}");
        ImporterRows.Add($"FBX Source: {(string.IsNullOrWhiteSpace(importFbxPath) ? "(not selected)" : importFbxPath)}");
        ImporterRows.Add("Static mesh import/export flow is still routed through the shared mesh workspace.");
    }

    private void ApplyPreviewSceneSettings()
    {
        previewScene.DisplayMode = GetSelectedEnum(PreviewDisplayModeComboBox, MeshPreviewDisplayMode.Overlay);
        previewScene.ShadingMode = GetSelectedEnum(PreviewShadingModeComboBox, MeshPreviewShadingMode.Lit);
        previewScene.BackgroundStyle = GetSelectedEnum(PreviewBackgroundComboBox, MeshPreviewBackgroundStyle.DarkGradient);
        previewScene.LightingPreset = GetSelectedEnum(PreviewLightingComboBox, MeshPreviewLightingPreset.Neutral);
        previewScene.MaterialChannel = GetSelectedEnum(PreviewMaterialChannelComboBox, MeshPreviewMaterialChannel.FullMaterial);
        previewScene.WeightViewMode = GetSelectedEnum(PreviewWeightViewComboBox, MeshPreviewWeightViewMode.SelectedBoneHeatmap);
        previewScene.SectionFocusMode = GetSelectedEnum(PreviewSectionFocusModeComboBox, MeshPreviewSectionFocusMode.None);
        previewScene.ShowFbxMesh = PreviewShowFbxCheckBox.IsChecked == true;
        previewScene.ShowUe3Mesh = PreviewShowUe3CheckBox.IsChecked != false;
        previewScene.Wireframe = PreviewWireframeCheckBox.IsChecked == true;
        previewScene.ShowGroundPlane = PreviewShowGroundCheckBox.IsChecked != false;
        previewScene.ShowBones = PreviewShowBonesCheckBox.IsChecked == true;
        previewScene.ShowBoneNames = PreviewShowBoneNamesCheckBox.IsChecked == true;
        previewScene.ShowWeights = PreviewShowWeightsCheckBox.IsChecked == true;
        previewScene.ShowSections = PreviewShowSectionsCheckBox.IsChecked == true;
        previewScene.ShowNormals = PreviewShowNormalsCheckBox.IsChecked == true;
        previewScene.ShowTangents = PreviewShowTangentsCheckBox.IsChecked == true;
        previewScene.ShowUvSeams = PreviewShowUvSeamsCheckBox.IsChecked == true;
        previewScene.AmbientLight = (float)(PreviewAmbientLightSlider.Value / 100.0);
        previewScene.SelectedBoneName = PreviewBoneComboBox.SelectedItem as string ?? string.Empty;
        if (previewScene.ShadingMode == MeshPreviewShadingMode.GameApprox)
        {
            previewScene.ShowWeights = false;
        }
        if (PreviewSectionComboBox.SelectedItem is PreviewSectionOption sectionOption && sectionOption.Mesh != MeshPreviewSectionFocusMesh.None)
        {
            previewScene.SectionFocusMesh = sectionOption.Mesh;
            previewScene.FocusedSectionIndex = sectionOption.SectionIndex;
        }
        else
        {
            previewScene.SectionFocusMesh = MeshPreviewSectionFocusMesh.None;
            previewScene.FocusedSectionIndex = -1;
        }
    }

    private void ClearInspectorCollections()
    {
        MeshSummaryRows.Clear();
        LodSummaryRows.Clear();
        MaterialRows.Clear();
        BoneRows.Clear();
        ExporterRows.Clear();
        ImporterRows.Clear();
        SectionRows.Clear();
        ChunkRows.Clear();
        SectionDetailRows.Clear();
    }

    private async Task RenderPreviewAsync(UStaticMesh staticMesh, int lodIndex)
    {
        if (staticMesh.LODModels is null || staticMesh.LODModels.Count == 0 || lodIndex < 0 || lodIndex >= staticMesh.LODModels.Count)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = "Selected mesh does not contain a renderable LOD.";
            PreviewMeshStatsText.Text = "No renderable LOD";
            currentPreviewMesh = null;
            currentPreviewSourceMesh = null;
            currentPreviewLodIndex = -1;
            return;
        }

        try
        {
            string rendererName = PreviewRendererComboBox.SelectedItem as string ?? "VorticeDirect3D11";
            string meshPath = currentExport?.GetPathName() ?? "(unknown)";
            App.WriteDiagnosticsLog("Mesh.RenderPreview", $"Start: {meshPath} LOD={lodIndex} renderer={rendererName}");
            bool needsMeshConversion =
                currentPreviewMesh is null ||
                !ReferenceEquals(currentPreviewSourceMesh, staticMesh) ||
                currentPreviewLodIndex != lodIndex;

            if (needsMeshConversion)
            {
                PreviewStatusText.Text = $"Building native WinUI preview mesh for LOD{lodIndex} with {rendererName}...";
                MeshPreviewMesh previewMesh = await Task.Run(() =>
                {
                    return ue3ToPreviewMeshConverter.Convert(staticMesh, lodIndex);
                }).ConfigureAwait(true);

                previewScene.SetUe3Mesh(null);
                previewScene.SetUe3Mesh(previewMesh);
                PreviewDisplayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Ue3Only);
                currentPreviewMesh = previewScene.Ue3Mesh;
                currentPreviewSourceMesh = staticMesh;
                currentPreviewLodIndex = lodIndex;
                RefreshPreviewSectionChoices();
                PreviewShowUe3CheckBox.IsChecked = true;
                if (previewScene.FbxMesh is null &&
                    string.Equals(PreviewDisplayModeComboBox.SelectedItem as string, nameof(MeshPreviewDisplayMode.FbxOnly), System.StringComparison.Ordinal))
                {
                    PreviewDisplayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Ue3Only);
                }
                SyncPreviewCamera(currentPreviewMesh);
            }
            else
            {
                PreviewStatusText.Text = $"Rendering native WinUI preview for LOD{lodIndex} with {rendererName}...";
                previewScene.SetUe3Mesh(null);
                previewScene.SetUe3Mesh(currentPreviewMesh);
            }

            if (currentPreviewMesh is null)
                return;

            UpdatePreviewHeader(rendererName);
            ApplyPreviewSceneSettings();

            int width = (int)Math.Max(320, PreviewSwapChainPanel.ActualWidth > 0 ? PreviewSwapChainPanel.ActualWidth : (PreviewImage.ActualWidth > 0 ? PreviewImage.ActualWidth : 960));
            int height = (int)Math.Max(240, PreviewSwapChainPanel.ActualHeight > 0 ? PreviewSwapChainPanel.ActualHeight : (PreviewImage.ActualHeight > 0 ? PreviewImage.ActualHeight : 540));

            if (string.Equals(rendererName, "VorticeDirect3D11", StringComparison.Ordinal))
            {
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewSwapChainPanel.Visibility = Visibility.Visible;
                d3dPreviewRenderer.AttachToPanel(PreviewSwapChainPanel, DispatcherQueue);
                d3dPreviewRenderer.SetFrame(previewScene, previewCamera);
                PreviewStatusText.Text = GetPreviewStatusText(rendererName, d3dPreviewRenderer.LastRenderSucceeded, d3dPreviewRenderer.Diagnostics);
                PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);
                if (!d3dPreviewRenderer.LastRenderSucceeded)
                    StatusRows.Add($"Native preview: {d3dPreviewRenderer.Diagnostics}");
            }
            else
            {
                d3dPreviewRenderer.DetachPanel();
                PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
                PreviewImage.Visibility = Visibility.Visible;

                WriteableBitmap bitmap = previewRenderer.Render(
                    previewScene,
                    width,
                    height,
                    previewCamera,
                    GetSelectedEnum(PreviewShadingModeComboBox, MeshPreviewShadingMode.Clay),
                    GetSelectedEnum(PreviewBackgroundComboBox, MeshPreviewBackgroundStyle.DarkGradient),
                    GetSelectedEnum(PreviewLightingComboBox, MeshPreviewLightingPreset.Neutral),
                    PreviewWireframeCheckBox.IsChecked == true,
                    PreviewShowGroundCheckBox.IsChecked == true);
                PreviewImage.Source = bitmap;
            }

            UpdateBoneNameOverlay();

            if (!string.Equals(rendererName, "VorticeDirect3D11", StringComparison.Ordinal))
            {
                PreviewStatusText.Text = string.Empty;
                PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);
            }

            App.WriteDiagnosticsLog("Mesh.RenderPreview", $"Completed: {meshPath} LOD={lodIndex} renderer={rendererName}");
        }
        catch (System.Exception ex)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = $"Native preview failed: 0x{ex.HResult:X8} {ex.Message}";
            PreviewMeshStatsText.Text = "Preview failed";
            StatusRows.Add($"Native preview failed: 0x{ex.HResult:X8} {ex.Message}");
        }
    }

    private async Task RenderPreviewAsync(USkeletalMesh skeletalMesh, int lodIndex)
    {
        if (skeletalMesh.LODModels is null || skeletalMesh.LODModels.Count == 0 || lodIndex < 0 || lodIndex >= skeletalMesh.LODModels.Count)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = "Selected mesh does not contain a renderable LOD.";
            PreviewMeshStatsText.Text = "No renderable LOD";
            currentPreviewMesh = null;
            currentPreviewSourceMesh = null;
            currentPreviewLodIndex = -1;
            return;
        }

        try
        {
            string rendererName = PreviewRendererComboBox.SelectedItem as string ?? "VorticeDirect3D11";
            string meshPath = currentExport?.GetPathName() ?? "(unknown)";
            App.WriteDiagnosticsLog("Mesh.RenderPreview", $"Start: {meshPath} LOD={lodIndex} renderer={rendererName}");
            bool needsMeshConversion =
                currentPreviewMesh is null ||
                !ReferenceEquals(currentPreviewSourceMesh, skeletalMesh) ||
                currentPreviewLodIndex != lodIndex;

            if (needsMeshConversion)
            {
                PreviewStatusText.Text = $"Building native WinUI preview mesh for LOD{lodIndex} with {rendererName}...";
                MeshPreviewMesh previewMesh = await Task.Run(() =>
                {
                    return ue3ToPreviewMeshConverter.Convert(skeletalMesh, lodIndex);
                }).ConfigureAwait(true);

                if (currentHeader is not null)
                {
                    await gameMaterialResolver.ApplyToSectionsAsync(
                        currentHeader.FullFilename,
                        skeletalMesh,
                        previewMesh,
                        message => App.WriteDiagnosticsLog("Mesh.GameApprox", message)).ConfigureAwait(true);
                }

                previewScene.SetUe3Mesh(null);
                previewScene.SetUe3Mesh(previewMesh);
                PreviewDisplayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Ue3Only);
            currentPreviewMesh = previewScene.Ue3Mesh;
            currentPreviewSourceMesh = skeletalMesh;
            currentPreviewLodIndex = lodIndex;
            RefreshPreviewSectionChoices();
            PreviewShowUe3CheckBox.IsChecked = true;
            if (previewScene.FbxMesh is null &&
                string.Equals(PreviewDisplayModeComboBox.SelectedItem as string, nameof(MeshPreviewDisplayMode.FbxOnly), System.StringComparison.Ordinal))
            {
                PreviewDisplayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Ue3Only);
            }
            SyncPreviewCamera(currentPreviewMesh);
        }
            else
            {
                PreviewStatusText.Text = $"Rendering native WinUI preview for LOD{lodIndex} with {rendererName}...";
                previewScene.SetUe3Mesh(null);
                previewScene.SetUe3Mesh(currentPreviewMesh);
            }

            if (currentPreviewMesh is null)
                return;

            UpdatePreviewHeader(rendererName);

            ApplyPreviewSceneSettings();

            int width = (int)Math.Max(320, PreviewSwapChainPanel.ActualWidth > 0 ? PreviewSwapChainPanel.ActualWidth : (PreviewImage.ActualWidth > 0 ? PreviewImage.ActualWidth : 960));
            int height = (int)Math.Max(240, PreviewSwapChainPanel.ActualHeight > 0 ? PreviewSwapChainPanel.ActualHeight : (PreviewImage.ActualHeight > 0 ? PreviewImage.ActualHeight : 540));

            if (string.Equals(rendererName, "VorticeDirect3D11", StringComparison.Ordinal))
            {
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewSwapChainPanel.Visibility = Visibility.Visible;
                d3dPreviewRenderer.AttachToPanel(PreviewSwapChainPanel, DispatcherQueue);
                d3dPreviewRenderer.SetFrame(previewScene, previewCamera);
                PreviewStatusText.Text = GetPreviewStatusText(rendererName, d3dPreviewRenderer.LastRenderSucceeded, d3dPreviewRenderer.Diagnostics);
                PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);
                if (!d3dPreviewRenderer.LastRenderSucceeded)
                    StatusRows.Add($"Native preview: {d3dPreviewRenderer.Diagnostics}");
            }
            else
            {
                d3dPreviewRenderer.DetachPanel();
                PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
                PreviewImage.Visibility = Visibility.Visible;

                WriteableBitmap bitmap = previewRenderer.Render(
                    previewScene,
                    width,
                    height,
                    previewCamera,
                    GetSelectedEnum(PreviewShadingModeComboBox, MeshPreviewShadingMode.Clay),
                    GetSelectedEnum(PreviewBackgroundComboBox, MeshPreviewBackgroundStyle.DarkGradient),
                    GetSelectedEnum(PreviewLightingComboBox, MeshPreviewLightingPreset.Neutral),
                    PreviewWireframeCheckBox.IsChecked == true,
                    PreviewShowGroundCheckBox.IsChecked == true);
                PreviewImage.Source = bitmap;
            }

            UpdateBoneNameOverlay();

            if (!string.Equals(rendererName, "VorticeDirect3D11", StringComparison.Ordinal))
            {
                PreviewStatusText.Text = string.Empty;
                PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);
            }

            App.WriteDiagnosticsLog("Mesh.RenderPreview", $"Completed: {meshPath} LOD={lodIndex} renderer={rendererName}");
        }
        catch (System.Exception ex)
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = $"Native preview failed: 0x{ex.HResult:X8} {ex.Message}";
            PreviewMeshStatsText.Text = "Preview failed";
            StatusRows.Add($"Native preview failed: 0x{ex.HResult:X8} {ex.Message}");
        }
    }

    private Task RenderCurrentPreviewAsync()
    {
        if (currentPreviewMesh is null)
            return Task.CompletedTask;

        string rendererName = PreviewRendererComboBox.SelectedItem as string ?? "VorticeDirect3D11";
        UpdatePreviewHeader(rendererName);
        ApplyPreviewSceneSettings();
        RefreshPreviewSectionChoices();

        SyncPreviewSurfaceSize();
        UpdateBoneNameOverlay();

        int width = (int)Math.Max(320,
            PreviewViewportHost.ActualWidth > 0 ? PreviewViewportHost.ActualWidth :
            PreviewSwapChainPanel.ActualWidth > 0 ? PreviewSwapChainPanel.ActualWidth :
            (PreviewImage.ActualWidth > 0 ? PreviewImage.ActualWidth : 960));
        int height = (int)Math.Max(240,
            PreviewViewportHost.ActualHeight > 0 ? PreviewViewportHost.ActualHeight :
            PreviewSwapChainPanel.ActualHeight > 0 ? PreviewSwapChainPanel.ActualHeight :
            (PreviewImage.ActualHeight > 0 ? PreviewImage.ActualHeight : 540));

        if (string.Equals(rendererName, "VorticeDirect3D11", StringComparison.Ordinal))
        {
            if (width < 32 || height < 32)
            {
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewSwapChainPanel.Visibility = Visibility.Visible;
                PreviewStatusText.Text = $"D3D11 waiting for usable panel size: {width}x{height}.";
                PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);

                if (!previewSurfaceRetryPending)
                {
                    previewSurfaceRetryPending = true;
                    _ = DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            await Task.Delay(50).ConfigureAwait(true);
                            previewSurfaceRetryPending = false;
                            if (currentPreviewMesh is not null)
                                await RenderCurrentPreviewAsync().ConfigureAwait(true);
                        }
                        catch
                        {
                            previewSurfaceRetryPending = false;
                        }
                    });
                }

                return Task.CompletedTask;
            }

            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            SyncPreviewSurfaceSize();
            PreviewSwapChainPanel.Visibility = Visibility.Visible;
            d3dPreviewRenderer.AttachToPanel(PreviewSwapChainPanel, DispatcherQueue);
            d3dPreviewRenderer.SetFrame(previewScene, previewCamera);
            PreviewStatusText.Text = GetPreviewStatusText(rendererName, d3dPreviewRenderer.LastRenderSucceeded, d3dPreviewRenderer.Diagnostics);
            PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);
            if (!d3dPreviewRenderer.LastRenderSucceeded)
                StatusRows.Add($"Native preview: {d3dPreviewRenderer.Diagnostics}");
        }
        else
        {
            d3dPreviewRenderer.DetachPanel();
            PreviewSwapChainPanel.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Visible;

            WriteableBitmap bitmap = previewRenderer.Render(
                previewScene,
                width,
                height,
                previewCamera,
                GetSelectedEnum(PreviewShadingModeComboBox, MeshPreviewShadingMode.Clay),
                GetSelectedEnum(PreviewBackgroundComboBox, MeshPreviewBackgroundStyle.DarkGradient),
                GetSelectedEnum(PreviewLightingComboBox, MeshPreviewLightingPreset.Neutral),
                PreviewWireframeCheckBox.IsChecked == true,
                PreviewShowGroundCheckBox.IsChecked == true);
            PreviewImage.Source = bitmap;
            PreviewStatusText.Text = string.Empty;
            PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);
        }

        return Task.CompletedTask;
    }

    private void SyncPreviewSurfaceSize()
    {
        double hostWidth = GetSafeSize(PreviewViewportHost.ActualWidth, PreviewViewportHost.Width, PreviewViewportHost.MinWidth);
        double hostHeight = GetSafeSize(PreviewViewportHost.ActualHeight, PreviewViewportHost.Height, PreviewViewportHost.MinHeight);

        PreviewSwapChainPanel.Tag = PreviewViewportHost;

        double swapWidth = Math.Max(320, hostWidth);
        double swapHeight = Math.Max(240, hostHeight);

        PreviewSwapChainPanel.Width = swapWidth;
        PreviewSwapChainPanel.Height = swapHeight;
        PreviewSwapChainPanel.MinWidth = swapWidth;
        PreviewSwapChainPanel.MinHeight = swapHeight;

        PreviewViewportHost.UpdateLayout();
        PreviewSwapChainPanel.UpdateLayout();
    }

    private static double GetSafeSize(params double[] values)
    {
        foreach (double value in values)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                continue;

            return value;
        }

        return 0;
    }

    private void SetActiveView(string view)
    {
        string normalizedView = string.IsNullOrWhiteSpace(view)
            ? PreviewView
            : view.Trim().ToLowerInvariant();

        currentView = normalizedView;
        PreviewPanel.Visibility = normalizedView == PreviewView ? Visibility.Visible : Visibility.Collapsed;
        ExporterPanel.Visibility = normalizedView == ExporterView ? Visibility.Visible : Visibility.Collapsed;
        ImporterPanel.Visibility = normalizedView == ImporterView ? Visibility.Visible : Visibility.Collapsed;
        SectionsPanel.Visibility = normalizedView == SectionsView ? Visibility.Visible : Visibility.Collapsed;

        InspectorTitle.Text = normalizedView switch
        {
            ExporterView => "Mesh Exporter",
            ImporterView => "Mesh Importer",
            SectionsView => "Mesh Sections",
            _ => "Mesh Preview"
        };

        InspectorSubtitle.Text = normalizedView switch
        {
            ExporterView => "This workspace will take over FBX export flow from the old mesh exporter panel.",
            ImporterView => "This workspace will take over FBX replacement and import diagnostics from the old mesh importer panel.",
            SectionsView => "Inspect the active LOD's section and chunk layout before deeper tools are ported.",
            _ => "Live SkeletalMesh and LOD inspection for the future WinUI mesh preview workspace."
        };

        SaveSessionState();
    }

    public string ActiveWorkspace => currentView;

    public string BuildDiagnosticsReport()
    {
        StringBuilder sb = new();
        sb.AppendLine("Mesh Diagnostics");
        sb.AppendLine($"Active Workspace: {currentView}");
        sb.AppendLine($"Current UPK: {currentHeader?.FullFilename ?? "(none)"}");
        sb.AppendLine($"Mesh Count: {meshExports.Count:N0}");
        sb.AppendLine($"Selected Export: {currentExport?.GetPathName() ?? "(none)"}");
        sb.AppendLine($"Selected LOD: {(LodComboBox.SelectedIndex >= 0 ? $"LOD{LodComboBox.SelectedIndex}" : "(none)")}");
        sb.AppendLine($"Selected Renderer: {PreviewRendererComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Display Mode: {PreviewDisplayModeComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Shading Mode: {PreviewShadingModeComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Lighting Preset: {PreviewLightingComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Material Channel: {PreviewMaterialChannelComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Background: {PreviewBackgroundComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Weight View: {PreviewWeightViewComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Section Focus: {PreviewSectionFocusModeComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Focused Section: {PreviewSectionComboBox.SelectedItem as string ?? "(All Sections)"}");
        sb.AppendLine($"Influence Bone: {PreviewBoneComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Preview Surface Loaded: {previewSurfaceLoaded}");
        sb.AppendLine($"Preview Controls Initialized: {previewControlsInitialized}");
        sb.AppendLine($"Preview Render In Progress: {previewRenderInProgress}");
        sb.AppendLine($"Preview Render Pending: {previewRenderPending}");
        sb.AppendLine($"Preview Host Size: {PreviewViewportHost.ActualWidth:0.##}x{PreviewViewportHost.ActualHeight:0.##}");
        sb.AppendLine($"Preview Panel Size: {PreviewSwapChainPanel.ActualWidth:0.##}x{PreviewSwapChainPanel.ActualHeight:0.##}");
        sb.AppendLine($"Preview Mesh Loaded: {currentPreviewMesh is not null}");
        sb.AppendLine($"FBX Mesh Loaded: {previewScene.FbxMesh is not null}");
        sb.AppendLine($"UE3 Mesh Loaded: {previewScene.Ue3Mesh is not null}");
        AppendGameMaterialDiagnostics(sb);
        sb.AppendLine($"Preview Status: {PreviewStatusText.Text}");
        sb.AppendLine($"D3D Last Success: {d3dPreviewRenderer.LastRenderSucceeded}");
        sb.AppendLine($"D3D Diagnostics: {d3dPreviewRenderer.Diagnostics}");
        sb.AppendLine($"Camera Target: {previewCamera.Target}");
        sb.AppendLine($"Camera Distance: {previewCamera.Distance:F2}");
        sb.AppendLine($"Camera Yaw: {previewCamera.YawDegrees:F2}");
        sb.AppendLine($"Camera Pitch: {previewCamera.PitchDegrees:F2}");
        return sb.ToString();
    }

    public async Task<string> RunMaterialProbeAsync()
    {
        if (currentPreviewMesh is null)
            return "Material Probe\nNo preview mesh is currently loaded.";

        if (!string.Equals(currentView, PreviewView, StringComparison.OrdinalIgnoreCase))
            SetWorkspaceMode(PreviewView);

        Vector3 savedTarget = previewCamera.Target;
        float savedDistance = previewCamera.Distance;
        float savedYaw = previewCamera.YawDegrees;
        float savedPitch = previewCamera.PitchDegrees;

        StringBuilder sb = new();
        sb.AppendLine("Material Probe");
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine($"UPK: {currentHeader?.FullFilename ?? "(none)"}");
        sb.AppendLine($"Export: {currentExport?.GetPathName() ?? "(none)"}");
        sb.AppendLine($"Renderer: {PreviewRendererComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Shading: {PreviewShadingModeComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Material Channel: {PreviewMaterialChannelComboBox.SelectedItem as string ?? "(none)"}");
        sb.AppendLine($"Selected Section: {(PreviewSectionComboBox.SelectedItem as PreviewSectionOption)?.Label ?? "(all)"}");
        sb.AppendLine();

        try
        {
            float[] yawValues = [28.0f, 45.0f, 62.0f];
            float[] pitchValues = [8.0f, 16.0f, 24.0f];
            float[] distanceMultipliers = [1.00f, 1.35f, 1.75f];

            Vector3 target = currentPreviewMesh.Center;
            float baseDistance = MathF.Max(1.0f, currentPreviewMesh.Radius * 1.35f);
            int pass = 0;

            foreach (float distanceMultiplier in distanceMultipliers)
            {
                foreach (float pitch in pitchValues)
                {
                    foreach (float yaw in yawValues)
                    {
                        pass++;
                        previewCamera.Configure(target, baseDistance * distanceMultiplier, yaw, pitch);
                        await RenderCurrentPreviewAsync().ConfigureAwait(true);
                        await Task.Delay(20).ConfigureAwait(true);

                        sb.AppendLine($"Pass {pass:00}: Distx{distanceMultiplier:F2} Yaw={yaw:F2} Pitch={pitch:F2}");
                        sb.AppendLine($"Status: {PreviewStatusText.Text}");
                        sb.AppendLine($"D3D: {d3dPreviewRenderer.Diagnostics}");
                        sb.AppendLine();
                    }
                }
            }
        }
        finally
        {
            previewCamera.Configure(savedTarget, savedDistance, savedYaw, savedPitch);
            await RenderCurrentPreviewAsync().ConfigureAwait(true);
        }

        string report = sb.ToString();
        App.WriteMaterialProbeLog("Mesh.MaterialProbe", report);
        return report;
    }

    private void AppendGameMaterialDiagnostics(StringBuilder sb)
    {
        if (currentPreviewMesh is null || currentPreviewMesh.Sections.Count == 0)
            return;

        int resolvedCount = currentPreviewMesh.Sections.Count(static section => section.GameMaterial is not null);
        sb.AppendLine($"GameApprox Materials Resolved: {resolvedCount}/{currentPreviewMesh.Sections.Count}");

        MeshPreviewSection? section = GetDiagnosticsSection();
        MeshPreviewGameMaterial? material = section?.GameMaterial;
        if (section is null || material is null)
        {
            sb.AppendLine("GameApprox Selected Section: (none)");
            return;
        }

        sb.AppendLine($"GameApprox Selected Section: {section.Index} ({section.Name})");
        sb.AppendLine($"GameApprox Material Path: {material.MaterialPath}");
        sb.AppendLine($"GameApprox Textures: Diffuse={material.HasTexture(MeshPreviewGameTextureSlot.Diffuse)}, Normal={material.HasTexture(MeshPreviewGameTextureSlot.Normal)}, SMSPSK={material.HasTexture(MeshPreviewGameTextureSlot.Smspsk)}, ESPA={material.HasTexture(MeshPreviewGameTextureSlot.Espa)}, SMRR={material.HasTexture(MeshPreviewGameTextureSlot.Smrr)}, SpecColor={material.HasTexture(MeshPreviewGameTextureSlot.SpecColor)}");
        sb.AppendLine($"GameApprox Scalars: Ambient={material.LightingAmbient:F2}, ShadowAmbient={material.ShadowAmbientMult:F2}, NormalStrength={material.NormalStrength:F2}, Reflection={material.ReflectionMult:F2}, Rim={material.RimColorMult:F2}, SpecMult={material.SpecMult:F2}, SpecMultLQ={material.SpecMultLq:F2}, SpecPower={material.SpecularPower:F2}, SpecMask={material.SpecularPowerMask:F2}, TwoSidedLighting={material.TwoSidedLighting:F2}");
        sb.AppendLine($"GameApprox Vectors: LambertAmbient={material.LambertAmbient}, Fill={material.FillLightColor}, ShadowAmbientColor={material.ShadowAmbientColor}, SpecularColor={material.SpecularColor}");
    }

    private MeshPreviewSection? GetDiagnosticsSection()
    {
        if (currentPreviewMesh is null)
            return null;

        if (PreviewSectionComboBox.SelectedItem is PreviewSectionOption option)
            return currentPreviewMesh.Sections.FirstOrDefault(section => section.Index == option.SectionIndex);

        return currentPreviewMesh.Sections.FirstOrDefault(static section => section.GameMaterial is not null) ??
               currentPreviewMesh.Sections.FirstOrDefault();
    }

    public void SetWorkspaceMode(string view)
    {
        if (string.IsNullOrWhiteSpace(view))
            return;

        if (string.Equals(view, PreviewView, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(view, ExporterView, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(view, ImporterView, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(view, SectionsView, StringComparison.OrdinalIgnoreCase))
        {
            SetActiveView(view);
            SaveSessionState();
        }
    }

    private void ApplySessionState()
    {
        WorkspaceSessionStore.MeshWorkspaceSession session = WorkspaceSessionStore.Mesh;
        if (!string.IsNullOrWhiteSpace(session.ViewMode))
            currentView = session.ViewMode;

        if (!string.IsNullOrWhiteSpace(session.UpkPath))
            CurrentPathText.Text = session.UpkPath;

        if (!string.IsNullOrWhiteSpace(session.ExportPath))
            exportFbxPath = session.ExportPath;

        if (!string.IsNullOrWhiteSpace(session.ImportPath))
            importFbxPath = session.ImportPath;
    }

    private void SaveSessionState()
    {
        if (suppressSessionWrites)
            return;

        WorkspaceSessionStore.RememberMesh(new WorkspaceSessionStore.MeshWorkspaceSession
        {
            UpkPath = CurrentUpkPath ?? string.Empty,
            ExportPath = exportFbxPath,
            ImportPath = importFbxPath,
            ViewMode = currentView
        });
    }

    private async void BrowseExportFbxButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("FBX File", [".fbx"]);
        picker.SuggestedFileName = currentExport?.ObjectNameIndex?.Name ?? "mesh_export";

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        exportFbxPath = file.Path;
        ExportFbxPathBox.Text = exportFbxPath;
        RefreshWorkflowRows();
        StatusRows.Add($"Selected export FBX path: {exportFbxPath}");
    }

    private async void BrowseImportFbxButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".fbx");

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        importFbxPath = file.Path;
        ImportFbxPathBox.Text = importFbxPath;
        RefreshWorkflowRows();
        StatusRows.Add($"Selected import FBX path: {importFbxPath}");
    }

    private async void ExportFbxButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        exportFbxPath = ExportFbxPathBox.Text?.Trim() ?? string.Empty;
        RefreshWorkflowRows();

        if (currentExport is null || (currentSkeletalMesh is null && currentStaticMesh is null))
        {
            StatusRows.Add("Export FBX skipped: no mesh export is selected yet.");
            return;
        }

        if (string.IsNullOrWhiteSpace(exportFbxPath))
        {
            StatusRows.Add("Export FBX skipped: choose an FBX output path first.");
            return;
        }

        await ExportSelectedMeshAsync();
    }

    private async void ImportMeshButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        importFbxPath = ImportFbxPathBox.Text?.Trim() ?? string.Empty;
        RefreshWorkflowRows();

        if (currentExport is null || (currentSkeletalMesh is null && currentStaticMesh is null))
        {
            StatusRows.Add("Import Mesh skipped: no mesh export is selected yet.");
            return;
        }

        if (string.IsNullOrWhiteSpace(importFbxPath))
        {
            StatusRows.Add("Import Mesh skipped: choose an FBX source path first.");
            return;
        }

        bool replaceAllLods = ReplaceAllLodsCheckBox.IsChecked == true;
        bool confirmed = await ConfirmImportAsync(replaceAllLods).ConfigureAwait(true);
        if (!confirmed)
        {
            StatusRows.Add("Import Mesh canceled.");
            return;
        }

        await ImportSelectedMeshAsync(replaceAllLods).ConfigureAwait(true);
    }

    private void RefreshWorkflowRows()
    {
        if (currentExport is null)
            return;

        exportFbxPath = ExportFbxPathBox.Text?.Trim() ?? exportFbxPath;
        importFbxPath = ImportFbxPathBox.Text?.Trim() ?? importFbxPath;
        if (currentStaticMesh is not null)
            PopulateWorkflowRows(currentExport, currentStaticMesh);
        else if (currentSkeletalMesh is not null)
            PopulateWorkflowRows(currentExport, currentSkeletalMesh);
        RefreshWorkflowLists();
    }

    private void RefreshWorkflowLists()
    {
        ExporterRowsList.ItemsSource = null;
        ExporterRowsList.ItemsSource = ExporterRows;
        ImporterRowsList.ItemsSource = null;
        ImporterRowsList.ItemsSource = ImporterRows;
    }

    private async Task ExportSelectedMeshAsync()
    {
        if (currentExport is null)
            return;

        if (currentSkeletalMesh is null)
        {
            StatusRows.Add("Export FBX skipped: static mesh export is not wired yet.");
            return;
        }

        try
        {
            ExporterRows.Add("Preparing SkeletalMesh export...");
            StatusRows.Add($"Exporting FBX for {currentExport.GetPathName()}");

            string meshName = currentExport.GetPathName();
            int lodIndex = System.Math.Max(0, LodComboBox.SelectedIndex);

            await Task.Run(() =>
            {
                SkeletalFbxExporter.Export(
                    exportFbxPath,
                    currentSkeletalMesh,
                    meshName,
                    lodIndex,
                    message => DispatcherQueue.TryEnqueue(() =>
                    {
                        ExporterRows.Add(message);
                    }));
            }).ConfigureAwait(true);

            StatusRows.Add($"FBX export completed: {exportFbxPath}");
            ExporterRows.Add("FBX export completed.");
        }
        catch (System.Exception ex)
        {
            StatusRows.Add($"FBX export failed: {ex.Message}");
            ExporterRows.Add($"FBX export failed: {ex.Message}");
        }
    }

    private async Task ImportSelectedMeshAsync(bool replaceAllLods)
    {
        if (currentExport is null || currentHeader is null)
            return;

        if (currentSkeletalMesh is null)
        {
            StatusRows.Add("Import Mesh skipped: static mesh import is not wired yet.");
            return;
        }

        try
        {
            string upkPath = currentHeader.FullFilename;
            string meshName = currentExport.GetPathName();
            int lodIndex = System.Math.Max(0, LodComboBox.SelectedIndex);

            ImporterRows.Add("Preparing SkeletalMesh import...");
            StatusRows.Add($"Importing FBX into {meshName}");

            Progress<MeshImportProgress> progress = new(progressUpdate =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    string message = string.IsNullOrWhiteSpace(progressUpdate.Message)
                        ? $"Import progress: {progressUpdate.Value}/{progressUpdate.Maximum}"
                        : progressUpdate.Message;
                    StatusRows.Add(message);
                });
            });

            string backupPath = await Task.Run(() =>
            {
                return MeshPreProcessor.ProcessAndReplaceMesh(
                    upkPath,
                    meshName,
                    importFbxPath,
                    lodIndex,
                    replaceAllLods,
                    progress,
                    message => DispatcherQueue.TryEnqueue(() =>
                    {
                        ImporterRows.Add(message);
                    }));
            }).ConfigureAwait(true);

            StatusRows.Add($"Import Mesh completed. Backup: {backupPath}");
            ImporterRows.Add("Import Mesh completed.");

            await LoadUpkAsync(upkPath, meshName).ConfigureAwait(true);
        }
        catch (System.Exception ex)
        {
            StatusRows.Add($"Import Mesh failed: {ex.Message}");
            ImporterRows.Add($"Import Mesh failed: {ex.Message}");
        }
    }

    private async Task<bool> ConfirmImportAsync(bool replaceAllLods)
    {
        if (currentHeader is null || currentExport is null)
            return false;

        ContentDialog dialog = new()
        {
            Title = "Replace SkeletalMesh in UPK?",
            Content = new TextBlock
            {
                Text = $"UPK: {currentHeader.FullFilename}\nMesh: {currentExport.GetPathName()}\nFBX: {importFbxPath}\nLOD: {System.Math.Max(0, LodComboBox.SelectedIndex)}\nReplace All LODs: {(replaceAllLods ? "Yes" : "No")}\n\nThis will write a backup before replacing the original UPK.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 720
            },
            PrimaryButtonText = "Import Mesh",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot!
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void MeshFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string? preferred = MeshComboBox.SelectedItem as string;
        ApplyMeshFilter(preferred);
    }

    private void ReplaceAllLodsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        importFbxPath = ImportFbxPathBox.Text?.Trim() ?? importFbxPath;
        RefreshWorkflowRows();
    }

    private async void PreviewComboSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (suppressPreviewSettingChanges)
            return;

        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private async void PreviewToggleSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressPreviewSettingChanges)
            return;

        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private async void PreviewSliderSetting_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (suppressPreviewSettingChanges)
            return;

        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private async void PreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        previewSurfaceLoaded = PreviewSwapChainPanel.XamlRoot is not null;
        SyncPreviewSurfaceSize();

        if (string.Equals(PreviewRendererComboBox.SelectedItem as string, "VorticeDirect3D11", StringComparison.Ordinal) &&
            previewSurfaceLoaded)
        {
            d3dPreviewRenderer.AttachToPanel(PreviewSwapChainPanel, DispatcherQueue);
            if (currentPreviewMesh is not null)
                d3dPreviewRenderer.SetFrame(previewScene, previewCamera);
        }

        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private async void PreviewSurface_Loaded(object sender, RoutedEventArgs e)
    {
        previewSurfaceLoaded = true;
        SyncPreviewSurfaceSize();

        if (string.Equals(PreviewRendererComboBox.SelectedItem as string, "VorticeDirect3D11", StringComparison.Ordinal) &&
            previewSurfaceLoaded)
        {
            d3dPreviewRenderer.AttachToPanel(PreviewSwapChainPanel, DispatcherQueue);
            if (currentPreviewMesh is not null)
                d3dPreviewRenderer.SetFrame(previewScene, previewCamera);
        }

        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private async void ResetPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        InitializePreviewControls(resetOnly: true);
        if (currentPreviewMesh is not null)
            previewCamera.Reset(currentPreviewMesh.Center, MathF.Max(1.0f, currentPreviewMesh.Radius));
        ApplyCameraToControls();
        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private async void ResetCameraButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentPreviewMesh is not null)
            previewCamera.Reset(currentPreviewMesh.Center, MathF.Max(1.0f, currentPreviewMesh.Radius));
        ApplyCameraToControls();
        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private void PreviewViewportHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (currentPreviewMesh is null)
            return;

        previewPointerCaptured = true;
        var properties = e.GetCurrentPoint(PreviewViewportHost).Properties;
        previewPanMode = properties.IsRightButtonPressed || properties.IsMiddleButtonPressed;
        lastPreviewPointerPoint = e.GetCurrentPoint(PreviewViewportHost).Position;
        PreviewViewportHost.CapturePointer(e.Pointer);
    }

    private async void PreviewViewportHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!previewPointerCaptured || currentPreviewMesh is null)
            return;

        Point currentPoint = e.GetCurrentPoint(PreviewViewportHost).Position;
        var properties = e.GetCurrentPoint(PreviewViewportHost).Properties;
        previewPanMode = properties.IsRightButtonPressed || properties.IsMiddleButtonPressed;
        float deltaX = (float)(currentPoint.X - lastPreviewPointerPoint.X);
        float deltaY = (float)(currentPoint.Y - lastPreviewPointerPoint.Y);
        lastPreviewPointerPoint = currentPoint;

        if (previewPanMode)
            previewCamera.Pan(deltaX, deltaY);
        else
            previewCamera.Orbit(deltaX, deltaY);

        ApplyCameraToControls();
        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private void PreviewViewportHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!previewPointerCaptured)
            return;

        previewPointerCaptured = false;
        previewPanMode = false;
        PreviewViewportHost.ReleasePointerCaptures();
    }

    private async void PreviewViewportHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        SyncPreviewSurfaceSize();

        if (currentPreviewMesh is null)
            return;

        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private async void PreviewViewportHost_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (currentPreviewMesh is null)
            return;

        int delta = e.GetCurrentPoint(PreviewViewportHost).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        previewCamera.Zoom(delta / 120.0f);
        ApplyCameraToControls();
        await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
    }

    private void ApplyMeshFilter(string? preferredSelection = null)
    {
        string filter = MeshFilterBox.Text?.Trim() ?? string.Empty;
        List<string> filtered = allMeshPaths
            .Where(path => string.IsNullOrWhiteSpace(filter) ||
                           path.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
            .Where(path => IncludeStaticMeshesCheckBox.IsChecked == true ||
                           !meshExportKinds.TryGetValue(path, out MeshExportKind kind) ||
                           kind != MeshExportKind.Static)
            .OrderBy(path => path)
            .ToList();

        MeshComboBox.ItemsSource = filtered;

        if (!string.IsNullOrWhiteSpace(preferredSelection) && filtered.Any(item => string.Equals(item, preferredSelection, System.StringComparison.OrdinalIgnoreCase)))
        {
            MeshComboBox.SelectedItem = preferredSelection;
        }
        else if (filtered.Count > 0)
        {
            MeshComboBox.SelectedIndex = 0;
        }
        else
        {
            MeshComboBox.SelectedIndex = -1;
        }
    }

    private void SectionRowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (currentSkeletalMesh?.LODModels is null || LodComboBox.SelectedIndex < 0 || LodComboBox.SelectedIndex >= currentSkeletalMesh.LODModels.Count)
            return;

        if (SectionRowsList.SelectedIndex < 0)
            return;

        FStaticLODModel lod = currentSkeletalMesh.LODModels[LodComboBox.SelectedIndex];
        if (lod.Sections is null || SectionRowsList.SelectedIndex >= lod.Sections.Count)
            return;

        FSkelMeshSection section = lod.Sections[SectionRowsList.SelectedIndex];
        SectionDetailRows.Clear();
        SectionDetailRows.Add($"Section Index: {SectionRowsList.SelectedIndex}");
        SectionDetailRows.Add($"Material Index: {section.MaterialIndex}");
        SectionDetailRows.Add($"Chunk Index: {section.ChunkIndex}");
        SectionDetailRows.Add($"Triangles: {section.NumTriangles}");
        SectionDetailRows.Add($"Base Index: {section.BaseIndex}");
        SectionDetailRows.Add($"Triangle Sorting: {section.TriangleSorting}");

        if (lod.Chunks is not null && section.ChunkIndex >= 0 && section.ChunkIndex < lod.Chunks.Count)
        {
            FSkelMeshChunk chunk = lod.Chunks[section.ChunkIndex];
            SectionDetailRows.Add($"Chunk Base Vertex: {chunk.BaseVertexIndex}");
            SectionDetailRows.Add($"Chunk Rigid Vertices: {chunk.NumRigidVertices}");
            SectionDetailRows.Add($"Chunk Soft Vertices: {chunk.NumSoftVertices}");
            SectionDetailRows.Add($"Chunk Max Influences: {chunk.MaxBoneInfluences}");
        }
        else if (lod.Chunks is not null)
        {
            SectionDetailRows.Add($"Chunk Index: {section.ChunkIndex} (invalid for this LOD)");
        }
    }

    private void ChunkRowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (currentSkeletalMesh?.LODModels is null || LodComboBox.SelectedIndex < 0 || LodComboBox.SelectedIndex >= currentSkeletalMesh.LODModels.Count)
            return;

        if (ChunkRowsList.SelectedIndex < 0)
            return;

        FStaticLODModel lod = currentSkeletalMesh.LODModels[LodComboBox.SelectedIndex];
        if (lod.Chunks is null || ChunkRowsList.SelectedIndex >= lod.Chunks.Count)
            return;

        FSkelMeshChunk chunk = lod.Chunks[ChunkRowsList.SelectedIndex];
        SectionDetailRows.Clear();
        SectionDetailRows.Add($"Chunk Index: {ChunkRowsList.SelectedIndex}");
        SectionDetailRows.Add($"Base Vertex Index: {chunk.BaseVertexIndex}");
        SectionDetailRows.Add($"Rigid Vertices: {chunk.NumRigidVertices}");
        SectionDetailRows.Add($"Soft Vertices: {chunk.NumSoftVertices}");
        SectionDetailRows.Add($"Max Bone Influences: {chunk.MaxBoneInfluences}");
        SectionDetailRows.Add($"Bone Map Count: {chunk.BoneMap?.Count ?? 0}");

        if (chunk.BoneMap is not null && chunk.BoneMap.Count > 0)
        {
            string preview = string.Join(", ", chunk.BoneMap.Take(12));
            SectionDetailRows.Add($"Bone Map Preview: {preview}");
        }
    }

    private void PreviewViewButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetActiveView(PreviewView);
    private void ExporterViewButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetActiveView(ExporterView);
    private void ImporterViewButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetActiveView(ImporterView);
    private void SectionsViewButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => SetActiveView(SectionsView);
    private void WorkflowsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => WorkflowsService.OpenWorkflowsWindow();

    private async Task RerenderPreviewFromControlsAsync()
    {
        if (!previewControlsInitialized || currentPreviewMesh is null)
            return;

        if (previewRenderInProgress)
        {
            previewRenderPending = true;
            return;
        }

        previewRenderInProgress = true;
        try
        {
            await RenderCurrentPreviewAsync().ConfigureAwait(true);
        }
        finally
        {
            previewRenderInProgress = false;
        }

        if (previewRenderPending)
        {
            previewRenderPending = false;
            await RerenderPreviewFromControlsAsync().ConfigureAwait(true);
        }
    }

    private static void LogMeshError(string stage, System.Exception ex)
    {
        try
        {
            File.AppendAllText(MeshErrorLogPath,
                $"[{DateTime.Now:O}] {stage}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void InitializePreviewControls(bool resetOnly = false)
    {
        previewControlsInitialized = false;

        PreviewDisplayModeComboBox.ItemsSource = Enum.GetNames<MeshPreviewDisplayMode>();
        PreviewShadingModeComboBox.ItemsSource = Enum.GetNames<MeshPreviewShadingMode>();
        PreviewBackgroundComboBox.ItemsSource = Enum.GetNames<MeshPreviewBackgroundStyle>();
        PreviewLightingComboBox.ItemsSource = Enum.GetNames<MeshPreviewLightingPreset>();
        PreviewMaterialChannelComboBox.ItemsSource = Enum.GetNames<MeshPreviewMaterialChannel>();
        PreviewWeightViewComboBox.ItemsSource = Enum.GetNames<MeshPreviewWeightViewMode>();
        PreviewSectionFocusModeComboBox.ItemsSource = Enum.GetNames<MeshPreviewSectionFocusMode>();
        PreviewRendererComboBox.ItemsSource = new[] { "VorticeDirect3D11", "OpenTK", "WinUI.NativePreview" };

        PreviewRendererComboBox.SelectedItem = "VorticeDirect3D11";
        PreviewDisplayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Overlay);
        PreviewShadingModeComboBox.SelectedItem = nameof(MeshPreviewShadingMode.GameApprox);
        PreviewBackgroundComboBox.SelectedItem = nameof(MeshPreviewBackgroundStyle.DarkGradient);
        PreviewLightingComboBox.SelectedItem = nameof(MeshPreviewLightingPreset.Neutral);
        PreviewMaterialChannelComboBox.SelectedItem = nameof(MeshPreviewMaterialChannel.BaseColor);
        PreviewWeightViewComboBox.SelectedItem = nameof(MeshPreviewWeightViewMode.SelectedBoneHeatmap);
        PreviewSectionFocusModeComboBox.SelectedItem = nameof(MeshPreviewSectionFocusMode.None);
        PreviewShowFbxCheckBox.IsChecked = true;
        PreviewShowUe3CheckBox.IsChecked = true;
        PreviewWireframeCheckBox.IsChecked = false;
        PreviewShowBonesCheckBox.IsChecked = false;
        PreviewShowBoneNamesCheckBox.IsChecked = false;
        PreviewShowWeightsCheckBox.IsChecked = false;
        PreviewShowSectionsCheckBox.IsChecked = false;
        PreviewShowNormalsCheckBox.IsChecked = false;
        PreviewShowTangentsCheckBox.IsChecked = false;
        PreviewShowUvSeamsCheckBox.IsChecked = false;
        PreviewShowGroundCheckBox.IsChecked = true;

        if (!resetOnly)
        {
            PreviewBoneComboBox.ItemsSource = Array.Empty<string>();
            PreviewSectionComboBox.ItemsSource = Array.Empty<PreviewSectionOption>();
        }

        previewControlsInitialized = true;
    }

    private void PopulatePreviewControlChoices(USkeletalMesh? skeletalMesh = null)
    {
        suppressPreviewSettingChanges = true;
        try
        {
            IEnumerable<string> boneNames = [];
            if (skeletalMesh?.RefSkeleton is not null)
            {
                boneNames = boneNames.Concat(skeletalMesh.RefSkeleton
                    .Select(bone => bone.Name?.ToString() ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
            }

            if (previewScene.FbxMesh?.Bones is not null)
            {
                boneNames = boneNames.Concat(previewScene.FbxMesh.Bones
                    .Select(bone => bone.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
            }

            if (skeletalMesh is null && previewScene.Ue3Mesh?.Bones is not null)
            {
                boneNames = boneNames.Concat(previewScene.Ue3Mesh.Bones
                    .Select(bone => bone.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
            }

            PreviewBoneComboBox.ItemsSource = boneNames
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .Take(64)
                .ToList();

            if (PreviewBoneComboBox.SelectedIndex < 0)
                PreviewBoneComboBox.SelectedIndex = -1;
        }
        finally
        {
            suppressPreviewSettingChanges = false;
        }
    }

    private void RefreshPreviewSectionChoices()
    {
        suppressPreviewSettingChanges = true;
        try
        {
            object? selected = PreviewSectionComboBox.SelectedItem;
            List<PreviewSectionOption> options = [new PreviewSectionOption(MeshPreviewSectionFocusMesh.None, -1, "(All Sections)")];

            if (previewScene.FbxMesh is not null)
            {
                options.AddRange(previewScene.FbxMesh.Sections
                    .Select(section => new PreviewSectionOption(MeshPreviewSectionFocusMesh.Fbx, section.Index, $"FBX: {section.Name}")));
            }

            if (previewScene.Ue3Mesh is not null)
            {
                options.AddRange(previewScene.Ue3Mesh.Sections
                    .Select(section => new PreviewSectionOption(MeshPreviewSectionFocusMesh.Ue3, section.Index, $"UE3: {section.Name}")));
            }

            PreviewSectionComboBox.ItemsSource = options;
            if (selected is PreviewSectionOption selectedOption)
            {
                PreviewSectionOption? match = options.FirstOrDefault(option =>
                    option.Mesh == selectedOption.Mesh &&
                    option.SectionIndex == selectedOption.SectionIndex);

                PreviewSectionComboBox.SelectedItem = match ?? options[0];
            }
            else if (PreviewSectionComboBox.SelectedIndex < 0)
            {
                PreviewSectionComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            suppressPreviewSettingChanges = false;
        }
    }

    private void SyncPreviewCamera(MeshPreviewMesh mesh)
    {
        ApplyCameraFromControls(mesh, IsD3dPreviewRendererSelected());
        ApplyCameraToControls();
    }

    private void ApplyCameraFromControls(MeshPreviewMesh mesh, bool useUe3OnlyBounds = false)
    {
        Vector3 center = mesh.Center;
        float radius = MathF.Max(1.0f, mesh.Radius);
        if (TryGetPreviewSceneBounds(out Vector3 sceneCenter, out float sceneRadius, useUe3OnlyBounds))
        {
            center = sceneCenter;
            radius = sceneRadius;
        }

        previewCamera.Reset(center, radius);
        previewCamera.Orbit(45.0f - DefaultPreviewYaw, 20.0f - DefaultPreviewPitch);
        float targetDistance = radius * Math.Max(1.35f, DefaultPreviewZoom);
        float wheelSteps = (1.0f - (targetDistance / previewCamera.Distance)) / 0.1f;
        previewCamera.Zoom(wheelSteps);
    }

    private bool TryGetPreviewSceneBounds(out Vector3 center, out float radius, bool useUe3OnlyBounds = false)
    {
        List<MeshPreviewMesh> meshes = [];
        if (previewScene.Ue3Mesh is not null)
            meshes.Add(previewScene.Ue3Mesh);
        if (!useUe3OnlyBounds && previewScene.FbxMesh is not null)
            meshes.Add(previewScene.FbxMesh);

        if (meshes.Count == 0)
        {
            center = Vector3.Zero;
            radius = 1.0f;
            return false;
        }

        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (MeshPreviewMesh mesh in meshes)
        {
            foreach (MeshPreviewVertex vertex in mesh.Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }
        }

        center = (min + max) * 0.5f;
        Vector3 extents = (max - min) * 0.5f;
        radius = MathF.Max(1.0f, extents.Length());
        return true;
    }

    private bool IsD3dPreviewRendererSelected()
    {
        return string.Equals(PreviewRendererComboBox.SelectedItem as string, "VorticeDirect3D11", StringComparison.Ordinal);
    }

    private void ApplyCameraToControls()
    {
        // Camera sliders were removed from the WinUI layout; keep the camera state internal.
    }

    private void UpdateBoneNameOverlay()
    {
        if (PreviewBoneNamesCanvas is null)
            return;

        PreviewBoneNamesCanvas.Children.Clear();

        if (!previewControlsInitialized || currentPreviewMesh is null || currentPreviewMesh.Bones.Count == 0 || PreviewShowBoneNamesCheckBox.IsChecked != true)
        {
            PreviewBoneNamesCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        double width = PreviewViewportHost.ActualWidth;
        double height = PreviewViewportHost.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            PreviewBoneNamesCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        Matrix4x4 view = previewCamera.GetViewMatrix();
        Matrix4x4 projection = previewCamera.GetProjectionMatrix((float)(width / Math.Max(1.0, height)));
        int labelsAdded = 0;

        foreach (MeshPreviewBone bone in currentPreviewMesh.Bones)
        {
            Vector4 clip = Vector4.Transform(Vector4.Transform(new Vector4(bone.GlobalTransform.Translation, 1.0f), view), projection);
            if (clip.W <= 0.0001f)
                continue;

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            if (ndcX < -1.1f || ndcX > 1.1f || ndcY < -1.1f || ndcY > 1.1f)
                continue;

            Border label = new()
            {
                IsHitTestVisible = false,
                Background = new SolidColorBrush(ColorHelper.FromArgb(170, 12, 14, 18)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(170, 120, 126, 134)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = bone.Name,
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 235, 239, 244)),
                    IsHitTestVisible = false,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 150
                }
            };

            Canvas.SetLeft(label, ((ndcX + 1.0f) * 0.5f * (float)width) + 8.0f);
            Canvas.SetTop(label, ((1.0f - ndcY) * 0.5f * (float)height) - 18.0f);
            PreviewBoneNamesCanvas.Children.Add(label);
            labelsAdded++;

            if (labelsAdded >= 40)
                break;
        }

        PreviewBoneNamesCanvas.Visibility = labelsAdded > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePreviewHeader(string rendererName)
    {
        PreviewMeshNameText.Text = currentExport?.ObjectNameIndex?.Name ?? "Mesh Preview";
        PreviewMeshStatsText.Text = BuildPreviewStatsText(rendererName);
    }

    private string BuildPreviewStatsText(string rendererName)
    {
        if (currentPreviewMesh is null)
            return "Idle";

        return $"Ready  |  Verts: {currentPreviewMesh.Vertices.Count:N0}  |  Tris: {currentPreviewMesh.Indices.Count / 3:N0}  |  Bones: {currentPreviewMesh.Bones.Count:N0}  |  {rendererName}";
    }

    private static TEnum GetSelectedEnum<TEnum>(ComboBox comboBox, TEnum fallback)
        where TEnum : struct
    {
        if (comboBox.SelectedItem is string value && Enum.TryParse<TEnum>(value, out TEnum parsed))
            return parsed;

        return fallback;
    }

    private static string GetPreviewStatusText(string rendererName, bool lastRenderSucceeded, string diagnostics)
    {
        if (!string.Equals(rendererName, "VorticeDirect3D11", System.StringComparison.Ordinal))
            return string.Empty;

        if (lastRenderSucceeded)
            return string.Empty;

        if (diagnostics.StartsWith("D3D11 renderer not initialized", System.StringComparison.Ordinal) ||
            diagnostics.StartsWith("D3D11 waiting for usable panel size", System.StringComparison.Ordinal))
        {
            return "Rendering native WinUI preview...";
        }

        return diagnostics;
    }

    private sealed record PreviewSectionOption(MeshPreviewSectionFocusMesh Mesh, int SectionIndex, string Label)
    {
        public override string ToString() => Label;
    }

    private void MeshPage_Unloaded(object sender, RoutedEventArgs e)
    {
        previewSurfaceLoaded = false;
        d3dPreviewRenderer.DetachPanel();
    }

    private void D3dPreviewRenderer_RenderCompleted(object? sender, EventArgs e)
    {
        if (!IsD3dPreviewRendererSelected() || !d3dPreviewRenderer.LastRenderSucceeded)
            return;

        UpdateBoneNameOverlay();
        PreviewStatusText.Text = string.Empty;
    }
}

