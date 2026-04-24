using System.Collections.ObjectModel;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialViewModels;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialOverrideTool;

public sealed class MaterialOverrideViewModel : MaterialToolViewModelBase
{
    private MhMaterialInstance? selectedMaterial;
    private MhMaterialOverrideProfile? selectedProfile;

    public MaterialOverrideViewModel()
    {
        Title = "Material Override Tool";
        Profiles = [];
    }

    public ObservableCollection<MhMaterialOverrideProfile> Profiles { get; }

    public MhMaterialInstance? SelectedMaterial
    {
        get => selectedMaterial;
        set
        {
            if (!SetProperty(ref selectedMaterial, value))
                return;

            Profiles.Clear();
            if (selectedMaterial is null)
                return;

            foreach (MhMaterialOverrideProfile profile in selectedMaterial.OverrideProfiles)
                Profiles.Add(profile);

            SelectedProfile = Profiles.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedMaterialSummaryText));
        }
    }

    public MhMaterialOverrideProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value))
                return;

            RaisePropertyChanged(nameof(SelectedProfileSummaryText));
        }
    }

    public string ProfileCountText => $"{Profiles.Count:N0} profile(s)";
    public string SelectedMaterialSummaryText => SelectedMaterial is null
        ? "No material selected."
        : $"Material: {SelectedMaterial.Name}";
    public string SelectedProfileSummaryText => SelectedProfile is null
        ? "No override profile selected."
        : $"Profile: {SelectedProfile.Name} | Tint: {SelectedProfile.TintColor}";
}

