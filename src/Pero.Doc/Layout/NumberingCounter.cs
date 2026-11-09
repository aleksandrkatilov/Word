using Pero.Doc.Model;

namespace Pero.Doc.Layout;

/// <summary>Счётчики нумерованных списков: метки «1.», «1.2.», «а)», маркеры.</summary>
internal sealed class NumberingCounter(Document doc)
{
    private readonly Dictionary<int, int[]> _counters = [];
    private readonly Dictionary<int, bool[]> _started = [];

    public string? Next(Paragraph p)
    {
        var ep = Resolve.Para(doc, p);
        if (ep.NumId is not int numId || numId == 0) return null;
        int ilvl = Math.Clamp(ep.Ilvl, 0, 8);
        var lvl = doc.Numbering.Level(numId, ilvl);
        if (lvl is null) return null;
        // счёт ведётся по абстрактному определению: экземпляры одного списка продолжают нумерацию
        int key = doc.Numbering.NumToAbstract.TryGetValue(numId, out int a) ? a : numId;
        if (!_counters.TryGetValue(key, out var c)) { _counters[key] = c = new int[9]; _started[key] = new bool[9]; }
        var started = _started[key];
        int start = doc.Numbering.StartOverride.TryGetValue((numId, ilvl), out int so) ? so : lvl.Start;
        c[ilvl] = started[ilvl] ? c[ilvl] + 1 : start;
        started[ilvl] = true;
        for (int k = ilvl + 1; k < 9; k++) started[k] = false;

        if (lvl.Format == "bullet") return lvl.Text;
        if (lvl.Format == "none") return lvl.Text.Contains('%') ? "" : lvl.Text;
        var sb = new System.Text.StringBuilder();
        string t = lvl.Text;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '%' && i + 1 < t.Length && t[i + 1] is >= '1' and <= '9')
            {
                int lv = t[i + 1] - '1';
                var l = doc.Numbering.Level(numId, lv);
                int value = lv == ilvl ? c[ilvl] : (started[lv] || lv < ilvl ? Math.Max(c[lv], l?.Start ?? 1) : l?.Start ?? 1);
                sb.Append(Format(value, l?.Format ?? "decimal"));
                i++;
            }
            else sb.Append(t[i]);
        }
        return sb.ToString();
    }

    private const string RuLetters = "абвгдежзиклмнопрстуфхцчшщэюя";

    public static string Format(int n, string fmt) => fmt switch
    {
        "decimalZero" => n < 10 ? "0" + n : n.ToString(),
        "lowerLetter" => Letters(n, "abcdefghijklmnopqrstuvwxyz"),
        "upperLetter" => Letters(n, "ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
        "russianLower" => Letters(n, RuLetters),
        "russianUpper" => Letters(n, RuLetters.ToUpperInvariant()),
        "lowerRoman" => Roman(n).ToLowerInvariant(),
        "upperRoman" => Roman(n),
        _ => n.ToString(),
    };

    // как в Word: a…z, затем aa…zz
    private static string Letters(int n, string alphabet)
    {
        if (n < 1) return n.ToString();
        int len = alphabet.Length;
        return new string(alphabet[(n - 1) % len], (n - 1) / len + 1);
    }

    private static string Roman(int n)
    {
        if (n is < 1 or > 3999) return n.ToString();
        (int V, string S)[] map = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var sb = new System.Text.StringBuilder();
        foreach (var (v, s) in map) while (n >= v) { sb.Append(s); n -= v; }
        return sb.ToString();
    }
}
