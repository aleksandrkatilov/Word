using Pero.Doc.Layout;

namespace Pero.Doc.Fonts;

/// <summary>Выбранное начертание для оформления: грань шрифта и признаки синтеза полужирного/курсива.</summary>
public sealed record ResolvedFont(FontFace Face, bool SimulateBold, bool SimulateItalic, bool Substituted);

/// <summary>
/// Установленные шрифты (FONT-02): поиск по имени семейства, подбор начертания, замена отсутствующих
/// шрифтов метрически близкими и запасные шрифты для символов, которых нет в выбранном.
/// </summary>
public sealed class FontCatalog
{
    private readonly Dictionary<string, List<FontFace>> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FontKey, ResolvedFont> _resolved = [];
    private readonly Dictionary<(FontFace, int), ResolvedFont?> _fallback = [];
    private readonly object _lock = new();

    /// <summary>Замены для отсутствующих шрифтов: метрически совместимые свободные аналоги и близкие по виду.</summary>
    private static readonly Dictionary<string, string[]> Substitutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arial"] = ["Liberation Sans", "Arimo", "Helvetica", "DejaVu Sans"],
        ["Helvetica"] = ["Arial", "Liberation Sans"],
        ["Times New Roman"] = ["Liberation Serif", "Tinos", "Times", "DejaVu Serif"],
        ["Courier New"] = ["Liberation Mono", "Cousine", "Courier", "DejaVu Sans Mono"],
        ["Calibri"] = ["Carlito", "Liberation Sans", "Arial"],
        ["Cambria"] = ["Caladea", "Liberation Serif", "Times New Roman"],
        ["Calibri Light"] = ["Calibri", "Carlito", "Arial"],
        ["Segoe UI"] = ["Arial", "Liberation Sans", "DejaVu Sans"],
        ["Verdana"] = ["DejaVu Sans", "Arial"],
        ["Georgia"] = ["Gelasio", "Times New Roman", "Liberation Serif"],
        ["Tahoma"] = ["Verdana", "DejaVu Sans", "Arial"],
        ["Aptos"] = ["Calibri", "Arial", "Liberation Sans"],
        ["Symbol"] = ["Segoe UI Symbol", "DejaVu Sans"],
        ["Wingdings"] = ["Segoe UI Symbol", "DejaVu Sans"],
    };

    /// <summary>Шрифты для символов, которых нет в выбранном шрифте.</summary>
    private static readonly string[] FallbackChain =
        ["Segoe UI", "Arial", "Segoe UI Symbol", "Segoe UI Emoji", "Cambria Math", "Liberation Sans", "DejaVu Sans", "Noto Sans", "FreeSans", "Noto Sans CJK SC", "SimSun", "MS Gothic"];

    public static string DefaultFamily => OperatingSystem.IsWindows() ? "Arial" : "Liberation Sans";

    public IEnumerable<string> Families => _families.Keys.Where(f => !f.StartsWith('@')).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);

    public bool HasFamily(string family) => _families.ContainsKey(family);

    public int FaceCount => _families.Values.Sum(v => v.Count);

    /// <summary>Каталог системных шрифтов (Windows: Fonts и пользовательские шрифты; Linux: /usr/share/fonts).</summary>
    public static FontCatalog System() => FromDirectories(SystemFontDirs());

    private static FontCatalog? _shared;
    private static readonly object SharedLock = new();
    /// <summary>Общий каталог процесса: сканирование выполняется один раз.</summary>
    public static FontCatalog Shared { get { lock (SharedLock) return _shared ??= System(); } }

    public static IEnumerable<string> SystemFontDirs()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        }
        else
        {
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts");
        }
    }

    public static FontCatalog FromDirectories(IEnumerable<string> dirs)
    {
        var cat = new FontCatalog();
        var files = new List<string>();
        foreach (var d in dirs.Where(Directory.Exists))
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
        }
        var faces = new System.Collections.Concurrent.ConcurrentBag<FontFace>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, f =>
        {
            foreach (var face in FontFace.Load(f)) faces.Add(face);
        });
        foreach (var face in faces.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.FaceIndex)) cat.Add(face);
        return cat;
    }

    public void Add(FontFace face)
    {
        foreach (var name in face.FamilyNames.Append(face.Family).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_families.TryGetValue(name, out var list)) _families[name] = list = [];
            list.Add(face);
        }
    }

    /// <summary>Начертание для семейства и признаков; при отсутствии семейства — замена.</summary>
    public ResolvedFont? Resolve(FontKey key)
    {
        lock (_lock)
        {
            if (_resolved.TryGetValue(key, out var r)) return r;
            r = ResolveCore(key);
            if (r is not null) _resolved[key] = r;
            return r;
        }
    }

    /// <summary>Какой шрифт будет показан вместо отсутствующего (для понятного сообщения пользователю).</summary>
    public string? SubstituteFor(string family) => HasFamily(family) ? null : Resolve(new FontKey(family, false, false))?.Face.Family;

    private ResolvedFont? ResolveCore(FontKey key)
    {
        bool substituted = false;
        if (!_families.TryGetValue(key.Family, out var faces))
        {
            substituted = true;
            faces = null;
            if (Substitutes.TryGetValue(key.Family, out var subs))
                foreach (var s in subs) if (_families.TryGetValue(s, out faces)) break;
            faces ??= _families.GetValueOrDefault(DefaultFamily) ?? _families.GetValueOrDefault("Arial") ?? _families.GetValueOrDefault("Liberation Sans")
                      ?? _families.GetValueOrDefault("DejaVu Sans") ?? _families.Values.FirstOrDefault();
            if (faces is null) return null;
        }
        // точное совпадение по жирности и наклону; иначе ближайшее с синтезом
        var best = faces.OrderBy(f => (f.Bold == key.Bold ? 0 : 2) + (f.Italic == key.Italic ? 0 : 1))
                        .ThenBy(f => Math.Abs(f.Weight - (key.Bold ? 700 : 400)))
                        .ThenBy(f => f.SubFamily.Length)
                        .First();
        return new ResolvedFont(best, key.Bold && !best.Bold, key.Italic && !best.Italic, substituted);
    }

    /// <summary>Шрифт для символа, которого нет в основном: первый из запасной цепочки, где он есть.</summary>
    public ResolvedFont? FallbackFor(ResolvedFont primary, int codepoint)
    {
        lock (_lock)
        {
            if (_fallback.TryGetValue((primary.Face, codepoint), out var r)) return r;
            r = null;
            foreach (var fam in FallbackChain)
            {
                if (!_families.TryGetValue(fam, out var faces)) continue;
                var f = faces.FirstOrDefault(x => x.Bold == primary.Face.Bold && x.Italic == primary.Face.Italic) ?? faces[0];
                if (f.HasGlyph(codepoint)) { r = new ResolvedFont(f, primary.SimulateBold || primary.Face.Bold && !f.Bold, primary.SimulateItalic, true); break; }
            }
            _fallback[(primary.Face, codepoint)] = r;
            return r;
        }
    }

    /// <summary>Шрифт, которым будет нарисован символ: основной или запасной.</summary>
    public ResolvedFont? FaceFor(FontKey key, int codepoint)
    {
        var p = Resolve(key);
        if (p is null) return null;
        if (codepoint < 0x20 || p.Face.HasGlyph(codepoint)) return p;
        return FallbackFor(p, codepoint) ?? p;
    }
}

