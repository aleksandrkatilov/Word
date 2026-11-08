namespace Pero.Core.Files;

/// <summary>
/// Отпечаток файла на диске для обнаружения внешних изменений (FILE-04):
/// фиксируется при открытии и после каждого успешного сохранения.
/// </summary>
public sealed record FileSnapshot(string Path, DateTime LastWriteUtc, long Length)
{
    public static FileSnapshot? Take(string path)
    {
        var fi = new FileInfo(path);
        return fi.Exists ? new FileSnapshot(fi.FullName, fi.LastWriteTimeUtc, fi.Length) : null;
    }

    /// <summary>true, если файл изменён или удалён другой программой.</summary>
    public bool ChangedOnDisk()
    {
        var now = Take(Path);
        return now is null || now.LastWriteUtc != LastWriteUtc || now.Length != Length;
    }

    public static bool IsReadOnly(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return false;
        if (fi.IsReadOnly) return true;
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return false;
        }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return false; } // занят другим процессом — это не read-only
    }
}
