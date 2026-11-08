using System.IO.Compression;
using System.Xml.Linq;

namespace Pero.Core.Packaging;

/// <summary>
/// Сохранность отделимых частей OOXML (DOCX-01). Движок импортирует документ в свою модель и при
/// записи может отбросить части, которые не отображает. Часть безопасно перенести, если она не
/// связана с разметкой текста: пользовательский XML (customXml) и пользовательские свойства
/// (docProps/custom.xml). Такие части запоминаются при открытии и возвращаются в пакет при
/// сохранении, если движок их не записал.
/// </summary>
public sealed class DetachedParts
{
    private const string PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string CtNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    public const string CustomXmlRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    public const string CustomPropsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string OfficeDocRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    private sealed record Part(string Name, byte[] Data, string ContentType);
    private sealed record Group(string RelType, bool FromRoot, string Target, List<Part> Parts);

    private readonly List<Group> _groups = [];

    public bool IsEmpty => _groups.Count == 0;
    public IEnumerable<string> PartNames => _groups.SelectMany(g => g.Parts).Select(p => p.Name);

    /// <summary>Запомнить отделимые части исходного пакета.</summary>
    public static DetachedParts Capture(Stream package, PackageLimits? limits = null)
    {
        var lim = limits ?? PackageLimits.Default;
        var result = new DetachedParts();
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var budget = new DecompressionBudget(Math.Min(lim.MaxTotalUncompressedBytes, 256L * 1024 * 1024));
        var ct = ReadContentTypes(zip, budget);

        var rootRels = ReadRels(zip, "_rels/.rels", budget);
        string? main = rootRels.FirstOrDefault(r => r.Type == OfficeDocRel)?.Target.TrimStart('/');
        if (main is null) return result;

        foreach (var rel in rootRels.Where(r => r.Type == CustomPropsRel && !r.External))
            result.AddGroup(zip, ct, budget, CustomPropsRel, fromRoot: true, Resolve("", rel.Target));

        string mainDir = DirOf(main);
        foreach (var rel in ReadRels(zip, RelsPathFor(main), budget).Where(r => r.Type == CustomXmlRel && !r.External))
            result.AddGroup(zip, ct, budget, CustomXmlRel, fromRoot: false, Resolve(mainDir, rel.Target));

        return result;
    }

    public static DetachedParts Capture(string path)
    {
        using var fs = File.OpenRead(path);
        return Capture(fs);
    }

