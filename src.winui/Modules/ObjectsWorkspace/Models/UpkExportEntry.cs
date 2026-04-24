using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Models;

public sealed class UpkExportEntry : INotifyPropertyChanged
{
    private int tableIndex;
    private string objectName = string.Empty;
    private string className = string.Empty;
    private string outerName = string.Empty;
    private int serialSize;
    private int serialOffset;
    private byte[] rawData = Array.Empty<byte>();
    private bool isExport = true;
    private bool isImport;
    private UpkBulkDataInfo? bulkDataInfo;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int TableIndex
    {
        get => tableIndex;
        set => SetField(ref tableIndex, value);
    }

    public string ObjectName
    {
        get => objectName;
        set => SetField(ref objectName, value);
    }

    public string ClassName
    {
        get => className;
        set => SetField(ref className, value);
    }

    public string OuterName
    {
        get => outerName;
        set => SetField(ref outerName, value);
    }

    public int SerialSize
    {
        get => serialSize;
        set => SetField(ref serialSize, value);
    }

    public int SerialOffset
    {
        get => serialOffset;
        set => SetField(ref serialOffset, value);
    }

    public byte[] RawData
    {
        get => rawData;
        set => SetField(ref rawData, value ?? Array.Empty<byte>());
    }

    public bool IsExport
    {
        get => isExport;
        set => SetField(ref isExport, value);
    }

    public bool IsImport
    {
        get => isImport;
        set => SetField(ref isImport, value);
    }

    public UpkBulkDataInfo? BulkDataInfo
    {
        get => bulkDataInfo;
        set => SetField(ref bulkDataInfo, value);
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
