using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Pero.Core.Packaging;

namespace Pero.Doc.Docx;

/// <summary>
/// Части пакета DOCX в памяти. Ядро переписывает только основную разметку, связи, типы содержимого и —
/// при добавлении стилей или нумерации — styles.xml / numbering.xml. Остальные части (колонтитулы,
/// темы, медиа, настройки, примечания…) записываются байт в байт.
/// </summary>
internal sealed class PackageStore
{
    public Dictionary<string, byte[]> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Order { get; } = [];
    public string MainPart { get; set; } = "word/document.xml";
    public XDocument? DocumentShell { get; set; }

    public string MainDir => MainPart.Contains('/') ? MainPart[..MainPart.LastIndexOf('/')] : "";
    public string MainRelsPath => (MainDir.Length == 0 ? "" : MainDir + "/") + "_rels/" + Path.GetFileName(MainPart) + ".rels";

    public static PackageStore Read(Stream stream, PackageLimits? limits = null)
    {
        var lim = limits ?? PackageLimits.Default;
        var store = new PackageStore();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > lim.MaxEntries) throw new InvalidDataException("слишком много частей пакета");
        var budget = new DecompressionBudget(lim.MaxTotalUncompressedBytes);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            using var s = new BoundedReadStream(e.Open(), budget);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            store.Parts[e.FullName] = ms.ToArray();
            store.Order.Add(e.FullName);
        }
        var rels = store.LoadXml("_rels/.rels") ?? throw new InvalidDataException("нет _rels/.rels");
        var main = rels.Root!.Elements(Ns.Rel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Type") == Ns.RelOfficeDoc)
            ?? throw new InvalidDataException("нет основной части документа");
        store.MainPart = ((string)main.Attribute("Target")!).TrimStart('/');
        if (!store.Parts.ContainsKey(store.MainPart)) throw new InvalidDataException("отсутствует основная часть документа");
        return store;
    }

    public XDocument? LoadXml(string part)
    {
        if (!Parts.TryGetValue(part, out var bytes)) return null;
        using var ms = new MemoryStream(bytes);
        using var r = SafeXml.Create(ms);
        return XDocument.Load(r, LoadOptions.PreserveWhitespace);
    }

    public void SaveXml(string part, XDocument doc)
    {
        using var ms = new MemoryStream();
        var settings = new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
        using (var w = System.Xml.XmlWriter.Create(ms, settings))
        {
            doc.Declaration ??= new XDeclaration("1.0", "UTF-8", "yes");
            doc.Save(w);
        }
        if (!Parts.ContainsKey(part)) Order.Add(part);
        Parts[part] = ms.ToArray();
    }

    public void WriteTo(Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        // [Content_Types].xml первым — так пишут Word и большинство библиотек
        foreach (var name in Order.OrderBy(n => n == "[Content_Types].xml" ? 0 : 1))
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = e.Open();
            s.Write(Parts[name]);
        }
    }

    public PackageStore CloneShallow()
    {
        var c = new PackageStore { MainPart = MainPart, DocumentShell = DocumentShell };
        foreach (var kv in Parts) c.Parts[kv.Key] = kv.Value;
        c.Order.AddRange(Order);
        return c;
    }

    // ── связи основной части ─────────────────────────────────────────────────

    public string ResolveTarget(string relId)
    {
        var rels = LoadXml(MainRelsPath);
        var rel = rels?.Root?.Elements(Ns.Rel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Id") == relId);
        return (string?)rel?.Attribute("Target") ?? "";
    }

    public string? RelTargetMode(string relId)
    {
        var rels = LoadXml(MainRelsPath);
        return (string?)rels?.Root?.Elements(Ns.Rel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Id") == relId)?.Attribute("TargetMode");
    }

    /// <summary>Найти или добавить связь основной части. Возвращает Id.</summary>
    public string EnsureRelationship(string type, string target, bool external)
    {
        var rels = LoadXml(MainRelsPath) ?? new XDocument(new XElement(Ns.Rel + "Relationships"));
        var root = rels.Root!;
        var existing = root.Elements(Ns.Rel + "Relationship").FirstOrDefault(r =>
            (string?)r.Attribute("Type") == type && (string?)r.Attribute("Target") == target &&
            ((string?)r.Attribute("TargetMode") == "External") == external);
        if (existing is not null) return (string)existing.Attribute("Id")!;
        var ids = root.Elements().Select(r => (string?)r.Attribute("Id")).ToHashSet();
        string id = Enumerable.Range(1, 100000).Select(i => "rIdP" + i).First(i => !ids.Contains(i));
        var rel = new XElement(Ns.Rel + "Relationship", new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));
        if (external) rel.Add(new XAttribute("TargetMode", "External"));
        root.Add(rel);
        SaveXml(MainRelsPath, rels);
        return id;
    }

    public string? FindRelTarget(string type)
    {
        var rels = LoadXml(MainRelsPath);
        var rel = rels?.Root?.Elements(Ns.Rel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Type") == type);
        if (rel is null) return null;
        string target = (string)rel.Attribute("Target")!;
        return target.StartsWith('/') ? target.TrimStart('/') : (MainDir.Length == 0 ? target : MainDir + "/" + target);
    }

    public void EnsureContentTypeDefault(string extension, string contentType)
    {
        var ct = LoadXml("[Content_Types].xml")!;
        var root = ct.Root!;
        if (root.Elements(Ns.CT + "Default").Any(d => string.Equals((string?)d.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase))) return;
        root.AddFirst(new XElement(Ns.CT + "Default", new XAttribute("Extension", extension), new XAttribute("ContentType", contentType)));
        SaveXml("[Content_Types].xml", ct);
    }

    public void EnsureContentTypeOverride(string partName, string contentType)
    {
        var ct = LoadXml("[Content_Types].xml")!;
        var root = ct.Root!;
        string pn = "/" + partName;
        if (root.Elements(Ns.CT + "Override").Any(o => string.Equals((string?)o.Attribute("PartName"), pn, StringComparison.OrdinalIgnoreCase))) return;
        root.Add(new XElement(Ns.CT + "Override", new XAttribute("PartName", pn), new XAttribute("ContentType", contentType)));
        SaveXml("[Content_Types].xml", ct);
    }

    // ── пустой документ ──────────────────────────────────────────────────────

    public static PackageStore Blank()
    {
        var s = new PackageStore();
        void Put(string name, string xml) { s.Parts[name] = Encoding.UTF8.GetBytes(xml); s.Order.Add(name); }
        Put("[Content_Types].xml", BlankTemplates.ContentTypes);
        Put("_rels/.rels", BlankTemplates.RootRels);
        Put("word/_rels/document.xml.rels", BlankTemplates.DocumentRels);
        Put("word/document.xml", BlankTemplates.Document);
        Put("word/styles.xml", BlankTemplates.Styles);
        Put("word/numbering.xml", BlankTemplates.NumberingXml);
        Put("word/settings.xml", BlankTemplates.Settings);
        Put("docProps/core.xml", BlankTemplates.Core);
        Put("docProps/app.xml", BlankTemplates.App);
        return s;
    }
}
