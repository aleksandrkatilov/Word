namespace Pero.Doc.Layout;

/// <summary>Гарнитура без размера: семейство и начертание.</summary>
public readonly record struct FontKey(string Family, bool Bold, bool Italic);

/// <summary>Метрики шрифта заданного размера, пункты.</summary>
public readonly record struct FontMetrics(double Ascent, double Descent, double LineSpacing);

/// <summary>
/// Измерение текста (LAYOUT-01). Разметка не зависит от WPF: в приложении — измеритель на GlyphTypeface,
/// в тестах — приближённый. Все величины — в пунктах (1/72 дюйма).
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Ширина символа (ширина продвижения) при размере sizePt.</summary>
    double Advance(FontKey font, double sizePt, char c);
    FontMetrics Metrics(FontKey font, double sizePt);
}

/// <summary>Приближённый измеритель без шрифтов: для тестов и как запасной вариант.</summary>
public sealed class ApproxMeasurer : ITextMeasurer
{
    public double Advance(FontKey font, double sizePt, char c)
    {
        double em = c switch
        {
            ' ' or ' ' => 0.25,
            'i' or 'l' or 'j' or '.' or ',' or ':' or ';' or '!' or '\'' or '|' or 'I' => 0.28,
            'm' or 'w' or 'M' or 'W' or 'ш' or 'щ' or 'Ш' or 'Щ' or 'ж' or 'Ж' or 'ю' or 'Ю' => 0.85,
            '­' or '​' => 0,
            _ when char.IsUpper(c) => 0.65,
            _ when char.IsLowSurrogate(c) => 0,
            _ => 0.5,
        };
        return em * sizePt * (font.Bold ? 1.05 : 1.0);
    }

    public FontMetrics Metrics(FontKey font, double sizePt) => new(0.9 * sizePt, 0.25 * sizePt, 1.15 * sizePt);
}

/// <summary>Кэш ширин поверх измерителя: разметка спрашивает одни и те же символы многократно.</summary>
internal sealed class CachedMeasurer(ITextMeasurer inner)
{
    private readonly Dictionary<(FontKey, double, char), double> _adv = [];
    private readonly Dictionary<(FontKey, double), FontMetrics> _met = [];

    public double Advance(FontKey f, double size, char c)
    {
        if (!_adv.TryGetValue((f, size, c), out double w)) _adv[(f, size, c)] = w = inner.Advance(f, size, c);
        return w;
    }

    public FontMetrics Metrics(FontKey f, double size)
    {
        if (!_met.TryGetValue((f, size), out var m)) _met[(f, size)] = m = inner.Metrics(f, size);
        return m;
    }

    public double Width(FontKey f, double size, string s)
    {
        double w = 0;
        foreach (char c in s) w += Advance(f, size, c);
        return w;
    }
}
