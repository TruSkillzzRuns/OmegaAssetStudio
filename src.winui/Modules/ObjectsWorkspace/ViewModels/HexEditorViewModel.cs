using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Commands;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.ViewModels;

public sealed class HexEditorViewModel : INotifyPropertyChanged
{
    private byte[] originalBytes = Array.Empty<byte>();
    private byte[] currentBytes = Array.Empty<byte>();
    private string hexText = string.Empty;
    private string titleText = "Hex Editor";
    private string statusText = "Load an export to inspect raw bytes.";
    private string byteSummary = "0 bytes";
    private bool hasSelection;
    private bool hasUnsavedChanges;
    private readonly ObjectsRelayCommand normalizeCommand;
    private readonly ObjectsRelayCommand revertCommand;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand NormalizeCommand => normalizeCommand;

    public ICommand RevertCommand => revertCommand;

    public HexEditorViewModel()
    {
        normalizeCommand = new ObjectsRelayCommand(NormalizeLoadedBytes, () => HasSelection);
        revertCommand = new ObjectsRelayCommand(Revert, () => HasSelection);
    }

    public string TitleText
    {
        get => titleText;
        private set => SetField(ref titleText, value);
    }

    public string StatusText
    {
        get => statusText;
        set => SetField(ref statusText, value);
    }

    public string HexText
    {
        get => hexText;
        private set => SetField(ref hexText, value);
    }

    public string ByteSummary
    {
        get => byteSummary;
        private set => SetField(ref byteSummary, value);
    }

    public bool HasSelection
    {
        get => hasSelection;
        private set
        {
            if (!SetField(ref hasSelection, value))
                return;

            normalizeCommand.NotifyCanExecuteChanged();
            revertCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasUnsavedChanges
    {
        get => hasUnsavedChanges;
        private set => SetField(ref hasUnsavedChanges, value);
    }

    public byte[] CurrentBytes => currentBytes.ToArray();

    public void LoadBytes(byte[] bytes, string? title = null)
    {
        originalBytes = bytes?.ToArray() ?? Array.Empty<byte>();
        currentBytes = originalBytes.ToArray();
        HexText = FormatBytes(originalBytes);
        TitleText = string.IsNullOrWhiteSpace(title) ? "Hex Editor" : title;
        ByteSummary = $"{currentBytes.Length:N0} byte{(currentBytes.Length == 1 ? string.Empty : "s")}";
        HasSelection = true;
        HasUnsavedChanges = false;
        StatusText = $"Loaded {currentBytes.Length:N0} byte{(currentBytes.Length == 1 ? string.Empty : "s")}.";
    }

    public void SetHexText(string text)
    {
        HexText = text ?? string.Empty;
        HasUnsavedChanges = HasSelection && !string.Equals(HexText, FormatBytes(currentBytes), StringComparison.Ordinal);
        StatusText = HasUnsavedChanges ? "Hex text modified." : StatusText;
    }

    public bool TryCommitHexText(string text, out byte[] bytes, out string message)
    {
        if (!TryParseHexText(text, out bytes, out message))
            return false;

        originalBytes = bytes.ToArray();
        currentBytes = bytes.ToArray();
        HexText = FormatBytes(currentBytes);
        ByteSummary = $"{currentBytes.Length:N0} byte{(currentBytes.Length == 1 ? string.Empty : "s")}";
        HasSelection = true;
        HasUnsavedChanges = false;
        StatusText = message;
        return true;
    }

    public void Revert()
    {
        currentBytes = originalBytes.ToArray();
        HexText = FormatBytes(originalBytes);
        ByteSummary = $"{currentBytes.Length:N0} byte{(currentBytes.Length == 1 ? string.Empty : "s")}";
        HasSelection = true;
        HasUnsavedChanges = false;
        StatusText = "Reverted to the loaded bytes.";
    }

    private void NormalizeLoadedBytes()
    {
        if (TryCommitHexText(HexText, out _, out string message))
            StatusText = message;
    }

    public bool TryParseHexText(string text, out byte[] bytes, out string message)
    {
        if (TryParseLines(text, out bytes))
        {
            message = $"Parsed {bytes.Length:N0} byte{(bytes.Length == 1 ? string.Empty : "s")}.";
            return true;
        }

        bytes = Array.Empty<byte>();
        message = "No hex bytes could be parsed.";
        return false;
    }

    private static bool TryParseLines(string text, out byte[] bytes)
    {
        List<byte> parsed = [];

        foreach (string rawLine in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            int colonIndex = line.IndexOf(':');
            if (colonIndex >= 0)
                line = line[(colonIndex + 1)..];

            int pipeIndex = line.IndexOf('|');
            if (pipeIndex >= 0)
                line = line[..pipeIndex];

            line = line.Trim();
            if (line.Length == 0)
                continue;

            string[] tokens = line.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
            bool foundToken = false;

            foreach (string token in tokens)
            {
                string cleaned = new(token.Where(Uri.IsHexDigit).ToArray());
                if (cleaned.Length == 2 && byte.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out byte value))
                {
                    parsed.Add(value);
                    foundToken = true;
                }
            }

            if (foundToken)
                continue;

            string compact = new(line.Where(Uri.IsHexDigit).ToArray());
            if (compact.Length < 2 || compact.Length % 2 != 0)
                continue;

            for (int index = 0; index < compact.Length; index += 2)
            {
                if (byte.TryParse(compact.Substring(index, 2), System.Globalization.NumberStyles.HexNumber, null, out byte value))
                    parsed.Add(value);
            }
        }

        bytes = parsed.ToArray();
        return bytes.Length > 0;
    }

    private static string FormatBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        StringBuilder builder = new(bytes.Length * 4);
        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            int count = Math.Min(16, bytes.Length - offset);
            builder.Append(offset.ToString("X8"));
            builder.Append(": ");

            for (int index = 0; index < 16; index++)
            {
                if (index < count)
                {
                    builder.Append(bytes[offset + index].ToString("X2"));
                    builder.Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }
            }

            builder.Append(" |");
            for (int index = 0; index < count; index++)
            {
                byte value = bytes[offset + index];
                builder.Append(value >= 32 && value <= 126 ? (char)value : '.');
            }
            builder.AppendLine("|");
        }

        return builder.ToString().TrimEnd();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
