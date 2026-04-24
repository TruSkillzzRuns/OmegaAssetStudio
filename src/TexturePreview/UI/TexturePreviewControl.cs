using OpenTK.GLControl;
using System.Numerics;

namespace OmegaAssetStudio.TexturePreview;

public sealed class TexturePreviewControl : UserControl
{
    private readonly GLControl _glControl;
    private readonly Panel _dropOverlay;
    private readonly Label _dropOverlayLabel;
    private readonly TexturePreviewRenderer _renderer = new();
    private readonly List<TexturePreviewTexture> _loadedTextures = [];
    private Point _lastMousePosition;
    private bool _isPanning;
    private float _zoom = 1.0f;
    private Vector2 _pan = Vector2.Zero;
    private int _currentTextureIndex;

    public TexturePreviewControl()
    {
        Dock = DockStyle.Fill;
        AllowDrop = true;

        _glControl = new GLControl(new GLControlSettings
        {
            API = OpenTK.Windowing.Common.ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = OpenTK.Windowing.Common.ContextProfile.Core,
            Flags = OpenTK.Windowing.Common.ContextFlags.Default
        })
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };

        Controls.Add(_glControl);

        _dropOverlay = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            BackColor = Color.FromArgb(160, 18, 18, 18)
        };
        _dropOverlayLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Drop texture here",
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold)
        };
        _dropOverlay.Controls.Add(_dropOverlayLabel);
        Controls.Add(_dropOverlay);
        _dropOverlay.BringToFront();

        _glControl.Load += (_, _) => _renderer.Initialize();
        _glControl.Paint += (_, _) => Draw();
        _glControl.Resize += (_, _) => _glControl.Invalidate();
        _glControl.MouseWheel += OnMouseWheel;
        _glControl.MouseDown += OnMouseDown;
        _glControl.MouseUp += (_, _) => _isPanning = false;
        _glControl.MouseMove += OnMouseMove;
        _glControl.KeyDown += OnKeyDown;
        _glControl.DragEnter += OnDragEnter;
        _glControl.DragLeave += OnDragLeave;
        _glControl.DragDrop += OnDragDrop;
        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        DragDrop += OnDragDrop;
    }

    public event EventHandler<IReadOnlyList<string>> TextureFilesDropped;

    public TexturePreviewTexture CurrentTexture { get; private set; }

    public void SetTexture(TexturePreviewTexture texture)
    {
        _loadedTextures.Clear();
        if (texture != null)
            _loadedTextures.Add(texture);

        _currentTextureIndex = 0;
        CurrentTexture = texture;
        _renderer.SetTexture(texture);
        ResetView();
        _glControl.Invalidate();
    }

    public void SetTextures(IReadOnlyList<TexturePreviewTexture> textures)
    {
        _loadedTextures.Clear();
        if (textures != null)
            _loadedTextures.AddRange(textures.Where(static texture => texture != null));

        _currentTextureIndex = 0;
        CurrentTexture = _loadedTextures.FirstOrDefault();
        _renderer.SetTexture(CurrentTexture);
        ResetView();
        _glControl.Invalidate();
    }

    public void ResetView()
    {
        _zoom = 1.0f;
        _pan = Vector2.Zero;
        _glControl.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer.Dispose();
            _glControl.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Draw()
    {
        _glControl.MakeCurrent();
        _renderer.Render(_glControl.ClientSize.Width, _glControl.ClientSize.Height, _zoom, _pan);
        _glControl.SwapBuffers();
    }

    private void OnMouseWheel(object sender, MouseEventArgs e)
    {
        float multiplier = e.Delta > 0 ? 1.15f : 1.0f / 1.15f;
        _zoom = Math.Clamp(_zoom * multiplier, 0.1f, 64.0f);
        _glControl.Invalidate();
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle)
            return;

        _isPanning = true;
        _lastMousePosition = e.Location;
        _glControl.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_loadedTextures.Count <= 1)
            return;

        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.PageDown)
        {
            CycleTexture(1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.PageUp)
        {
            CycleTexture(-1);
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
            return;

        _pan += new Vector2(e.X - _lastMousePosition.X, e.Y - _lastMousePosition.Y);
        _lastMousePosition = e.Location;
        _glControl.Invalidate();
    }

    private void CycleTexture(int delta)
    {
        if (_loadedTextures.Count == 0)
            return;

        _currentTextureIndex = (_currentTextureIndex + delta) % _loadedTextures.Count;
        if (_currentTextureIndex < 0)
            _currentTextureIndex += _loadedTextures.Count;

        CurrentTexture = _loadedTextures[_currentTextureIndex];
        _renderer.SetTexture(CurrentTexture);
        ResetView();
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            _dropOverlay.Visible = true;
            _dropOverlay.BringToFront();
        }
    }

    private void OnDragLeave(object sender, EventArgs e)
    {
        _dropOverlay.Visible = false;
    }

    private void OnDragDrop(object sender, DragEventArgs e)
    {
        _dropOverlay.Visible = false;
        string[] files = e.Data?.GetData(DataFormats.FileDrop) as string[];
        if (files is null || files.Length == 0)
            return;

        TextureFilesDropped?.Invoke(this, files);
    }
}

