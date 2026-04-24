using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;

public sealed class ShaderVariantService
{
    public IReadOnlyList<MhShaderPermutation> BuildVariants(MhShaderReference shaderReference)
    {
        ArgumentNullException.ThrowIfNull(shaderReference);
        return shaderReference.Permutations.Count == 0
            ? [new MhShaderPermutation { Name = "Default", Value = shaderReference.Name, IsActive = true }]
            : shaderReference.Permutations.ToArray();
    }
}

