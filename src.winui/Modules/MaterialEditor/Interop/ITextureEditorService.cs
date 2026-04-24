using System.Threading.Tasks;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Interop;

public interface ITextureEditorService
{
    Task<TextureMetadata?> GetTextureMetadataAsync(string textureName, string texturePath, string sourceUpkPath);
    Task OpenTextureAsync(string textureName, string texturePath, string sourceUpkPath);
    Task ReplaceTextureAsync(string textureName, string texturePath, string sourceUpkPath, string newTextureFilePath);
    Task<string?> BrowseForNewTextureAsync();
}

