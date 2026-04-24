using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using OmegaAssetStudio.WinUI.Models;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI.Textures2;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Interop;

public sealed class Textures2TextureEditorServiceAdapter : ITextureEditorService
{
    private readonly UpkParser upkParser = new();
    private readonly Texture2DExtractor textureExtractor = new();

    public async Task<TextureMetadata?> GetTextureMetadataAsync(string textureName, string texturePath, string sourceUpkPath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            throw new ArgumentException("Texture path is required.", nameof(texturePath));

        if (string.IsNullOrWhiteSpace(sourceUpkPath))
            throw new ArgumentException("Source UPK path is required.", nameof(sourceUpkPath));

        if (!File.Exists(sourceUpkPath))
            return new TextureMetadata
            {
                TextureName = textureName,
                TexturePath = texturePath,
                SourceUpkPath = sourceUpkPath,
                ExportClass = "<missing>",
                ContainerType = "Missing",
                SourceDescription = "Source UPK not available.",
                Format = "Missing",
                Compression = "Missing",
                Width = 0,
                Height = 0,
                MipCount = 0,
                IsTexture2D = false,
                MipSummaries = []
            };

        try
        {
            UpkMetadata metadata = await upkParser.ParseAsync(sourceUpkPath).ConfigureAwait(true);
            ExportInfo? export = metadata.Exports.FirstOrDefault(entry => string.Equals(entry.PathName, texturePath, StringComparison.OrdinalIgnoreCase));

            if (export is null)
            {
                return new TextureMetadata
                {
                    TextureName = textureName,
                    TexturePath = texturePath,
                    SourceUpkPath = sourceUpkPath,
                    ExportClass = "<missing>",
                    ContainerType = "Missing",
                    SourceDescription = "Texture export not found in source UPK.",
                    Format = "Missing",
                    Compression = "Missing",
                    Width = 0,
                    Height = 0,
                    MipCount = 0,
                    IsTexture2D = false,
                    MipSummaries = []
                };
            }

            TextureType inferredType = InferTextureType(textureName, texturePath);
            Texture2DInfo info = await textureExtractor.ExtractAsync(sourceUpkPath, texturePath, inferredType).ConfigureAwait(true);

            return new TextureMetadata
            {
                TextureName = textureName,
                TexturePath = texturePath,
                SourceUpkPath = sourceUpkPath,
                ExportClass = export.ClassName,
                Format = info.Format,
                Compression = info.Compression,
                ContainerType = info.ContainerType,
                SourceDescription = info.SourceDescription,
                Width = info.Width,
                Height = info.Height,
                MipCount = info.MipCount,
                IsTexture2D = export.IsTexture2D,
                MipSummaries = info.Mips.Select(mip => $"{mip.AbsoluteIndex}: {mip.Width}x{mip.Height} {mip.Format} {mip.Source}").ToArray()
            };
        }
        catch (Exception)
        {
            return new TextureMetadata
            {
                TextureName = textureName,
                TexturePath = texturePath,
                SourceUpkPath = sourceUpkPath,
                ExportClass = "<unavailable>",
                ContainerType = "Unavailable",
                SourceDescription = "Texture metadata unavailable.",
                Format = "Unavailable",
                Compression = "Unavailable",
                Width = 0,
                Height = 0,
                MipCount = 0,
                IsTexture2D = false,
                MipSummaries = []
            };
        }
    }