/// <summary>Измерение текста по реальным шрифтам: те же ширины на экране, при печати и в PDF.</summary>
public sealed class FontMeasurer(FontCatalog catalog) : ITextMeasurer
{
    private readonly ApproxMeasurer _approx = new();

    public FontCatalog Catalog => catalog;

    public double Advance(FontKey font, double sizePt, char c)
    {
        if (char.IsLowSurrogate(c)) return 0;
        if (c is '­' or '​') return 0;
        var r = catalog.FaceFor(font, c == ' ' ? ' ' : c);
        if (r is null) return _approx.Advance(font, sizePt, c);
        int cp = c == ' ' && !r.Face.HasGlyph(0xA0) ? ' ' : c;
        return r.Face.Advance(cp, sizePt);
    }

    public FontMetrics Metrics(FontKey font, double sizePt)
    {
        var r = catalog.Resolve(font);
        if (r is null) return _approx.Metrics(font, sizePt);
        var f = r.Face;
        double upm = f.UnitsPerEm;
        double asc = f.WinAscent / upm * sizePt, desc = f.WinDescent / upm * sizePt;
        // одинарный интервал как в Word: сумма win-метрик (у большинства шрифтов включает межстрочный зазор)
        double spacing = Math.Max(asc + desc, (f.HheaAscent + f.HheaDescent + f.HheaLineGap) / upm * sizePt);
        return new FontMetrics(asc, desc, spacing);
    }
}
