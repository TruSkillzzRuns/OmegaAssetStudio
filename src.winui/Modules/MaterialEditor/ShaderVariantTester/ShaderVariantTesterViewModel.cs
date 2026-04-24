using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ShaderVariantTester;

public sealed class ShaderVariantTesterViewModel : MaterialToolViewModelBase
{
    private MhShaderReference? selectedShader;
    private MhShaderPermutation? selectedPermutation;

    public ShaderVariantTesterViewModel()
    {
        Title = "Shader Variant Tester";
        Variants = [];
    }

    public ObservableCollection<MhShaderPermutation> Variants { get; }

    public MhShaderReference? SelectedShader
    {
        get => selectedShader;
        set
        {
            if (!SetProperty(ref selectedShader, value))
                return;

            Variants.Clear();
            if (selectedShader is null)
                return;

            foreach (MhShaderPermutation permutation in selectedShader.Permutations)
                Variants.Add(permutation);

            SelectedPermutation = Variants.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedShaderSummaryText));
        }
    }

    public MhShaderPermutation? SelectedPermutation
    {
        get => selectedPermutation;
        set
        {
            if (!SetProperty(ref selectedPermutation, value))
                return;

            RaisePropertyChanged(nameof(SelectedPermutationSummaryText));
        }
    }

    public string VariantCountText => $"{Variants.Count:N0} variant(s)";
    public string SelectedShaderSummaryText => SelectedShader is null
        ? "No shader selected."
        : $"Shader: {SelectedShader.Name} | Source: {SelectedShader.SourcePath}";
    public string SelectedPermutationSummaryText => SelectedPermutation is null
        ? "No variant selected."
        : $"Variant: {SelectedPermutation.Name} | Value: {SelectedPermutation.Value}";
}

