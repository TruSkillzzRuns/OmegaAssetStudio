using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;

public sealed class ShaderService
{
    public MhShaderReference BuildReference(string shaderName, string sourcePath, string sourceUpkPath)
    {
        return new MhShaderReference
        {
            Name = shaderName,
            SourcePath = sourcePath,
            SourceUpkPath = sourceUpkPath,
            Permutations =
            [
                new MhShaderPermutation { Name = "Default", Value = shaderName, IsActive = true }
            ]
        };
    }
}

