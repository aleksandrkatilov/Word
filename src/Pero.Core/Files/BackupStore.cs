using System.Security.Cryptography;
using System.Text;

namespace Pero.Core.Files;

/// <summary>Резервная версия файла.</summary>
public sealed record BackupVersion(string Path, DateTime SavedUtc, long Length);

/// <summary>
/// Резервные версии (REL-01): перед заменой файла при сохранении прежняя версия копируется в локальное
/// хранилище. По каждому файлу хранится не меньше 10 последних версий (по умолчанию 20).
/// </summary>
public sealed class BackupStore(string root, int keep = 20)
{
    public string Root { get; } = root;
    public int Keep { get; } = Math.Max(10, keep);

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pero", "Backups");

    private string DirFor(string file)
    {
        string full = Path.GetFullPath(file);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..16];
        return Path.Combine(Root, key);
    }

    /// <summary>Сохранить текущую версию файла (если он есть). Ошибки не мешают сохранению документа.</summary>
    public BackupVersion? Backup(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            string dir = DirFor(file);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "source.txt"), Path.GetFullPath(file));
            string name = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + Path.GetExtension(file);
            string dst = Path.Combine(dir, name);
            File.Copy(file, dst, overwrite: true);
            Prune(dir);
            var fi = new FileInfo(dst);
            return new BackupVersion(dst, fi.LastWriteTimeUtc, fi.Length);
        }
        catch { return null; }
    }

    /// <summary>Версии файла, новые первыми.</summary>
    public IReadOnlyList<BackupVersion> List(string file)
    {
        string dir = DirFor(file);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir).Where(f => !f.EndsWith("source.txt", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupVersion(f.FullName, ParseTime(f.Name) ?? f.LastWriteTimeUtc, f.Length)).ToList();
    }

    private static DateTime? ParseTime(string name) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(name), "yyyyMMdd-HHmmss-fff", null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    private void Prune(string dir)
    {
        var files = Directory.EnumerateFiles(dir).Where(f => !f.EndsWith("source.txt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
        foreach (var old in files.Skip(Keep)) try { File.Delete(old); } catch { }
    }
}

/// <summary>
/// Признак «файл открыт в другом окне "Пера"» (REL-02): файл-метка с номером процесса в локальной папке
/// приложения. Метка устаревшего (завершённого) процесса не мешает открытию.
/// </summary>
public sealed class FileLock : IDisposable
{
    private readonly string _lockPath;
    public bool Acquired { get; }
    /// <summary>Номер процесса, который держит файл, если он не наш.</summary>
    public int? OwnerPid { get; }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pero", "Locks");

    public FileLock(string file, string? root = null)
    {
        root ??= DefaultRoot;
        Directory.CreateDirectory(root);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..16];
        _lockPath = Path.Combine(root, key + ".lock");
        int me = Environment.ProcessId;
        try
        {
            if (File.Exists(_lockPath) && int.TryParse(File.ReadAllText(_lockPath).Trim(), out int pid) && pid != me && Alive(pid))
            {
                OwnerPid = pid;
                return;
            }
            File.WriteAllText(_lockPath, me.ToString());
            Acquired = true;
        }
        catch { Acquired = true; } // при сбое метки не мешаем работе
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited && string.Equals(p.ProcessName, System.Diagnostics.Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (!Acquired) return;
        try
        {
            if (File.Exists(_lockPath) && File.ReadAllText(_lockPath).Trim() == Environment.ProcessId.ToString()) File.Delete(_lockPath);
        }
        catch { }
    }
}
