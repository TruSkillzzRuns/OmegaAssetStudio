using System.Threading.Tasks;

using UpkManager.Helpers;
using UpkManager.Models.UpkFile.Objects;

namespace OmegaAssetStudio.WinUI.Modules.ObjectsWorkspace.Interop;

public sealed class RawUnrealObject : UnrealObjectBase
{
    private readonly byte[] rawBytes;

    public RawUnrealObject(byte[] rawBytes)
    {
        this.rawBytes = rawBytes ?? [];
    }

    public override int GetBuilderSize()
    {
        BuilderSize = rawBytes.Length;
        return BuilderSize;
    }

    public override async Task WriteBuffer(ByteArrayWriter writer, int CurrentOffset)
    {
        await writer.WriteBytes(rawBytes);
    }
}
