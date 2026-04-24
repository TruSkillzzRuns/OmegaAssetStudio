using System.Numerics;
using System.Runtime.InteropServices;
using OmegaAssetStudio.MeshPreview;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using OmegaAssetStudio.TexturePreview;
using OmegaAssetStudio.WinUI;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using WinRT;
using static Vortice.Direct3D11.D3D11;

namespace OmegaAssetStudio.WinUI.Rendering;

internal sealed class MeshPreviewD3D11Renderer : IDisposable
{
    public event EventHandler? RenderCompleted;

    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGISwapChain1? swapChain;
    private ID3D11RenderTargetView? renderTargetView;
    private ID3D11Texture2D? depthTexture;
    private ID3D11DepthStencilView? depthStencilView;
    private ID3D11VertexShader? meshVertexShader;
    private ID3D11PixelShader? meshPixelShader;
    private ID3D11InputLayout? meshInputLayout;
    private ID3D11VertexShader? lineVertexShader;
    private ID3D11PixelShader? linePixelShader;
    private ID3D11InputLayout? lineInputLayout;
    private ID3D11VertexShader? testTriangleVertexShader;
    private ID3D11PixelShader? testTrianglePixelShader;
    private ID3D11InputLayout? testTriangleInputLayout;
    private ID3D11Buffer? meshConstantBuffer;
    private ID3D11Buffer? lineConstantBuffer;
    private ID3D11SamplerState? textureSampler;
    private ID3D11RasterizerState? solidRasterizerState;
    private ID3D11RasterizerState? wireframeRasterizerState;
    private ID3D11RasterizerState? solidRasterizerStateNoCull;
    private ID3D11RasterizerState? wireframeRasterizerStateNoCull;
private readonly Dictionary<MeshPreviewMesh, CachedMeshBuffers> meshBuffers = [];
private readonly Dictionary<string, ID3D11ShaderResourceView> gameMaterialTextures = [];
private readonly HashSet<string> currentFrameGameApproxBranches = [];
private readonly List<string> currentFrameGameApproxSectionStates = [];
    private SwapChainPanel? currentPanel;
    private int width;
    private int height;
    private bool disposed;
    private bool lastRenderSucceeded;
    private SwapChainPanel? attachedPanel;
    private DispatcherQueue? dispatcherQueue;
    private bool panelLoaded;
    private bool renderQueued;
    private bool waitingForUsablePanelSize;
    private MeshPreviewScene? currentScene;
    private MeshPreviewCamera? currentCamera;
    private bool useExternalMatrices;
    private Matrix4x4 externalViewMatrix;
    private Matrix4x4 externalProjectionMatrix;
    public string Diagnostics { get; private set; } = "D3D11 renderer not initialized.";
    public bool LastRenderSucceeded => lastRenderSucceeded;

    public void AttachToPanel(SwapChainPanel panel, DispatcherQueue dispatcherQueue)
    {
        if (disposed || ReferenceEquals(attachedPanel, panel))
            return;

        DetachPanel();
        this.attachedPanel = panel;
        this.dispatcherQueue = dispatcherQueue;
        panel.Loaded += Panel_Loaded;
        panel.SizeChanged += Panel_SizeChanged;
        panel.Unloaded += Panel_Unloaded;
        panelLoaded = panel.XamlRoot is not null && panel.ActualWidth > 0 && panel.ActualHeight > 0;
    }

    public void DetachPanel()
    {
        if (attachedPanel is not null)
        {
            attachedPanel.Loaded -= Panel_Loaded;
            attachedPanel.SizeChanged -= Panel_SizeChanged;
            attachedPanel.Unloaded -= Panel_Unloaded;
        }

        attachedPanel = null;
        panelLoaded = false;
        renderQueued = false;
    }

    public void SetFrame(MeshPreviewScene scene, MeshPreviewCamera camera)
    {
        if (disposed)
            return;

        currentScene = scene;
        currentCamera = camera;
        useExternalMatrices = false;
        QueueRender();
    }

    public void SetFrame(MeshPreviewScene scene, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
    {
        if (disposed)
            return;

        currentScene = scene;
        currentCamera = null;
        externalViewMatrix = viewMatrix;
        externalProjectionMatrix = projectionMatrix;
        useExternalMatrices = true;
        QueueRender();
    }

    private void Panel_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        panelLoaded = true;
        QueueRender();
    }

    private void Panel_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        if (disposed)
            return;

