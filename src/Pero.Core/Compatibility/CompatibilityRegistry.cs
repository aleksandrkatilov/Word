using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pero.Core.Compatibility;

public enum Support { Full, Partial, None, Unknown }

/// <summary>Конструкции DOCX, которые распознаёт инспектор пакета.</summary>
public enum Construct
{
    Text, CharacterFormatting, ParagraphFormatting, Styles, Numbering,
    Tables, MergedCells, NestedTables, InlineImages, Hyperlinks,
    HeadersFooters, PageFields, PageSetup, PageBreaks,
    MultipleSections, Columns, FloatingObjects, TextBoxes, Shapes,
    TrackChanges, Comments, Footnotes, Endnotes, Bookmarks, OtherFields,
    ContentControls, Equations, Charts, SmartArt, OleObjects, Macros,
    ActiveX, CustomXml, EmbeddedFonts, AltChunk, Glossary, Ink, UnknownPart,
}

/// <summary>Статусы одной конструкции по четырём осям из раздела 5 ТЗ.</summary>
public sealed record CompatibilityEntry(
    Construct Construct,
    string Title,
    Support Read,
    Support Display,
    Support Edit,
    Support Save,
    bool Verified,
    string Note = "")
{
    /// <summary>Безопасен ли круговой обмен (DOCX-01). Partial допускается с предупреждением.</summary>
    [JsonIgnore]
    public bool RoundTripSafe => Save is Support.Full or Support.Partial;
}

/// <summary>
/// Реестр совместимости. Значения по умолчанию — предварительные для движка SfRichTextBoxAdv
/// (Verified = false) и уточняются замерами этапа 0. Файл compat-registry.json рядом с exe
/// переопределяет значения без пересборки.
/// </summary>
public sealed class CompatibilityRegistry
{
    private readonly Dictionary<Construct, CompatibilityEntry> _entries;

    public CompatibilityRegistry(IEnumerable<CompatibilityEntry> entries) =>
        _entries = entries.ToDictionary(e => e.Construct);

    public CompatibilityEntry this[Construct c] =>
        _entries.TryGetValue(c, out var e) ? e : _entries[Construct.UnknownPart];

    public IReadOnlyCollection<CompatibilityEntry> Entries => _entries.Values;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(_entries.Values.OrderBy(e => e.Construct), Json);

    public static CompatibilityRegistry LoadOrDefault(string? overridePath)
    {
        var map = CreateDefault()._entries;
        if (overridePath is not null && File.Exists(overridePath))
        {
            var overrides = JsonSerializer.Deserialize<List<CompatibilityEntry>>(File.ReadAllText(overridePath), Json) ?? [];
            foreach (var o in overrides) map[o.Construct] = o;
        }
        return new CompatibilityRegistry(map.Values);
    }

