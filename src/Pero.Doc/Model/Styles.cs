using System.Xml.Linq;

namespace Pero.Doc.Model;

public enum StyleType { Paragraph, Character, Table, Numbering }

public sealed class Style
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public StyleType Type { get; init; }
    public string? BasedOn { get; init; }
    /// <summary>Стиль следующего абзаца (w:next).</summary>
    public string? Next { get; init; }
    public bool IsDefault { get; init; }
    public RunProps Run { get; init; } = new();
    public ParaProps Para { get; init; } = new();
}

/// <summary>Стили документа (только чтение для разметки; styles.xml переносится как есть, новые стили дописываются).</summary>
public sealed class StyleSheet
{
    public Dictionary<string, Style> ById { get; } = new(StringComparer.Ordinal);
    public RunProps DefaultRun { get; set; } = new();
    public ParaProps DefaultPara { get; set; } = new();
    public string MinorFont { get; set; } = "Calibri";
    public string MajorFont { get; set; } = "Calibri Light";
    /// <summary>Стили, добавленные при правке (например, «Заголовок 2» в документ без него).</summary>
    public List<XElement> Added { get; } = [];

    public string? DefaultParagraphStyleId =>
        ById.Values.FirstOrDefault(s => s.Type == StyleType.Paragraph && s.IsDefault)?.Id;

    /// <summary>Цепочка стилей от базового к указанному.</summary>
    public IEnumerable<Style> Chain(string? id)
    {
        var stack = new Stack<Style>();
        var seen = new HashSet<string>();
        while (id is not null && seen.Add(id) && ById.TryGetValue(id, out var s))
        {
            stack.Push(s);
            id = s.BasedOn;
        }
        return stack;
    }

    /// <summary>Найти стиль по встроенному имени (heading 1, Title…) или идентификатору.</summary>
    public Style? FindBuiltIn(string name) =>
        ById.Values.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ??
        (ById.TryGetValue(name.Replace(" ", ""), out var byId) ? byId : null);
}

public sealed class NumLevel
{
    public string Format { get; init; } = "decimal";   // decimal, bullet, lowerLetter, upperRoman…
    public string Text { get; init; } = "%1.";
    public int Start { get; init; } = 1;
    public int? IndLeft { get; init; }
    public int? IndHanging { get; init; }
    public string? Font { get; init; }
}

/// <summary>Нумерация (numbering.xml): экземпляр num → абстрактное определение с уровнями.</summary>
public sealed class Numbering
{
    public Dictionary<int, List<NumLevel>> Abstract { get; } = [];
    public Dictionary<int, int> NumToAbstract { get; } = [];
    public Dictionary<(int Num, int Level), int> StartOverride { get; } = [];

    public NumLevel? Level(int numId, int ilvl) =>
        NumToAbstract.TryGetValue(numId, out int a) && Abstract.TryGetValue(a, out var levels) && ilvl >= 0 && ilvl < levels.Count
            ? levels[ilvl] : null;

    public bool IsBullet(int numId) => Level(numId, 0)?.Format == "bullet";
}
