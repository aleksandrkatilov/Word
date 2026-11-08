using System.IO.Compression;
using System.Xml.Linq;

namespace Pero.Lab;

/// <summary>
/// Без ключа лицензии движок дописывает в документ абзац «Created with a trial version of Syncfusion…».
/// Для замера потерь этот абзац вырезается из копии; при наличии ключа файл не трогается.
/// </summary>
public static class Watermark
{
    private const string Marker = "trial version of Syncfusion";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static bool HasKey => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PERO_SYNCFUSION_KEY"));

    public static string StripIfTrial(string path)
    {
        if (HasKey) return path;
        string outPath = Path.Combine(Path.GetDirectoryName(path)!, "nowm-" + Path.GetFileName(path));
        using (var src = ZipFile.OpenRead(path))
        using (var dst = ZipFile.Open(outPath, ZipArchiveMode.Create))
        {
            foreach (var e in src.Entries)
            {
                var ne = dst.CreateEntry(e.FullName);
                using var from = e.Open();
                using var to = ne.Open();
                if (e.FullName != "word/document.xml") { from.CopyTo(to); continue; }
                var doc = XDocument.Load(from);
                foreach (var p in doc.Descendants(W + "p").Where(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)).Contains(Marker)).ToList())
                    p.Remove();
                doc.Save(to);
            }
        }
        return outPath;
    }
}
