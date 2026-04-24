using System.IO;
using System.Text.Json;

namespace OmegaAssetStudio.WinUI;

internal static class WorkspaceSessionStore
{
    private static readonly string StoragePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaAssetStudio", "WorkspaceSession.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static WorkspaceSessionData Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return new WorkspaceSessionData();

            WorkspaceSessionData? data = JsonSerializer.Deserialize<WorkspaceSessionData>(File.ReadAllText(StoragePath), JsonOptions);
            return data ?? new WorkspaceSessionData();
        }
        catch
        {
            return new WorkspaceSessionData();
        }
    }

    public static void Save(WorkspaceSessionData data)
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

    public static void RememberLastWorkspace(string? workspaceTag)
    {
        WorkspaceSessionData data = Load();
        data.LastWorkspaceTag = workspaceTag ?? string.Empty;
        Save(data);
    }

    public static TextureWorkspaceSession Texture
    {
        get => Load().Texture;
    }

    public static OmegaIntelWorkspaceSession OmegaIntel
    {
        get => Load().OmegaIntel;
    }

    public static MeshWorkspaceSession Mesh
    {
        get => Load().Mesh;
    }

    public static RetargetWorkspaceSession Retarget
    {
        get => Load().Retarget;
    }

    public static BackupWorkspaceSessionState Backup
    {
        get => Load().Backup;
    }

    public static void RememberTexture(TextureWorkspaceSession session)
    {
        WorkspaceSessionData data = Load();
        data.Texture = session;
        Save(data);
    }

    public static void RememberOmegaIntel(OmegaIntelWorkspaceSession session)
    {
        WorkspaceSessionData data = Load();
        data.OmegaIntel = session;
        Save(data);
    }

    public static void RememberMesh(MeshWorkspaceSession session)
    {
        WorkspaceSessionData data = Load();
        data.Mesh = session;
        Save(data);
    }

    public static void RememberRetarget(RetargetWorkspaceSession session)
    {
        WorkspaceSessionData data = Load();
        data.Retarget = session;
        Save(data);
    }

    public static void RememberBackup(BackupWorkspaceSessionState session)
    {
        WorkspaceSessionData data = Load();
        data.Backup = session;
        Save(data);
    }

    public sealed class WorkspaceSessionData
    {
        public string LastWorkspaceTag { get; set; } = string.Empty;
        public TextureWorkspaceSession Texture { get; set; } = new();
        public OmegaIntelWorkspaceSession OmegaIntel { get; set; } = new();
        public MeshWorkspaceSession Mesh { get; set; } = new();
        public RetargetWorkspaceSession Retarget { get; set; } = new();
        public BackupWorkspaceSessionState Backup { get; set; } = new();
    }

    public sealed class TextureWorkspaceSession
    {
        public string UpkPath { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public string TextureType { get; set; } = string.Empty;
        public string UpscaleMethod { get; set; } = string.Empty;
        public string PreviewChannel { get; set; } = string.Empty;
        public string SelectedExportPath { get; set; } = string.Empty;
        public string PendingReplacementPath { get; set; } = string.Empty;
    }

    public sealed class OmegaIntelWorkspaceSession
    {
        public string RootPath { get; set; } = string.Empty;
        public string ScanSearchText { get; set; } = string.Empty;
        public bool ScanUpkOnly { get; set; }
        public string GraphSearchText { get; set; } = string.Empty;
        public string GraphKindFilter { get; set; } = "All";
    }

    public sealed class MeshWorkspaceSession
    {
        public string UpkPath { get; set; } = string.Empty;
        public string ExportPath { get; set; } = string.Empty;
        public string ImportPath { get; set; } = string.Empty;
        public string ViewMode { get; set; } = string.Empty;
    }

    public sealed class RetargetWorkspaceSession
    {
        public string UpkPath { get; set; } = string.Empty;
        public string ExportPath { get; set; } = string.Empty;
        public string BatchFolderPath { get; set; } = string.Empty;
        public string SelectedPosePreset { get; set; } = string.Empty;
    }

    public sealed class BackupWorkspaceSessionState
    {
        public string LastFolderPath { get; set; } = string.Empty;
        public bool Recursive { get; set; }
    }
}

