using System.Text.Json;

namespace Pero.Core.Files;

public sealed record RecoveryEntry(
    string DocId,
    string SessionId,
    string? OriginalPath,
    string Title,
    DateTime SavedUtc)
{
    public string DataPath(string root) => Path.Combine(root, DocId + ".docx");
}

/// <summary>
/// Локальные копии автовосстановления (FILE-03, QA-04). Копии лежат отдельно от исходных файлов.
/// Каждый запущенный процесс держит эксклюзивную блокировку session-*.lock; копии сеанса,
/// чья блокировка не удерживается, считаются оставшимися после аварии.
/// </summary>
public sealed class RecoveryStore
{
    public string Root { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public RecoveryStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pero", "Recovery");

    public Session StartSession() => new(this);

    public sealed class Session : IDisposable
    {
        private readonly RecoveryStore _store;
        private readonly FileStream _lock;
        public string Id { get; } = Guid.NewGuid().ToString("N");

        internal Session(RecoveryStore store)
        {
            _store = store;
            _lock = new FileStream(LockPath(store.Root, Id), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.DeleteOnClose);
        }

        /// <summary>Записать копию документа. Пишется атомарно, предыдущая копия остаётся до успешной записи.</summary>
        public void Save(string docId, string? originalPath, string title, Action<Stream> write)
        {
            var entry = new RecoveryEntry(docId, Id, originalPath, title, DateTime.UtcNow);
            AtomicFileWriter.Write(entry.DataPath(_store.Root), write);
            AtomicFileWriter.Write(MetaPath(_store.Root, docId),
                s => JsonSerializer.Serialize(s, entry, Json));
        }

        /// <summary>Удалить копию после штатного сохранения или осознанного закрытия без сохранения.</summary>
        public void Remove(string docId) => _store.Remove(docId);

        public void Dispose() => _lock.Dispose();
    }

    private static string LockPath(string root, string sessionId) => Path.Combine(root, $"session-{sessionId}.lock");
    private static string MetaPath(string root, string docId) => Path.Combine(root, docId + ".json");

    public void Remove(string docId)
    {
        TryDelete(Path.Combine(Root, docId + ".docx"));
        TryDelete(MetaPath(Root, docId));
    }

    /// <summary>Копии, оставшиеся от аварийно завершённых сеансов.</summary>
    public IReadOnlyList<RecoveryEntry> FindOrphans(string? currentSessionId = null)
    {
        var result = new List<RecoveryEntry>();
        foreach (var meta in Directory.EnumerateFiles(Root, "*.json"))
        {
            RecoveryEntry? e;
            try { e = JsonSerializer.Deserialize<RecoveryEntry>(File.ReadAllText(meta)); }
            catch { continue; }
            if (e is null || e.SessionId == currentSessionId) continue;
            if (!File.Exists(e.DataPath(Root))) continue;
            if (IsSessionAlive(e.SessionId)) continue;
            result.Add(e);
        }
        return result.OrderByDescending(e => e.SavedUtc).ToList();
    }

    private bool IsSessionAlive(string sessionId)
    {
        string path = LockPath(Root, sessionId);
        if (!File.Exists(path)) return false;
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false; // открылся эксклюзивно — владелец не работает
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>Удалить копии старше указанного срока и незавершённые временные файлы.</summary>
    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var f in Directory.EnumerateFiles(Root))
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith("session-", StringComparison.Ordinal)) continue;
            if (name.StartsWith("~$", StringComparison.Ordinal) || File.GetLastWriteTimeUtc(f) < cutoff)
                TryDelete(f);
        }
    }

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
