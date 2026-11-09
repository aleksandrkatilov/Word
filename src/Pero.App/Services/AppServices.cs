using System.IO;
using System.Text.Json;
using Pero.Core.Compatibility;
using Pero.Core.Files;
using Pero.Core.Packaging;

namespace Pero.Services;

/// <summary>Общие службы процесса: реестр совместимости, инспектор, восстановление, последние файлы.</summary>
public sealed class AppServices : IDisposable
{
    public CompatibilityRegistry Registry { get; }
    public PackageInspector Inspector { get; }
    public RecoveryStore Recovery { get; }
    public RecoveryStore.Session RecoverySession { get; }
    public RecentFiles Recent { get; }
    public BackupStore Backups { get; } = new(BackupStore.DefaultRoot);
    public AppSettings Settings { get; }
    public string TempDir { get; }

    public AppServices(AppSettings settings)
    {
        Settings = settings;
        string exeDir = AppContext.BaseDirectory;
        Registry = CompatibilityRegistry.LoadOrDefault(Path.Combine(exeDir, "compat-registry.json"));
        Inspector = new PackageInspector(Registry);
        Recovery = new RecoveryStore(RecoveryStore.DefaultRoot);
        Recovery.Cleanup(TimeSpan.FromDays(14));
        RecoverySession = Recovery.StartSession();
        Recent = new RecentFiles(RecentFiles.DefaultPath).Load();

        TempDir = Path.Combine(Path.GetTempPath(), "Pero", RecoverySession.Id);
        Directory.CreateDirectory(TempDir);
        CleanupStaleTemp();
    }

    /// <summary>Путь для промежуточного файла движка (не в папке пользователя).</summary>
    public string NewTempPath(string extension) => Path.Combine(TempDir, Guid.NewGuid().ToString("N") + extension);

    private void CleanupStaleTemp()
    {
        try
        {
            string root = Path.Combine(Path.GetTempPath(), "Pero");
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(dir, TempDir, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                    try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        RecoverySession.Dispose();
        try { Directory.Delete(TempDir, true); } catch { }
    }
}

public sealed class AppSettings
{
    /// <summary>Язык интерфейса: ru или en.</summary>
    public string Language { get; set; } = "ru";
    public int AutoRecoverSeconds { get; set; } = 30;
    public bool ShowHorizontalRuler { get; set; } = true;
    public bool ShowVerticalRuler { get; set; } = true;

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pero", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            AtomicFileWriter.Write(FilePath, s => JsonSerializer.Serialize(s, this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