        panelLoaded = true;
        QueueRender();
    }

    private void Panel_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        panelLoaded = false;
        ReleaseRenderTargets();
        swapChain?.Dispose();
        swapChain = null;
        currentPanel = null;
    }

    private void QueueRender()
    {
        if (disposed || dispatcherQueue is null || renderQueued || attachedPanel is null)
            return;

        renderQueued = true;
        dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (disposed || attachedPanel is null)
                    return;

                RenderAttachedPanel();
            }
            finally
            {
                renderQueued = false;
            }
        });
    }

    public void RenderAttachedPanel()
    {
        lastRenderSucceeded = false;
        string stage = "start";
        try
        {
            stage = "validate-panel";
            if (disposed || attachedPanel is null)
            {
                Diagnostics = "D3D11 render skipped: invalid renderer state or panel state.";
                return;
            }

            stage = "validate-scene";
            bool cameraReady = useExternalMatrices || currentCamera is not null;
            if (currentScene is null || !cameraReady)
            {
                Diagnostics = $"D3D11 render skipped: scene or camera not ready. ExternalMatrices={useExternalMatrices}, Scene={(currentScene is null ? "null" : "ready")}, Camera={(currentCamera is null ? "null" : "ready")}.";
                return;
            }

            double panelWidth = GetEffectiveSize(attachedPanel.ActualWidth, attachedPanel.Width, attachedPanel.MinWidth);
            double panelHeight = GetEffectiveSize(attachedPanel.ActualHeight, attachedPanel.Height, attachedPanel.MinHeight);
            FrameworkElement? hostElement = attachedPanel.Tag as FrameworkElement;
            if ((panelWidth <= 1 || panelHeight <= 1) && hostElement is not null)
            {
                panelWidth = Math.Max(panelWidth, GetEffectiveSize(hostElement.ActualWidth, hostElement.Width, hostElement.MinWidth));
                panelHeight = Math.Max(panelHeight, GetEffectiveSize(hostElement.ActualHeight, hostElement.Height, hostElement.MinHeight));
            }
            if ((panelWidth <= 1 || panelHeight <= 1) && attachedPanel.Parent is FrameworkElement host)
            {
                panelWidth = Math.Max(panelWidth, GetEffectiveSize(host.ActualWidth, host.Width, host.MinWidth));
                panelHeight = Math.Max(panelHeight, GetEffectiveSize(host.ActualHeight, host.Height, host.MinHeight));
            }

            int renderWidth = (int)Math.Max(1, Math.Round(panelWidth));
            int renderHeight = (int)Math.Max(1, Math.Round(panelHeight));
            if (renderWidth < 32 || renderHeight < 32)
            {
                Diagnostics = $"D3D11 waiting for usable panel size: {renderWidth}x{renderHeight}. Host={hostElement?.ActualWidth ?? 0:0}x{hostElement?.ActualHeight ?? 0:0}.";
                if (!waitingForUsablePanelSize && dispatcherQueue is not null)
                {
                    waitingForUsablePanelSize = true;
                    dispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            await Task.Delay(50).ConfigureAwait(true);
                        }
                        catch
                        {
                        }
                        finally
                        {
                            waitingForUsablePanelSize = false;
                        }

                        if (!disposed && attachedPanel is not null && currentScene is not null && currentCamera is not null)
                            QueueRender();
                    });
                }
                return;
            }
            waitingForUsablePanelSize = false;
            stage = "ensure-device";
            EnsureDevice(attachedPanel, renderWidth, renderHeight);
            stage = "validate-device";
            if (device is null || context is null || swapChain is null || renderTargetView is null || depthStencilView is null)
            {
                if (string.IsNullOrWhiteSpace(Diagnostics) ||
                    Diagnostics.StartsWith("D3D11 renderer not initialized", StringComparison.Ordinal) ||
                    Diagnostics.StartsWith("D3D11 render skipped:", StringComparison.Ordinal))
                {
                    Diagnostics = "D3D11 render skipped: device or swap chain is not ready.";
                }
                return;
            }

            stage = "set-viewport";
            context.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, renderWidth, renderHeight));
            stage = "set-targets";
            context.OMSetRenderTargets(renderTargetView, depthStencilView);
            stage = "clear-rt";
            context.ClearRenderTargetView(renderTargetView, ResolveClearColor(currentScene.BackgroundStyle));
            stage = "clear-depth";
            context.ClearDepthStencilView(depthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
            float aspect = Math.Max(1, renderWidth) / (float)Math.Max(1, renderHeight);
            Matrix4x4 projection = useExternalMatrices
                ? externalProjectionMatrix
                : currentCamera!.GetProjectionMatrix(aspect);
            Matrix4x4 view = useExternalMatrices
                ? externalViewMatrix
                : currentCamera!.GetViewMatrix();
            int fbxSectionsDrawn = 0;
            int ue3SectionsDrawn = 0;
            currentFrameGameApproxBranches.Clear();
            currentFrameGameApproxSectionStates.Clear();

        stage = "draw-ground";
        if (currentScene.ShowGroundPlane)
            DrawGround(currentScene, projection, view);

        stage = "upload-fbx-buffers";
        if (currentScene.FbxMesh is not null)
            EnsureMeshBuffersReady(currentScene.FbxMesh);
        stage = "upload-ue3-buffers";
        if (currentScene.Ue3Mesh is not null)
            EnsureMeshBuffersReady(currentScene.Ue3Mesh);

        stage = "draw-meshes";
        if (currentScene.DisplayMode == MeshPreviewDisplayMode.SideBySide)
        {
            int leftWidth = Math.Max(1, renderWidth / 2);
            int rightWidth = Math.Max(1, renderWidth - leftWidth);

            if (currentScene.ShowFbxMesh && currentScene.FbxMesh is not null)
            {
                context.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, leftWidth, renderHeight));
                fbxSectionsDrawn = DrawMesh(currentScene, currentCamera, currentScene.FbxMesh, projection, view, false);
            }

            if (currentScene.ShowUe3Mesh && currentScene.Ue3Mesh is not null)
            {
                context.RSSetViewport(new Vortice.Mathematics.Viewport(leftWidth, 0, rightWidth, renderHeight));
                ue3SectionsDrawn = DrawMesh(currentScene, currentCamera, currentScene.Ue3Mesh, projection, view, true);
            }
        }
        else
        {
            if (currentScene.ShowFbxMesh && currentScene.FbxMesh is not null && currentScene.DisplayMode != MeshPreviewDisplayMode.Ue3Only)
                fbxSectionsDrawn = DrawMesh(currentScene, currentCamera, currentScene.FbxMesh, projection, view, false);

            if (currentScene.ShowUe3Mesh && currentScene.Ue3Mesh is not null && currentScene.DisplayMode != MeshPreviewDisplayMode.FbxOnly)
                ue3SectionsDrawn = DrawMesh(currentScene, currentCamera, currentScene.Ue3Mesh, projection, view, true);
        }

            stage = "present";
            swapChain.Present(1, PresentFlags.None);
            lastRenderSucceeded = true;
            Diagnostics = BuildFrameDiagnostics(renderWidth, renderHeight, fbxSectionsDrawn, ue3SectionsDrawn);
            App.WriteDiagnosticsLog("Mesh.RenderPreview", Diagnostics);
            RenderCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 render failed at {stage}: 0x{ex.HResult:X8} {ex.Message}";
            App.WriteDiagnosticsLog("Mesh.RenderPreview", Diagnostics);
        }
    }

    public void Clear()
    {
        if (context is null || swapChain is null || renderTargetView is null)
            return;

        context.OMSetRenderTargets(renderTargetView, null);
        context.ClearRenderTargetView(renderTargetView, new Vortice.Mathematics.Color4(0.07f, 0.08f, 0.09f, 1.0f));
        swapChain.Present(0, PresentFlags.None);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        foreach ((_, CachedMeshBuffers buffers) in meshBuffers)
            buffers.Dispose();

        meshBuffers.Clear();
        depthStencilView?.Dispose();
        depthTexture?.Dispose();
        renderTargetView?.Dispose();
        swapChain?.Dispose();
        meshVertexShader?.Dispose();
        meshPixelShader?.Dispose();
        meshInputLayout?.Dispose();
        lineVertexShader?.Dispose();
        linePixelShader?.Dispose();
        lineInputLayout?.Dispose();
        testTriangleVertexShader?.Dispose();
        testTrianglePixelShader?.Dispose();
        testTriangleInputLayout?.Dispose();
        meshConstantBuffer?.Dispose();
        lineConstantBuffer?.Dispose();
        textureSampler?.Dispose();
        solidRasterizerState?.Dispose();
        wireframeRasterizerState?.Dispose();
        solidRasterizerStateNoCull?.Dispose();
        wireframeRasterizerStateNoCull?.Dispose();
        foreach ((_, ID3D11ShaderResourceView texture) in gameMaterialTextures)
            texture?.Dispose();
        gameMaterialTextures.Clear();
        context?.Dispose();
        device?.Dispose();
    }

    private void EnsureDevice(SwapChainPanel panel, int renderWidth, int renderHeight)
    {
        string stage = "start";
        try
        {
            if (device is null || context is null)
            {
                stage = "create-device-resources";
                CreateDeviceResources();
            }

            if (device is null || context is null)
                return;

            bool panelChanged = !ReferenceEquals(currentPanel, panel);
            bool sizeChanged = width != renderWidth || height != renderHeight;
            bool createdSwapChain = false;
            if (swapChain is null || panelChanged)
            {
                stage = "create-swapchain";
                CreateSwapChain(panel, renderWidth, renderHeight);
                createdSwapChain = swapChain is not null;
            }

            if (swapChain is null || renderTargetView is null || depthStencilView is null)
                return;

            if (createdSwapChain)
                return;

            if (!sizeChanged)
                return;

            width = renderWidth;
            height = renderHeight;
            stage = "reset-output-bindings";
            ResetOutputBindings();
            stage = "release-render-targets";
            ReleaseRenderTargets();
            try
            {
                stage = "resize-buffers";
                Diagnostics = $"D3D11 resizing swapchain to {renderWidth}x{renderHeight}.";
                swapChain.ResizeBuffers(0, (uint)renderWidth, (uint)renderHeight, Format.Unknown, SwapChainFlags.None);
            }
            catch (Exception ex)
            {
                Diagnostics = $"D3D11 resize failed at {stage}: 0x{ex.HResult:X8} {ex.Message}";
                return;
            }

            stage = "create-render-targets";
            CreateRenderTargets(renderWidth, renderHeight);
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 ensure-device failed at {stage}: 0x{ex.HResult:X8} {ex.Message}";
            throw;
        }
    }

    private void CreateDeviceResources()
    {
        string stage = "start";
        try
        {
            FeatureLevel[] levels =
            [
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            ];
            List<string> attempts = [];

            stage = "create-device";
            if (!TryCreateDevice(DriverType.Hardware, GetPreferredFlags(), levels, out device, out context, out string? failure))
            {
                attempts.Add($"Hardware+PreferredFlags failed: {failure}");
                if (!TryCreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out device, out context, out failure))
                {
                    attempts.Add($"Hardware+BgraSupport failed: {failure}");
                    if (!TryCreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out device, out context, out failure))
                    {
                        attempts.Add($"WARP+BgraSupport failed: {failure}");
                        throw new InvalidOperationException($"Unable to create D3D11 preview device. {string.Join(" | ", attempts)}");
                    }

                    Diagnostics = "D3D11 device created with WARP fallback.";
                }
                else
                {
                    Diagnostics = "D3D11 hardware device created without debug layer.";
                }
            }
            else
            {
                Diagnostics = "D3D11 hardware device created.";
            }

            stage = "compile-shaders";
            using Blob meshVsBlob = CompileShader(MeshVertexShaderSource, "VSMain", "vs_5_0");
            using Blob meshPsBlob = CompileShader(MeshPixelShaderSource, "PSMain", "ps_5_0");
            using Blob lineVsBlob = CompileShader(LineVertexShaderSource, "VSMain", "vs_5_0");
            using Blob linePsBlob = CompileShader(LinePixelShaderSource, "PSMain", "ps_5_0");
            using Blob triVsBlob = CompileShader(TestTriangleVertexShaderSource, "VSMain", "vs_5_0");
            using Blob triPsBlob = CompileShader(TestTrianglePixelShaderSource, "PSMain", "ps_5_0");

            stage = "create-shaders";
            meshVertexShader = device!.CreateVertexShader(meshVsBlob);
            meshPixelShader = device.CreatePixelShader(meshPsBlob);
            lineVertexShader = device.CreateVertexShader(lineVsBlob);
            linePixelShader = device.CreatePixelShader(linePsBlob);
            testTriangleVertexShader = device.CreateVertexShader(triVsBlob);
            testTrianglePixelShader = device.CreatePixelShader(triPsBlob);

            stage = "create-input-layouts";
            meshInputLayout = device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TANGENT", 0, Format.R32G32B32_Float, 24, 0),
                new InputElementDescription("BINORMAL", 0, Format.R32G32B32_Float, 36, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 48, 0),
                new InputElementDescription("BONEINDICES", 0, Format.R32G32B32A32_SInt, 56, 0),
                new InputElementDescription("BONEWEIGHTS", 0, Format.R32G32B32A32_Float, 72, 0),
                new InputElementDescription("SECTIONINDEX", 0, Format.R32_SInt, 88, 0)
            ], meshVsBlob);

            lineInputLayout = device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)
            ], lineVsBlob);

            textureSampler = device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.Anisotropic,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MaxAnisotropy = 16
            });

            testTriangleInputLayout = device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)
            ], triVsBlob);

            stage = "create-buffers";
            uint meshConstantsSize = AlignConstantBufferSize((uint)Marshal.SizeOf<MeshConstants>());
            uint lineConstantsSize = AlignConstantBufferSize((uint)Marshal.SizeOf<LineConstants>());
            meshConstantBuffer = device.CreateBuffer(new BufferDescription(meshConstantsSize, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
            lineConstantBuffer = device.CreateBuffer(new BufferDescription(lineConstantsSize, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));

            stage = "create-rasterizer-states";
            solidRasterizerState = device.CreateRasterizerState(new RasterizerDescription(CullMode.Back, FillMode.Solid));
            wireframeRasterizerState = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Wireframe));
            solidRasterizerStateNoCull = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
            wireframeRasterizerStateNoCull = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Wireframe));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"CreateDeviceResources failed at {stage}: {ex.Message}", ex);
        }
    }

    private void CreateSwapChain(SwapChainPanel panel, int renderWidth, int renderHeight)
    {
        if (device is null)
            return;

        ReleaseRenderTargets();
        swapChain?.Dispose();

        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();

        SwapChainDescription1 description = new()
        {
            Width = (uint)Math.Max(1, renderWidth),
            Height = (uint)Math.Max(1, renderHeight),
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Premultiplied
        };

        try
        {
            Diagnostics = $"D3D11 creating swapchain {renderWidth}x{renderHeight}.";
            swapChain = factory.CreateSwapChainForComposition(device, description);
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 CreateSwapChainForComposition failed: 0x{ex.HResult:X8} {ex.Message}";
            swapChain = null;
            return;
        }

        currentPanel = panel;
        width = renderWidth;
        height = renderHeight;

        try
        {
            Diagnostics = "D3D11 binding swapchain to SwapChainPanel.";
            IntPtr panelUnknown = Marshal.GetIUnknownForObject(panel);
            IntPtr panelNativePtr = IntPtr.Zero;
            try
            {
                Guid iid = typeof(ISwapChainPanelNativeInterop).GUID;
                Marshal.QueryInterface(panelUnknown, ref iid, out panelNativePtr);
                ISwapChainPanelNativeInterop panelNative = (ISwapChainPanelNativeInterop)Marshal.GetObjectForIUnknown(panelNativePtr);
                panelNative.SetSwapChain(swapChain.NativePointer);
            }
            finally
            {
                if (panelNativePtr != IntPtr.Zero)
                    Marshal.Release(panelNativePtr);

                if (panelUnknown != IntPtr.Zero)
                    Marshal.Release(panelUnknown);
            }
            context?.Flush();
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 SetSwapChain failed: 0x{ex.HResult:X8} {ex.Message}";
            ReleaseRenderTargets();
            swapChain?.Dispose();
            swapChain = null;
            return;
        }

        try
        {
            Diagnostics = $"D3D11 creating render targets {renderWidth}x{renderHeight}.";
            CreateRenderTargets(renderWidth, renderHeight);
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 CreateRenderTargets failed: 0x{ex.HResult:X8} {ex.Message}";
            ReleaseRenderTargets();
            swapChain?.Dispose();
            swapChain = null;
        }
    }

    private void CreateRenderTargets(int renderWidth, int renderHeight)
    {
        if (device is null || swapChain is null)
            return;

        using ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        renderTargetView = device.CreateRenderTargetView(backBuffer);

        Texture2DDescription depthDescription = new()
        {
            Width = (uint)Math.Max(1, renderWidth),
            Height = (uint)Math.Max(1, renderHeight),
            ArraySize = 1,
            MipLevels = 1,
            Format = Format.D24_UNorm_S8_UInt,
            BindFlags = BindFlags.DepthStencil,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default
        };

        depthTexture = device.CreateTexture2D(depthDescription);
        depthStencilView = device.CreateDepthStencilView(depthTexture);
    }

    private void ReleaseRenderTargets()
    {
        depthStencilView?.Dispose();
        depthStencilView = null;
        depthTexture?.Dispose();
        depthTexture = null;
        renderTargetView?.Dispose();
        renderTargetView = null;
    }

    private void ResetOutputBindings()
    {
        if (context is null)
            return;

        context.OMSetRenderTargets((ID3D11RenderTargetView?)null!, null);
        context.ClearState();
        context.Flush();
    }

    private static DeviceCreationFlags GetPreferredFlags()
    {
        DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        flags |= DeviceCreationFlags.Debug;
#endif
        return flags;
    }

    private static bool TryCreateDevice(
        DriverType driverType,
        DeviceCreationFlags flags,
        FeatureLevel[] levels,
        out ID3D11Device? createdDevice,
        out ID3D11DeviceContext? createdContext,
        out string? failure)
    {
        failure = "Unknown D3D11 device creation failure.";
        try
        {
            D3D11CreateDevice(null, driverType, flags, levels, out createdDevice, out createdContext).CheckError();
            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        createdDevice = null;
        createdContext = null;
        return false;
    }

    private static uint AlignConstantBufferSize(uint size)
    {
        return (size + 15u) & ~15u;
    }

    private int DrawMesh(MeshPreviewScene scene, MeshPreviewCamera? camera, MeshPreviewMesh mesh, Matrix4x4 projection, Matrix4x4 view, bool isUe3Mesh)
    {
        if (context is null || meshInputLayout is null || meshVertexShader is null || meshPixelShader is null || meshConstantBuffer is null || solidRasterizerState is null || wireframeRasterizerState is null || solidRasterizerStateNoCull is null || wireframeRasterizerStateNoCull is null)
            return 0;

        if (!TryGetMeshBuffers(mesh, out CachedMeshBuffers? buffers))
            return 0;

        if (buffers is null)
            return 0;

        MeshConstants constants = new()
        {
            Projection = projection,
            View = view,
            Model = ResolveModelMatrix(scene, isUe3Mesh),
            CameraPosition = camera?.GetPosition() ?? Vector3.Zero,
            AmbientLight = scene.AmbientLight,
            BaseColor = ResolveBaseColor(scene),
            LightDirection = ResolveLightDirection(scene.LightingPreset),
            Light0Color = new Vector3(0.90f, 0.90f, 0.90f),
            Light1Direction = Vector3.Normalize(new Vector3(-1.0f, -1.0f, 1.0f)),
            Light1Color = new Vector3(0.60f, 0.60f, 0.60f),
            LightingPreset = (int)scene.LightingPreset,
            MaterialChannel = (int)scene.MaterialChannel,
            ShadingMode = (int)scene.ShadingMode,
            ShowSections = scene.ShowSections ? 1 : 0,
            HighlightSection = 0,
            SectionColor = Vector4.One,
            SelectedBone = ResolveBoneIndex(mesh, scene.SelectedBoneName),
            WeightMode = scene.ShowWeights ? 1 : 0,
            ShowWeights = scene.ShowWeights ? 1 : 0,
            WeightViewMode = (int)scene.WeightViewMode
        };

        context.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, width, height));
        if (meshInputLayout is null)
            return 0;

        context.IASetInputLayout(meshInputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetVertexBuffer(0, buffers.VertexBuffer, (uint)Marshal.SizeOf<D3DMeshVertex>());
        context.IASetIndexBuffer(buffers.IndexBuffer, Format.R32_UInt, 0);
        context.VSSetShader(meshVertexShader);
        context.PSSetShader(meshPixelShader);
        context.VSSetConstantBuffer(0, meshConstantBuffer);
        context.PSSetConstantBuffer(0, meshConstantBuffer);
        if (textureSampler is not null)
            context.PSSetSampler(0, textureSampler);
        context.RSSetState(ResolveRasterizerState(scene, isUe3Mesh));

        int sectionsDrawn = 0;
        try
        {
            Diagnostics = $"D3D11 draw-mesh start: mesh={mesh.Name ?? "<unnamed>"}, isUe3={isUe3Mesh}, camera={(camera is null ? "external" : "scene")}, sections={mesh.Sections.Count}, verts={mesh.Vertices.Count}, tris={mesh.Indices.Count / 3}.";
            foreach (MeshPreviewSection section in mesh.Sections)
            {
                if (!scene.IsSectionVisible(isUe3Mesh, section.Index))
                    continue;

                ApplySectionMaterialState(scene, section, isUe3Mesh, ref constants);
                constants.SectionColor = section.Color;
                constants.HighlightSection = scene.IsSectionHighlighted(isUe3Mesh, section.Index) ? 1 : 0;
                if (constants.UseGameMaterial > 0.5f)
                {
                    currentFrameGameApproxSectionStates.Add(
                        $"S{section.Index}: UseGM={constants.UseGameMaterial:0} MatCh={constants.MaterialChannel} Diff={constants.UseDiffuseMap:0}/{constants.HasDiffuseMap:0} Norm={constants.UseNormalMap:0}/{constants.HasNormalMap:0} Smspsk={constants.HasSmspsk:0} Espa={constants.HasEspa:0} Smrr={constants.HasSmrr:0} SpecColor={constants.HasSpecColorMap:0} SpecMult={constants.SpecMult:F2} SpecLq={constants.SpecMultLq:F2} NormStrength={constants.NormalStrength:F2}");
                }
                context.UpdateSubresource(in constants, meshConstantBuffer);
                context.DrawIndexed((uint)section.IndexCount, (uint)section.BaseIndex, 0);
                sectionsDrawn++;
            }

            if (sectionsDrawn == 0 && mesh.Indices.Count > 0)
            {
                context.UpdateSubresource(in constants, meshConstantBuffer);
                context.DrawIndexed((uint)mesh.Indices.Count, 0, 0);
                sectionsDrawn = Math.Max(1, mesh.Sections.Count);
            }
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 draw failed: 0x{ex.HResult:X8} {ex.Message}";
            return sectionsDrawn;
        }

        if (scene.ShowNormals)
            DrawLines(buffers.NormalLines, buffers.NormalLineCount, projection, view, new Vector4(1f, 0.1f, 0.9f, 1f));
        if (scene.ShowTangents)
            DrawLines(buffers.TangentLines, buffers.TangentLineCount, projection, view, new Vector4(0.1f, 1f, 1f, 1f));
        if (scene.ShowUvSeams)
            DrawLines(buffers.UvSeamLines, buffers.UvSeamLineCount, projection, view, new Vector4(1f, 0.8f, 0.2f, 1f));
        if (scene.ShowBones)
            DrawBoneOverlay(mesh, projection, view);

        return sectionsDrawn;
    }

    private ID3D11RasterizerState ResolveRasterizerState(MeshPreviewScene scene, bool isUe3Mesh)
    {
        bool disableBackfaceCulling = isUe3Mesh ? scene.DisableBackfaceCullingForUe3 : scene.DisableBackfaceCullingForFbx;
        if (isUe3Mesh)
            disableBackfaceCulling = true;
        if (scene.Wireframe)
            return disableBackfaceCulling ? wireframeRasterizerStateNoCull! : wireframeRasterizerState!;

        return disableBackfaceCulling ? solidRasterizerStateNoCull! : solidRasterizerState!;
    }

    private void ApplySectionMaterialState(MeshPreviewScene scene, MeshPreviewSection section, bool isUe3Mesh, ref MeshConstants constants)
    {
        if (context is null)
            return;

        for (uint slot = 0; slot <= 8; slot++)
            context.PSSetShaderResource(slot, null!);

        constants.UseGameMaterial = 0;
        constants.HasDiffuseMap = 0f;
        constants.HasNormalMap = 0f;
        constants.HasSmspsk = 0f;
        constants.HasEspa = 0f;
        constants.HasSmrr = 0f;
        constants.HasSpecColorMap = 0f;
        constants.AlphaTest = 0f;

        bool useGameMaterial = scene.ShadingMode == MeshPreviewShadingMode.GameApprox &&
            section.GameMaterial?.Enabled == true;

        if (!useGameMaterial)
            return;

        MeshPreviewGameMaterial material = section.GameMaterial!;
        constants.UseGameMaterial = 1;
        constants.ShadingMode = (int)MeshPreviewShadingMode.Lit;
        constants.MaterialChannel = (int)scene.MaterialChannel;
        constants.UseDiffuseMap = material.HasTexture(MeshPreviewGameTextureSlot.Diffuse) ? 1 : 0;
        constants.UseNormalMap = 0;
        constants.UseSpecularMap = 0;
        constants.UseEmissiveMap = 0;
        constants.UseMaskMap = 0;
        constants.AlphaTest = material.BlendMode == UpkManager.Models.UpkFile.Engine.Material.EBlendMode.BLEND_Masked ? 1.0f : 0.0f;
        constants.LambertDiffusePower = material.LambertDiffusePower;
        constants.PhongDiffusePower = material.PhongDiffusePower;
        constants.LightingAmbient = material.LightingAmbient;
        constants.ShadowAmbientMult = material.ShadowAmbientMult;
        constants.NormalStrength = material.NormalStrength;
        constants.ReflectionMult = material.ReflectionMult;
        constants.RimColorMult = material.RimColorMult;
        constants.RimFalloff = material.RimFalloff;
        constants.ScreenLightAmount = material.ScreenLightAmount;
        constants.ScreenLightMult = material.ScreenLightMult;
        constants.ScreenLightPower = material.ScreenLightPower;
        constants.SpecMult = material.SpecMult;
        constants.SpecMultLq = material.SpecMultLq;
        constants.SpecularPower = material.SpecularPower;
        constants.SpecularPowerMask = material.SpecularPowerMask;
        constants.LambertAmbient = Vector3.Max(material.LambertAmbient, new Vector3(0.28f, 0.28f, 0.28f));
        constants.SkinScatterStrength = material.SkinScatterStrength;
        constants.ShadowAmbientColor = new Vector3(0.08f, 0.07f, 0.07f);
        constants.TwoSidedLighting = material.TwoSidedLighting;
        constants.FillLightColor = new Vector3(0.0f, 0.0f, 0.0f);
        constants.DiffuseColor = material.DiffuseColor;
        constants.SpecularColor = material.SpecularColor;
        constants.SubsurfaceInscatteringColor = material.SubsurfaceInscatteringColor;
        constants.SubsurfaceAbsorptionColor = material.SubsurfaceAbsorptionColor;
        constants.ImageReflectionNormalDampening = material.ImageReflectionNormalDampening;

        constants.SpecMult = 0.0f;
        constants.SpecMultLq = 0.0f;
        constants.SpecularPowerMask = 0.0f;
        constants.RimColorMult = 0.0f;
        constants.ScreenLightAmount = 0.0f;
        constants.ReflectionMult = 0.0f;
        constants.SkinScatterStrength = 0.0f;
        constants.LightingAmbient = MathF.Max(constants.LightingAmbient, 1.00f);
        constants.ShadowAmbientMult = MathF.Max(constants.ShadowAmbientMult, 0.70f);
        constants.SpecularColor = Vector3.One;

        BindGameMaterialTexture(0, material, MeshPreviewGameTextureSlot.Diffuse, ref constants.HasDiffuseMap);
        BindGameMaterialTexture(1, material, MeshPreviewGameTextureSlot.Normal, ref constants.HasNormalMap);
        BindGameMaterialTexture(2, material, MeshPreviewGameTextureSlot.Smspsk, ref constants.HasSmspsk);
        BindGameMaterialTexture(3, material, MeshPreviewGameTextureSlot.Espa, ref constants.HasEspa);
        BindGameMaterialTexture(4, material, MeshPreviewGameTextureSlot.Smrr, ref constants.HasSmrr);
        BindGameMaterialTexture(5, material, MeshPreviewGameTextureSlot.SpecColor, ref constants.HasSpecColorMap);
        currentFrameGameApproxBranches.Add(ResolveGameApproxBranch(constants));
    }

    private void BindGameMaterialTexture(int slot, MeshPreviewGameMaterial material, MeshPreviewGameTextureSlot gameSlot, ref float hasTexture)
    {
        if (context is null || !material.HasTexture(gameSlot))
        {
            hasTexture = 0.0f;
            context?.PSSetShaderResource((uint)slot, null!);
            return;
        }

        ID3D11ShaderResourceView? resource = GetOrCreateGameMaterialTextureView(material.GetTexture(gameSlot));
        if (resource is not null)
            context.PSSetShaderResource((uint)slot, resource);
        else
            context.PSSetShaderResource((uint)slot, null!);
        hasTexture = resource is not null ? 1.0f : 0.0f;
    }

    private ID3D11ShaderResourceView? GetOrCreateGameMaterialTextureView(TexturePreviewTexture texture)
    {
        if (device is null)
            return null;

        string key = $"{texture.SourcePath}|{texture.ExportPath}|{texture.Slot}|{texture.SelectedMipIndex}|{texture.Width}x{texture.Height}";
        if (gameMaterialTextures.TryGetValue(key, out ID3D11ShaderResourceView? existing))
            return existing;

        try
        {
            ID3D11ShaderResourceView created = CreateTextureView(texture);
            gameMaterialTextures[key] = created;
            return created;
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 GameApprox texture fallback: 0x{ex.HResult:X8} {ex.Message}";
            return null;
        }
    }

    private ID3D11ShaderResourceView CreateTextureView(TexturePreviewTexture texture)
    {
        Format format = texture.Slot is TexturePreviewMaterialSlot.Diffuse or TexturePreviewMaterialSlot.Specular or TexturePreviewMaterialSlot.Emissive
            ? Format.R8G8B8A8_UNorm_SRgb
            : Format.R8G8B8A8_UNorm;
        Texture2DDescription description = new()
        {
            Width = (uint)texture.Width,
            Height = (uint)texture.Height,
            ArraySize = 1,
            MipLevels = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource
        };

        GCHandle pixelsHandle = GCHandle.Alloc(texture.RgbaPixels, GCHandleType.Pinned);
        try
        {
            SubresourceData data = new(Marshal.UnsafeAddrOfPinnedArrayElement(texture.RgbaPixels, 0), (uint)(texture.Width * 4), 0);
            using ID3D11Texture2D tex = device!.CreateTexture2D(description, [data]);
            return device.CreateShaderResourceView(tex);
        }
        finally
        {
            pixelsHandle.Free();
        }
    }

    private void DrawGround(MeshPreviewScene scene, Matrix4x4 projection, Matrix4x4 view)
    {
        if (scene.Ue3Mesh is null || scene.Ue3Mesh.Vertices.Count == 0)
            return;

        Vector3 center = scene.Ue3Mesh.Center;
        float radius = MathF.Max(1.0f, scene.Ue3Mesh.Radius);
        float z = scene.Ue3Mesh.Vertices.Min(v => v.Position.Z) - MathF.Max(0.02f, radius * 0.04f);
        float halfExtent = MathF.Max(1.5f, radius * 1.8f);
        float gridStep = MathF.Max(radius / 4.0f, 0.5f);
        List<Vector3> gridLines = [];

        for (float x = center.X - halfExtent; x <= center.X + halfExtent + 0.001f; x += gridStep)
        {
            gridLines.Add(new Vector3(x, center.Y - halfExtent, z));
            gridLines.Add(new Vector3(x, center.Y + halfExtent, z));
        }

        for (float y = center.Y - halfExtent; y <= center.Y + halfExtent + 0.001f; y += gridStep)
        {
            gridLines.Add(new Vector3(center.X - halfExtent, y, z));
            gridLines.Add(new Vector3(center.X + halfExtent, y, z));
        }

        DrawDynamicLines(gridLines, projection, view, new Vector4(0.22f, 0.23f, 0.25f, 1f));
    }

    private void DrawBoneOverlay(MeshPreviewMesh mesh, Matrix4x4 projection, Matrix4x4 view)
    {
        List<Vector3> boneLines = [];
        List<Vector3> jointLines = [];
        float jointSize = MathF.Max(0.03f, mesh.Radius * 0.015f);

        foreach (MeshPreviewBone bone in mesh.Bones)
        {
            Vector3 center = bone.GlobalTransform.Translation;
            if (bone.ParentIndex >= 0 && bone.ParentIndex < mesh.Bones.Count)
            {
                boneLines.Add(mesh.Bones[bone.ParentIndex].GlobalTransform.Translation);
                boneLines.Add(center);
            }

            jointLines.Add(center + new Vector3(-jointSize, 0f, 0f));
            jointLines.Add(center + new Vector3(jointSize, 0f, 0f));
            jointLines.Add(center + new Vector3(0f, -jointSize, 0f));
            jointLines.Add(center + new Vector3(0f, jointSize, 0f));
            jointLines.Add(center + new Vector3(0f, 0f, -jointSize));
            jointLines.Add(center + new Vector3(0f, 0f, jointSize));
        }

        if (boneLines.Count > 0)
            DrawDynamicLines(boneLines, projection, view, new Vector4(0.8f, 0.8f, 0.8f, 1f));

        if (jointLines.Count > 0)
            DrawDynamicLines(jointLines, projection, view, new Vector4(1f, 0.9f, 0.2f, 1f));
    }

    private void DrawDynamicLines(List<Vector3> lines, Matrix4x4 projection, Matrix4x4 view, Vector4 color)
    {
        if (device is null)
            return;

        using ID3D11Buffer? buffer = lines.Count > 0
            ? device.CreateBuffer(lines.ToArray(), BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0)
            : null;

        DrawLines(buffer, lines.Count, projection, view, color);
    }

    private void DrawLines(ID3D11Buffer? buffer, int vertexCount, Matrix4x4 projection, Matrix4x4 view, Vector4 color)
    {
        if (context is null || lineInputLayout is null || lineVertexShader is null || linePixelShader is null || lineConstantBuffer is null || solidRasterizerState is null || buffer is null || vertexCount <= 0)
            return;

        LineConstants constants = new()
        {
            Projection = projection,
            View = view,
            Model = Matrix4x4.Identity,
            Color = color
        };

        context.UpdateSubresource(in constants, lineConstantBuffer);
        context.IASetInputLayout(lineInputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
        context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<Vector3>());
        context.VSSetShader(lineVertexShader);
        context.PSSetShader(linePixelShader);
        context.VSSetConstantBuffer(0, lineConstantBuffer);
        context.PSSetConstantBuffer(0, lineConstantBuffer);
        context.RSSetState(solidRasterizerState);
        context.Draw((uint)vertexCount, 0);
    }

    private bool TryGetMeshBuffers(MeshPreviewMesh mesh, out CachedMeshBuffers? buffers)
    {
        if (meshBuffers.TryGetValue(mesh, out CachedMeshBuffers? cachedBuffers))
        {
            buffers = cachedBuffers;
            return true;
        }

        if (device is null)
        {
            Diagnostics = "D3D11 mesh buffer upload skipped: device is not initialized.";
            buffers = null;
            return false;
        }

        try
        {
            buffers = new CachedMeshBuffers(device, mesh);
            meshBuffers[mesh] = buffers;
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics = $"D3D11 mesh buffer upload failed: 0x{ex.HResult:X8} {ex.Message}";
            buffers = null;
            return false;
        }
    }

    private void EnsureMeshBuffersReady(MeshPreviewMesh mesh)
    {
        TryGetMeshBuffers(mesh, out _);
    }

    private string BuildFrameDiagnostics(int renderWidth, int renderHeight, int fbxSectionsDrawn, int ue3SectionsDrawn)
    {
        if (currentScene is null || currentCamera is null)
            return $"D3D11 frame presented at {renderWidth}x{renderHeight}.";

        string fbxState = currentScene.FbxMesh is null ? "FBX=none" : $"FBX=v{currentScene.FbxMesh.Vertices.Count:N0}/i{currentScene.FbxMesh.Indices.Count:N0}/s{currentScene.FbxMesh.Sections.Count:N0}";
        string ue3State = currentScene.Ue3Mesh is null ? "UE3=none" : $"UE3=v{currentScene.Ue3Mesh.Vertices.Count:N0}/i{currentScene.Ue3Mesh.Indices.Count:N0}/s{currentScene.Ue3Mesh.Sections.Count:N0}";
        string cameraState = $"Cam=({currentCamera.GetPosition().X:F2},{currentCamera.GetPosition().Y:F2},{currentCamera.GetPosition().Z:F2}) Target=({currentCamera.Target.X:F2},{currentCamera.Target.Y:F2},{currentCamera.Target.Z:F2}) Dist={currentCamera.Distance:F2}";
        string sceneState = $"Display={currentScene.DisplayMode}, Shade={currentScene.ShadingMode}, Wire={currentScene.Wireframe}, Fbx={currentScene.ShowFbxMesh}, Ue3={currentScene.ShowUe3Mesh}";
        string drawState = $"Draws: FBX={fbxSectionsDrawn}, UE3={ue3SectionsDrawn}";
        string branchState = currentFrameGameApproxBranches.Count > 0
            ? $"GA={string.Join(",", currentFrameGameApproxBranches.OrderBy(value => value, StringComparer.Ordinal))}"
            : "GA=None";
        string sectionState = currentFrameGameApproxSectionStates.Count > 0
            ? $"Sections=[{string.Join(" | ", currentFrameGameApproxSectionStates)}]"
            : "Sections=None";
        return $"D3D11 frame presented at {renderWidth}x{renderHeight}. {fbxState}; {ue3State}; {cameraState}; {sceneState}; {drawState}; {branchState}; {sectionState}.";
    }

    private static string ResolveGameApproxBranch(MeshConstants constants)
    {
        if (constants.UseGameMaterial == 0)
            return "Disabled";

        bool hasSmspsk = constants.HasSmspsk > 0.5f;
        bool hasEspa = constants.HasEspa > 0.5f;
        bool hasSmrr = constants.HasSmrr > 0.5f;
        bool hasNormal = constants.HasNormalMap > 0.5f;
        bool smspskOnly = hasSmspsk && !hasEspa && !hasSmrr;

        if (smspskOnly && constants.NormalStrength <= 0.01f)
            return "SmspskOnlyNoNormal";

        if (smspskOnly)
            return "SmspskOnly";

        if (hasSmspsk && hasEspa && hasSmrr)
            return hasNormal ? "SmspskEspaSmrr" : "SmspskEspaSmrrNoNormal";

        if (hasEspa && hasSmrr)
            return hasNormal ? "EspaSmrr" : "EspaSmrrNoNormal";

        if (hasSmspsk)
            return hasNormal ? "SmspskMixed" : "SmspskMixedNoNormal";

        if (hasSmrr)
            return hasNormal ? "DiffuseNormalSmrr" : "DiffuseSmrr";

        if (hasNormal)
            return "DiffuseNormalOnly";

        return "DiffuseOnly";
    }

    private static double GetEffectiveSize(double actual, double layout, double minimum)
    {
        if (actual > 0)
            return actual;

        if (layout > 0)
            return layout;

        if (minimum > 0)
            return minimum;

        return 0;
    }

    private static Vector3 ResolveBaseColor(MeshPreviewScene scene)
    {
        return scene.ShadingMode switch
        {
            MeshPreviewShadingMode.Studio => new Vector3(0.76f, 0.75f, 0.73f),
            MeshPreviewShadingMode.GameApprox => new Vector3(0.68f, 0.69f, 0.71f),
            MeshPreviewShadingMode.Clay => new Vector3(0.86f, 0.84f, 0.80f),
            MeshPreviewShadingMode.MatCap => new Vector3(0.72f, 0.76f, 0.86f),
            _ => new Vector3(0.74f, 0.74f, 0.74f)
        };
    }

    private static Matrix4x4 ResolveModelMatrix(MeshPreviewScene scene, bool isUe3Mesh)
    {
        return isUe3Mesh ? scene.Ue3ModelMatrix : scene.FbxModelMatrix;
    }

    private static Vector3 ResolveLightDirection(MeshPreviewLightingPreset preset)
    {
        return Vector3.Normalize(preset switch
        {
            MeshPreviewLightingPreset.Studio => new Vector3(-0.25f, -0.85f, -0.45f),
            MeshPreviewLightingPreset.Harsh => new Vector3(-0.55f, -0.65f, -0.25f),
            MeshPreviewLightingPreset.Soft => new Vector3(-0.15f, -0.75f, -0.65f),
            _ => new Vector3(-0.45f, -0.7f, -0.4f)
        });
    }

    private static Vortice.Mathematics.Color4 ResolveClearColor(MeshPreviewBackgroundStyle style)
    {
        return style switch
        {
            MeshPreviewBackgroundStyle.StudioGray => new Vortice.Mathematics.Color4(0.27f, 0.28f, 0.29f, 1.0f),
            MeshPreviewBackgroundStyle.FlatBlack => new Vortice.Mathematics.Color4(0.02f, 0.02f, 0.025f, 1.0f),
            MeshPreviewBackgroundStyle.Checker => new Vortice.Mathematics.Color4(0.16f, 0.17f, 0.18f, 1.0f),
            _ => new Vortice.Mathematics.Color4(0.11f, 0.12f, 0.14f, 1.0f)
        };
    }

    private static int ResolveBoneIndex(MeshPreviewMesh mesh, string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
            return -1;

        for (int index = 0; index < mesh.Bones.Count; index++)
        {
            if (string.Equals(mesh.Bones[index].Name, boneName, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static Blob CompileShader(string source, string entryPoint, string profile)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"OmegaAssetStudio-winui-meshpreview-{Guid.NewGuid():N}.hlsl");
        try
        {
            File.WriteAllText(tempPath, source);
            Compiler.CompileFromFile(
                tempPath,
                null,
                null,
                entryPoint,
                profile,
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None,
                out Blob code,
                out Blob error);

            using (error)
            {
                if (code is null)
                    throw new InvalidOperationException(error?.AsString() ?? $"Failed to compile shader '{entryPoint}' ({profile}).");
            }

            return code;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DMeshVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Tangent;
        public Vector3 Bitangent;
        public Vector2 Uv;
        public Int4 BoneIndices;
        public Vector4 BoneWeights;
        public int SectionIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Int4
    {
        public int X;
        public int Y;
        public int Z;
        public int W;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeshConstants
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 Model;
        public Vector3 CameraPosition;
        private readonly float padding0;
        public Vector3 LightDirection;
        public float AmbientLight;
        public Vector3 Light0Color;
        private readonly float padding0b;
        public Vector3 BaseColor;
        public int LightingPreset;
        public int MaterialChannel;
        public int WeightMode;
        public int WeightViewMode;
        public int ShadingMode;
        public int ShowSections;
        public int HighlightSection;
        public int SelectedBone;
        public int ShowWeights;
        public int UseGameMaterial;
        public int UseDiffuseMap;
        public int UseNormalMap;
        public int UseSpecularMap;
        public int UseEmissiveMap;
        public int UseMaskMap;
        public float HasDiffuseMap;
        public float HasNormalMap;
        public float HasSmspsk;
        public float HasEspa;
        public float HasSmrr;
        public float HasSpecColorMap;
        public float AlphaTest;
        public Vector3 Light1Direction;
        private readonly float padding1a;
        public Vector3 Light1Color;
        private readonly float padding1b;
        public float LambertDiffusePower;
        public float PhongDiffusePower;
        public float LightingAmbient;
        public float ShadowAmbientMult;
        public float NormalStrength;
        public float ReflectionMult;
        public float RimColorMult;
        public float RimFalloff;
        public float ScreenLightAmount;
        public float ScreenLightMult;
        public float ScreenLightPower;
        public float SpecMult;
        public float SpecMultLq;
        public float SpecularPower;
        public float SpecularPowerMask;
        public float ImageReflectionNormalDampening;
        public Vector3 LambertAmbient;
        private readonly float padding1;
        public float SkinScatterStrength;
        public Vector3 ShadowAmbientColor;
        public float TwoSidedLighting;
        public Vector3 FillLightColor;
        private readonly float padding2;
        public Vector3 DiffuseColor;
        private readonly float padding3;
        public Vector3 SpecularColor;
        private readonly float padding4;
        public Vector3 SubsurfaceInscatteringColor;
        private readonly float padding5;
        public Vector3 SubsurfaceAbsorptionColor;
        private readonly float padding6;
        public Vector4 SectionColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LineConstants
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 Model;
        public Vector4 Color;
    }

    private sealed class CachedMeshBuffers : IDisposable
    {
        public CachedMeshBuffers(ID3D11Device device, MeshPreviewMesh mesh)
        {
            D3DMeshVertex[] vertices = new D3DMeshVertex[mesh.Vertices.Count];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                MeshPreviewVertex source = mesh.Vertices[i];
                vertices[i] = new D3DMeshVertex
                {
                    Position = source.Position,
                    Normal = source.Normal,
                    Tangent = source.Tangent,
                    Bitangent = source.Bitangent,
                    Uv = source.Uv,
                    BoneIndices = new Int4 { X = source.Bone0, Y = source.Bone1, Z = source.Bone2, W = source.Bone3 },
                    BoneWeights = new Vector4(source.Weight0, source.Weight1, source.Weight2, source.Weight3),
                    SectionIndex = source.SectionIndex
                };
            }

            VertexBuffer = device.CreateBuffer(vertices, BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
            IndexBuffer = device.CreateBuffer(mesh.Indices.ToArray(), BindFlags.IndexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
            NormalLines = CreateLineBuffer(device, BuildDirectionLines(mesh.Vertices, static v => v.Normal, mesh.Radius * 0.05f), out int normalCount);
            NormalLineCount = normalCount;
            TangentLines = CreateLineBuffer(device, BuildDirectionLines(mesh.Vertices, static v => v.Tangent, mesh.Radius * 0.05f), out int tangentCount);
            TangentLineCount = tangentCount;
            UvSeamLines = CreateLineBuffer(device, mesh.UvSeamLines, out int uvCount);
            UvSeamLineCount = uvCount;
        }

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer IndexBuffer { get; }
        public ID3D11Buffer? NormalLines { get; }
        public int NormalLineCount { get; }
        public ID3D11Buffer? TangentLines { get; }
        public int TangentLineCount { get; }
        public ID3D11Buffer? UvSeamLines { get; }
        public int UvSeamLineCount { get; }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
            NormalLines?.Dispose();
            TangentLines?.Dispose();
            UvSeamLines?.Dispose();
        }

        private static List<Vector3> BuildDirectionLines(IReadOnlyList<MeshPreviewVertex> vertices, Func<MeshPreviewVertex, Vector3> selector, float scale)
        {
            List<Vector3> lines = new(vertices.Count * 2);
            foreach (MeshPreviewVertex vertex in vertices)
            {
                Vector3 direction = selector(vertex);
                if (direction.LengthSquared() <= 1e-6f)
                    continue;

                lines.Add(vertex.Position);
                lines.Add(vertex.Position + (Vector3.Normalize(direction) * scale));
            }

            return lines;
        }

        private static ID3D11Buffer? CreateLineBuffer(ID3D11Device device, IReadOnlyList<Vector3> lines, out int count)
        {
            count = lines.Count;
            return count > 0
                ? device.CreateBuffer(lines.ToArray(), BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0)
                : null;
        }
    }

    private const string MeshVertexShaderSource = """
        cbuffer MeshConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float3 uCameraPos;
            float _padding0;
            float3 uLightDirection;
            float uAmbientLight;
            float3 uLight0Color;
            float _padding0b;
            float3 uBaseColor;
            int uLightingPreset;
            int uMaterialChannel;
            int uWeightMode;
            int uWeightViewMode;
            int uShadingMode;
            int uShowSections;
            int uHighlightSection;
            int uSelectedBone;
            int uShowWeights;
            int uUseGameMaterial;
            int uUseDiffuseMap;
            int uUseNormalMap;
            int uUseSpecularMap;
            int uUseEmissiveMap;
            int uUseMaskMap;
            float uHasDiffuseMap;
            float uHasNormalMap;
            float uHasSMSPSK;
            float uHasESPA;
            float uHasSMRR;
            float uHasSpecColorMap;
            float uAlphaTest;
            float3 uLight1Direction;
            float _padding1a;
            float3 uLight1Color;
            float _padding1b;
            float uLambertDiffusePower;
            float uPhongDiffusePower;
            float uLightingAmbient;
            float uShadowAmbientMult;
            float uNormalStrength;
            float uReflectionMult;
            float uRimColorMult;
            float uRimFalloff;
            float uScreenLightAmount;
            float uScreenLightMult;
            float uScreenLightPower;
            float uSpecMult;
            float uSpecMultLQ;
            float uSpecularPower;
            float uSpecularPowerMask;
            float uImageReflectionNormalDampening;
            float3 uLambertAmbient;
            float _padding1;
            float uSkinScatterStrength;
            float3 uShadowAmbientColor;
            float uTwoSidedLighting;
            float3 uFillLightColor;
            float _padding2;
            float3 uDiffuseColor;
            float _padding3;
            float3 uSpecularColor;
            float _padding4;
            float3 uSubsurfaceInscatteringColor;
            float _padding5;
            float3 uSubsurfaceAbsorptionColor;
            float _padding6;
            float4 uSectionColor;
        };

        struct VSInput
        {
            float3 Position : POSITION;
            float3 Normal : NORMAL;
            float3 Tangent : TANGENT;
            float3 Bitangent : BINORMAL;
            float2 Uv : TEXCOORD0;
            int4 BoneIndices : BONEINDICES;
            float4 BoneWeights : BONEWEIGHTS;
            int SectionIndex : SECTIONINDEX;
        };

        struct VSOutput
        {
            float4 Position : SV_POSITION;
            float3 Normal : NORMAL0;
            float3 WorldPosition : TEXCOORD0;
            float3 Tangent : TEXCOORD1;
            float3 Bitangent : TEXCOORD2;
            float2 Uv : TEXCOORD3;
            nointerpolation int4 BoneIndices : BONEINDICES;
            float4 BoneWeights : BONEWEIGHTS;
        };

        VSOutput VSMain(VSInput input)
        {
            VSOutput output;
            float4 worldPosition = mul(float4(input.Position, 1.0f), uModel);
            output.Position = mul(mul(worldPosition, uView), uProjection);
            output.Normal = mul(float4(input.Normal, 0.0f), uModel).xyz;
            output.WorldPosition = worldPosition.xyz;
            output.Tangent = mul(float4(input.Tangent, 0.0f), uModel).xyz;
            output.Bitangent = mul(float4(input.Bitangent, 0.0f), uModel).xyz;
            output.Uv = input.Uv;
            output.BoneIndices = input.BoneIndices;
            output.BoneWeights = input.BoneWeights;
            return output;
        }
        """;

    private const string TestTriangleVertexShaderSource = """
        struct VSInput
        {
            float3 Position : POSITION;
        };

        struct VSOutput
        {
            float4 Position : SV_Position;
        };

        VSOutput VSMain(VSInput input)
        {
            VSOutput output;
            output.Position = float4(input.Position, 1.0f);
            return output;
        }
        """;

    private const string TestTrianglePixelShaderSource = """
        float4 PSMain() : SV_Target
        {
            return float4(0.08f, 0.95f, 1.0f, 1.0f);
        }
        """;

    private const string MeshPixelShaderSource = """
        cbuffer MeshConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float3 uCameraPos;
            float _padding0;
            float3 uLightDirection;
            float uAmbientLight;
            float3 uLight0Color;
            float _padding0b;
            float3 uBaseColor;
            int uLightingPreset;
            int uMaterialChannel;
            int uWeightMode;
            int uWeightViewMode;
            int uShadingMode;
            int uShowSections;
            int uHighlightSection;
            int uSelectedBone;
            int uShowWeights;
            int uUseGameMaterial;
            int uUseDiffuseMap;
            int uUseNormalMap;
            int uUseSpecularMap;
            int uUseEmissiveMap;
            int uUseMaskMap;
            float uHasDiffuseMap;
            float uHasNormalMap;
            float uHasSMSPSK;
            float uHasESPA;
            float uHasSMRR;
            float uHasSpecColorMap;
            float uAlphaTest;
            float3 uLight1Direction;
            float _padding1a;
            float3 uLight1Color;
            float _padding1b;
            float uLambertDiffusePower;
            float uPhongDiffusePower;
            float uLightingAmbient;
            float uShadowAmbientMult;
            float uNormalStrength;
            float uReflectionMult;
            float uRimColorMult;
            float uRimFalloff;
            float uScreenLightAmount;
            float uScreenLightMult;
            float uScreenLightPower;
            float uSpecMult;
            float uSpecMultLQ;
            float uSpecularPower;
            float uSpecularPowerMask;
            float uImageReflectionNormalDampening;
            float3 uLambertAmbient;
            float _padding1;
            float uSkinScatterStrength;
            float3 uShadowAmbientColor;
            float uTwoSidedLighting;
            float3 uFillLightColor;
            float _padding2;
            float3 uDiffuseColor;
            float _padding3;
            float3 uSpecularColor;
            float _padding4;
            float3 uSubsurfaceInscatteringColor;
            float _padding5;
            float3 uSubsurfaceAbsorptionColor;
            float _padding6;
            float4 uSectionColor;
        };

        struct PSInput
        {
            float4 Position : SV_POSITION;
            float3 Normal : NORMAL0;
            float3 WorldPosition : TEXCOORD0;
            float3 Tangent : TEXCOORD1;
            float3 Bitangent : TEXCOORD2;
            float2 Uv : TEXCOORD3;
            nointerpolation int4 BoneIndices : BONEINDICES;
            float4 BoneWeights : BONEWEIGHTS;
        };

        Texture2D uDiffuseMap : register(t0);
        Texture2D uNormalMap : register(t1);
        Texture2D uSMSPSKMap : register(t2);
        Texture2D uESPAMap : register(t3);
        Texture2D uSMRRMap : register(t4);
        Texture2D uSpecColorMap : register(t5);
        Texture2D uSpecularMap : register(t6);
        Texture2D uEmissiveMap : register(t7);
        Texture2D uMaskMap : register(t8);
        SamplerState uTextureSampler : register(s0);

        float SelectedWeight(PSInput input)
        {
            if (uSelectedBone < 0)
                return max(max(input.BoneWeights.x, input.BoneWeights.y), max(input.BoneWeights.z, input.BoneWeights.w));

            float value = 0.0;
            if (input.BoneIndices.x == uSelectedBone) value = max(value, input.BoneWeights.x);
            if (input.BoneIndices.y == uSelectedBone) value = max(value, input.BoneWeights.y);
            if (input.BoneIndices.z == uSelectedBone) value = max(value, input.BoneWeights.z);
            if (input.BoneIndices.w == uSelectedBone) value = max(value, input.BoneWeights.w);
            return value;
        }

        float MaxInfluenceWeight(PSInput input)
        {
            return max(max(input.BoneWeights.x, input.BoneWeights.y), max(input.BoneWeights.z, input.BoneWeights.w));
        }

        struct MaterialMasks
        {
            float specMult;
            float specPower;
            float skinMask;
            float reflectivity;
            float emissive;
            float ambientOcclusion;
            float rimMask;
        };

        MaterialMasks GetMaterialMasks(PSInput input)
        {
            MaterialMasks masks;
            if (uHasSMSPSK > 0.5)
            {
                float4 smspsk = uSMSPSKMap.Sample(uTextureSampler, input.Uv);
                masks.specMult = smspsk.r;
                masks.specPower = smspsk.g;
                masks.skinMask = smspsk.b;
                masks.reflectivity = smspsk.a;
                masks.emissive = 0.0;
                masks.ambientOcclusion = 1.0;
                masks.rimMask = 1.0;
                if (uHasESPA <= 0.5 && uHasSMRR <= 0.5)
                {
                    masks.specMult *= 0.35;
                    masks.specPower *= 0.35;
                }
            }
            else if (uHasESPA > 0.5 && uHasSMRR > 0.5)
            {
                float4 espa = uESPAMap.Sample(uTextureSampler, input.Uv);
                float4 smrr = uSMRRMap.Sample(uTextureSampler, input.Uv);
                masks.specMult = smrr.r;
                masks.rimMask = smrr.g;
                masks.reflectivity = smrr.b;
                masks.skinMask = espa.b;
                masks.specPower = espa.g;
                masks.emissive = espa.r;
                masks.ambientOcclusion = 1.0;
            }
            else if (uHasSMRR > 0.5)
            {
                float4 smrr = uSMRRMap.Sample(uTextureSampler, input.Uv);
                masks.specMult = smrr.r;
                masks.specPower = smrr.g;
                masks.skinMask = 0.0;
                masks.reflectivity = smrr.b;
                masks.emissive = 0.0;
                masks.ambientOcclusion = 1.0;
                masks.rimMask = smrr.g;
            }
            else
            {
                masks.specMult = 0.0;
                masks.specPower = 0.0;
                masks.skinMask = 0.0;
                masks.reflectivity = 0.0;
                masks.emissive = 0.0;
                masks.ambientOcclusion = 1.0;
                masks.rimMask = 1.0;
            }

            return masks;
        }

        float3 CalculateGameSpecular(float3 normal, float3 lightDir, float3 viewDir, float specMult, float specPower, float skinMask, PSInput input)
        {
            float3 L = normalize(-lightDir);
            float3 H = normalize(L + viewDir);
            float NdotH = max(dot(normal, H), 0.0);
            float finalSpecPower = uSpecularPower;
            float previewSpecScale = uMaterialChannel == 2 ? 0.55 : (uMaterialChannel == 0 ? 0.45 : 1.0);
            if (uHasSMSPSK > 0.5)
            {
                finalSpecPower = lerp(uSpecularPower, uSpecularPower * 4.0, specPower) * uSpecularPowerMask * previewSpecScale;
                if (skinMask > 0.0)
                    finalSpecPower *= lerp(1.0, 2.0, skinMask);
            }
            else
            {
                finalSpecPower *= previewSpecScale;
            }

            float spec = pow(NdotH, finalSpecPower);
            float materialSpecMult = uSpecMult;
            if (uHasSMSPSK > 0.5)
                materialSpecMult = lerp(uSpecMult, uSpecMultLQ, 0.0);
            else if (uHasSMRR > 0.5)
                materialSpecMult = max(uSpecMult, 0.55);
            float finalSpecMult = materialSpecMult * (uMaterialChannel == 2 ? 0.55 : (uMaterialChannel == 0 ? 0.45 : 1.0));
            float3 specColor = uHasSpecColorMap > 0.5 ? uSpecColorMap.Sample(uTextureSampler, input.Uv).rgb : uSpecularColor;
            return specColor * spec * finalSpecMult * specMult;
        }

        float3 CalculateGameLighting(float3 normal, float3 viewDir, float3 diffuseColor, PSInput input)
        {
            MaterialMasks masks = GetMaterialMasks(input);
            float ambientOcclusion = lerp(0.65, 1.0, masks.ambientOcclusion);
            diffuseColor *= ambientOcclusion;

            float3 ambient = uLambertAmbient * uLightingAmbient;
            ambient += uShadowAmbientColor * uShadowAmbientMult;

            float3 lightDir0 = normalize(-uLightDirection);
            float3 lightDir1 = normalize(uLight1Direction);
            float lambert0 = pow(max(dot(normal, lightDir0), 0.0), uLambertDiffusePower);
            float phong0 = pow(max(dot(normal, lightDir0), 0.0), uPhongDiffusePower);
            float diffuse0 = lerp(lambert0, phong0, 0.5) * uLight0Color.r;
            float lambert1 = pow(max(dot(normal, lightDir1), 0.0), uLambertDiffusePower);
            float phong1 = pow(max(dot(normal, lightDir1), 0.0), uPhongDiffusePower);
            float diffuse1 = lerp(lambert1, phong1, 0.5) * uLight1Color.r;

            if (uTwoSidedLighting > 0.5)
            {
                float backLight0 = max(0.0, dot(-normal, lightDir0));
                float backLight1 = max(0.0, dot(-normal, lightDir1));
                diffuse0 = max(diffuse0, backLight0 * 0.5);
                diffuse1 = max(diffuse1, backLight1 * 0.5);
            }

            if (masks.skinMask > 0.0 && uHasSMSPSK > 0.5)
            {
                float scatter0 = pow(max(0.0, dot(-normal, lightDir0)), 2.0);
                float scatter1 = pow(max(0.0, dot(-normal, lightDir1)), 2.0);
                float3 subsurface = uSubsurfaceInscatteringColor * uSkinScatterStrength * masks.skinMask * uSubsurfaceAbsorptionColor;
                diffuse0 += dot(subsurface, float3(0.3333, 0.3333, 0.3333)) * scatter0;
                diffuse1 += dot(subsurface, float3(0.3333, 0.3333, 0.3333)) * scatter1;
            }

            float3 specular0 = CalculateGameSpecular(normal, uLightDirection, viewDir, masks.specMult, masks.specPower, masks.skinMask, input) * uLight0Color;
            float3 specular1 = CalculateGameSpecular(normal, uLight1Direction, viewDir, masks.specMult, masks.specPower, masks.skinMask, input) * uLight1Color;
            float3 fillLight = uFillLightColor * max(0.0, dot(normal, float3(0.0, 1.0, 0.0))) * 0.35;
            float3 rimLight = float3(0.0, 0.0, 0.0);

            if (uRimColorMult > 0.0)
            {
                float rim = 1.0 - max(dot(normal, viewDir), 0.0);
                rim = pow(rim, uRimFalloff) * uRimColorMult;
                rimLight = uFillLightColor * rim * masks.rimMask;
                if (uMaterialChannel == 0)
                    rimLight *= 0.20;
            }

            float screenLight = 0.0;
            if (uScreenLightAmount > 0.0)
            {
                float3 screenNormal = normal * 0.5 + 0.5;
                screenLight = pow(screenNormal.y, uScreenLightPower) * uScreenLightMult * uScreenLightAmount;
            }

            float3 finalColor = diffuseColor * (ambient + diffuse0 + diffuse1 + fillLight);
            finalColor += specular0 + specular1 + rimLight;
            finalColor += screenLight.xxx;

            if (masks.reflectivity > 0.0)
                finalColor += ((masks.reflectivity * uReflectionMult) / (1.0 + uImageReflectionNormalDampening)).xxx * 0.2;

            if (masks.emissive > 0.0)
                finalColor += diffuseColor * masks.emissive * 2.0;

            return saturate(finalColor);
        }

        float4 PSMain(PSInput input) : SV_TARGET
        {
            float normalMapBias = uMaterialChannel == 2 ? 2.00 : 1.10;
            float detailMapBias = uMaterialChannel == 2 ? 1.75 : 0.90;
            float normalStrength = uNormalStrength * (uMaterialChannel == 2 ? 0.35 : (uMaterialChannel == 0 ? 0.0 : 0.42));
            if (uHasSMSPSK > 0.5 && uHasESPA <= 0.5 && uHasSMRR <= 0.5)
                normalStrength = 0.0;
            if (uUseGameMaterial == 1)
            {
                float4 diffuseSample = uHasDiffuseMap > 0.5 ? uDiffuseMap.Sample(uTextureSampler, input.Uv) : float4(uDiffuseColor, 1.0);
                if (uAlphaTest > 0.0 && diffuseSample.a < 0.5)
                    discard;

                float3 normal = normalize(input.Normal);
                if (uHasNormalMap > 0.5)
                {
                    float3 tangent = normalize(input.Tangent);
                    float3 bitangent = normalize(input.Bitangent);
                    float3 sampledNormal = uNormalMap.SampleBias(uTextureSampler, input.Uv, normalMapBias).rgb * 2.0 - 1.0;
                    if (uMaterialChannel == 0)
                        sampledNormal = float3(0.0, 0.0, 1.0);
                    float3x3 tbn = float3x3(tangent, bitangent, normal);
                    normal = normalize(mul(tbn, normalize(sampledNormal * float3(1.0, 1.0, normalStrength))));
                }

                float3 viewDir = normalize(uCameraPos - input.WorldPosition);
                float3 lit = CalculateGameLighting(normal, viewDir, diffuseSample.rgb, input);
                if (uMaterialChannel == 1)
                    lit = diffuseSample.rgb;
                else if (uMaterialChannel == 2)
                    lit = normal * 0.5 + 0.5;
                else if (uMaterialChannel == 3)
                {
                    float specView = uHasSpecColorMap > 0.5
                        ? dot(uSpecColorMap.SampleBias(uTextureSampler, input.Uv, detailMapBias).rgb, float3(0.3333, 0.3333, 0.3333))
                        : (uHasSMSPSK > 0.5 ? uSMSPSKMap.SampleBias(uTextureSampler, input.Uv, detailMapBias).r : (uHasSMRR > 0.5 ? uSMRRMap.SampleBias(uTextureSampler, input.Uv, detailMapBias).r : 0.0));
                    lit = specView.xxx;
                }
                else if (uMaterialChannel == 4)
                    lit = uHasESPA > 0.5 ? uESPAMap.SampleBias(uTextureSampler, input.Uv, detailMapBias).rgb : float3(0.0, 0.0, 0.0);
                else if (uMaterialChannel == 5)
                {
                    float3 maskView = float3(0.0, 0.0, 0.0);
                    if (uHasSMSPSK > 0.5)
                        maskView = uSMSPSKMap.SampleBias(uTextureSampler, input.Uv, detailMapBias).rgb;
                    else if (uHasSMRR > 0.5)
                        maskView = uSMRRMap.SampleBias(uTextureSampler, input.Uv, detailMapBias).rgb;
                    lit = maskView;
                }
                else if (uMaterialChannel == 0)
                {
                    lit = lerp(diffuseSample.rgb, lit, 0.52);
                }
                if (uShowSections == 1)
                    lit = lerp(lit, uSectionColor.rgb, 0.18);
                if (uHighlightSection == 1)
                    lit = lerp(lit, uSectionColor.rgb, 0.40);
                if (uWeightMode == 1)
                {
                    float weight = uWeightViewMode == 1
                        ? saturate(MaxInfluenceWeight(input))
                        : saturate(SelectedWeight(input));
                    lit = lerp(float3(0.05, 0.15, 0.95), float3(1.0, 0.1, 0.05), weight);
                }

                float outputAlpha = uAlphaTest > 0.0 ? diffuseSample.a : 1.0;
                return float4(lit, outputAlpha);
            }

            float3 normal = normalize(input.Normal);
            if (uUseNormalMap == 1)
            {
                float3 tangent = normalize(input.Tangent - (normal * dot(input.Tangent, normal)));
                float3 inputBitangent = normalize(input.Bitangent);
                float3 bitangent = normalize(cross(normal, tangent));
                if (dot(bitangent, inputBitangent) < 0.0)
                    bitangent = -bitangent;
                float3 sampled = uNormalMap.SampleBias(uTextureSampler, input.Uv, 0.75).xyz * 2.0 - 1.0;
                sampled.y = -sampled.y;
                float3x3 tbn = float3x3(tangent, bitangent, normal);
                normal = normalize(mul(tbn, sampled));
            }
            float3 lightDir = normalize(-uLightDirection);
            float diffuse = max(dot(normal, lightDir), 0.0);
            float3 fillDir = normalize(float3(0.35, 0.4, 0.85));
            float fill = max(dot(normal, fillDir), 0.0);
            float3 color = uBaseColor;

            if (uUseDiffuseMap == 1)
                color = uDiffuseMap.Sample(uTextureSampler, input.Uv).rgb;

            if (uShowSections == 1)
                color = lerp(color, uSectionColor.rgb, 0.35);

            if (uHighlightSection == 1)
                color = lerp(color, uSectionColor.rgb, 0.72);

            if (uShadingMode == 2)
                color = float3(0.78, 0.76, 0.72);

            float specularStrength = uUseSpecularMap == 1 ? dot(uSpecularMap.Sample(uTextureSampler, input.Uv).rgb, float3(0.3333, 0.3333, 0.3333)) : 0.15;
            float3 viewDir = normalize(uCameraPos - input.WorldPosition);
            float3 reflectDir = reflect(-lightDir, normal);
            float specPower = uLightingPreset == 1 ? 36.0 : (uLightingPreset == 2 ? 48.0 : (uLightingPreset == 3 ? 18.0 : 24.0));
            float effectiveSpecPower = uShadingMode == 1 ? max(specPower, 36.0) : (uShadingMode == 4 ? max(specPower, 32.0) : specPower);
            float specular = pow(max(dot(viewDir, reflectDir), 0.0), effectiveSpecPower) * specularStrength * (uShadingMode == 4 ? 1.2 : 1.0);
            float rim = pow(1.0 - max(dot(normal, viewDir), 0.0), uShadingMode == 1 ? 2.2 : (uShadingMode == 4 ? 4.2 : 3.5)) * (uShadingMode == 1 ? 0.18 : (uShadingMode == 4 ? 0.03 : 0.06));

            if (uUseMaskMap == 1)
                color *= uMaskMap.Sample(uTextureSampler, input.Uv).rgb;

            float3 emissive = uUseEmissiveMap == 1 ? uEmissiveMap.Sample(uTextureSampler, input.Uv).rgb : float3(0.0, 0.0, 0.0);
            float ambient = uAmbientLight + (uLightingPreset == 1 ? 0.10 : (uLightingPreset == 2 ? 0.02 : (uLightingPreset == 3 ? 0.16 : 0.04))) + (uShadingMode == 4 ? 0.03 : 0.0);
            float fillStrength = uLightingPreset == 1 ? 0.32 : (uLightingPreset == 2 ? 0.05 : (uLightingPreset == 3 ? 0.28 : 0.14));
            float3 rimColor = uShadingMode == 1 ? float3(0.38, 0.44, 0.52) : (uShadingMode == 4 ? float3(0.12, 0.14, 0.16) : float3(0.18, 0.20, 0.24));
            if (uShadingMode == 3)
            {
                float facing = clamp(dot(normal, viewDir) * 0.5 + 0.5, 0.0, 1.0);
                float3 matCap = lerp(float3(0.22, 0.24, 0.28), float3(0.84, 0.86, 0.88), facing);
                color = matCap;
            }
            else if (uShadingMode == 4)
            {
                if (uUseDiffuseMap == 1)
                    color = uDiffuseMap.Sample(uTextureSampler, input.Uv).rgb;
            }
            else
            {
                color = color * (ambient + diffuse + fill * fillStrength) + specular.xxx + rimColor * rim + emissive;
            }

            if (uMaterialChannel == 1)
                color = uUseDiffuseMap == 1 ? uDiffuseMap.Sample(uTextureSampler, input.Uv).rgb : uBaseColor;
            else if (uMaterialChannel == 2)
                color = normal * 0.5 + 0.5;
            else if (uMaterialChannel == 3)
            {
                float specView = uUseSpecularMap == 1 ? dot(uSpecularMap.Sample(uTextureSampler, input.Uv).rgb, float3(0.3333, 0.3333, 0.3333)) : specularStrength;
                color = specView.xxx;
            }
            else if (uMaterialChannel == 4)
                color = emissive;
            else if (uMaterialChannel == 5)
                color = uUseMaskMap == 1 ? uMaskMap.Sample(uTextureSampler, input.Uv).rgb : float3(0.0, 0.0, 0.0);
            if (uWeightMode == 1)
            {
                float weight = uWeightViewMode == 1
                    ? saturate(MaxInfluenceWeight(input))
                    : saturate(SelectedWeight(input));
                color = lerp(float3(0.05, 0.15, 0.95), float3(1.0, 0.1, 0.05), weight);
            }

            return float4(saturate(color), 1.0);
        }
        """;

    private const string LineVertexShaderSource = """
        cbuffer LineConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float4 uColor;
        };

        struct VSInput
        {
            float3 Position : POSITION;
        };

        struct VSOutput
        {
            float4 Position : SV_POSITION;
        };

        VSOutput VSMain(VSInput input)
        {
            VSOutput output;
            float4 worldPosition = mul(float4(input.Position, 1.0f), uModel);
            float4 viewPosition = mul(worldPosition, uView);
            output.Position = mul(viewPosition, uProjection);
            return output;
        }
        """;

    private const string LinePixelShaderSource = """
        cbuffer LineConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float4 uColor;
        };

        float4 PSMain() : SV_TARGET
        {
            return uColor;
        }
        """;

    [ComImport]
    [Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISwapChainPanelNativeInterop
    {
        void SetSwapChain(IntPtr swapChain);
    }
}