    private void AddGroup(ZipArchive zip, ContentTypes ct, DecompressionBudget budget, string relType, bool fromRoot, string target)
    {
        var parts = new List<Part>();
        var queue = new Queue<string>([target]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string topFolder = target.Split('/')[0];
        while (queue.Count > 0)
        {
            string name = queue.Dequeue();
            if (!seen.Add(name)) continue;
            var entry = zip.GetEntry(name);
            if (entry is null) continue;
            parts.Add(new Part(name, ReadAll(entry, budget), ct.TypeOf(name)));

            string relsPath = RelsPathFor(name);
            var relsEntry = zip.GetEntry(relsPath);
            if (relsEntry is null) continue;
            parts.Add(new Part(relsPath, ReadAll(relsEntry, budget), "application/vnd.openxmlformats-package.relationships+xml"));
            foreach (var r in ReadRels(zip, relsPath, budget).Where(r => !r.External))
            {
                string t = Resolve(DirOf(name), r.Target);
                // переносим только части в том же каталоге верхнего уровня, чтобы не задеть основную разметку
                if (t.Split('/')[0].Equals(topFolder, StringComparison.OrdinalIgnoreCase)) queue.Enqueue(t);
            }
        }
        if (parts.Count > 0) _groups.Add(new Group(relType, fromRoot, target, parts));
    }

    public sealed record ReinjectResult(IReadOnlyList<string> Restored, IReadOnlyList<string> Skipped);

    /// <summary>
    /// Скопировать пакет <paramref name="saved"/> в <paramref name="output"/>, вернув отделимые части,
    /// которые движок не записал. Если движок сохранил связь нужного типа сам, группа не трогается.
    /// </summary>
    public ReinjectResult ReinjectInto(Stream saved, Stream output)
    {
        var restored = new List<string>();
        var skipped = new List<string>();

        using var src = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var budget = new DecompressionBudget(PackageLimits.Default.MaxTotalUncompressedBytes);

        var rootRelsDoc = LoadXml(src, "_rels/.rels", budget) ?? throw new InvalidDataException("нет _rels/.rels");
        string main = rootRelsDoc.Root!.Elements(XName.Get("Relationship", PkgRelNs))
            .First(e => (string?)e.Attribute("Type") == OfficeDocRel).Attribute("Target")!.Value.TrimStart('/');
        string mainRelsPath = RelsPathFor(main);
        var mainRelsDoc = LoadXml(src, mainRelsPath, budget) ?? new XDocument(new XElement(XName.Get("Relationships", PkgRelNs)));
        var ctDoc = LoadXml(src, "[Content_Types].xml", budget) ?? throw new InvalidDataException("нет [Content_Types].xml");

        var existing = new HashSet<string>(src.Entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
        var newParts = new List<Part>();
        bool rootChanged = false, mainChanged = false;

        foreach (var typeGroups in _groups.GroupBy(g => (g.RelType, g.FromRoot)))
        {
            var relsDoc = typeGroups.Key.FromRoot ? rootRelsDoc : mainRelsDoc;
            bool engineKept = relsDoc.Root!.Elements(XName.Get("Relationship", PkgRelNs))
                .Any(e => (string?)e.Attribute("Type") == typeGroups.Key.RelType);
            if (engineKept) continue;

            foreach (var g in typeGroups)
            {
                var clash = g.Parts.FirstOrDefault(p => existing.Contains(p.Name));
                if (clash is not null) { skipped.Add($"{g.Target} (имя занято: {clash.Name})"); continue; }

                newParts.AddRange(g.Parts);
                foreach (var p in g.Parts) existing.Add(p.Name);
                string relTarget = g.FromRoot ? g.Target : RelativeFrom(DirOf(main), g.Target);
                relsDoc.Root.Add(new XElement(XName.Get("Relationship", PkgRelNs),
                    new XAttribute("Id", UniqueId(relsDoc)),
                    new XAttribute("Type", g.RelType),
                    new XAttribute("Target", relTarget)));
                if (g.FromRoot) rootChanged = true; else mainChanged = true;
                restored.Add(g.Target);
            }
        }

        foreach (var p in newParts.Where(p => !p.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            EnsureContentType(ctDoc, p);

        using var dst = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var e in src.Entries)
        {
            if (e.FullName == "[Content_Types].xml") { WriteXml(dst, e.FullName, ctDoc); continue; }
            if (rootChanged && e.FullName == "_rels/.rels") { WriteXml(dst, e.FullName, rootRelsDoc); continue; }
            if (mainChanged && e.FullName.Equals(mainRelsPath, StringComparison.OrdinalIgnoreCase)) { WriteXml(dst, e.FullName, mainRelsDoc); continue; }
            var ne = dst.CreateEntry(e.FullName, CompressionLevel.Optimal);
            ne.LastWriteTime = e.LastWriteTime;
            using var from = new BoundedReadStream(e.Open(), budget);
            using var to = ne.Open();
            from.CopyTo(to);
        }
        if (mainChanged && src.GetEntry(mainRelsPath) is null) WriteXml(dst, mainRelsPath, mainRelsDoc);
        foreach (var p in newParts)
        {
            var ne = dst.CreateEntry(p.Name, CompressionLevel.Optimal);
            using var to = ne.Open();
            to.Write(p.Data);
        }
        return new ReinjectResult(restored, skipped);
    }

    // ── вспомогательное ───────────────────────────────────────────────────────

    private static void EnsureContentType(XDocument ct, Part p)
    {
        if (string.IsNullOrEmpty(p.ContentType)) return;
        var root = ct.Root!;
        string ext = Path.GetExtension(p.Name).TrimStart('.');
        var def = root.Elements(XName.Get("Default", CtNs))
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("Extension"), ext, StringComparison.OrdinalIgnoreCase));
        if (def is not null && (string?)def.Attribute("ContentType") == p.ContentType) return;
        string partName = "/" + p.Name;
        if (root.Elements(XName.Get("Override", CtNs)).Any(e => string.Equals((string?)e.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase)))
            return;
        if (def is null && ext.Length > 0)
            root.Add(new XElement(XName.Get("Default", CtNs), new XAttribute("Extension", ext), new XAttribute("ContentType", p.ContentType)));
        else
            root.Add(new XElement(XName.Get("Override", CtNs), new XAttribute("PartName", partName), new XAttribute("ContentType", p.ContentType)));
    }

    private static string UniqueId(XDocument rels)
    {
        var ids = rels.Root!.Elements().Select(e => (string?)e.Attribute("Id")).ToHashSet();
        for (int i = 1; ; i++) if (!ids.Contains($"rIdPero{i}")) return $"rIdPero{i}";
    }

    private sealed record Rel(string Type, string Target, bool External);

    private static List<Rel> ReadRels(ZipArchive zip, string path, DecompressionBudget budget)
    {
        var doc = LoadXml(zip, path, budget);
        if (doc is null) return [];
        return doc.Root!.Elements(XName.Get("Relationship", PkgRelNs))
            .Select(e => new Rel((string?)e.Attribute("Type") ?? "", (string?)e.Attribute("Target") ?? "",
                string.Equals((string?)e.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private sealed class ContentTypes(Dictionary<string, string> defaults, Dictionary<string, string> overrides)
    {
        public string TypeOf(string name) =>
            overrides.TryGetValue("/" + name, out var t) ? t
            : defaults.TryGetValue(Path.GetExtension(name).TrimStart('.'), out t) ? t : "";
    }

    private static ContentTypes ReadContentTypes(ZipArchive zip, DecompressionBudget budget)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = LoadXml(zip, "[Content_Types].xml", budget);
        if (doc is not null)
        {
            foreach (var e in doc.Root!.Elements(XName.Get("Default", CtNs)))
                d[(string?)e.Attribute("Extension") ?? ""] = (string?)e.Attribute("ContentType") ?? "";
            foreach (var e in doc.Root!.Elements(XName.Get("Override", CtNs)))
                o[(string?)e.Attribute("PartName") ?? ""] = (string?)e.Attribute("ContentType") ?? "";
        }
        return new ContentTypes(d, o);
    }

    private static XDocument? LoadXml(ZipArchive zip, string path, DecompressionBudget budget)
    {
        var e = zip.GetEntry(path);
        if (e is null) return null;
        using var s = new BoundedReadStream(e.Open(), budget);
        using var r = SafeXml.Create(s, 64 * 1024 * 1024);
        return XDocument.Load(r);
    }

    private static void WriteXml(ZipArchive zip, string path, XDocument doc)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = e.Open();
        doc.Declaration ??= new XDeclaration("1.0", "UTF-8", "yes");
        doc.Save(s, SaveOptions.DisableFormatting);
    }

    private static byte[] ReadAll(ZipArchiveEntry e, DecompressionBudget budget)
    {
        using var s = new BoundedReadStream(e.Open(), budget);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    internal static string RelsPathFor(string part)
    {
        string dir = DirOf(part);
        string file = part[(dir.Length == 0 ? 0 : dir.Length + 1)..];
        return (dir.Length == 0 ? "" : dir + "/") + "_rels/" + file + ".rels";
    }

    internal static string DirOf(string part)
    {
        int i = part.LastIndexOf('/');
        return i < 0 ? "" : part[..i];
    }

    /// <summary>Разрешить относительную ссылку из каталога baseDir в имя части без ведущего «/».</summary>
    internal static string Resolve(string baseDir, string target)
    {
        if (target.StartsWith('/')) return target.TrimStart('/');
        var stack = new List<string>(baseDir.Length == 0 ? [] : baseDir.Split('/'));
        foreach (var seg in target.Split('/'))
        {
            if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
            else if (seg != "." && seg.Length > 0) stack.Add(seg);
        }
        return string.Join('/', stack);
    }

    internal static string RelativeFrom(string baseDir, string target)
    {
        if (baseDir.Length == 0) return target;
        var b = baseDir.Split('/');
        var t = target.Split('/');
        int common = 0;
        while (common < b.Length && common < t.Length - 1 && b[common].Equals(t[common], StringComparison.OrdinalIgnoreCase)) common++;
        return string.Join('/', Enumerable.Repeat("..", b.Length - common).Concat(t.Skip(common)));
    }
}