    public static CompatibilityRegistry CreateDefault()
    {
        const Support F = Support.Full, P = Support.Partial, N = Support.None, U = Support.Unknown;
        return new CompatibilityRegistry(
        [
            new(Construct.Text, Loc.L("Текст"), F, F, F, F, true),
            new(Construct.CharacterFormatting, Loc.L("Формат символов"), F, F, F, F, false),
            new(Construct.ParagraphFormatting, Loc.L("Формат абзацев"), F, F, F, F, false),
            new(Construct.Styles, Loc.L("Стили и наследование"), F, F, F, F, true),
            new(Construct.Numbering, Loc.L("Списки и нумерация"), F, F, F, F, true),
            new(Construct.Tables, Loc.L("Таблицы"), F, F, F, F, true),
            new(Construct.MergedCells, Loc.L("Объединённые ячейки"), F, F, F, F, true, Loc.L("Вертикальные объединения достраивает EngineFixups")),
            new(Construct.NestedTables, Loc.L("Вложенные таблицы"), F, F, P, P, false, "P1"),
            new(Construct.InlineImages, Loc.L("Изображения в строке"), F, F, F, F, true),
            new(Construct.Hyperlinks, Loc.L("Гиперссылки"), F, F, F, F, true, Loc.L("Сохраняются полем HYPERLINK")),
            new(Construct.HeadersFooters, Loc.L("Колонтитулы"), F, F, F, F, true),
            new(Construct.PageFields, Loc.L("Поля PAGE / NUMPAGES"), F, F, F, F, true),
            new(Construct.PageSetup, Loc.L("Параметры страницы"), F, F, F, F, false),
            new(Construct.PageBreaks, Loc.L("Разрывы страниц"), F, F, F, F, true),
            new(Construct.MultipleSections, Loc.L("Несколько разделов"), F, F, P, F, true, Loc.L("Сохраняются без потерь (замер CI); вставка разрыва раздела — нет в движке")),
            new(Construct.Columns, Loc.L("Колонки"), P, P, P, P, false, "P1"),
            new(Construct.FloatingObjects, Loc.L("Плавающие объекты"), F, F, P, F, true, Loc.L("Изображение с обтеканием сохраняется (замер CI)")),
            new(Construct.TextBoxes, Loc.L("Текстовые поля"), P, P, P, U, false, "P1"),
            new(Construct.Shapes, Loc.L("Фигуры"), U, U, N, U, false, "P1"),
            new(Construct.TrackChanges, Loc.L("Исправления"), N, N, N, N, true, Loc.L("Движок 35.1 молча принимает вставки и удаления (замер CI)")),
            new(Construct.Comments, Loc.L("Примечания"), F, F, F, F, true, Loc.L("Сохраняются (замер CI)")),
            new(Construct.Footnotes, Loc.L("Сноски"), N, N, N, N, false, Loc.L("В модели движка 35.1 нет сносок")),
            new(Construct.Endnotes, Loc.L("Концевые сноски"), N, N, N, N, false, Loc.L("В модели движка 35.1 нет концевых сносок")),
            new(Construct.Bookmarks, Loc.L("Закладки"), P, P, N, N, true, Loc.L("Движок 35.1 теряет закладки при сохранении (замер CI)")),
            new(Construct.OtherFields, Loc.L("Прочие поля (TOC, REF…)"), P, P, N, N, true, Loc.L("Движок 35.1 превращает код поля TOC в текст (замер CI)")),
            new(Construct.ContentControls, Loc.L("Элементы управления содержимым"), U, U, U, U, false),
            new(Construct.Equations, Loc.L("Формулы"), N, N, N, N, false, "P2"),
            new(Construct.Charts, Loc.L("Диаграммы"), N, N, N, N, false, Loc.L("Вне P0")),
            new(Construct.SmartArt, "SmartArt", N, N, N, N, false, Loc.L("Вне P0")),
            new(Construct.OleObjects, Loc.L("Внедрённые OLE-объекты"), N, N, N, N, false, Loc.L("Не выполняются (DOCX-02)")),
            new(Construct.Macros, Loc.L("Макросы VBA"), N, N, N, N, false, Loc.L("Не выполняются (DOCX-02)")),
            new(Construct.ActiveX, "ActiveX", N, N, N, N, false, Loc.L("Не выполняются (DOCX-02)")),
            new(Construct.CustomXml, Loc.L("Пользовательский XML и свойства"), F, N, N, F, false, Loc.L("Переносится оболочкой (DetachedParts)")),
            new(Construct.EmbeddedFonts, Loc.L("Встроенные шрифты"), N, N, N, N, false),
            new(Construct.AltChunk, Loc.L("Вложенные фрагменты altChunk"), U, U, N, N, false),
            new(Construct.Glossary, Loc.L("Стандартные блоки (glossary)"), N, N, N, N, false),
            new(Construct.Ink, Loc.L("Рукописный ввод"), N, N, N, N, false),
            new(Construct.UnknownPart, Loc.L("Неизвестная часть пакета"), N, N, N, N, false),
        ]);
    }
}
