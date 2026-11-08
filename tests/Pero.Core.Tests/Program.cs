using System.IO.Compression;
using System.Text;
using Pero.Core.Compatibility;
using Pero.Core.Files;
using Pero.Core.Packaging;
using Pero.Core.Security;
using Pero.Core.Tests;

Console.OutputEncoding = Encoding.UTF8;
string work = Path.Combine(Path.GetTempPath(), "pero-tests-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(work);

var registry = CompatibilityRegistry.CreateDefault();
var inspector = new PackageInspector(registry);
var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
static void Assert(bool cond, string msg) { if (!cond) throw new Exception(msg); }
static void Throws<T>(Action a, string msg) where T : Exception
{
    try { a(); } catch (T) { return; }
    throw new Exception(msg);
}

// ── Инспектор пакетов (SEC-01, DOCX-01, DOCX-02) ───────────────────────────
Test("Простой документ открывается для редактирования", () =>
{
    var r = inspector.Inspect(new MiniDocx().Save(work, "plain.docx"));
    Assert(r.Status == InspectionStatus.Ok, $"статус {r.Status}");
    Assert(r.Findings.Any(f => f.Construct == Construct.Text), "текст не распознан");
});

Test("Формула переводит документ в режим только чтения", () =>
{
    var d = new MiniDocx { Body = "<w:p><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></w:p>" };
    var r = inspector.Inspect(d.Save(work, "math.docx"));
    Assert(r.Status == InspectionStatus.ReadOnlyRecommended, $"статус {r.Status}");
    Assert(r.ToUserText().Contains("Формулы"), "в отчёте нет конструкции");
});

Test("Неизвестная часть пакета → только чтение с именем части", () =>
{
    var d = new MiniDocx();
    d.ExtraParts.Add(("word/weird.bin", "application/x-unknown", "zz"));
    var r = inspector.Inspect(d.Save(work, "unknown.docx"));
    Assert(r.Status == InspectionStatus.ReadOnlyRecommended, $"статус {r.Status}");
    Assert(r.Findings.Single(f => f.Construct == Construct.UnknownPart).Detail!.Contains("/word/weird.bin"), "нет имени части");
});

Test("Макросы распознаются по части vbaProject", () =>
{
    var d = new MiniDocx();
    d.ExtraParts.Add(("word/vbaProject.bin", "application/vnd.ms-office.vbaProject", "x"));
    var r = inspector.Inspect(d.Save(work, "macro.docx"));
    Assert(r.Findings.Any(f => f.Construct == Construct.Macros), "макросы не найдены");
    Assert(r.Status == InspectionStatus.ReadOnlyRecommended, $"статус {r.Status}");
});

Test("Плавающий объект — предупреждение, а не блокировка", () =>
{
    var d = new MiniDocx { Body = "<w:p><w:r><w:drawing><wp:anchor/></w:drawing></w:r></w:p>" };
    var r = inspector.Inspect(d.Save(work, "anchor.docx"));
    Assert(r.Status == InspectionStatus.OkWithWarnings, $"статус {r.Status}");
});

Test("Поля PAGE/NUMPAGES в простой и сложной разметке", () =>
{
    var d = new MiniDocx
    {
        Body = "<w:p><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple>" +
               "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> NUMPAGES </w:instrText></w:r>" +
               "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>3</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>",
    };
    var r = inspector.Inspect(d.Save(work, "fields.docx"));
    Assert(r.Findings.Single(f => f.Construct == Construct.PageFields).Count == 2, "поля не посчитаны");
    Assert(r.Status == InspectionStatus.Ok, $"статус {r.Status}");
});

Test("Путь с «..» в ZIP отклоняется", () =>
{
    var d = new MiniDocx();
    d.RawEntries.Add(("../evil.txt", "x"));
    var r = inspector.Inspect(d.Save(work, "slip.docx"));
    Assert(r.Status == InspectionStatus.Rejected && r.RejectReason!.Contains("недопустимый путь"), r.RejectReason ?? "");
});

Test("Превышение числа частей отклоняется", () =>
{
    var strict = new PackageInspector(registry, new PackageLimits { MaxEntries = 3 });
    var r = strict.Inspect(new MiniDocx().Save(work, "many.docx"));
    Assert(r.Status == InspectionStatus.Rejected && r.RejectReason!.Contains("частей"), r.RejectReason ?? "");
});

Test("Бюджет распаковки проверяется по фактическим байтам", () =>
{
    var strict = new PackageInspector(registry, new PackageLimits { MaxTotalUncompressedBytes = 200 });
    var r = strict.Inspect(new MiniDocx().Save(work, "budget.docx"));
    Assert(r.Status == InspectionStatus.Rejected, $"статус {r.Status}");
});

Test("Зашифрованный или старый DOC распознаётся с понятным сообщением", () =>
{
    string p = Path.Combine(work, "enc.docx");
    File.WriteAllBytes(p, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0]);
    var r = inspector.Inspect(p);
    Assert(r.Status == InspectionStatus.Rejected && r.IsPasswordProtectedOrLegacy, "не распознан");
    Assert(r.RejectReason!.Contains("паролем"), r.RejectReason);
});

