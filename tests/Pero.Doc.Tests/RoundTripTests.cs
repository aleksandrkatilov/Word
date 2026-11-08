using Pero.Core.Compatibility;
using Pero.Core.Packaging;
using Pero.Doc.Docx;
using static Pero.Doc.Tests.T;

namespace Pero.Doc.Tests;

/// <summary>Круговой обмен без правок (REL-04): чтение → запись → сравнение отпечатков с исходником.</summary>
internal static class RoundTripTests
{
    public static void Register(string? samplesDir)
    {
        if (samplesDir is null || !Directory.Exists(samplesDir)) return;
        var inspector = new PackageInspector(CompatibilityRegistry.CreateDefault());
        foreach (var file in Directory.EnumerateFiles(samplesDir, "*.docx").OrderBy(f => f))
        {
            var report = inspector.Inspect(file);
            if (report.Status == InspectionStatus.Rejected) continue;
            string name = Path.GetFileName(file);
            Test($"Круговой обмен без правок: {name}", () =>
            {
                var doc = DocxReader.Read(file);
                using var ms = new MemoryStream();
                DocxWriter.Write(doc, ms);
                ms.Position = 0;
                var saved = ms.ToArray();
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "pero-rt-" + name), saved);
                var diffs = DocxFingerprint.FromFile(file).CompareTo(DocxFingerprint.FromStream(new MemoryStream(saved)));
                var bad = diffs.Where(d => d.IsLoss || d.Property is "Абзацы" or "Таблицы" or "Ячейки" or "Изображения в строке").ToList();
                Assert(bad.Count == 0, string.Join("\n", bad.Select(d => $"{d.Property}: {d.Before} → {d.After}")));
                var re = inspector.Inspect(new MemoryStream(saved), name);
                Assert(re.Status != InspectionStatus.Rejected, "сохранённый пакет отклонён: " + re.RejectReason);
                // повторное чтение даёт ту же модель
                var doc2 = DocxReader.Read(new MemoryStream(saved));
                Eq(string.Join("\n", doc.AllParagraphs().Select(p => p.Text)), string.Join("\n", doc2.AllParagraphs().Select(p => p.Text)), "текст после повторного чтения");
            });
        }
    }
}
