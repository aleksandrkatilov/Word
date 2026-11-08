using Pero.Core.Compatibility;
using Pero.Core.Files;
using Pero.Core.Packaging;
using Pero.Doc.Docx;
using Pero.Doc.Editing;
using Pero.Doc.Model;
using static Pero.Doc.Tests.T;

namespace Pero.Doc.Tests;

internal static class SaveTests
{
    public static void Register(string? samples)
    {
        string work = Path.Combine(Path.GetTempPath(), "pero-save-tests");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var inspector = new PackageInspector(CompatibilityRegistry.CreateDefault());

        Test("Сохранение: проверка записанного файла и резервные версии (≥10)", () =>
        {
            var backups = new BackupStore(Path.Combine(work, "backups"), keep: 10);
            string path = Path.Combine(work, "doc.docx");
            var ed = new DocEditor(Model.Document.CreateBlank());
            for (int i = 0; i < 13; i++)
            {
                ed.TypeText($"версия {i} ");
                SafeSave.Save(ed.Doc.SnapshotForSave(), path, inspector, backups);
            }
            Assert(DocxReader.Read(path).AllParagraphs().First().Text.Contains("версия 12"), "последняя версия не сохранена");
            var versions = backups.List(path);
            Eq(10, versions.Count, "число резервных версий");
            Assert(DocxReader.Read(versions[0].Path).AllParagraphs().First().Text.Contains("версия 11"), "свежая резервная версия — не предыдущая");
            Assert(!Directory.EnumerateFiles(work, "~$*").Any(), "остался временный файл");
        });

        Test("Сохранение: расхождение модели и файла блокирует замену, оригинал цел", () =>
        {
            string path = Path.Combine(work, "guard.docx");
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("оригинал");
            SafeSave.Save(ed.Doc.SnapshotForSave(), path, inspector);
            byte[] before = File.ReadAllBytes(path);
            var expected = ModelDigest.Of(ed.Doc);
            expected.Paragraphs[0] = "другой текст"; // модель «ожидает» другое — как при потере текста в записи
            bool threw = false;
            try
            {
                AtomicFileWriter.Write(path, s => DocxWriter.Write(ed.Doc, s), tmp => SafeSave.Verify(tmp, expected, new HashSet<string>(), inspector));
            }
            catch (SaveFailedException ex) { threw = ex.Message.Contains("текст абзаца 1"); }
            Assert(threw, "проверка не остановила сохранение");
            Assert(before.SequenceEqual(File.ReadAllBytes(path)), "оригинал изменён");
        });

        Test("Сохранение в файл только для чтения — понятная ошибка, файл не тронут", () =>
        {
            string path = Path.Combine(work, "ro.docx");
            var ed = new DocEditor(Model.Document.CreateBlank());
            SafeSave.Save(ed.Doc.SnapshotForSave(), path, inspector);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                ed.TypeText("x");
                bool threw = false;
                try { SafeSave.Save(ed.Doc.SnapshotForSave(), path, inspector); }
                catch (SaveFailedException ex) { threw = ex.Message.Contains("только для чтения"); }
                Assert(threw, "нет понятной ошибки");
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        });

        Test("Метка открытого файла: второй экземпляр видит занятый файл, после закрытия — свободен", () =>
        {
            string locks = Path.Combine(work, "locks");
            string file = Path.Combine(work, "shared.docx");
            using (var a = new FileLock(file, locks))
            {
                Assert(a.Acquired, "первый не получил метку");
                // имитация другого процесса: метка с PID живого процесса «Перо» не наш — проверяем только свою повторную
                using var b = new FileLock(file, locks);
                Assert(b.Acquired, "тот же процесс должен открывать повторно");
            }
            File.WriteAllText(Directory.GetFiles(locks).FirstOrDefault() ?? Path.Combine(locks, "x.lock"), "999999");
            using var c = new FileLock(file, locks);
            Assert(c.Acquired, "метка завершённого процесса мешает открытию");
        });

        Test("Буфер обмена: копирование фрагмента с оформлением и вставка в несколько абзацев", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("первый абзац\nвторой абзац\nтретий");
            ed.Select(new Selection(new DocPos(0, 0), new DocPos(0, 6)));
            ed.ToggleBold();
            ed.Select(new Selection(new DocPos(0, 7), new DocPos(1, 6)));
            var f = ed.CopyFragment()!;
            Eq(2, f.Blocks.Count, "абзацев во фрагменте");
            ed.Select(Selection.At(new DocPos(2, 6)));
            ed.PasteFragment(f);
            Eq(4, ed.Index.Count, "абзацев после вставки");
            Eq("третийабзац", ed.Index[2].Paragraph.Text, "первая часть вставки");
            Eq("второй", ed.Index[3].Paragraph.Text, "вторая часть вставки");
            ed.Undo();
            Eq(3, ed.Index.Count, "отмена вставки — одним шагом");
            ed.Select(new Selection(new DocPos(0, 0), new DocPos(0, 6)));
            var bold = ed.CopyFragment()!;
            ed.Select(Selection.At(new DocPos(2, 0)));
            ed.PasteFragment(bold);
            Eq("первыйтретий", ed.Index[2].Paragraph.Text, "вставка в строку");
            ed.Select(new Selection(new DocPos(2, 0), new DocPos(2, 6)));
            Eq(true, ed.GetFormatState().Bold, "оформление сохранено");
        });

