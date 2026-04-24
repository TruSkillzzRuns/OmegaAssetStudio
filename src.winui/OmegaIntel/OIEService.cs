using System;
using System.IO;
using System.Threading.Tasks;

namespace OmegaAssetStudio.WinUI.OmegaIntel;

internal sealed class OIEService
{
    private readonly OmegaIntelAnalyzerService analyzerService = new();

    public Task<OmegaIntelScanResult> StartScan(string upkPath)
    {
        return StartScan(upkPath, null);
    }

    public Task<OmegaIntelScanResult> StartScan(string upkPath, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new ArgumentException("A UPK path is required.", nameof(upkPath));

        string fullPath = Path.GetFullPath(upkPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(fullPath);

        return Task.Run(() => analyzerService.ScanUpkAsync(fullPath, log ?? (_ => { })));
    }
}