Test("Повреждённый ZIP отклоняется без исключения", () =>
{
    var bytes = new MiniDocx().Build();
    string p = Path.Combine(work, "broken.docx");
    File.WriteAllBytes(p, bytes[..(bytes.Length / 2)]);
    Assert(inspector.Inspect(p).Status == InspectionStatus.Rejected, "не отклонён");
});

Test("DTD в XML запрещён", () =>
{
    var d = new MiniDocx();
    d.RawEntries.Add(("word/header1.xml", "x"));
    d.ExtraParts.Add(("word/footer1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml",
        "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"aaaa\">]><w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">&a;</w:ftr>"));
    var r = inspector.Inspect(d.Save(work, "dtd.docx"));
    Assert(r.Status == InspectionStatus.Rejected, $"статус {r.Status}");
});

// ── Атомарное сохранение (FILE-02) ─────────────────────────────────────────
Test("Сохранение заменяет файл и не оставляет временных", () =>
{
    string p = Path.Combine(work, "atomic.txt");
    File.WriteAllText(p, "старое");
    AtomicFileWriter.Write(p, s => s.Write("новое"u8));
    Assert(File.ReadAllText(p) == "новое", "содержимое не заменено");
    Assert(!Directory.EnumerateFiles(work, "~$atomic*").Any(), "остался временный файл");
});

Test("Сбой при записи оставляет оригинал нетронутым", () =>
{
    string p = Path.Combine(work, "atomic2.txt");
    File.WriteAllText(p, "оригинал");
    Throws<SaveFailedException>(() => AtomicFileWriter.Write(p, s => { s.Write("част"u8); throw new IOException("сбой диска"); }), "нет исключения");
    Assert(File.ReadAllText(p) == "оригинал", "оригинал повреждён");
    Assert(!Directory.EnumerateFiles(work, "~$atomic2*").Any(), "остался временный файл");
});

Test("Непрошедшая проверку запись не заменяет оригинал", () =>
{
    string p = Path.Combine(work, "atomic3.txt");
    File.WriteAllText(p, "оригинал");
    Throws<SaveFailedException>(() => AtomicFileWriter.Write(p, s => s.Write("мусор"u8), _ => "пакет повреждён"), "нет исключения");
    Assert(File.ReadAllText(p) == "оригинал", "оригинал повреждён");
});

Test("Файл только для чтения не перезаписывается", () =>
{
    string p = Path.Combine(work, "ro.txt");
    File.WriteAllText(p, "ro");
    File.SetAttributes(p, FileAttributes.ReadOnly);
    try
    {
        Throws<SaveFailedException>(() => AtomicFileWriter.Write(p, s => s.Write("x"u8)), "нет исключения");
        Assert(File.ReadAllText(p) == "ro", "изменён");
        Assert(FileSnapshot.IsReadOnly(p), "IsReadOnly = false");
    }
    finally { File.SetAttributes(p, FileAttributes.Normal); }
});

// ── Внешние изменения (FILE-04) ────────────────────────────────────────────
Test("Внешнее изменение файла обнаруживается", () =>
{
    string p = Path.Combine(work, "ext.txt");
    File.WriteAllText(p, "a");
    var snap = FileSnapshot.Take(p)!;
    Assert(!snap.ChangedOnDisk(), "ложное срабатывание");
    File.WriteAllText(p, "ab");
    File.SetLastWriteTimeUtc(p, snap.LastWriteUtc.AddSeconds(5));
    Assert(snap.ChangedOnDisk(), "изменение не обнаружено");
});

// ── Автовосстановление (FILE-03, QA-04) ────────────────────────────────────
Test("Копии работающего сеанса не считаются аварийными, после завершения — считаются", () =>
{
    var store = new RecoveryStore(Path.Combine(work, "rec"));
    var session = store.StartSession();
    session.Save("doc1", @"C:\docs\a.docx", "a.docx", s => s.Write("копия"u8));
    Assert(store.FindOrphans().Count == 0, "живой сеанс принят за аварийный");

    session.Dispose(); // имитация аварии: копия осталась, блокировки нет
    var orphans = new RecoveryStore(store.Root).FindOrphans();
    Assert(orphans.Count == 1 && orphans[0].OriginalPath == @"C:\docs\a.docx", "аварийная копия не найдена");
    Assert(File.ReadAllText(orphans[0].DataPath(store.Root)) == "копия", "содержимое копии");

    store.Remove("doc1");
    Assert(store.FindOrphans().Count == 0, "копия не удалена");
});

// ── Сохранность отделимых частей (DOCX-01) ─────────────────────────────────
Test("customXml и пользовательские свойства возвращаются в пакет после движка", () =>
{
    var original = new MiniDocx { WithCustomXml = true, WithCustomProps = true }.Build();
    var detached = DetachedParts.Capture(new MemoryStream(original));
    Assert(detached.PartNames.Count() == 4, $"захвачено частей: {detached.PartNames.Count()}");

    var engineOutput = MiniDocx.StripDetached(original);
    using var merged = new MemoryStream();
    var res = detached.ReinjectInto(new MemoryStream(engineOutput), merged);
    Assert(res.Restored.Count == 2 && res.Skipped.Count == 0, $"восстановлено {res.Restored.Count}, пропущено {res.Skipped.Count}");

    merged.Position = 0;
    var before = DocxFingerprint.FromStream(new MemoryStream(original));
    var after = DocxFingerprint.FromStream(merged);
    var diffs = before.CompareTo(after);
    Assert(diffs.Count == 0, "различия: " + string.Join("; ", diffs.Select(d => $"{d.Property}: {d.Before} → {d.After}")));

    merged.Position = 0;
    var r = inspector.Inspect(merged, "merged.docx");
    Assert(r.Status == InspectionStatus.Ok, $"повторная проверка: {r.Status} {r.RejectReason}");

    merged.Position = 0;
    using var z = new ZipArchive(merged, ZipArchiveMode.Read, true);
    string rels = new StreamReader(z.GetEntry("word/_rels/document.xml.rels")!.Open()).ReadToEnd();
    Assert(rels.Contains("Target=\"../customXml/item1.xml\""), "нет связи customXml: " + rels);
    string ct = new StreamReader(z.GetEntry("[Content_Types].xml")!.Open()).ReadToEnd();
    Assert(ct.Contains("custom-properties+xml") && ct.Contains("customXmlProperties+xml"), "нет типов содержимого");
});

Test("Если движок сам сохранил части, они не дублируются", () =>
{
    var original = new MiniDocx { WithCustomXml = true }.Build();
    var detached = DetachedParts.Capture(new MemoryStream(original));
    using var merged = new MemoryStream();
    var res = detached.ReinjectInto(new MemoryStream(original), merged);
    Assert(res.Restored.Count == 0, "части продублированы");
});

Test("Пути частей разрешаются относительно каталога", () =>
{
    Assert(DetachedParts.Resolve("word", "../customXml/item1.xml") == "customXml/item1.xml", "Resolve ..");
    Assert(DetachedParts.Resolve("", "docProps/custom.xml") == "docProps/custom.xml", "Resolve root");
    Assert(DetachedParts.RelativeFrom("word", "customXml/item1.xml") == "../customXml/item1.xml", "RelativeFrom");
    Assert(DetachedParts.RelsPathFor("word/document.xml") == "word/_rels/document.xml.rels", "RelsPathFor");
});

// ── Отпечаток для кругового обмена (QA-01) ─────────────────────────────────
Test("Сравнение находит потерю текста и таблицы", () =>
{
    var a = new MiniDocx { Body = "<w:p><w:r><w:t>Раз два</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>" }.Build();
    var b = new MiniDocx { Body = "<w:p><w:r><w:t>Раз</w:t></w:r></w:p>" }.Build();
    var diffs = DocxFingerprint.FromStream(new MemoryStream(a)).CompareTo(DocxFingerprint.FromStream(new MemoryStream(b)));
    Assert(diffs.Any(d => d.Property == "Текст" && d.IsLoss), "потеря текста не найдена");
    Assert(diffs.Any(d => d.Property == "Таблицы" && d.IsLoss), "потеря таблицы не найдена");
});

Test("Неразрывный пробел и табуляция не считаются изменением текста", () =>
{
    var a = new MiniDocx { Body = "<w:p><w:r><w:t xml:space=\"preserve\">A\u00A0B</w:t></w:r></w:p>" }.Build();
    var b = new MiniDocx { Body = "<w:p><w:r><w:t xml:space=\"preserve\">A B</w:t></w:r></w:p>" }.Build();
    var diffs = DocxFingerprint.FromStream(new MemoryStream(a)).CompareTo(DocxFingerprint.FromStream(new MemoryStream(b)));
    Assert(diffs.Count == 0, string.Join("; ", diffs.Select(d => d.Property)));
});

// ── Прочее ─────────────────────────────────────────────────────────────────
Test("Последние файлы: без дублей, удаление не трогает файл", () =>
{
    string store = Path.Combine(work, "recent.json");
    string f = Path.Combine(work, "r.docx");
    File.WriteAllText(f, "x");
    var rf = new RecentFiles(store, 3).Load();
    rf.Add(f); rf.Add(Path.Combine(work, "b.docx")); rf.Add(f);
    Assert(rf.Items.Count == 2 && rf.Items[0] == Path.GetFullPath(f), "порядок/дубли");
    rf.Remove(rf.Items[0]);
    Assert(File.Exists(f), "файл удалён с диска");
    Assert(new RecentFiles(store).Load().Items.Count == 1, "не сохранено");
});

Test("Реестр совместимости переопределяется из JSON", () =>
{
    string p = Path.Combine(work, "compat.json");
    File.WriteAllText(p, """[{"Construct":"Equations","Title":"Формулы","Read":"Full","Display":"Full","Edit":"None","Save":"Full","Verified":true,"Note":""}]""");
    var reg = CompatibilityRegistry.LoadOrDefault(p);
    Assert(reg[Construct.Equations].RoundTripSafe && reg[Construct.Equations].Verified, "переопределение не применено");
    Assert(!reg[Construct.Charts].RoundTripSafe, "остальные значения потеряны");
    Assert(reg.ToJson().Contains("\"Equations\""), "JSON");
});

// ── Ссылки (LINK-01) ───────────────────────────────────────────────────────
Test("Ссылки: веб и почта — с подтверждением, опасные схемы — блок", () =>
{
    Assert(LinkPolicy.Evaluate("https://example.com/a?b=1").Decision == LinkDecision.Confirm, "https");
    Assert(LinkPolicy.Evaluate("mailto:a@b.ru").Decision == LinkDecision.Confirm, "mailto");
    Assert(LinkPolicy.Evaluate("www.example.com").Decision == LinkDecision.Confirm, "www");
    Assert(LinkPolicy.Evaluate("#_Toc123").Decision == LinkDecision.Allow, "закладка");
    foreach (var bad in new[] { "javascript:alert(1)", "vbscript:x", "data:text/html,<b>", "ms-msdt:/id", "search-ms:query=x", "file://server/share/x.docx", "file:///C:/Windows/calc.exe", "", "http://" })
        Assert(LinkPolicy.Evaluate(bad).Decision == LinkDecision.Block, "не заблокировано: " + bad);
    Assert(LinkPolicy.Evaluate("file:///C:/Users/me/report.pdf").Decision == LinkDecision.Confirm, "локальный pdf");
});

// ── Вставка из браузера (EDIT-03) ──────────────────────────────────────────
Test("HTML из буфера: скрипты, обработчики и опасные ссылки удаляются", () =>
{
    string dirty = "<p onclick=\"steal()\">Текст <b>жирный</b></p><script>alert(1)</script>" +
                   "<a href=\"javascript:alert(2)\">x</a><a href=\"https://ok.ru\">ok</a>" +
                   "<img src=\"https://tracker.example/p.gif\"><img src=\"data:image/png;base64,iVBORw0KGgo=\">" +
                   "<iframe src=\"https://evil\"></iframe><scr<script>x</script>ipt>alert(3)</script>" +
                   "<span style=\"background:url(https://x)\">s</span><span style=\"color:red\">r</span>";
    string clean = HtmlClipboardSanitizer.Sanitize(dirty);
    foreach (var bad in new[] { "onclick", "<script", "javascript:", "tracker.example", "<iframe", "url(" })
        Assert(!clean.Contains(bad, StringComparison.OrdinalIgnoreCase), $"осталось «{bad}»: {clean}");
    foreach (var good in new[] { "<b>жирный</b>", "https://ok.ru", "data:image/png;base64", "color:red" })
        Assert(clean.Contains(good), $"потеряно «{good}»: {clean}");
});

Test("CF_HTML: фрагмент извлекается и упаковывается обратно", () =>
{
    string cf = HtmlClipboardSanitizer.ToCfHtml("<p>Привет</p>");
    Assert(HtmlClipboardSanitizer.ExtractFragment(cf) == "<p>Привет</p>", "фрагмент: " + HtmlClipboardSanitizer.ExtractFragment(cf));
});

// ── запуск ─────────────────────────────────────────────────────────────────
int failed = 0;
foreach (var (name, body) in tests)
{
    try { body(); Console.WriteLine($"  ✓ {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"  ✗ {name}\n      {ex.Message}"); }
}
try { Directory.Delete(work, true); } catch { }
Console.WriteLine($"\n{tests.Count - failed} из {tests.Count} тестов пройдено.");
return failed == 0 ? 0 : 1;
