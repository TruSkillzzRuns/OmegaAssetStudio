using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Windows.Foundation;
using OmegaAssetStudio.WinUI;
using OmegaAssetStudio.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UpkManager.Constants;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Types;
using UpkManager.Repository;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Xaml.Navigation;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class ObjectsPage : Page
{
    private readonly UpkFileRepository repository = new();
    private static readonly string ObjectsLogPath = RuntimeLogPaths.ObjectsLogPath;
    private UnrealHeader? currentHeader;
    private ObjectTreeItem? currentSelection;
    private UnrealImportTableEntry? currentImportSelection;
    private UnrealExportTableEntry? currentExportSelection;
    private string currentFilter = string.Empty;
    private string currentPropertyFilter = string.Empty;
    private PropertyTreeItem? currentPropertySelection;
    private List<PropertyTreeItem> propertyTreeSource = [];

    public ObservableCollection<ObjectTreeItem> RootItems { get; } = [];
    public ObservableCollection<string> FileRows { get; } = [];
    public ObservableCollection<string> NameRows { get; } = [];
    public ObservableCollection<string> ImportRows { get; } = [];
    public ObservableCollection<string> ExportRows { get; } = [];
    public ObservableCollection<string> PropertyRows { get; } = [];
    public ObservableCollection<PropertyTreeItem> PropertyTreeRoots { get; } = [];
    public ObservableCollection<string> PropertyDetailRows { get; } = [];

    public ObjectsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        FileRows.Add("Open a UPK to inspect package header details.");
        PropertyRows.Add("Open a UPK to inspect file, import, export, and name-table data.");
        PropertyDetailRows.Add("Select a parsed property node to inspect it here.");
        UpdateActionButtons();
    }

    private void ObjectsPaneWidthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ObjectsPaneColumn is null)
            return;

        ObjectsPaneColumn.Width = new GridLength(e.NewValue);
    }

    private async void OpenUpkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
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
        catch (System.Exception ex)
        {
            LogObjectsError("OpenUpkButton_Click", ex);
            InspectorTitle.Text = "Objects Load Failed";
            InspectorSubtitle.Text = $"Failed to load objects from the selected UPK: {ex.Message}";
            FileRows.Clear();
            FileRows.Add("Failed to load package.");
            PropertyRows.Clear();
            PropertyRows.Add(ex.ToString());
            ClearPropertyTree();
            ResetPropertyDetails();
        }
    }

    private async Task LoadUpkAsync(string path)
    {
        try
        {
            CurrentPathText.Text = "Loading...";
            InspectorTitle.Text = "Inspector";
            InspectorSubtitle.Text = "Reading package header, imports, exports, and names...";

            UnrealHeader header = await repository.LoadUpkFile(path);
            await header.ReadHeaderAsync(null);

            currentHeader = header;
            CurrentPathText.Text = path;
            RecentUpkSession.RecordUpk(path, "objects", title: Path.GetFileName(path), summary: $"Objects workspace load: Names={header.NameTable.Count:N0}, Imports={header.ImportTable.Count:N0}, Exports={header.ExportTable.Count:N0}");

            PopulateFileRows(header, path);
            PopulateLists(header);
            RebuildTree();
            currentSelection = null;
            currentImportSelection = null;
            currentExportSelection = null;
            UpdateActionButtons();

            InspectorTitle.Text = Path.GetFileName(path);
            InspectorSubtitle.Text = "Package loaded. Select an import or export on the left to inspect summary details.";
            PropertyRows.Clear();
            PropertyRows.Add($"File: {Path.GetFileName(path)}");
            PropertyRows.Add($"Names: {header.NameTable.Count:N0}");
            PropertyRows.Add($"Imports: {header.ImportTable.Count:N0}");
            PropertyRows.Add($"Exports: {header.ExportTable.Count:N0}");
            ClearPropertyTree();
            ResetPropertyDetails();
        }
        catch (System.Exception ex)
        {
            LogObjectsError($"LoadUpkAsync({path})", ex);
            throw;
        }
    }

    private void PopulateFileRows(UnrealHeader header, string path)
    {
        FileRows.Clear();
        FileRows.Add($"UPK Path: {path}");
        FileRows.Add($"Names: {header.NameTable.Count:N0}");
        FileRows.Add($"Imports: {header.ImportTable.Count:N0}");
        FileRows.Add($"Exports: {header.ExportTable.Count:N0}");
        FileRows.Add($"Version: {header.Version}");
        FileRows.Add($"Licensee Version: {header.Licensee}");
        FileRows.Add($"Compression: {(CompressionTypes)header.CompressionFlags}");
        FileRows.Add($"Package Flags: {(EPackageFlags)header.Flags}");
        FileRows.Add($"Engine Version: {header.EngineVersion}");
        FileRows.Add($"Cooker Version: {header.CookerVersion}");
        FileRows.Add($"GUID: {new Guid(header.Guid)}");
        FileRows.Add($"Package Source: 0x{header.PackageSource:X8}");
        FileRows.Add($"Thumbnail Table Offset: {header.ThumbnailTableOffset}");
        FileRows.Add($"Additional Packages To Cook: {header.AdditionalPackagesToCook.Count:N0}");
        FileRows.Add($"Depends Count: {header.DependsTable.Count:N0}");
        FileRows.Add($"Generations: {header.GenerationTable.Count:N0}");
    }

    private void PopulateLists(UnrealHeader header)
    {
        NameRows.Clear();
        foreach (var entry in header.NameTable.Take(500))
            NameRows.Add($"[{entry.TableIndex}] {entry.Name.String} :: Flags={(EObjectFlags)entry.Flags}");

        ImportRows.Clear();
        foreach (var entry in header.ImportTable.Take(500))
        {
            ImportRows.Add(
                $"[{entry.TableIndex}] {entry.GetPathName()} ::{entry.ClassNameIndex.Name} | Package={entry.PackageNameIndex?.Name} | Outer={entry.OuterReferenceNameIndex?.Name ?? "(root)"}");
        }

        ExportRows.Clear();
        foreach (var entry in header.ExportTable.Take(500))
        {
            string className = entry.ClassReferenceNameIndex?.Name ?? "Unknown";
            string superName = entry.SuperReferenceNameIndex?.Name ?? "(none)";
            string flags = $"{(EObjectFlags)entry.ObjectFlags}";
            ExportRows.Add(
                $"[{entry.TableIndex}] {entry.GetPathName()} {superName}::{className} | SerialSize={entry.SerialDataSize:N0} | Flags={flags}");
        }

        ResetContextualTabs(header);
    }

    private void RebuildTree()
    {
        RootItems.Clear();

        if (currentHeader is null)
        {
            RefreshTreeView();
            return;
        }

        RootItems.Add(BuildExportRoot(currentHeader));
        RootItems.Add(BuildImportRoot(currentHeader));
        RefreshTreeView();
    }

    private ObjectTreeItem BuildImportRoot(UnrealHeader header)
    {
        var nodes = header.ImportTable.ToDictionary(
            entry => entry.TableIndex,
            entry => new ObjectTreeItem
            {
                Name = $"{entry.ObjectNameIndex.Name} [{entry.TableIndex}]",
                Kind = $"::{entry.ClassNameIndex.Name}",
                PathName = entry.GetPathName(),
                TableIndex = entry.TableIndex,
                IsImport = true
            });

        var root = new ObjectTreeItem
        {
            Name = "Imports",
            Kind = $"{header.ImportTable.Count:N0} package dependencies"
        };

        foreach (var entry in header.ImportTable)
        {
            if (!MatchesFilter(entry.ObjectNameIndex.Name, entry.ClassNameIndex.Name, entry.GetPathName()))
                continue;

            var node = nodes[entry.TableIndex];
            if (entry.OuterReference == 0)
                root.Children.Add(node);
            else if (nodes.TryGetValue(entry.OuterReference, out var parent))
                parent.Children.Add(node);
            else
                root.Children.Add(node);
        }

        return root;
    }

    private ObjectTreeItem BuildExportRoot(UnrealHeader header)
    {
        var nodes = header.ExportTable.ToDictionary(
            entry => entry.TableIndex,
            entry => new ObjectTreeItem
            {
                Name = $"{entry.ObjectNameIndex.Name} [{entry.TableIndex}]",
                Kind = $"{entry.SuperReferenceNameIndex?.Name}::{entry.ClassReferenceNameIndex?.Name}",
                PathName = entry.GetPathName(),
                TableIndex = entry.TableIndex,
                IsExport = true
            });

        var root = new ObjectTreeItem
        {
            Name = "Exports",
            Kind = $"{header.ExportTable.Count:N0} runtime objects"
        };

        foreach (var entry in header.ExportTable)
        {
            string className = entry.ClassReferenceNameIndex?.Name ?? string.Empty;
            if (!MatchesFilter(entry.ObjectNameIndex.Name, className, entry.GetPathName()))
                continue;

            var node = nodes[entry.TableIndex];
            if (entry.OuterReference == 0)
                root.Children.Add(node);
            else if (nodes.TryGetValue(entry.OuterReference, out var parent))
                parent.Children.Add(node);
            else
                root.Children.Add(node);
        }

        return root;
    }

    private bool MatchesFilter(params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(currentFilter))
            return true;

        return values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                                   value.Contains(currentFilter, System.StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshTreeView()
    {
        ObjectsTree.RootNodes.Clear();
        foreach (ObjectTreeItem item in RootItems)
            ObjectsTree.RootNodes.Add(CreateNode(item, [], isRoot: true));
    }

    private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        currentFilter = sender.Text?.Trim() ?? string.Empty;
        RebuildTree();
    }

    private async void ObjectsTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedNode?.Content is not ObjectTreeItem item)
            return;

        currentSelection = item;
        currentImportSelection = null;
        currentExportSelection = null;
        InspectorTitle.Text = item.Name;
        InspectorSubtitle.Text = item.Kind;

        PropertyRows.Clear();
        ClearPropertyTree();
        ResetPropertyDetails();
        PropertyRows.Add($"Name: {item.Name}");
        PropertyRows.Add($"Kind: {item.Kind}");
        if (!string.IsNullOrWhiteSpace(item.PathName))
            PropertyRows.Add($"Path: {item.PathName}");
        if (item.TableIndex != 0)
            PropertyRows.Add($"Table Index: {item.TableIndex}");

        if (currentHeader is not null)
        {
            PropertyRows.Add($"Package: {Path.GetFileName(currentHeader.FullFilename)}");

            if (item.IsImport)
            {
                UnrealImportTableEntry? import = currentHeader.ImportTable.FirstOrDefault(entry => entry.TableIndex == item.TableIndex);
                if (import is not null)
                {
                    currentImportSelection = import;
                    PropertyRows.Add($"Type: Import");
                    PropertyRows.Add($"Class: ::{import.ClassNameIndex.Name}");
                    PropertyRows.Add($"Outer: {import.OuterReferenceNameIndex?.Name ?? "(root)"}");
                    PropertyRows.Add($"Package Name: {import.PackageNameIndex.Name}");
                    PopulateImportContext(import);
                }
            }
            else if (item.IsExport)
            {
                UnrealExportTableEntry? export = currentHeader.ExportTable.FirstOrDefault(entry => entry.TableIndex == item.TableIndex);
                if (export is not null)
                {
                    currentExportSelection = export;
                    if (export.UnrealObject == null)
                        await export.ParseUnrealObject(false, false);

                    PropertyRows.Add("Type: Export");
                    PropertyRows.Add($"Class: {export.ClassReferenceNameIndex?.Name ?? "Unknown"}");
                    PropertyRows.Add($"Super: {export.SuperReferenceNameIndex?.Name ?? "(none)"}");
                    PropertyRows.Add($"Outer: {export.OuterReferenceNameIndex?.Name ?? "(root)"}");
                    PropertyRows.Add($"Archetype: {export.ArchetypeReferenceNameIndex?.Name ?? "(none)"}");
                    PropertyRows.Add($"Serial Size: {export.SerialDataSize:N0}");
                    PropertyRows.Add($"Serial Offset: {export.SerialDataOffset:N0}");
                    PropertyRows.Add($"Flags: 0x{export.ObjectFlags:X}");
                    PropertyRows.Add($"Export Flags: 0x{export.ExportFlags:X}");
                    PropertyRows.Add($"Package Flags: 0x{export.PackageFlags:X}");
                    PropertyRows.Add($"Package Guid: {new Guid(export.PackageGuid)}");
                    PropertyRows.Add($"Net Object Count: {export.NetObjects.Count:N0}");
                    if (export.UnrealObject is IUnrealObject unrealObject)
                    {
                        PropertyRows.Add($"Parsed Unreal Object: {export.UnrealObject.GetType().Name}");
                        PopulatePropertyTree(unrealObject);
                    }
                    PopulateExportContext(export);
                }
            }
        }

        UpdateActionButtons();
    }

    private void PopulatePropertyTree(IUnrealObject unrealObject)
    {
        propertyTreeSource.Clear();

        foreach (VirtualNode fieldNode in unrealObject.FieldNodes)
        {
            PropertyTreeItem item = ConvertVirtualNode(fieldNode);
            propertyTreeSource.Add(item);
        }

        UBuffer buffer = unrealObject.Buffer;
        if (!buffer.IsAbstractClass && (buffer.ResultProperty != ResultProperty.None || buffer.DataSize != 0))
        {
            PropertyTreeItem dataItem = new()
            {
                Text = $"Data [{buffer.ResultProperty}][{buffer.DataSize}]"
            };
            propertyTreeSource.Add(dataItem);
        }

        RefreshPropertyTreeView();
    }

    private void ClearPropertyTree()
    {
        propertyTreeSource.Clear();
        PropertyTreeRoots.Clear();
        PropertiesTree.RootNodes.Clear();
        currentPropertyFilter = string.Empty;
        PropertyFilterBox.Text = string.Empty;
        currentPropertySelection = null;
        CopyPropertyButton.IsEnabled = false;
    }

    private void UpdateActionButtons()
    {
        bool hasSelection = currentSelection is not null;
        bool hasExport = currentExportSelection is not null;
        bool hasParsedExport = currentExportSelection?.UnrealObject is IUnrealObject;
        bool hasMesh = currentExportSelection?.UnrealObject is IUnrealObject { UObject: USkeletalMesh or UStaticMesh };

        CopyPathButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(currentSelection?.PathName);
        OpenHexButton.IsEnabled = hasParsedExport;
        MeshActionButton.IsEnabled = hasMesh;
    }

    private async void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentSelection?.PathName))
            return;

        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(currentSelection.PathName);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        await ShowInfoDialogAsync("Path Copied", currentSelection.PathName);
    }

    private async void OpenHexButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentExportSelection?.UnrealObject is not IUnrealObject unrealObject)
            return;

        byte[] data = unrealObject.Buffer.Reader.GetBytes();
        string safeName = SanitizeFilename(currentExportSelection.ObjectNameIndex?.Name ?? $"export_{currentExportSelection.TableIndex}");
        HexEditorWorkspace.LoadRawBytes(data, $"{safeName}_{currentExportSelection.TableIndex}");
        WorkspaceTabs.SelectedItem = HexEditorTab;
        await Task.CompletedTask;
    }

    private void MeshActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentExportSelection?.UnrealObject is not IUnrealObject unrealObject)
            return;

        if (App.MainWindow is null || currentExportSelection is null)
            return;

        if (unrealObject.UObject is USkeletalMesh skeletalMesh)
        {
            WorkspaceLaunchContext context = new()
            {
                WorkspaceTag = "mesh",
                Title = currentExportSelection.ObjectNameIndex?.Name ?? "SkeletalMesh",
                UpkPath = currentHeader?.FullFilename ?? string.Empty,
                ExportPath = currentExportSelection.GetPathName(),
                ObjectType = "USkeletalMesh",
                Summary =
                    $"Materials={skeletalMesh.Materials?.Count ?? 0}, Sockets={skeletalMesh.Sockets?.Count ?? 0}, " +
                    $"LODModels={skeletalMesh.LODModels?.Count ?? 0}, Bones={skeletalMesh.RefSkeleton?.Count ?? 0}, Depth={skeletalMesh.SkeletalDepth}"
            };

            App.MainWindow.NavigateToTag("mesh", context);
            return;
        }

        if (unrealObject.UObject is UStaticMesh staticMesh)
        {
            WorkspaceLaunchContext context = new()
            {
                WorkspaceTag = "mesh",
                Title = currentExportSelection.ObjectNameIndex?.Name ?? "StaticMesh",
                UpkPath = currentHeader?.FullFilename ?? string.Empty,
                ExportPath = currentExportSelection.GetPathName(),
                ObjectType = "UStaticMesh",
                Summary =
                    $"LODModels={staticMesh.LODModels?.Count ?? 0}, Bounds={staticMesh.Bounds}, " +
                    $"LightMapCoordinateIndex={staticMesh.LightMapCoordinateIndex}, LightMapResolution={staticMesh.LightMapResolution}"
            };

            App.MainWindow.NavigateToTag("mesh", context);
        }
    }

    private void ResetContextualTabs(UnrealHeader header)
    {
        NamesTabSubtitle.Text = $"Package name table preview. Showing up to {Math.Min(header.NameTable.Count, 500):N0} rows.";
        ImportsTabSubtitle.Text = $"Package import table preview. Showing up to {Math.Min(header.ImportTable.Count, 500):N0} rows.";
        ExportsTabSubtitle.Text = $"Package export table preview. Showing up to {Math.Min(header.ExportTable.Count, 500):N0} rows.";
    }

    private void PopulateImportContext(UnrealImportTableEntry import)
    {
        if (currentHeader is null)
            return;

        NameRows.Clear();
        AddNameRow("Object Name", import.ObjectNameIndex?.Name);
        AddNameRow("Class Name", import.ClassNameIndex?.Name);
        AddNameRow("Package Name", import.PackageNameIndex?.Name);
        AddNameRow("Outer Name", import.OuterReferenceNameIndex?.Name);
        NamesTabSubtitle.Text = $"Names related to import [{import.TableIndex}] {import.ObjectNameIndex?.Name}.";

        ImportRows.Clear();
        ImportRows.Add($"Current Import: [{import.TableIndex}] {import.GetPathName()} ::{import.ClassNameIndex?.Name ?? "Unknown"}");
        ImportRows.Add($"Package: {import.PackageNameIndex?.Name ?? "Unknown"}");
        ImportRows.Add($"Outer Reference: {ResolveReferenceLabel(import.OuterReference)}");
        foreach (var childImport in currentHeader.ImportTable.Where(entry => entry.OuterReference == import.TableIndex).Take(100))
            ImportRows.Add($"Child Import: [{childImport.TableIndex}] {childImport.GetPathName()} ::{childImport.ClassNameIndex.Name}");
        ImportsTabSubtitle.Text = $"Selected import plus child imports that live under it.";

        ExportRows.Clear();
        foreach (var childExport in currentHeader.ExportTable.Where(entry => entry.OuterReference == import.TableIndex).Take(100))
        {
            string className = childExport.ClassReferenceNameIndex?.Name ?? "Unknown";
            ExportRows.Add($"Child Export: [{childExport.TableIndex}] {childExport.GetPathName()} ::{className}");
        }

        if (ExportRows.Count == 0)
            ExportRows.Add("No child exports found for this import.");
        ExportsTabSubtitle.Text = $"Exports whose outer reference points at the selected import.";
    }

    private void PopulateExportContext(UnrealExportTableEntry export)
    {
        if (currentHeader is null)
            return;

        NameRows.Clear();
        AddNameRow("Object Name", export.ObjectNameIndex?.Name);
        AddNameRow("Class Name", export.ClassReferenceNameIndex?.Name);
        AddNameRow("Super Name", export.SuperReferenceNameIndex?.Name);
        AddNameRow("Outer Name", export.OuterReferenceNameIndex?.Name);
        AddNameRow("Archetype Name", export.ArchetypeReferenceNameIndex?.Name);
        NamesTabSubtitle.Text = $"Names related to export [{export.TableIndex}] {export.ObjectNameIndex?.Name}.";

        ImportRows.Clear();
        AddReferenceRow(ImportRows, "Class Reference", export.ClassReference);
        AddReferenceRow(ImportRows, "Super Reference", export.SuperReference);
        AddReferenceRow(ImportRows, "Outer Reference", export.OuterReference);
        AddReferenceRow(ImportRows, "Archetype Reference", export.ArchetypeReference);
        if (ImportRows.Count == 0)
            ImportRows.Add("No import/export references resolved for this export.");
        ImportsTabSubtitle.Text = $"References used by the selected export.";

        ExportRows.Clear();
        foreach (var childExport in currentHeader.ExportTable.Where(entry => entry.OuterReference == export.TableIndex).Take(100))
        {
            string className = childExport.ClassReferenceNameIndex?.Name ?? "Unknown";
            ExportRows.Add($"Child Export: [{childExport.TableIndex}] {childExport.GetPathName()} ::{className}");
        }

        if (ExportRows.Count == 0)
            ExportRows.Add("No child exports found for this export.");
        ExportsTabSubtitle.Text = $"Exports nested under the selected export.";
    }

    private void AddNameRow(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            NameRows.Add($"{label}: {value}");
    }

    private void AddReferenceRow(ObservableCollection<string> rows, string label, int reference)
    {
        if (reference == 0)
            return;

        rows.Add($"{label}: {ResolveReferenceLabel(reference)}");
    }

    private string ResolveReferenceLabel(int reference)
    {
        if (currentHeader is null || reference == 0)
            return "(root)";

        UnrealImportTableEntry? import = currentHeader.ImportTable.FirstOrDefault(entry => entry.TableIndex == reference);
        if (import is not null)
            return $"Import [{import.TableIndex}] {import.GetPathName()} ::{import.ClassNameIndex.Name}";

        UnrealExportTableEntry? export = currentHeader.ExportTable.FirstOrDefault(entry => entry.TableIndex == reference);
        if (export is not null)
        {
            string className = export.ClassReferenceNameIndex?.Name ?? "Unknown";
            return $"Export [{export.TableIndex}] {export.GetPathName()} ::{className}";
        }

        return $"Reference {reference}";
    }

    private static TreeViewNode CreateNode(ObjectTreeItem item, HashSet<string> ancestry, bool isRoot = false)
    {
        string key = $"{item.TableIndex}:{item.IsImport}:{item.IsExport}:{item.Name}";
        TreeViewNode node = new()
        {
            Content = item,
            IsExpanded = isRoot
        };

        if (!ancestry.Add(key))
        {
            node.Children.Add(new TreeViewNode
            {
                Content = new ObjectTreeItem
                {
                    Name = "[cycle omitted]",
                    Kind = "Detected cyclical outer-reference chain"
                }
            });
            return node;
        }

        foreach (ObjectTreeItem child in item.Children)
        {
            node.Children.Add(CreateNode(child, [.. ancestry]));
        }

        return node;
    }

    private static PropertyTreeItem ConvertVirtualNode(VirtualNode node)
    {
        PropertyTreeItem item = new()
        {
            Text = node.Text,
            Tag = node.Tag
        };

        foreach (VirtualNode child in node.Children)
            item.Children.Add(ConvertVirtualNode(child));

        return item;
    }

    private static TreeViewNode CreatePropertyNode(PropertyTreeItem item)
    {
        TreeViewNode node = new()
        {
            Content = item,
            IsExpanded = item.Children.Count is > 0 and < 11 || string.Equals(item.Text, "Properties", System.StringComparison.Ordinal)
        };

        foreach (PropertyTreeItem child in item.Children)
            node.Children.Add(CreatePropertyNode(child));

        return node;
    }

    private void RefreshPropertyTreeView()
    {
        PropertyTreeRoots.Clear();
        PropertiesTree.RootNodes.Clear();

        IEnumerable<PropertyTreeItem> source = propertyTreeSource;
        if (!string.IsNullOrWhiteSpace(currentPropertyFilter))
        {
            source = propertyTreeSource
                .Select(item => FilterPropertyTreeItem(item, currentPropertyFilter))
                .Where(item => item is not null)!;
        }

        foreach (PropertyTreeItem item in source)
        {
            PropertyTreeRoots.Add(item);
            PropertiesTree.RootNodes.Add(CreatePropertyNode(item));
        }
    }

    private static PropertyTreeItem? FilterPropertyTreeItem(PropertyTreeItem item, string filter)
    {
        List<PropertyTreeItem> matchingChildren = item.Children
            .Select(child => FilterPropertyTreeItem(child, filter))
            .Where(child => child is not null)!
            .Cast<PropertyTreeItem>()
            .ToList();

        bool matchesSelf = item.Text.Contains(filter, System.StringComparison.OrdinalIgnoreCase);
        if (!matchesSelf && matchingChildren.Count == 0)
            return null;

        PropertyTreeItem clone = new()
        {
            Text = item.Text,
            Tag = item.Tag
        };

        foreach (PropertyTreeItem child in matchingChildren)
            clone.Children.Add(child);

        return clone;
    }

    private void PropertyFilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        currentPropertyFilter = sender.Text?.Trim() ?? string.Empty;
        RefreshPropertyTreeView();
    }

    private void PropertiesTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        currentPropertySelection = sender.SelectedNode?.Content as PropertyTreeItem;
        CopyPropertyButton.IsEnabled = currentPropertySelection is not null;

        ResetPropertyDetails();
        if (currentPropertySelection is null)
            return;

        PropertySelectionTitle.Text = currentPropertySelection.Text;
        PropertyDetailRows.Clear();
        PropertyDetailRows.Add($"Text: {currentPropertySelection.Text}");
        PropertyDetailRows.Add($"Children: {currentPropertySelection.Children.Count}");
        PropertyDetailRows.Add($"Tag Type: {currentPropertySelection.Tag?.GetType().Name ?? "(none)"}");

        if (currentPropertySelection.Tag is byte[] bytes)
        {
            PropertyDetailRows.Add($"Byte Length: {bytes.Length}");
            PropertyDetailRows.Add($"Preview: {BitConverter.ToString(bytes.Take(24).ToArray())}");
        }
        else if (currentPropertySelection.Tag is not null)
        {
            PropertyDetailRows.Add($"Tag Value: {currentPropertySelection.Tag}");
        }
    }

    private async void CopyPropertyButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentPropertySelection is null)
            return;

        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(currentPropertySelection.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        await ShowInfoDialogAsync("Property Copied", currentPropertySelection.Text);
    }

    private static void LogObjectsError(string source, System.Exception ex)
    {
        try
        {
            File.AppendAllText(
                ObjectsLogPath,
                $"Time: {System.DateTime.Now:O}{System.Environment.NewLine}Source: {source}{System.Environment.NewLine}{ex}{System.Environment.NewLine}{new string('-', 80)}{System.Environment.NewLine}");
        }
        catch
        {
        }
    }

    private async Task ShowInfoDialogAsync(string title, string content)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 720
                },
                MaxHeight = 480
            },
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot!
        };

        await dialog.ShowAsync();
    }

    private static string SanitizeFilename(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static IEnumerable<string> BuildHexLines(byte[] data)
    {
        const int width = 16;
        for (int offset = 0; offset < data.Length; offset += width)
        {
            byte[] slice = data.Skip(offset).Take(width).ToArray();
            string hex = string.Join(" ", slice.Select(b => b.ToString("X2")));
            string ascii = new string(slice.Select(b => b is >= 32 and < 127 ? (char)b : '.').ToArray());
            yield return $"{offset:X8}  {hex.PadRight(width * 3 - 1)}  {ascii}";
        }
    }

    private void ResetPropertyDetails()
    {
        PropertySelectionTitle.Text = "Property Details";
        PropertyDetailRows.Clear();
        PropertyDetailRows.Add("Select a parsed property node to inspect it here.");
    }
}

