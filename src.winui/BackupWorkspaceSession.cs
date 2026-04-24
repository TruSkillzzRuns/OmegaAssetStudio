using System.IO;
using System.Text.Json;

namespace OmegaAssetStudio.WinUI;

internal static class BackupWorkspaceSession
{
    private static readonly string StoragePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaAssetStudio", "BackupWorkspaceSession.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string? LastFolderPath => ReadData()?.LastFolderPath;
    public static bool Recursive => ReadData()?.Recursive ?? false;

    public static void Remember(string? folderPath = null, bool? recursive = null)
    {
        SessionData data = ReadData() ?? new SessionData();
        if (!string.IsNullOrWhiteSpace(folderPath))
            data.LastFolderPath = folderPath;
        if (recursive.HasValue)
            data.Recursive = recursive.Value;
        WriteData(data);
    }

    private static SessionData? ReadData()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return null;

            return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(StoragePath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteData(SessionData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch
        {
        }
    }

    private sealed class SessionData
    {
        public string? LastFolderPath { get; set; }
        public bool Recursive { get; set; }
    }
}

