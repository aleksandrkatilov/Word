using System.Text.Json;

namespace Pero.Core.Files;

/// <summary>Список последних файлов (UI-02). Удаление из списка не трогает сам файл.</summary>
public sealed class RecentFiles(string storePath, int capacity = 20)
{
    private List<string> _items = [];

    public IReadOnlyList<string> Items => _items;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pero", "recent.json");

    public RecentFiles Load()
    {
        try
        {
            if (File.Exists(storePath))
                _items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(storePath)) ?? [];
        }
        catch { _items = []; }
        return this;
    }

    public void Add(string path)
    {
        string full = Path.GetFullPath(path);
        _items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, full);
        if (_items.Count > capacity) _items.RemoveRange(capacity, _items.Count - capacity);
        Persist();
    }

    public void Remove(string path)
    {
        _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Persist();
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            AtomicFileWriter.Write(storePath, s => JsonSerializer.Serialize(s, _items));
        }
        catch { /* список последних файлов не критичен */ }
    }
}
