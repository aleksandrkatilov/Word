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
            new(Construct.Text, "Текст", F, F, F, F, false),
            new(Construct.CharacterFormatting, "Формат символов", F, F, F, F, false),
            new(Construct.ParagraphFormatting, "Формат абзацев", F, F, F, F, false),
            new(Construct.Styles, "Стили и наследование", F, F, F, F, false),
            new(Construct.Numbering, "Списки и нумерация", F, F, F, F, false),
            new(Construct.Tables, "Таблицы", F, F, F, F, false),
            new(Construct.MergedCells, "Объединённые ячейки", F, F, F, F, false),
            new(Construct.NestedTables, "Вложенные таблицы", F, F, P, P, false, "P1"),
            new(Construct.InlineImages, "Изображения в строке", F, F, F, F, false),
            new(Construct.Hyperlinks, "Гиперссылки", F, F, F, F, false),
            new(Construct.HeadersFooters, "Колонтитулы", F, F, F, F, false),
            new(Construct.PageFields, "Поля PAGE / NUMPAGES", F, F, F, F, false),
            new(Construct.PageSetup, "Параметры страницы", F, F, F, F, false),
            new(Construct.PageBreaks, "Разрывы страниц", F, F, F, F, false),
            new(Construct.MultipleSections, "Несколько разделов", F, F, P, P, false, "P1; в P0 параметры страницы едины"),
            new(Construct.Columns, "Колонки", P, P, P, P, false, "P1"),
            new(Construct.FloatingObjects, "Плавающие объекты", P, P, P, P, false, "P1; проверить обтекание"),
            new(Construct.TextBoxes, "Текстовые поля", P, P, P, U, false, "P1"),
            new(Construct.Shapes, "Фигуры", U, U, N, U, false, "P1"),
            new(Construct.TrackChanges, "Исправления", F, F, P, F, false, "P1"),
            new(Construct.Comments, "Примечания", F, F, P, F, false, "P1"),
            new(Construct.Footnotes, "Сноски", U, U, U, U, false, "P1; замерить"),
            new(Construct.Endnotes, "Концевые сноски", U, U, U, U, false, "P1; замерить"),
            new(Construct.Bookmarks, "Закладки", F, F, P, F, false),
            new(Construct.OtherFields, "Прочие поля (TOC, REF…)", P, P, P, P, false, "P1"),
            new(Construct.ContentControls, "Элементы управления содержимым", U, U, U, U, false),
            new(Construct.Equations, "Формулы", N, N, N, N, false, "P2"),
            new(Construct.Charts, "Диаграммы", N, N, N, N, false, "Вне P0"),
            new(Construct.SmartArt, "SmartArt", N, N, N, N, false, "Вне P0"),
            new(Construct.OleObjects, "Внедрённые OLE-объекты", N, N, N, N, false, "Не выполняются (DOCX-02)"),
            new(Construct.Macros, "Макросы VBA", N, N, N, N, false, "Не выполняются (DOCX-02)"),
            new(Construct.ActiveX, "ActiveX", N, N, N, N, false, "Не выполняются (DOCX-02)"),
            new(Construct.CustomXml, "Пользовательский XML и свойства", F, N, N, F, false, "Переносится оболочкой (DetachedParts)"),
            new(Construct.EmbeddedFonts, "Встроенные шрифты", N, N, N, N, false),
            new(Construct.AltChunk, "Вложенные фрагменты altChunk", U, U, N, N, false),
            new(Construct.Glossary, "Стандартные блоки (glossary)", N, N, N, N, false),
            new(Construct.Ink, "Рукописный ввод", N, N, N, N, false),
            new(Construct.UnknownPart, "Неизвестная часть пакета", N, N, N, N, false),
        ]);
    }
}
