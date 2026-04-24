using DDSLib;
using DDSLib.Constants;
using OmegaAssetStudio.TextureManager;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI.Models;
using OmegaAssetStudio.WinUI.Modules.Workflows;
using OmegaAssetStudio.WinUI.Textures2;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;
using Point = global::Windows.Foundation.Point;
using Size = global::Windows.Foundation.Size;

namespace OmegaAssetStudio.WinUI.Pages;

public sealed partial class TexturesPage : Page
{
    private readonly TextureLoader textureLoader = new();
    private readonly UpkTextureLoader upkTextureLoader = new();
    private readonly UpkParser upkParser = new();
    private readonly Texture2DExtractor textureExtractor = new();
    private readonly InlineMipService inlineMipService = new();
    private readonly TfcService tfcService = new();
    private readonly SafeInjectService injectService = new();
    private readonly UpscaleService upscaleService = new();
    private readonly SearchFilterService searchFilterService = new();
    private readonly TextureHistoryService historyService = new();
    private readonly UndoRedoService<TexturePreviewTexture> undoRedoService = new();
    private readonly TextureToMaterialConverter slotResolver = new();
    private readonly List<TextureItemViewModel> allTextures = [];

    private string currentUpkPath = string.Empty;
    private UpkMetadata? currentMetadata;
    private Texture2DInfo? currentTextureInfo;
    private TextureItemViewModel? selectedItem;
    private TexturePreviewTexture? currentTexture;
    private TexturePreviewTexture? previousTexture;
    private bool dragging;
    private Point dragStart;
    private double dragOriginX;
    private double dragOriginY;
    private double previewHostWidth = 1;
    private double previewHostHeight = 1;
    private double previewZoom = 1;
    private double _previewPanX;
    private double _previewPanY;
    private bool suppressPreview;
    private bool pageReady;
    private bool suppressSessionWrites;

    public ObservableCollection<TextureItemViewModel> VisibleTextures { get; } = [];
    public ObservableCollection<string> DetailRows { get; } = [];
    public ObservableCollection<string> CacheRows { get; } = [];
    public ObservableCollection<string> HistoryRows { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];

    public TexturesPage()
    {
        InitializeComponent();
        DataContext = this;
        NavigationCacheMode = NavigationCacheMode.Required;
        suppressSessionWrites = true;
        tfcService.Initialize();
        PreviewChannelComboBox.ItemsSource = Enum.GetNames<TexturePreviewChannelView>();
        PreviewChannelComboBox.SelectedItem = nameof(TexturePreviewChannelView.Rgba);
        TextureTypeComboBox.ItemsSource = Enum.GetNames<TextureType>();
        TextureTypeComboBox.SelectedItem = nameof(TextureType.Diffuse);
        UpscaleProviderComboBox.ItemsSource = upscaleService.ProviderNames;
        UpscaleProviderComboBox.SelectedItem = upscaleService.ProviderNames.FirstOrDefault();
        TexturesListView.ItemsSource = VisibleTextures;
        AlphaToggle.IsOn = true;
        ApplySessionState();
        suppressSessionWrites = false;
        pageReady = true;
        AppendLog("Textures 2.0 ready.");
        RefreshHistoryRows();
        UpdateButtons();
        UpdateDetailRows();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.WriteDiagnosticsLog("Textures2", "Navigated to Textures 2.0.");

        if (e.Parameter is WorkspaceLaunchContext context && !string.IsNullOrWhiteSpace(context.UpkPath))
        {
            try
            {
                await LoadUpkButton_ClickAsync(context.UpkPath).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(context.ExportPath))
                {
                    TextureItemViewModel? match = VisibleTextures.FirstOrDefault(item =>
                        string.Equals(item.ExportPath, context.ExportPath, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        TexturesListView.SelectedItem = match;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Context restore failed: {ex.Message}");
                App.WriteDiagnosticsLog("Textures2.ContextRestore", ex.ToString());
            }

            return;
        }

        TryAutoLoadSessionTexture();
    }

    private static void InitializePicker(FileOpenPicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static void InitializePicker(FileSavePicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static void InitializePicker(FolderPicker picker)
    {
        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    private async void LoadUpkButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".upk");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        currentUpkPath = file.Path;
        CurrentUpkText.Text = currentUpkPath;
        SearchBox.Text = string.Empty;
        currentMetadata = null;
        currentTextureInfo = null;
        AppendLog($"Loading UPK textures from {currentUpkPath}.");

        try
        {
            currentMetadata = await upkParser.ParseAsync(currentUpkPath).ConfigureAwait(true);
            List<ExportInfo> exports = currentMetadata.Exports.Where(export => export.IsTexture2D).ToList();
            allTextures.Clear();
            foreach (ExportInfo export in exports)
            {
                TextureType type = ResolveTextureType(export.PathName);
                allTextures.Add(new TextureItemViewModel
                {
                    DisplayName = Path.GetFileName(export.PathName),
                    SourcePath = currentUpkPath,
                    ExportPath = export.PathName,
                    SourceKind = "UPK",
                    Format = "Pending",
                    SizeText = "Pending",
                    SlotText = type.ToString(),
                    TextureType = type
                });
            }

            AppendLog($"Loaded {allTextures.Count} texture export(s) from {Path.GetFileName(currentUpkPath)}. Use search or the UPK-only toggle to narrow the list.");
            RefreshVisibleTextures();
            RestoreSelectedTextureFromSession();
            RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Load, Notes = Path.GetFileName(currentUpkPath) });
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"UPK load failed while reading texture exports from {Path.GetFileName(currentUpkPath)}: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.LoadUpk", ex.ToString());
        }
    }

    private void WorkflowsButton_Click(object sender, RoutedEventArgs e)
    {
        WorkflowsService.OpenWorkflowsWindow();
    }

