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

    // Файл (backstage)
    public static string PasteText => T("Вставить только текст", "Paste text only");
    public static string FileTab => T("Файл", "File");
    public static string BsInfo => T("Сведения", "Info");
    public static string BsNew => T("Создать", "New");
    public static string BsOpen => T("Открыть", "Open");
    public static string BsSaveAs => T("Сохранить как", "Save As");
    public static string BsPrint => T("Печать", "Print");
    public static string BsExport => T("Экспорт", "Export");
    public static string BsOptions => T("Параметры", "Options");
    public static string BsClose => T("Закрыть", "Close");
    public static string InfoSize => T("Размер: {0:N0} КБ", "Size: {0:N0} KB");
    public static string InfoCompat => T("Совместимость", "Compatibility");
    public static string CompatOk => T("все конструкции поддерживаются", "all content is supported");
    public static string CompatWarn => T("часть оформления поддерживается частично", "some formatting is partly supported");
    public static string CompatReadOnly => T("только чтение — есть неподдерживаемое содержимое", "read-only — unsupported content");
    public static string Browse => T("Обзор…", "Browse…");
    public static string RemoveFromList => T("Удалить из списка", "Remove from list");
    public static string RecentMissing => T("Файл не найден: {0}", "File not found: {0}");
    public static string OptLanguage => T("Язык интерфейса", "Display language");
    public static string OptRestart => T("Язык изменится после перезапуска RWrite.", "The language changes after restarting Pero.");
    public static string OptAutoRecover => T("Автовосстановление, секунд (10–600)", "AutoRecover interval, seconds (10–600)");
    public static string OptSpell => T("Проверять орфографию при вводе", "Check spelling as you type");

    // Макет
    public static string LayoutTab => T("Макет", "Layout");
    public static string MarginsGroup => T("Поля", "Margins");
    public static string MarginName(string key) => key switch
    {
        "Normal" => T("Обычные", "Normal"),
        "Narrow" => T("Узкие", "Narrow"),
        "Moderate" => T("Средние", "Moderate"),
        "Wide" => T("Широкие", "Wide"),
        _ => key,
    };
    public static string CustomMargins => T("Настраиваемые поля…", "Custom margins…");
    public static string Orientation => T("Ориентация", "Orientation");
    public static string Portrait => T("Книжная", "Portrait");
    public static string Landscape => T("Альбомная", "Landscape");
    public static string PaperSize => T("Размер", "Size");
    public static string OtherSize => T("Другой размер…", "More paper sizes…");
    public static string ParagraphGroup => T("Абзац", "Paragraph");
    public static string ParagraphSettings => T("Отступы и интервалы…", "Indents and spacing…");
    public static string PageBreak => T("Разрыв страницы", "Page break");
    public static string ShowGroup => T("Показать", "Show");
    public static string PageSetupTitle => T("Параметры страницы", "Page Setup");
    public static string WidthCm => T("Ширина, см", "Width, cm");
    public static string HeightCm => T("Высота, см", "Height, cm");
    public static string MarginsCm => T("Поля, см", "Margins, cm");
    public static string Top => T("Верхнее", "Top");
    public static string Bottom => T("Нижнее", "Bottom");
    public static string Left => T("Левое", "Left");
    public static string Right => T("Правое", "Right");
    public static string Ok => T("ОК", "OK");
    public static string Cancel => T("Отмена", "Cancel");
    public static string MarginsTooLarge => T("Поля не помещаются на странице.", "The margins do not fit on the page.");
    public static string ValueOutOfRange => T("Введите число от {0} до {1}.", "Enter a number from {0} to {1}.");

    // Навигация и ссылки
    public static string NavPane => T("Область навигации", "Navigation pane");
    public static string SearchDocument => T("Поиск в документе", "Search document");
    public static string NavHeadings => T("Заголовки", "Headings");
    public static string SearchResults => T("Результаты поиска", "Search results");
    public static string NoHeadings => T("В документе нет заголовков. Примените стили «Заголовок 1–3».", "No headings. Apply Heading 1–3 styles.");
    public static string NoResults => T("Ничего не найдено", "No results");
    public static string ResultsCount => T("Найдено: {0}", "{0} results");
    public static string LinkBlocked => T("Ссылка не открыта: {0}\n\nПричина: {1}.", "The link was not opened: {0}\n\nReason: {1}.");
    public static string LinkConfirm => T("Открыть ссылку?\n\n{0}", "Open this link?\n\n{0}");

    public static string AppName => "RWrite";
    public static string Untitled => T("Новый документ{0}", "New document{0}");
    public static string StateSaved => T("Сохранено", "Saved");
    public static string StateUnsaved => T("Есть изменения", "Unsaved changes");
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
