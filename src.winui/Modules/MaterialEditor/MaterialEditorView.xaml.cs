using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.ColorVariantGenerator;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialServices;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialCore.MaterialModels;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialGraphEditor;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.MaterialOverrideTool;
using OmegaAssetStudio.WinUI.Models;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.ShaderInspector;
using OmegaAssetStudio.WinUI.Modules.MaterialEditor.ShaderVariantTester;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor;

public sealed partial class MaterialEditorView : Page
{
    private readonly MaterialEditorContext materialEditorContext = new();
    private readonly MaterialEditorViewModel viewModel;
    private readonly List<MaterialToolEntry> materialTools = [];
    private readonly MaterialGraphEditorView materialGraphEditorView;
    private readonly ShaderInspectorView shaderInspectorView;
    private readonly ShaderVariantTesterView shaderVariantTesterView;
    private readonly MaterialOverrideView materialOverrideView;
    private readonly ColorVariantGeneratorView colorVariantGeneratorView;

    public MaterialEditorView()
    {
        InitializeComponent();
        viewModel = new MaterialEditorViewModel();
        DataContext = viewModel;
        NavigationCacheMode = NavigationCacheMode.Required;
        viewModel.AttachContext(materialEditorContext);
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        materialGraphEditorView = new MaterialGraphEditorView { DataContext = new MaterialGraphEditorViewModel() };
        shaderInspectorView = new ShaderInspectorView { DataContext = new ShaderInspectorViewModel() };
        shaderVariantTesterView = new ShaderVariantTesterView { DataContext = new ShaderVariantTesterViewModel() };
        materialOverrideView = new MaterialOverrideView { DataContext = new MaterialOverrideViewModel() };
        colorVariantGeneratorView = new ColorVariantGeneratorView { DataContext = new ColorVariantGeneratorViewModel() };
        AttachContexts();
        InitializeMaterialTools();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is not MaterialEditorViewModel viewModel)
            return;

        if (e.Parameter is WorkspaceLaunchContext context)
            await viewModel.LoadFromWorkspaceContextAsync(context).ConfigureAwait(true);
    }

    private void LoadUpk_Click(object sender, RoutedEventArgs e)
    {
        viewModel.LoadMaterialCommand.Execute(null);
    }

    private void SaveMaterial_Click(object sender, RoutedEventArgs e)
    {
        viewModel.SaveMaterialCommand.Execute(null);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ResetSelectedMaterial();
        PreviewHost.ResetPreview();
    }

    private void SelectTextureSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            viewModel.SelectTextureSlot(element.Tag as MaterialTextureSlot);
    }

    private async void OpenTextureInTextures2_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            await viewModel.OpenTextureInTextures2Async(element.Tag as MaterialTextureSlot).ConfigureAwait(true);
    }

    private async void ReplaceTextureSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            await viewModel.ReplaceTextureSlotAsync(element.Tag as MaterialTextureSlot).ConfigureAwait(true);
    }

    private void ResetParameter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            viewModel.ResetParameter(element.Tag as MaterialParameter);
    }

    private void InitializeMaterialTools()
    {
        materialTools.Clear();
        materialTools.Add(new MaterialToolEntry(MaterialWorkspaceTool.LegacyWorkspace, "Legacy Workspace", null));
        materialTools.Add(new MaterialToolEntry(MaterialWorkspaceTool.MaterialGraphEditor, "Material Graph Editor", materialGraphEditorView));
        materialTools.Add(new MaterialToolEntry(MaterialWorkspaceTool.ShaderInspector, "Shader Inspector", shaderInspectorView));
        materialTools.Add(new MaterialToolEntry(MaterialWorkspaceTool.ShaderVariantTester, "Shader Variant Tester", shaderVariantTesterView));
        materialTools.Add(new MaterialToolEntry(MaterialWorkspaceTool.MaterialOverrideTool, "Material Override Tool", materialOverrideView));
        materialTools.Add(new MaterialToolEntry(MaterialWorkspaceTool.ColorVariantGenerator, "Color Variant Generator", colorVariantGeneratorView));
        MaterialToolComboBox.ItemsSource = materialTools;
        MaterialToolComboBox.SelectedIndex = 0;
        MaterialToolHost.Content = null;
    }

    private void AttachContexts()
    {
        (materialGraphEditorView.DataContext as MaterialGraphEditorViewModel)?.AttachContext(materialEditorContext);
        (shaderInspectorView.DataContext as ShaderInspectorViewModel)?.AttachContext(materialEditorContext);
        (shaderVariantTesterView.DataContext as ShaderVariantTesterViewModel)?.AttachContext(materialEditorContext);
        (materialOverrideView.DataContext as MaterialOverrideViewModel)?.AttachContext(materialEditorContext);
        (colorVariantGeneratorView.DataContext as ColorVariantGeneratorViewModel)?.AttachContext(materialEditorContext);
        materialEditorContext.SharedPreview.AttachContext(materialEditorContext);
    }

    private void MaterialToolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MaterialToolComboBox.SelectedItem is not MaterialToolEntry entry)
            return;

        materialEditorContext.SetActiveTool(entry.Tool);
        MaterialToolHost.Content = entry.View;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MaterialEditorViewModel.SelectedMaterial), StringComparison.Ordinal))
            return;

        if (viewModel.SelectedMaterial is null)
            return;

        MhMaterialInstance instance = materialEditorContext.SelectedMaterial ?? materialEditorContext.Services.MaterialInstances.BuildInstance(viewModel.SelectedMaterial);
        if (materialGraphEditorView.DataContext is MaterialGraphEditorViewModel graphViewModel)
            graphViewModel.LoadMaterial(instance);

        if (shaderInspectorView.DataContext is ShaderInspectorViewModel shaderInspectorViewModel)
            shaderInspectorViewModel.SelectedShader = instance.ShaderReference;

        if (shaderVariantTesterView.DataContext is ShaderVariantTesterViewModel shaderVariantTesterViewModel)
            shaderVariantTesterViewModel.SelectedShader = instance.ShaderReference;

        if (materialOverrideView.DataContext is MaterialOverrideViewModel materialOverrideViewModel)
            materialOverrideViewModel.SelectedMaterial = instance;

        if (colorVariantGeneratorView.DataContext is ColorVariantGeneratorViewModel colorVariantGeneratorViewModel)
            colorVariantGeneratorViewModel.SelectedMaterial = instance;
    }

    private sealed record MaterialToolEntry(MaterialWorkspaceTool Tool, string DisplayName, object? View)
    {
        public override string ToString() => DisplayName;
    }
}