    private async void LoadManifestButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".bin");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        try
        {
            int count = tfcService.LoadManifest(file.Path);
            AppendLog($"Loaded texture manifest: {file.Path} ({count} entries).");
            UpdateDetailRows();
            UpdateButtons();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Manifest load failed while opening {Path.GetFileName(file.Path)}: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.Manifest", ex.ToString());
        }
    }

    private void ReloadManifestButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string manifestPath = TextureManifest.Instance?.ManifestFilePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(manifestPath) && !string.IsNullOrWhiteSpace(currentUpkPath))
            {
                string packageDirectory = Path.GetDirectoryName(currentUpkPath) ?? string.Empty;
                manifestPath = Path.Combine(packageDirectory, TextureManifest.ManifestName);
            }

            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                AppendLog("Reload manifest: no manifest file available.");
                return;
            }

            int count = tfcService.LoadManifest(manifestPath);
            AppendLog($"Reloaded texture manifest: {manifestPath} ({count} entries).");
            UpdateDetailRows();
            UpdateButtons();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Manifest reload failed while resolving the current cache manifest: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.ManifestReload", ex.ToString());
        }
    }

    private void SaveManifestButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            tfcService.SaveManifest();
            AppendLog($"Saved texture manifest: {TextureManager.TextureManifest.Instance.ManifestFilePath}");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Manifest save failed while writing the current cache manifest: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.ManifestSave", ex.ToString());
        }
    }

    private void ReloadCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TextureManifest.Instance is null || TextureManifest.Instance.Entries.Count == 0)
            {
                AppendLog("Reload cache: load a manifest first.");
                return;
            }

            if (TextureFileCache.Instance.Entry is null)
            {
                AppendLog("Reload cache: no cache entry is currently resolved.");
                return;
            }

            TextureFileCache.Instance.LoadTextureCache();
            AppendLog($"Reloaded cache: {TextureFileCache.Instance.Entry.Data.TextureFileName}.tfc");
            UpdateDetailRows();
            UpdateButtons();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Cache reload failed while refreshing the current texture cache: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.CacheReload", ex.ToString());
        }
    }

    private async void ExtractCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FolderPicker picker = new();
            InitializePicker(picker);
            StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is null)
                return;

            string outputPath = tfcService.ExtractCurrentCache(folder.Path, AppendLog);
            AppendLog($"Extracted current cache to {outputPath}.");
            RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Extract, Notes = outputPath });
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Cache extract failed while exporting the current cache snapshot: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.CacheExtract", ex.ToString());
        }
    }

    private void RebuildCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string outputPath = tfcService.RebuildCurrentCache(AppendLog);
            AppendLog($"Rebuilt current cache to {outputPath}.");
            RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.RebuildCache, Notes = outputPath });
            UpdateDetailRows();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Cache rebuild failed while rebuilding the current cache snapshot: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.CacheRebuild", ex.ToString());
        }
    }

    private void DefragCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string outputPath = tfcService.DefragmentCurrentCache(AppendLog);
            AppendLog($"Defragmented current cache to {outputPath}.");
            RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.DefragmentCache, Notes = outputPath });
            UpdateDetailRows();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Cache defrag failed while compacting the current cache snapshot: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.CacheDefrag", ex.ToString());
        }
    }

    private async void ValidateInjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentTexture is null)
        {
            AppendLog("Validate inject: no texture selected.");
            return;
        }

        if (selectedItem is null || !string.Equals(selectedItem.SourceKind, "UPK", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Validate inject: select a UPK texture first.");
            return;
        }

        if (!injectService.CanInject(currentTexture, out string reason))
        {
            AppendLog($"Validate inject failed: {reason}");
            return;
        }

        try
        {
            var validation = await injectService.ValidateTargetAsync(currentUpkPath, selectedItem.ExportPath, currentTexture, AppendLog).ConfigureAwait(true);
            AppendLog(validation.CanInject
                ? $"Validate inject passed: {validation.Reason}"
                : $"Validate inject failed: {validation.Reason}");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Validate inject failed while checking the selected texture against the target UPK: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.ValidateInject", ex.ToString());
        }
    }

    private async void LoadFilesButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        foreach (string ext in new[] { ".png", ".dds", ".tga", ".bmp", ".jpg", ".jpeg" })
            picker.FileTypeFilter.Add(ext);
        InitializePicker(picker);
        var files = await picker.PickMultipleFilesAsync();
        if (files is null)
            return;

        foreach (var file in files)
        {
            try
            {
                await LoadDroppedTextureAsync(file.Path);
            }
            catch (Exception ex)
            {
                AppendLog($"Skipped {Path.GetFileName(file.Path)}: {ex.Message}");
            }
        }

        RefreshVisibleTextures();
        if (selectedItem is null && VisibleTextures.Count > 0)
            TexturesListView.SelectedItem = VisibleTextures[0];

        SaveSessionState();
    }

    private async void BatchOperationsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ContentDialog dialog = new()
            {
                Title = "Batch Operations",
                Content = "Choose a batch action for the current texture set.",
                PrimaryButtonText = "Export",
                SecondaryButtonText = "Inject",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        if (result == ContentDialogResult.Primary)
        {
            BatchExportButton_Click(sender, e);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            BatchInjectButton_Click(sender, e);
        }

        SaveSessionState();
    }
        catch (Exception ex)
        {
            AppendLog($"Batch operations dialog failed: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.BatchDialog", ex.ToString());
        }
    }

    private async Task LoadDiskTextureAsync(string filePath) => await LoadDroppedTextureAsync(filePath);

    private async Task LoadDroppedTextureAsync(string filePath)
    {
        TextureType type = ResolveTextureType(Path.GetFileNameWithoutExtension(filePath));
        TexturePreviewTexture texture = textureLoader.LoadFromFile(filePath, MapTextureSlot(type));
        TextureFileFormat detectedFormat = DdsService.DetectFormat(filePath);
        Track(texture);

        allTextures.Add(new TextureItemViewModel
        {
            DisplayName = texture.Name,
            SourcePath = filePath,
            ExportPath = string.Empty,
            SourceKind = "File",
            Format = detectedFormat == TextureFileFormat.Unknown ? texture.Format : detectedFormat.ToString(),
            SizeText = texture.ResolutionText,
            SlotText = type.ToString(),
            TextureType = type,
            Texture = texture,
            OriginalTexture = CloneTexture(texture),
            PendingReplacementPath = filePath,
            PendingReplacementFormat = detectedFormat == TextureFileFormat.Unknown ? texture.Format : detectedFormat.ToString(),
            IsLoaded = true
        });

        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Load, Notes = filePath });
        await Task.CompletedTask;
    }

    private async void TexturesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (TexturesListView.SelectedItem is not TextureItemViewModel item)
                return;

            selectedItem = item;
            previousTexture = null;

            if (item.Texture is null && string.Equals(item.SourceKind, "UPK", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(currentUpkPath))
                    return;

                Texture2DInfo info = await textureExtractor.ExtractAsync(
                    currentUpkPath,
                    item.ExportPath,
                    item.TextureType,
                    requestedMipIndex: null,
                    AppendLog).ConfigureAwait(true);

                currentTextureInfo = info;
                TexturePreviewTexture loaded = await upkTextureLoader.LoadFromUpkAsync(
                    currentUpkPath,
                    item.ExportPath,
                    MapTextureSlot(item.TextureType),
                    AppendLog,
                    requestedMipIndex: null).ConfigureAwait(true);

                Track(loaded);
                item.Texture = loaded;
                item.OriginalTexture ??= CloneTexture(loaded);
                item.IsLoaded = true;
                item.Format = info.Format;
                item.SizeText = $"{info.Width} x {info.Height}";
            }
            else if (!string.Equals(item.SourceKind, "UPK", StringComparison.OrdinalIgnoreCase))
            {
                currentTextureInfo = null;
            }

            currentTexture = item.Texture ?? item.OriginalTexture;
            if (currentTexture is null)
                return;

            item.Texture ??= currentTexture;
            item.SlotText = MapTextureType(currentTexture.Slot).ToString();
            item.TextureType = MapTextureType(currentTexture.Slot);
            MipSlider.IsEnabled = currentTexture.AvailableMipLevels.Count > 1 && item.SourceKind == "UPK";
            MipSlider.Minimum = 0;
            MipSlider.Maximum = Math.Max(0, currentTexture.AvailableMipLevels.Count - 1);
            MipSlider.Value = 0;
            UpdateDetailRows();
            RefreshPreview();
            UpdateButtons();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Texture selection failed: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.SelectionChanged", ex.ToString());
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshVisibleTextures();
        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void CompareToggle_Toggled(object sender, RoutedEventArgs e) => RefreshIfReady();
    private void AlphaToggle_Toggled(object sender, RoutedEventArgs e) => RefreshIfReady();
    private void PreviewChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshIfReady();
        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void TextureTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectedItem is null)
            return;

        selectedItem.TextureType = GetSelectedType();
        selectedItem.SlotText = selectedItem.TextureType.ToString();
        if (selectedItem.Texture is not null)
            selectedItem.Texture.Slot = MapTextureSlot(selectedItem.TextureType);
        UpdateDetailRows();
        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void UpscaleProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UpscaleProviderComboBox.SelectedItem is string providerName && !string.IsNullOrWhiteSpace(providerName))
            AppendLog($"Upscale method selected: {providerName}");

        if (!suppressSessionWrites)
            SaveSessionState();
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (suppressPreview)
            return;

        previewZoom = e.NewValue;
        RefreshPreview();
    }

    private async void MipSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (suppressPreview || currentTexture is null || selectedItem is null || !string.Equals(selectedItem.SourceKind, "UPK", StringComparison.OrdinalIgnoreCase))
            return;

        int index = (int)Math.Clamp(e.NewValue, 0, Math.Max(0, currentTexture.AvailableMipLevels.Count - 1));
        if (currentTexture.AvailableMipLevels.Count == 0)
            return;

        int absolute = currentTexture.AvailableMipLevels[index].AbsoluteIndex;
        TexturePreviewTexture loaded = await upkTextureLoader.LoadFromUpkAsync(
            currentUpkPath,
            selectedItem.ExportPath,
            MapTextureSlot(selectedItem.TextureType),
            AppendLog,
            requestedMipIndex: absolute).ConfigureAwait(true);
        Track(loaded);
        selectedItem.Texture = loaded;
        currentTexture = loaded;
        UpdateDetailRows();
        RefreshPreview();
    }

    private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem is null)
            return;

        FileOpenPicker picker = new();
        foreach (string ext in new[] { ".png", ".dds", ".tga", ".bmp", ".jpg", ".jpeg" })
            picker.FileTypeFilter.Add(ext);
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        previousTexture = currentTexture is null ? null : CloneTexture(currentTexture);
        if (currentTexture is not null)
            undoRedoService.Push(CloneTexture(currentTexture));
        TexturePreviewTexture loaded = textureLoader.LoadFromFile(file.Path, MapTextureSlot(selectedItem.TextureType));
        selectedItem.PendingReplacementPath = file.Path;
        selectedItem.PendingReplacementFormat = DdsService.DetectFormat(file.Path).ToString();
        Track(loaded);
        currentTexture = loaded;
        selectedItem.Texture = loaded;
        selectedItem.IsLoaded = true;
        selectedItem.OriginalTexture ??= CloneTexture(loaded);
        selectedItem.Format = loaded.Format;
        selectedItem.SizeText = loaded.ResolutionText;
        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Replace, Notes = file.Path });
        UpdateDetailRows();
        RefreshPreview();
        UpdateButtons();
        SaveSessionState();
    }

    private async void ApplyReplacementButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentTexture is null || selectedItem is null)
            return;

        if (string.IsNullOrWhiteSpace(selectedItem.PendingReplacementPath) || !File.Exists(selectedItem.PendingReplacementPath))
        {
            AppendLog("Apply pending replacement: load a replacement file first.");
            return;
        }

        if (!string.Equals(selectedItem.SourceKind, "UPK", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Apply pending replacement: select a UPK texture first.");
            return;
        }

        try
        {
            selectedItem.PendingReplacementFormat = DdsService.DetectFormat(selectedItem.PendingReplacementPath).ToString();
            TexturePreviewTexture pending = textureLoader.LoadFromFile(selectedItem.PendingReplacementPath, MapTextureSlot(selectedItem.TextureType));
            var validation = await injectService.ValidateTargetAsync(currentUpkPath, selectedItem.ExportPath, pending, AppendLog).ConfigureAwait(true);
            if (!validation.CanInject)
            {
                AppendLog($"Apply pending replacement blocked: {validation.Reason}");
                return;
            }

            previousTexture = currentTexture is null ? null : CloneTexture(currentTexture);
            if (currentTexture is not null)
                undoRedoService.Push(CloneTexture(currentTexture));

            Track(pending);
            currentTexture = pending;
            selectedItem.Texture = pending;
            selectedItem.IsLoaded = true;
            selectedItem.OriginalTexture ??= CloneTexture(pending);
            selectedItem.Format = pending.Format;
            selectedItem.SizeText = pending.ResolutionText;
            await injectService.InjectAsync(currentUpkPath, selectedItem.ExportPath, pending, AppendLog).ConfigureAwait(true);
            AppendLog($"Applied pending replacement: {selectedItem.PendingReplacementPath}");
            RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Replace, Notes = selectedItem.PendingReplacementPath });
            UpdateDetailRows();
            RefreshPreview();
            UpdateButtons();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Apply pending replacement failed: {ex.GetType().Name}: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.ApplyPendingReplacement", ex.ToString());
        }
    }

    private async void ReplaceInlineMipButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem is null || !string.Equals(selectedItem.SourceKind, "UPK", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Replace inline mip: select a UPK texture first.");
            return;
        }

        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".dds");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".tga");
        picker.FileTypeFilter.Add(".bmp");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        try
        {
            string outputPath = await inlineMipService.ReplaceInlineMipFromDdsAsync(currentUpkPath, file.Path, inplace: false, AppendLog).ConfigureAwait(true);
            AppendLog($"Inline mip replaced to {outputPath}.");
            RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.ReplaceInlineMip, Notes = file.Path });
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Replace inline mip failed: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.ReplaceInlineMip", ex.ToString());
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentTexture is null)
            return;

        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("PNG", [".png"]);
        picker.FileTypeChoices.Add("DDS", [".dds"]);
        picker.SuggestedFileName = selectedItem?.DisplayName ?? currentTexture.Name;
        InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        string path = file.Path;
        if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            await SaveDdsAsync(path, currentTexture).ConfigureAwait(true);
        }
        else
        {
            currentTexture.Bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Export, Notes = path });
        AppendLog($"Exported {Path.GetFileName(path)}.");
        SaveSessionState();
    }

    private async void InjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentTexture is null || selectedItem is null || !string.Equals(selectedItem.SourceKind, "UPK", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var validation = await injectService.ValidateTargetAsync(currentUpkPath, selectedItem.ExportPath, currentTexture, AppendLog).ConfigureAwait(true);
            if (!validation.CanInject)
            {
                AppendLog($"Inject blocked: {validation.Reason}");
                return;
            }

            await injectService.InjectAsync(currentUpkPath, selectedItem.ExportPath, currentTexture, AppendLog).ConfigureAwait(true);
            AppendLog($"Injected {selectedItem.DisplayName}.");
            SaveSessionState();
        }
        catch (Exception ex)
        {
            AppendLog($"Inject failed: {ex.GetType().Name}: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.Inject", ex.ToString());
        }
    }

    private void UpscaleButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentTexture is null || selectedItem is null)
            return;

        previousTexture = currentTexture is null ? null : CloneTexture(currentTexture);
        if (currentTexture is not null)
            undoRedoService.Push(CloneTexture(currentTexture));
        string? providerName = UpscaleProviderComboBox.SelectedItem as string;
        TexturePreviewTexture upscaled = upscaleService.Upscale(currentTexture!, 4096, providerName);
        Track(upscaled);
        currentTexture = upscaled;
        selectedItem.Texture = upscaled;
        selectedItem.Format = upscaled.Format;
        selectedItem.SizeText = upscaled.ResolutionText;
        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Upscale, Notes = $"4096 via {providerName ?? "default"}" });
        UpdateDetailRows();
        RefreshPreview();
        SaveSessionState();
    }

    private void RegenButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentTexture is null || selectedItem is null)
            return;

        previousTexture = currentTexture is null ? null : CloneTexture(currentTexture);
        if (currentTexture is not null)
            undoRedoService.Push(CloneTexture(currentTexture));
        DdsFile dds = DdsService.BuildDds(currentTexture!);
        TexturePreviewTexture regenerated = CreateTextureFromDds(dds, currentTexture!, selectedItem.TextureType);
        Track(regenerated);
        currentTexture = regenerated;
        selectedItem.Texture = regenerated;
        selectedItem.Format = regenerated.Format;
        selectedItem.SizeText = regenerated.ResolutionText;
        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.RegenerateMipmaps });
        UpdateDetailRows();
        RefreshPreview();
        SaveSessionState();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem is null)
            return;

        if (!undoRedoService.TryUndo(out TexturePreviewTexture value))
            return;

        currentTexture = value;
        selectedItem.Texture = currentTexture;
        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Undo });
        UpdateDetailRows();
        RefreshPreview();
        SaveSessionState();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem is null)
            return;

        if (!undoRedoService.TryRedo(out TexturePreviewTexture value))
            return;

        currentTexture = value;
        selectedItem.Texture = currentTexture;
        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Redo });
        UpdateDetailRows();
        RefreshPreview();
        SaveSessionState();
    }
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem?.OriginalTexture is null)
            return;

        previousTexture = currentTexture is null ? null : CloneTexture(currentTexture);
        if (currentTexture is not null)
            undoRedoService.Push(CloneTexture(currentTexture));
        currentTexture = CloneTexture(selectedItem.OriginalTexture);
        Track(currentTexture);
        selectedItem.Texture = currentTexture;
        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Reset });
        UpdateDetailRows();
        RefreshPreview();
        SaveSessionState();
    }
    private async void BatchExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (VisibleTextures.Count == 0)
            return;

        FolderPicker picker = new();
        InitializePicker(picker);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        int exported = 0;
        foreach (TextureItemViewModel item in VisibleTextures.Where(item => item.Texture is not null))
        {
            try
            {
                string fileName = $"{SanitizeFileName(item.DisplayName)}.dds";
                string path = Path.Combine(folder.Path, fileName);
                await SaveDdsAsync(path, item.Texture!).ConfigureAwait(true);
                exported++;
            }
            catch (Exception ex)
            {
                AppendLog($"Batch export failed for {item.DisplayName}: {ex.Message}");
            }
        }

        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.BatchExport, Notes = $"{exported} file(s)" });
        AppendLog($"Batch export complete: {exported} file(s).");
    }

    private async void BatchInjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentUpkPath))
            return;

        FolderPicker picker = new();
        InitializePicker(picker);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        string[] extensions = [".dds", ".png", ".tga", ".bmp", ".jpg", ".jpeg"];
        int injected = 0;

        foreach (TextureItemViewModel item in VisibleTextures.Where(item => item.SourceKind == "UPK"))
        {
            string baseName = Path.GetFileNameWithoutExtension(item.DisplayName);
            string? replacementPath = extensions
                .Select(ext => Path.Combine(folder.Path, baseName + ext))
                .FirstOrDefault(File.Exists);

            if (replacementPath is null)
                continue;

            try
            {
                TexturePreviewTexture replacement = textureLoader.LoadFromFile(replacementPath, MapTextureSlot(item.TextureType));
                var validation = await injectService.ValidateTargetAsync(currentUpkPath, item.ExportPath, replacement, AppendLog).ConfigureAwait(true);
                if (!validation.CanInject)
                {
                    AppendLog($"Batch inject skipped for {item.DisplayName}: {validation.Reason}");
                    continue;
                }

                await injectService.InjectAsync(currentUpkPath, item.ExportPath, replacement, AppendLog).ConfigureAwait(true);
                injected++;
            }
            catch (Exception ex)
            {
                AppendLog($"Batch inject failed for {item.DisplayName}: {ex.GetType().Name}: {ex.Message}");
                App.WriteDiagnosticsLog("Textures2.BatchInject", ex.ToString());
            }
        }

        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.BatchInject, Notes = $"{injected} file(s)" });
        AppendLog($"Batch inject complete: {injected} file(s).");
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        previewHostWidth = Math.Max(1, e.NewSize.Width);
        previewHostHeight = Math.Max(1, e.NewSize.Height);
        RefreshIfReady();
    }

    private void PreviewHost_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
        else
            e.AcceptedOperation = DataPackageOperation.None;
    }

    private async void PreviewHost_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                return;

            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync().AsTask().ConfigureAwait(true);
            if (items is null || items.Count == 0)
                return;

            if (items.Count > 1)
                AppendLog($"Dropped {items.Count} files; using the first one only.");

            StorageFile? file = items.OfType<StorageFile>().FirstOrDefault();
            if (file is null)
                return;

            await LoadDroppedTextureAsync(file.Path).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog($"Drop failed: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.Drop", ex.ToString());
        }
    }

    private void PreviewHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (currentTexture is null)
            return;

        dragging = true;
        dragStart = e.GetCurrentPoint(PreviewHost).Position;
        dragOriginX = CurrentPreviewTranslate.X;
        dragOriginY = CurrentPreviewTranslate.Y;
        PreviewHost.CapturePointer(e.Pointer);
    }

    private void PreviewHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging)
            return;

        Point position = e.GetCurrentPoint(PreviewHost).Position;
        _previewPanX = dragOriginX + (position.X - dragStart.X);
        _previewPanY = dragOriginY + (position.Y - dragStart.Y);
        RefreshPreview();
    }

    private void PreviewHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        dragging = false;
        PreviewHost.ReleasePointerCaptures();
    }

    private void PreviewHost_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(PreviewHost).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        previewZoom = Math.Clamp(previewZoom + (delta > 0 ? 0.1 : -0.1), 0.25, 6.0);
        suppressPreview = true;
        ZoomSlider.Value = previewZoom;
        suppressPreview = false;
        RefreshIfReady();
    }

    private void RefreshVisibleTextures()
    {
        VisibleTextures.Clear();
        foreach (TextureItemViewModel item in searchFilterService.Filter(allTextures, SearchBox.Text ?? string.Empty))
            VisibleTextures.Add(item);
    }

    private void RefreshIfReady()
    {
        if (!pageReady)
            return;

        RefreshPreview();
    }

    private void RefreshHistoryRows()
    {
        HistoryRows.Clear();
        foreach (TextureHistoryEntry entry in historyService.Entries.TakeLast(20))
            HistoryRows.Add(entry.ToString());

        HistoryRowsListView.ItemsSource = HistoryRows;
    }

    private void RefreshPreview()
    {
        if (suppressPreview)
            return;

        if (currentTexture is null)
        {
            CurrentPreviewImage.Source = null;
            PreviousPreviewImage.Source = null;
            PreviousPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }

        TexturePreviewChannelView channel = GetSelectedChannel();
        bool showAlpha = AlphaToggle.IsOn == true;
        CurrentPreviewImage.Source = BuildPreviewBitmap(currentTexture, channel, showAlpha);
        Size currentSize = GetDisplaySize(currentTexture);
        CurrentPreviewImage.Width = currentSize.Width;
        CurrentPreviewImage.Height = currentSize.Height;
        CurrentPreviewTranslate.X = _previewPanX;
        CurrentPreviewTranslate.Y = _previewPanY;

        bool compare = CompareToggle.IsOn == true && previousTexture is not null;
        PreviousPreviewBorder.Visibility = compare ? Visibility.Visible : Visibility.Collapsed;
        if (compare)
        {
            Grid.SetColumn(PreviousPreviewBorder, 0);
            Grid.SetColumnSpan(PreviousPreviewBorder, 1);
            Grid.SetColumn(CurrentPreviewBorder, 1);
            Grid.SetColumnSpan(CurrentPreviewBorder, 1);
            PreviousPreviewImage.Source = BuildPreviewBitmap(previousTexture!, channel, showAlpha);
            Size previousSize = GetDisplaySize(previousTexture!);
            PreviousPreviewImage.Width = previousSize.Width;
            PreviousPreviewImage.Height = previousSize.Height;
            PreviousPreviewTranslate.X = _previewPanX;
            PreviousPreviewTranslate.Y = _previewPanY;
        }
        else
        {
            Grid.SetColumn(CurrentPreviewBorder, 0);
            Grid.SetColumnSpan(CurrentPreviewBorder, 2);
            PreviousPreviewImage.Source = null;
        }
    }

    private Size GetDisplaySize(TexturePreviewTexture texture)
    {
        double fitScale = Math.Min(previewHostWidth / Math.Max(1, texture.Width), previewHostHeight / Math.Max(1, texture.Height));
        fitScale = Math.Max(1.0, fitScale);
        return new Size(texture.Width * fitScale * previewZoom, texture.Height * fitScale * previewZoom);
    }

    private static WriteableBitmap BuildPreviewBitmap(TexturePreviewTexture texture, TexturePreviewChannelView channel, bool showAlpha)
    {
        byte[] bgra = new byte[texture.RgbaPixels.Length];
        for (int i = 0; i < texture.RgbaPixels.Length; i += 4)
        {
            byte r = texture.RgbaPixels[i + 0];
            byte g = texture.RgbaPixels[i + 1];
            byte b = texture.RgbaPixels[i + 2];
            byte a = showAlpha ? texture.RgbaPixels[i + 3] : (byte)255;

            switch (channel)
            {
                case TexturePreviewChannelView.Red:
                    g = b = r;
                    break;
                case TexturePreviewChannelView.Green:
                    r = b = g;
                    break;
                case TexturePreviewChannelView.Blue:
                    r = g = b;
                    b = texture.RgbaPixels[i + 2];
                    break;
                case TexturePreviewChannelView.Alpha:
                    r = g = b = a;
                    a = 255;
                    break;
            }

            bgra[i + 0] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = a;
        }

        WriteableBitmap bitmap = new(texture.Width, texture.Height);
        using Stream stream = bitmap.PixelBuffer.AsStream();
        stream.Write(bgra, 0, bgra.Length);
        bitmap.Invalidate();
        return bitmap;
    }

    private void UpdateDetailRows()
    {
        DetailRows.Clear();
        if (selectedItem is null || currentTexture is null)
        {
            DetailRows.Add("Select a texture to inspect it here.");
            CacheRows.Clear();
            CacheRows.Add("Select a UPK texture to inspect cache state.");
            DetailRowsListView.ItemsSource = DetailRows;
            CacheRowsListView.ItemsSource = CacheRows;
            return;
        }

        TexturePreviewTexture texture = currentTexture;
        DetailRows.Add($"Name: {selectedItem.DisplayName}");
        DetailRows.Add($"Source: {selectedItem.SourcePath}");
        if (!string.IsNullOrWhiteSpace(selectedItem.ExportPath))
            DetailRows.Add($"Export: {selectedItem.ExportPath}");
        if (currentMetadata is not null)
        {
            DetailRows.Add($"UPK Exports: {currentMetadata.ExportTableCount}");
            DetailRows.Add($"UPK Imports: {currentMetadata.ImportTableCount}");
            DetailRows.Add($"UPK Names: {currentMetadata.NameTableCount}");
        }
        if (currentTextureInfo is not null)
        {
            DetailRows.Add($"Texture Info: {currentTextureInfo.Format} / {currentTextureInfo.ContainerType}");
            DetailRows.Add($"Texture Mips: {currentTextureInfo.MipCount}");
            DetailRows.Add($"Cache Backed: {currentTextureInfo.Mips.Any(m => string.Equals(m.Source, "TFC", StringComparison.OrdinalIgnoreCase))}");
        }
        if (!string.IsNullOrWhiteSpace(currentTexture.SourcePath) && File.Exists(currentTexture.SourcePath))
        {
            DetailRows.Add($"Detected Format: {DdsService.DetectFormat(currentTexture.SourcePath)}");
        }
        bool hasAlpha = currentTexture.RgbaPixels?.Length >= 4 && currentTexture.RgbaPixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha < 255);
        DetailRows.Add($"Recommended DDS: {DdsService.SuggestFormatLabel(selectedItem.TextureType, hasAlpha, selectedItem.DisplayName)}");
        CacheRows.Clear();
        if (TextureManifest.Instance is not null)
        {
            CacheRows.Add($"Manifest Loaded: {TextureManifest.Instance.Entries.Count > 0}");
            CacheRows.Add($"Manifest Path: {TextureManifest.Instance.ManifestFilePath}");
            if (selectedItem.SourceKind == "UPK" && currentTexture is not null)
            {
                try
                {
                    var cacheEntry = TextureFileCache.Instance.Entry;
                    CacheRows.Add($"Cache Entry: {cacheEntry?.Head.TextureName ?? "(none)"}");
                    string cacheSummary = cacheEntry?.Data?.TextureFileName ?? "(none)";
                    CacheRows.Add($"Cache File: {cacheSummary}");
                    CacheRows.Add($"Cache Mips Loaded: {TextureFileCache.Instance.Loaded}");
                }
                catch
                {
                    CacheRows.Add("Cache File: (unresolved)");
                }
            }
        }
        else
        {
            CacheRows.Add("Manifest not loaded.");
        }
        DetailRows.Add($"Size: {texture.Width} x {texture.Height}");
        DetailRows.Add($"Format: {texture.Format}");
        DetailRows.Add($"Compression: {texture.Compression}");
        DetailRows.Add($"Container: {texture.ContainerType}");
        DetailRows.Add($"Mip Count: {texture.MipCount}");
        DetailRows.Add($"Selected Mip: {texture.SelectedMipIndex}");
        DetailRows.Add($"Texture Type: {selectedItem.TextureType}");
        DetailRows.Add($"Slot: {selectedItem.SlotText}");
        DetailRows.Add($"Pending Replacement: {(string.IsNullOrWhiteSpace(selectedItem.PendingReplacementPath) ? "(none)" : selectedItem.PendingReplacementPath)}");
        if (!string.IsNullOrWhiteSpace(selectedItem.PendingReplacementFormat))
            DetailRows.Add($"Pending Format: {selectedItem.PendingReplacementFormat}");
        DetailRows.Add($"Loaded: {selectedItem.IsLoaded}");
        DetailRowsListView.ItemsSource = DetailRows;
        CacheRowsListView.ItemsSource = CacheRows;
    }

    private void UpdateButtons()
    {
        bool hasTexture = currentTexture is not null;
        bool manifestReady = TextureManifest.Instance is not null &&
            TextureManifest.Instance.Entries.Count > 0 &&
            !string.IsNullOrWhiteSpace(TextureManifest.Instance.ManifestFilePath);
        ExportButton.IsEnabled = hasTexture;
        ReplaceButton.IsEnabled = selectedItem is not null;
        ReplaceInlineMipButton.IsEnabled = selectedItem?.SourceKind == "UPK";
        InjectButton.IsEnabled = hasTexture && selectedItem?.SourceKind == "UPK" && manifestReady;
        UpscaleButton.IsEnabled = hasTexture;
        RegenButton.IsEnabled = hasTexture;
        bool cacheReady = TextureManifest.Instance is not null && TextureFileCache.Instance.Entry is not null;
        ExtractCacheButton.IsEnabled = cacheReady;
        RebuildCacheButton.IsEnabled = cacheReady;
        DefragCacheButton.IsEnabled = cacheReady;
        ApplyReplacementButton.IsEnabled = hasTexture && selectedItem?.SourceKind == "UPK" && manifestReady && !string.IsNullOrWhiteSpace(selectedItem?.PendingReplacementPath);
        UndoButton.IsEnabled = undoRedoService.CanUndo;
        RedoButton.IsEnabled = undoRedoService.CanRedo;
        ResetButton.IsEnabled = selectedItem is not null;
    }

    private void AppendLog(string message)
    {
        void Append()
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogEntries.Add(line);
            if (LogEntries.Count > 500)
                LogEntries.RemoveAt(0);

            LogListView.ScrollIntoView(line);
            App.WriteDiagnosticsLog("Textures2", message);
        }

        if (DispatcherQueue.HasThreadAccess)
            Append();
        else
            DispatcherQueue.TryEnqueue(Append);
    }

    private void SaveSessionState()
    {
        if (suppressSessionWrites)
            return;

        WorkspaceSessionStore.RememberTexture(new WorkspaceSessionStore.TextureWorkspaceSession
        {
            UpkPath = currentUpkPath,
            SearchText = SearchBox.Text?.Trim() ?? string.Empty,
            TextureType = TextureTypeComboBox.SelectedItem?.ToString() ?? string.Empty,
            UpscaleMethod = UpscaleProviderComboBox.SelectedItem?.ToString() ?? string.Empty,
            PreviewChannel = PreviewChannelComboBox.SelectedItem?.ToString() ?? string.Empty,
            SelectedExportPath = selectedItem?.ExportPath ?? string.Empty,
            PendingReplacementPath = selectedItem?.PendingReplacementPath ?? string.Empty
        });
    }

    private void ApplySessionState()
    {
        WorkspaceSessionStore.TextureWorkspaceSession session = WorkspaceSessionStore.Texture;
        if (string.IsNullOrWhiteSpace(session.UpkPath) &&
            string.IsNullOrWhiteSpace(session.SearchText) &&
            string.IsNullOrWhiteSpace(session.TextureType) &&
            string.IsNullOrWhiteSpace(session.UpscaleMethod) &&
            string.IsNullOrWhiteSpace(session.PreviewChannel) &&
            string.IsNullOrWhiteSpace(session.SelectedExportPath) &&
            string.IsNullOrWhiteSpace(session.PendingReplacementPath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.SearchText))
            SearchBox.Text = session.SearchText;

        if (!string.IsNullOrWhiteSpace(session.TextureType) && Enum.TryParse(session.TextureType, out TextureType type))
            TextureTypeComboBox.SelectedItem = type.ToString();

        if (!string.IsNullOrWhiteSpace(session.UpscaleMethod) && upscaleService.ProviderNames.Contains(session.UpscaleMethod))
            UpscaleProviderComboBox.SelectedItem = session.UpscaleMethod;

        if (!string.IsNullOrWhiteSpace(session.PreviewChannel) && Enum.TryParse(session.PreviewChannel, out TexturePreviewChannelView channel))
            PreviewChannelComboBox.SelectedItem = channel.ToString();
    }

    private void RestoreSelectedTextureFromSession()
    {
        WorkspaceSessionStore.TextureWorkspaceSession session = WorkspaceSessionStore.Texture;
        if (string.IsNullOrWhiteSpace(session.SelectedExportPath))
            return;

        TextureItemViewModel? match = VisibleTextures.FirstOrDefault(item =>
            string.Equals(item.ExportPath, session.SelectedExportPath, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            TexturesListView.SelectedItem = match;
    }

    private async void TryAutoLoadSessionTexture()
    {
        WorkspaceSessionStore.TextureWorkspaceSession session = WorkspaceSessionStore.Texture;
        if (string.IsNullOrWhiteSpace(session.UpkPath) || !File.Exists(session.UpkPath) || !string.IsNullOrWhiteSpace(currentUpkPath))
            return;

        try
        {
            AppendLog($"Restoring last texture session from {Path.GetFileName(session.UpkPath)}.");
            await LoadUpkButton_ClickAsync(session.UpkPath).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(session.SelectedExportPath))
            {
                TextureItemViewModel? match = VisibleTextures.FirstOrDefault(item =>
                    string.Equals(item.ExportPath, session.SelectedExportPath, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    TexturesListView.SelectedItem = match;
                else
                    AppendLog("Saved texture selection no longer exists in the current UPK; leaving the first visible texture selected.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Session restore failed while loading the last texture workspace: {ex.Message}");
            App.WriteDiagnosticsLog("Textures2.SessionRestore", ex.ToString());
        }
    }

    private async Task LoadUpkButton_ClickAsync(string path)
    {
        currentUpkPath = path;
        CurrentUpkText.Text = currentUpkPath;
        SearchBox.Text = SearchBox.Text ?? string.Empty;
        currentMetadata = null;
        currentTextureInfo = null;
        AppendLog($"Loading UPK textures from {currentUpkPath}.");

        currentMetadata = await upkParser.ParseAsync(currentUpkPath).ConfigureAwait(true);
        List<ExportInfo> exports = currentMetadata.Exports.Where(export => export.IsTexture2D).ToList();
        allTextures.Clear();
        foreach (ExportInfo export in exports)
        {
            TextureType type = ResolveTextureType(export.PathName);
            allTextures.Add(new TextureItemViewModel
            {
                DisplayName = Path.GetFileName(export.PathName),
                SourcePath = currentUpkPath,
                ExportPath = export.PathName,
                SourceKind = "UPK",
                Format = "Pending",
                SizeText = "Pending",
                SlotText = type.ToString(),
                TextureType = type
            });
        }

        AppendLog($"Loaded {allTextures.Count} texture export(s) from {Path.GetFileName(currentUpkPath)}.");
        RefreshVisibleTextures();
        if (VisibleTextures.Count > 0 && TexturesListView.SelectedItem is null)
            TexturesListView.SelectedItem = VisibleTextures[0];
        RecordHistory(new TextureHistoryEntry { Operation = TextureOperation.Load, Notes = Path.GetFileName(currentUpkPath) });
        SaveSessionState();
    }

    private void Track(TexturePreviewTexture texture)
    {
        undoRedoService.Push(CloneTexture(texture));
    }

    private void RecordHistory(TextureHistoryEntry entry)
    {
        historyService.Record(entry);
        RefreshHistoryRows();
    }

    private static TexturePreviewTexture CloneTexture(TexturePreviewTexture texture)
    {
        return new TexturePreviewTexture
        {
            Name = texture.Name,
            SourcePath = texture.SourcePath,
            SourceDescription = texture.SourceDescription,
            ExportPath = texture.ExportPath,
            Bitmap = new Bitmap(texture.Bitmap),
            RgbaPixels = (byte[])texture.RgbaPixels.Clone(),
            Width = texture.Width,
            Height = texture.Height,
            MipCount = texture.MipCount,
            SelectedMipIndex = texture.SelectedMipIndex,
            Format = texture.Format,
            Compression = texture.Compression,
            ContainerType = texture.ContainerType,
            MipSource = texture.MipSource,
            Slot = texture.Slot,
            ContainerBytes = texture.ContainerBytes is null ? null : (byte[])texture.ContainerBytes.Clone(),
            AvailableMipLevels = texture.AvailableMipLevels
        };
    }

    private static async Task SaveDdsAsync(string path, TexturePreviewTexture texture)
    {
        DdsFile dds = DdsService.BuildDds(texture);
        using MemoryStream stream = new();
        dds.Save(stream, new DdsSaveConfig(DdsService.ResolveFormat(texture), 0, 0, false, false));
        await File.WriteAllBytesAsync(path, stream.ToArray()).ConfigureAwait(true);
    }

    private static TexturePreviewTexture CreateTextureFromDds(DdsFile dds, TexturePreviewTexture source, TextureType type)
    {
        Bitmap bitmap = BitmapSourceToBitmap(dds.BitmapSource);
        return new TexturePreviewTexture
        {
            Name = source.Name,
            SourcePath = source.SourcePath,
            SourceDescription = "Generated DDS",
            ExportPath = source.ExportPath,
            Bitmap = bitmap,
            RgbaPixels = BitmapToRgba(bitmap),
            Width = bitmap.Width,
            Height = bitmap.Height,
            MipCount = dds.MipMaps.Count,
            SelectedMipIndex = 0,
            Format = dds.FileFormat.ToString(),
            Compression = dds.FileFormat.ToString(),
            ContainerType = "DDS",
            MipSource = "Generated",
            Slot = MapTextureSlot(type),
            ContainerBytes = null,
            AvailableMipLevels = []
        };
    }

    private static Bitmap BitmapSourceToBitmap(System.Windows.Media.Imaging.BitmapSource bitmapSource)
    {
        using MemoryStream outStream = new();
        System.Windows.Media.Imaging.BitmapEncoder encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
        encoder.Save(outStream);
        outStream.Position = 0;
        using Bitmap temp = new(outStream);
        return new Bitmap(temp);
    }

    private static byte[] BitmapToRgba(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[bitmap.Width * bitmap.Height * 4];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
            for (int i = 0; i < bgra.Length; i += 4)
                (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);

            return bgra;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private TexturePreviewChannelView GetSelectedChannel()
    {
        if (PreviewChannelComboBox.SelectedItem is string value &&
            Enum.TryParse(value, out TexturePreviewChannelView channel))
        {
            return channel;
        }

        return TexturePreviewChannelView.Rgba;
    }

    private TextureType ResolveTextureType(string sourceName)
    {
        TexturePreviewMaterialSlot slot = slotResolver.ResolveSlot(sourceName, TexturePreviewMaterialSlot.Diffuse);
        return slot switch
        {
            TexturePreviewMaterialSlot.Normal => TextureType.Normal,
            TexturePreviewMaterialSlot.Specular => TextureType.Specular,
            TexturePreviewMaterialSlot.Emissive => TextureType.Emissive,
            TexturePreviewMaterialSlot.Mask => TextureType.Mask,
            _ => TextureType.Diffuse
        };
    }

    private static TexturePreviewMaterialSlot MapTextureSlot(TextureType type)
    {
        return type switch
        {
            TextureType.Normal => TexturePreviewMaterialSlot.Normal,
            TextureType.Specular => TexturePreviewMaterialSlot.Specular,
            TextureType.Emissive => TexturePreviewMaterialSlot.Emissive,
            TextureType.Mask => TexturePreviewMaterialSlot.Mask,
            _ => TexturePreviewMaterialSlot.Diffuse
        };
    }

    private TextureType MapTextureType(TexturePreviewMaterialSlot slot)
    {
        return slot switch
        {
            TexturePreviewMaterialSlot.Normal => TextureType.Normal,
            TexturePreviewMaterialSlot.Specular => TextureType.Specular,
            TexturePreviewMaterialSlot.Emissive => TextureType.Emissive,
            TexturePreviewMaterialSlot.Mask => TextureType.Mask,
            _ => TextureType.Diffuse
        };
    }

    private TextureType GetSelectedType()
    {
        if (TextureTypeComboBox.SelectedItem is string value && Enum.TryParse(value, out TextureType type))
            return type;

        return TextureType.Diffuse;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(value) ? "texture" : value;
    }
}


