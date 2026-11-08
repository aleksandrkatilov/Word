using System.IO.Compression;
using System.Text;

namespace Pero.Core.Tests;

/// <summary>Сборщик минимальных DOCX-пакетов для тестов.</summary>
internal sealed class MiniDocx
{
    public string Body { get; set; } = "<w:p><w:r><w:t>Привет, мир</w:t></w:r></w:p>";
    public bool WithCustomXml { get; set; }
    public bool WithCustomProps { get; set; }
    public List<(string Name, string ContentType, string Data)> ExtraParts { get; } = [];
    public List<(string Name, string Data)> RawEntries { get; } = [];

    private const string Ns = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                              "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                              "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var ct = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>""");
            var rootRels = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>""");
            var docRels = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");

            if (WithCustomProps)
            {
                ct.Append("""<Override PartName="/docProps/custom.xml" ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/>""");
                rootRels.Append("""<Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" Target="docProps/custom.xml"/>""");
                Put(z, "docProps/custom.xml", """<?xml version="1.0"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"/>""");
            }
            if (WithCustomXml)
            {
                ct.Append("""<Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>""");
                docRels.Append("""<Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" Target="../customXml/item1.xml"/>""");
                Put(z, "customXml/item1.xml", "<data><owner>ACME</owner></data>");
                Put(z, "customXml/_rels/item1.xml.rels", """<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" Target="itemProps1.xml"/></Relationships>""");
                Put(z, "customXml/itemProps1.xml", """<?xml version="1.0"?><ds:datastoreItem ds:itemID="{1}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>""");
            }
            foreach (var (name, type, data) in ExtraParts)
            {
                ct.Append($"""<Override PartName="/{name}" ContentType="{type}"/>""");
                Put(z, name, data);
            }

            Put(z, "[Content_Types].xml", ct.Append("</Types>").ToString());
            Put(z, "_rels/.rels", rootRels.Append("</Relationships>").ToString());
            Put(z, "word/_rels/document.xml.rels", docRels.Append("</Relationships>").ToString());
            Put(z, "word/document.xml", $"""<?xml version="1.0" encoding="UTF-8"?><w:document {Ns}><w:body>{Body}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="850" w:bottom="1134" w:left="1701"/></w:sectPr></w:body></w:document>""");
            foreach (var (name, data) in RawEntries) Put(z, name, data);
        }
        return ms.ToArray();
    }

    public string Save(string dir, string name)
    {
        string p = Path.Combine(dir, name);
        File.WriteAllBytes(p, Build());
        return p;
    }

    private static void Put(ZipArchive z, string name, string data)
    {
        using var s = z.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(data));
    }

    /// <summary>Имитирует движок, который при сохранении выбрасывает отделимые части.</summary>
    public static byte[] StripDetached(byte[] docx)
    {
        using var input = new MemoryStream(docx);
        using var src = new ZipArchive(input, ZipArchiveMode.Read);
        using var ms = new MemoryStream();
        using (var dst = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var e in src.Entries)
            {
                if (e.FullName.StartsWith("customXml/") || e.FullName == "docProps/custom.xml") continue;
                using var r = new StreamReader(e.Open());
                string text = r.ReadToEnd();
                if (e.FullName.EndsWith(".rels"))
                    text = System.Text.RegularExpressions.Regex.Replace(text, "<Relationship [^>]*(customXml\"|custom-properties\")[^>]*/>", "");
                using var w = new StreamWriter(dst.CreateEntry(e.FullName).Open());
                w.Write(text);
            }
        }
        return ms.ToArray();
    }
}
