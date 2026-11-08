using System.Text;
using Pero.Core.Compatibility;
using Pero.Core.Packaging;

// Утилита этапа 0: проверка пакетов DOCX, сравнение до/после кругового обмена, матрица совместимости.
Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0) return Usage();

string? registryPath = Environment.GetEnvironmentVariable("PERO_COMPAT_REGISTRY");
var registry = CompatibilityRegistry.LoadOrDefault(registryPath);
var inspector = new PackageInspector(registry);

switch (args[0])
{
    case "inspect" when args.Length >= 2:
        int worst = 0;
        foreach (var file in ExpandFiles(args.Skip(1)))
        {
            var r = inspector.Inspect(file);
            Console.WriteLine($"── {Path.GetFileName(file)}  [{r.Status}]  частей: {r.PartCount}, распаковано: {r.UncompressedBytes / 1024} КБ");
            if (r.Status == InspectionStatus.Rejected) { Console.WriteLine($"   {r.RejectReason}"); worst = Math.Max(worst, 3); continue; }
            foreach (var f in r.Findings)
            {
                string mark = !f.Entry.RoundTripSafe ? "✗" : f.Entry.Save == Support.Partial ? "⚠" : "✓";
                Console.WriteLine($"   {mark} {f.Entry.Title,-34} {f.Count,6}{(f.Detail is null ? "" : "  " + f.Detail)}");
            }
            worst = Math.Max(worst, (int)r.Status);
        }
        return worst >= 3 ? 3 : worst == 2 ? 2 : 0;

    case "compare" when args.Length == 3:
    {
        var before = DocxFingerprint.FromFile(args[1]);
        var after = DocxFingerprint.FromFile(args[2]);
        var diffs = before.CompareTo(after);
        if (diffs.Count == 0) { Console.WriteLine("✓ Различий в содержимом и структуре не найдено."); return 0; }
        foreach (var d in diffs)
            Console.WriteLine($"{(d.IsLoss ? "✗" : "~")} {d.Property}: {d.Before}  →  {d.After}");
        return diffs.Any(d => d.IsLoss) ? 1 : 0;
    }

    case "matrix" when args.Length >= 2:
    {
        var files = ExpandFiles(args.Skip(1)).ToList();
        var reports = files.Select(inspector.Inspect).ToList();
        var constructs = reports.SelectMany(r => r.Findings.Select(f => f.Construct)).Distinct().OrderBy(c => c).ToList();
        var sb = new StringBuilder();
        sb.Append("| Файл | Статус | ").AppendJoin(" | ", constructs.Select(c => registry[c].Title)).AppendLine(" |");
        sb.Append("|---|---|").Append(string.Concat(constructs.Select(_ => "---|"))).AppendLine();
        foreach (var r in reports)
        {
            sb.Append($"| {Path.GetFileName(r.Path)} | {r.Status} |");
            foreach (var c in constructs)
                sb.Append($" {r.Findings.FirstOrDefault(f => f.Construct == c)?.Count.ToString() ?? ""} |");
            sb.AppendLine();
        }
        Console.Write(sb);
        return 0;
    }

    case "pipeline" or "reinject" when args.Length == 4:
    {
        // Конвейер сохранения приложения: файл движка (args[2]) → исправления → части исходника (args[1]) → args[3].
        var detached = DetachedParts.Capture(args[1]);
        using var input = File.OpenRead(args[2]);
        using var output = File.Create(args[3]);
        var res = SavePipeline.Process(input, output, detached);
        Console.WriteLine($"ячеек объединения восстановлено: {res.MergeCellsRestored}; частей возвращено: {string.Join(", ", res.PartsRestored)}");
        return 0;
    }

    case "registry":
        Console.WriteLine(registry.ToJson());
        return 0;

    default:
        return Usage();
}

static IEnumerable<string> ExpandFiles(IEnumerable<string> paths) =>
    paths.SelectMany<string, string>(p => Directory.Exists(p)
        ? Directory.EnumerateFiles(p, "*.docx").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        : new[] { p });

static int Usage()
{
    Console.WriteLine("""
        pero-probe — проверка DOCX для этапа 0

          inspect <файл|папка>...      проверить пакет и показать найденные конструкции
          compare <до.docx> <после.docx> сравнить структуру после кругового обмена (код 1 = потери)
          matrix  <папка>               матрица конструкций по набору файлов (Markdown)
          pipeline <исходный> <файл движка> <результат>  конвейер сохранения: исправления + отделимые части
          registry                      вывести реестр совместимости в JSON

        PERO_COMPAT_REGISTRY=<путь>  — файл переопределения реестра.
        """);
    return 64;
}
