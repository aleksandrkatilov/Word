namespace Pero.Core.Security;

public enum LinkDecision
{
    /// <summary>Можно открыть через ОС после явного Ctrl+щелчка.</summary>
    Allow,
    /// <summary>Внешний адрес — открыть только после подтверждения пользователем.</summary>
    Confirm,
    /// <summary>Опасная или неподдерживаемая схема — не открывать.</summary>
    Block,
}

/// <summary>
/// LINK-01: проверка гиперссылок перед открытием. Ссылки никогда не открываются автоматически;
/// по Ctrl+щелчку разрешены http(s) и mailto, локальные файлы — с подтверждением, остальное блокируется.
/// </summary>
public static class LinkPolicy
{
    private static readonly HashSet<string> Web = new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    public static (LinkDecision Decision, Uri? Uri, string Reason) Evaluate(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return (LinkDecision.Block, null, Loc.L("пустой адрес"));
        string t = target.Trim();
        if (t.StartsWith('#')) return (LinkDecision.Allow, null, Loc.L("закладка в документе"));
        if (t.Any(c => char.IsControl(c))) return (LinkDecision.Block, null, Loc.L("управляющие символы в адресе"));

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
        {
            // «www.example.com» без схемы — как в Word, считаем веб-адресом
            if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate("https://" + t, UriKind.Absolute, out uri))
                return (LinkDecision.Confirm, uri, Loc.L("веб-адрес"));
            return (LinkDecision.Block, null, Loc.L("некорректный адрес"));
        }

        if (Web.Contains(uri.Scheme))
            return string.IsNullOrEmpty(uri.Host) ? (LinkDecision.Block, null, Loc.L("нет имени узла")) : (LinkDecision.Confirm, uri, Loc.L("веб-адрес"));
        if (uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase))
            return (LinkDecision.Confirm, uri, Loc.L("адрес почты"));
        if (uri.IsFile)
        {
            if (uri.IsUnc) return (LinkDecision.Block, uri, Loc.L("сетевой путь не открывается из документа"));
            string ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
            if (DangerousExtensions.Contains(ext)) return (LinkDecision.Block, uri, Loc.F("исполняемый файл ({0})", ext));
            return (LinkDecision.Confirm, uri, Loc.L("локальный файл"));
        }
        return (LinkDecision.Block, uri, Loc.F("схема «{0}» не поддерживается", uri.Scheme));
    }

    private static readonly HashSet<string> DangerousExtensions =
    [
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp",
        ".scr", ".pif", ".cpl", ".hta", ".lnk", ".reg", ".jar", ".dll", ".application", ".appref-ms", ".url", ".docm", ".dotm", ".xlsm",
    ];
}