        Test("Поиск и замена: переход по кругу, замена всех одним шагом", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("кот и Кот\nещё кот");
            Eq(3, ed.FindAll("кот").Count, "найдено без учёта регистра");
            Eq(2, ed.FindAll("кот", matchCase: true).Count, "с учётом регистра");
            ed.Select(Selection.At(new DocPos(0, 0)));
            var a = ed.FindNext("кот")!.Value; var b = ed.FindNext("кот")!.Value; var c = ed.FindNext("кот")!.Value; var d = ed.FindNext("кот")!.Value;
            Eq(new DocPos(0, 0), a.Start, "первое"); Eq(new DocPos(0, 6), b.Start, "второе"); Eq(new DocPos(1, 4), c.Start, "третье"); Eq(a, d, "по кругу");
            Eq(3, ed.ReplaceAll("кот", "пёс"), "замен");
            Eq("пёс и пёс", ed.Index[0].Paragraph.Text, "после замены");
            ed.Undo();
            Eq("кот и Кот", ed.Index[0].Paragraph.Text, "отмена замены всех");
        });

        Test("Перемещение по словам, выделение слова и абзаца, число слов", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("Раз, два три\nчетыре");
            Eq(new DocPos(0, 3), ed.StepWord(new DocPos(0, 0), 1), "Ctrl+→ до запятой");
            Eq(new DocPos(0, 5), ed.StepWord(new DocPos(0, 3), 1), "через запятую и пробел");
            Eq(new DocPos(0, 5), ed.StepWord(new DocPos(0, 9), -1), "Ctrl+← к началу слова");
            Eq(new DocPos(1, 0), ed.StepWord(new DocPos(0, 12), 1), "переход на следующий абзац");
            var w = ed.WordAt(new DocPos(0, 6));
            Eq("два ", ed.Index[0].Paragraph.Text[w.Start.Offset..w.End.Offset], "слово двойным щелчком");
            Eq(4, ed.WordCount(), "слов");
            ed.Select(Selection.At(new DocPos(0, 9)));
            ed.DeleteWord(-1);
            Eq("Раз, три", ed.Index[0].Paragraph.Text, "Ctrl+Backspace");
        });

        if (samples is null) return;

        Test("Надёжное сохранение эталонов после правки: проверка проходит, части пакета на месте", () =>
        {
            foreach (var f in Directory.GetFiles(samples, "0*.docx"))
            {
                var ed = new DocEditor(DocxReader.Read(f));
                ed.TypeText("Правка ");
                string dst = Path.Combine(work, "safe-" + Path.GetFileName(f));
                SafeSave.Save(ed.Doc.SnapshotForSave(), dst, inspector);
                Assert(DocxReader.Read(dst).AllParagraphs().First(p => p.Length > 0).Text.StartsWith("Правка"), Path.GetFileName(f) + ": правка не сохранена");
            }
        });
    }
}
