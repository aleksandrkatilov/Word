using System.Globalization;

namespace Pero.Services;

/// <summary>Строки оболочки на русском и английском (раздел «Исходные допущения»: RU и EN в первой версии).</summary>
public static class Strings
{
    private static bool _en;

    public static void Use(CultureInfo culture) => _en = culture.TwoLetterISOLanguageName == "en";

    private static string T(string ru, string en) => _en ? en : ru;

    public static bool IsRussian => !_en;
    public static string RecentFiles => T("Последние файлы", "Recent files");
    public static string Save => T("Сохранить", "Save");
    public static string FormatNotInP0 => T("Сохранение в этом формате не входит в первую версию. Доступны DOCX, TXT и PDF.", "This format is not part of the first release. DOCX, TXT and PDF are available.");
    public static string UnsavedEditsNote => T("В документе есть несохранённые правки — различия могут быть ожидаемыми.", "The document has unsaved edits, so some differences may be expected.");
    public static string AppName => T("Перо", "Pero");
    public static string Untitled => T("Документ{0}", "Document{0}");
    public static string StateSaved => T("Сохранено", "Saved");
    public static string StateUnsaved => T("Не сохранено", "Not saved");
    public static string StateSaving => T("Сохранение…", "Saving…");
    public static string StateReadOnly => T("Только чтение", "Read-only");
    public static string StateRecovered => T("Восстановлено", "Recovered");

    public static string StatusPage => T("Страница {0} из {1}", "Page {0} of {1}");
    public static string StatusWords => T("Слов: {0}", "{0} words");
    public static string StatusLanguage => T("русский", "English (US)");

    public static string FilterDocx => T("Документ Word (*.docx)|*.docx", "Word Document (*.docx)|*.docx");
    public static string FilterOpen => T("Документы Word (*.docx)|*.docx|Текст (*.txt)|*.txt", "Word Documents (*.docx)|*.docx|Text (*.txt)|*.txt");
    public static string FilterPdf => T("PDF (*.pdf)|*.pdf", "PDF (*.pdf)|*.pdf");
    public static string FilterTxt => T("Текст в UTF-8 (*.txt)|*.txt", "UTF-8 Text (*.txt)|*.txt");

    public static string AskSaveChanges => T("Сохранить изменения в «{0}»?", "Save changes to \"{0}\"?");
    public static string CannotOpen => T("Не удалось открыть «{0}».\n\n{1}", "Cannot open \"{0}\".\n\n{1}");
    public static string ReadOnlyOpenPrompt => T(
        "«{0}»\n\n{1}\nОткрыть документ только для чтения?",
        "\"{0}\"\n\n{1}\nOpen the document as read-only?");
    public static string ConvertedCopyPrompt => T(
        "Документ открыт только для чтения, потому что часть его содержимого редактор пока не сохраняет:\n\n{0}\nСохранить преобразованную копию под другим именем? Исходный файл не изменится.",
        "The document is read-only because the editor cannot yet save some of its content:\n\n{0}\nSave a converted copy under a different name? The original file stays unchanged.");
    public static string CopyMustDiffer => T("Выберите другое имя: исходный файл не перезаписывается.", "Choose a different name: the original file is never overwritten.");
    public static string FileIsReadOnly => T("Файл «{0}» доступен только для чтения. Сохраните копию под другим именем.", "\"{0}\" is read-only. Save a copy under a different name.");
    public static string ExternalChange => T(
        "Файл «{0}» был изменён другой программой после открытия.\n\nДа — сохранить под другим именем\nНет — перезаписать изменения другой программы\nОтмена — не сохранять",
        "\"{0}\" was changed by another program after it was opened.\n\nYes — save under a different name\nNo — overwrite the other program's changes\nCancel — don't save");
    public static string ConfirmOverwrite => T("Файл «{0}» уже существует. Заменить его?", "\"{0}\" already exists. Replace it?");
    public static string SaveFailed => T("Не удалось сохранить.\n\n{0}", "Could not save.\n\n{0}");
    public static string PdfDone => T("PDF сохранён: {0}", "PDF saved: {0}");
    public static string PdfFailed => T("Не удалось экспортировать PDF.\n\n{0}", "PDF export failed.\n\n{0}");
    public static string RecoverPrompt => T(
        "После аварийного завершения остались несохранённые документы:\n\n{0}\n\nДа — восстановить\nНет — удалить копии\nОтмена — решить при следующем запуске",
        "Unsaved documents were left after an unexpected shutdown:\n\n{0}\n\nYes — restore\nNo — delete the copies\nCancel — decide next time");
    public static string RecoveredBanner => T(
        "Документ восстановлен после аварийного завершения. Сохраните его, чтобы не потерять изменения.",
        "This document was recovered after an unexpected shutdown. Save it to keep your changes.");
    public static string WarningsBanner => T("Часть оформления поддерживается частично и может измениться при сохранении.", "Some formatting is only partly supported and may change when saved.");
    public static string ReadOnlyBanner => T("Только чтение: документ содержит конструкции, которые редактор пока не сохраняет без потерь.", "Read-only: the document contains content the editor cannot yet save without loss.");
    public static string FileReadOnlyBanner => T("Файл доступен только для чтения. Изменения можно сохранить в копию.", "The file is read-only. You can save your changes to a copy.");
    public static string Details => T("Подробнее", "Details");
    public static string SaveCopy => T("Сохранить копию", "Save a copy");
    public static string CompatReportTitle => T("Отчёт о совместимости", "Compatibility report");
    public static string UnexpectedError => T("Непредвиденная ошибка: {0}\n\nДокумент и копия автовосстановления сохранены.", "Unexpected error: {0}\n\nThe document and its recovery copy are kept.");
    public static string ProtoTab => T("Этап 0", "Stage 0");
    public static string ProtoGroup => T("Проверка", "Validation");
    public static string ExportPdf => T("Экспорт PDF", "Export PDF");
    public static string ExportTxt => T("Экспорт TXT", "Export TXT");
    public static string CompatReport => T("Отчёт о совместимости", "Compatibility report");
    public static string RoundTrip => T("Проверка круговым обменом", "Round-trip check");
    public static string RoundTripOk => T("Круговой обмен: потерь не найдено.", "Round trip: no losses found.");
    public static string RoundTripLoss => T("Круговой обмен: найдены различия.\n\n{0}", "Round trip: differences found.\n\n{0}");
    public static string NeedSavedFile => T("Сначала откройте или сохраните документ DOCX.", "Open or save a DOCX document first.");
}
