namespace OmegaAssetStudio.MeshPreview;

internal interface IMeshPreviewViewportBackend : IDisposable
{
    Control View { get; }
    void ResetCamera();
    void RefreshPreview();
}