    public async Task OpenTextureAsync(string textureName, string texturePath, string sourceUpkPath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            throw new ArgumentException("Texture path is required.", nameof(texturePath));

        if (App.MainWindow is null)
            throw new InvalidOperationException("Main window is not available.");

        if (string.IsNullOrWhiteSpace(sourceUpkPath) || !File.Exists(sourceUpkPath))
            return;

        string exportPath = string.Empty;
        try
        {
            TextureMetadata? metadata = await GetTextureMetadataAsync(textureName, texturePath, sourceUpkPath).ConfigureAwait(true);
            if (metadata is not null &&
                metadata.IsTexture2D &&
                !string.Equals(metadata.ExportClass, "<missing>", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(metadata.ExportClass, "<unavailable>", StringComparison.OrdinalIgnoreCase))
            {
                exportPath = texturePath;
            }
        }
        catch
        {
            exportPath = string.Empty;
        }

        try
        {
            WorkspaceLaunchContext context = new()
            {
                WorkspaceTag = "textures",
                UpkPath = sourceUpkPath,
                ExportPath = exportPath
            };

            if (App.MainWindow.DispatcherQueue.HasThreadAccess)
            {
                App.MainWindow.NavigateToTag("textures", context);
            }
            else
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() => App.MainWindow.NavigateToTag("textures", context));
            }
        }
        catch (Exception ex)
        {
            App.WriteDiagnosticsLog("MaterialEditor.OpenTexture", ex.ToString());
        }
    }

    public Task ReplaceTextureAsync(string textureName, string texturePath, string sourceUpkPath, string newTextureFilePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            throw new ArgumentException("Texture path is required.", nameof(texturePath));

        if (string.IsNullOrWhiteSpace(newTextureFilePath))
            throw new ArgumentException("Replacement texture path is required.", nameof(newTextureFilePath));

        if (!File.Exists(newTextureFilePath))
            throw new FileNotFoundException("Replacement texture file not found.", newTextureFilePath);

        if (App.MainWindow is null)
            throw new InvalidOperationException("Main window is not available.");

        TextureLoader loader = new();
        TexturePreviewTexture replacement = loader.LoadFromFile(newTextureFilePath, MapSlot(textureName, texturePath));
        SafeInjectService injectService = new();

        return Task.Run(async () =>
        {
            var validation = await injectService.ValidateTargetAsync(sourceUpkPath, texturePath, replacement).ConfigureAwait(true);
            if (!validation.CanInject)
                throw new InvalidOperationException(validation.Reason);

            await injectService.InjectAsync(sourceUpkPath, texturePath, replacement).ConfigureAwait(true);
        });
    }

    public Task<string?> BrowseForNewTextureAsync()
    {
        if (App.MainWindow is null)
            return Task.FromResult<string?>(null);

        FileOpenPicker picker = new();
        foreach (string ext in new[] { ".png", ".dds", ".tga", ".bmp", ".jpg", ".jpeg" })
            picker.FileTypeFilter.Add(ext);

        nint hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        return BrowseAsync(picker);
    }

    private static async Task<string?> BrowseAsync(FileOpenPicker picker)
    {
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static TexturePreviewMaterialSlot MapSlot(string textureName, string texturePath)
    {
        TextureType type = InferTextureType(textureName, texturePath);
        return type switch
        {
            TextureType.Normal => TexturePreviewMaterialSlot.Normal,
            TextureType.Specular => TexturePreviewMaterialSlot.Specular,
            TextureType.Emissive => TexturePreviewMaterialSlot.Emissive,
            TextureType.Mask => TexturePreviewMaterialSlot.Mask,
            _ => TexturePreviewMaterialSlot.Diffuse
        };
    }

    private static TextureType InferTextureType(string textureName, string texturePath)
    {
        string value = $"{textureName} {texturePath}".ToLowerInvariant();
        if (value.Contains("normal") || value.Contains("_n") || value.Contains("_nm") || value.Contains("nrm"))
            return TextureType.Normal;
        if (value.Contains("spec") || value.Contains("gloss") || value.Contains("refl"))
            return TextureType.Specular;
        if (value.Contains("mask") || value.Contains("alpha") || value.Contains("occlusion"))
            return TextureType.Mask;
        if (value.Contains("emit") || value.Contains("glow") || value.Contains("illum"))
            return TextureType.Emissive;
        return TextureType.Diffuse;
    }
}

