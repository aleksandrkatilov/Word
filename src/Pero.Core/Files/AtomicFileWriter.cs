namespace Pero.Core.Files;

public sealed class SaveFailedException(string userMessage, Exception? inner = null) : Exception(userMessage, inner);

/// <summary>
/// Атомарное сохранение (FILE-02): временный файл в каталоге назначения → сброс на диск →
/// проверка пакета → замена оригинала. При любой ошибке исходный файл не изменяется.
/// </summary>
public static class AtomicFileWriter
{
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorDiskFull = unchecked((int)0x80070070);

    /// <param name="targetPath">Итоговый путь.</param>
    /// <param name="write">Записывает содержимое в поток.</param>
    /// <param name="validate">Проверяет готовый временный файл; вернуть текст ошибки или null.</param>
    public static void Write(string targetPath, Action<Stream> write, Func<string, string?>? validate = null)
    {
        string fullTarget = Path.GetFullPath(targetPath);
        string dir = Path.GetDirectoryName(fullTarget) ?? throw new SaveFailedException(Loc.L("Некорректный путь сохранения."));
        string temp = Path.Combine(dir, $"~${Path.GetFileNameWithoutExtension(fullTarget)}.{Guid.NewGuid():N}.tmp");

        try
        {
            if (File.Exists(fullTarget) && new FileInfo(fullTarget).IsReadOnly)
                throw new SaveFailedException(Loc.L("Файл доступен только для чтения. Сохраните копию под другим именем."));

            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                TryHide(temp);
                write(fs);
                fs.Flush(flushToDisk: true);
            }

            if (validate?.Invoke(temp) is { } problem)
                throw new SaveFailedException(Loc.F("Сохранённый файл не прошёл проверку: {0}. Исходный файл не изменён.", problem));

            if (File.Exists(fullTarget))
                File.Replace(temp, fullTarget, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, fullTarget);
            TryUnhide(fullTarget);
        }
        catch (SaveFailedException)
        {
            TryDelete(temp);
            throw;
        }
        catch (IOException ex) when (ex.HResult is ErrorDiskFull or ErrorHandleDiskFull || ex.Message.Contains("No space", StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(temp);
            throw new SaveFailedException(Loc.L("Недостаточно места на диске. Исходный файл не изменён."), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(temp);
            throw new SaveFailedException(Loc.L("Нет прав на запись в эту папку. Исходный файл не изменён."), ex);
        }
        catch (IOException ex)
        {
            TryDelete(temp);
            throw new SaveFailedException(Loc.F("Не удалось сохранить файл: {0}. Исходный файл не изменён.", ex.Message), ex);
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            throw new SaveFailedException(Loc.F("Ошибка при сохранении: {0}. Исходный файл не изменён.", ex.Message), ex);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* временный файл удалим при следующем запуске */ }
    }

    private static void TryHide(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); } catch { }
    }

    private static void TryUnhide(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var a = File.GetAttributes(path);
            if (a.HasFlag(FileAttributes.Hidden)) File.SetAttributes(path, a & ~FileAttributes.Hidden);
        }
        catch { }
    }
}
