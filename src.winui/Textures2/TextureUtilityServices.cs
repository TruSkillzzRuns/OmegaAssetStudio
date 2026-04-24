using OmegaAssetStudio.WinUI.Models;

namespace OmegaAssetStudio.WinUI.Textures2;

public sealed class SearchFilterService
{
    public IReadOnlyList<TextureItemViewModel> Filter(IEnumerable<TextureItemViewModel> source, string filterText)
    {
        ArgumentNullException.ThrowIfNull(source);

        string filter = filterText?.Trim() ?? string.Empty;
        IEnumerable<TextureItemViewModel> query = source;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(item =>
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.SourcePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.ExportPath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.SourceKind.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.Format.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.SizeText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.SlotText.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderBy(item => item.DisplayName).ToArray();
    }
}

public sealed class UndoRedoService<T>
{
    private readonly Stack<T> _undo = new();
    private readonly Stack<T> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(T item)
    {
        _undo.Push(item);
        _redo.Clear();
    }

    public bool TryUndo(out T value)
    {
        if (_undo.Count == 0)
        {
            value = default!;
            return false;
        }

        value = _undo.Pop();
        _redo.Push(value);
        return true;
    }

    public bool TryRedo(out T value)
    {
        if (_redo.Count == 0)
        {
            value = default!;
            return false;
        }

        value = _redo.Pop();
        _undo.Push(value);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

public sealed class TextureHistoryService
{
    private readonly List<TextureHistoryEntry> _entries = [];

    public IReadOnlyList<TextureHistoryEntry> Entries => _entries;

    public void Record(TextureHistoryEntry entry)
    {
        _entries.Add(entry);
    }

    public void Clear() => _entries.Clear();

    public TextureHistoryEntry? LastOrDefault() => _entries.Count == 0 ? null : _entries[^1];
}

