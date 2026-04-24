using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.ShaderInspector;

public sealed class ShaderInspectorViewModel : MaterialToolViewModelBase
{
    private MhShaderReference? selectedShader;
    private MhShaderPermutation? selectedPermutation;

    public ShaderInspectorViewModel()
    {
        Title = "Shader Inspector";
        Permutations = [];
    }

    public ObservableCollection<MhShaderPermutation> Permutations { get; }

    public MhShaderReference? SelectedShader
    {
        get => selectedShader;
        set
        {
            if (!SetProperty(ref selectedShader, value))
                return;

            Permutations.Clear();
            if (selectedShader is null)
                return;

            foreach (MhShaderPermutation permutation in selectedShader.Permutations)
                Permutations.Add(permutation);

            SelectedPermutation = Permutations.FirstOrDefault();
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

    public string PermutationCountText => $"{Permutations.Count:N0} permutation(s)";
    public string SelectedShaderSummaryText => SelectedShader is null
        ? "No shader selected."
        : $"Shader: {SelectedShader.Name} | Source: {SelectedShader.SourcePath}";
    public string SelectedPermutationSummaryText => SelectedPermutation is null
        ? "No permutation selected."
        : $"Permutation: {SelectedPermutation.Name} | Value: {SelectedPermutation.Value}";
}

