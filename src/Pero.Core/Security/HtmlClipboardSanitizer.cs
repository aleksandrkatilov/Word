using System.Text;
using System.Text.RegularExpressions;

namespace Pero.Core.Security;

/// <summary>
/// EDIT-03: очистка HTML из буфера обмена (вставка из браузера) до передачи движку —
/// удаляются исполняемые элементы, обработчики событий, опасные схемы ссылок и внешние ресурсы.
/// Работает с форматом CF_HTML («Version:… StartFragment:…») и с обычным HTML.
/// </summary>
public static partial class HtmlClipboardSanitizer
{
    [GeneratedRegex(@"<(script|style|iframe|frame|frameset|object|embed|applet|form|input|button|textarea|select|meta|link|base|svg|math|noscript|template)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PairedDangerous();

    [GeneratedRegex(@"<(script|style|iframe|frame|object|embed|applet|form|input|button|meta|link|base|svg|math|noscript|template)\b[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex SingleDangerous();

    [GeneratedRegex(@"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlers();

    [GeneratedRegex(@"\s+(href|src|action|formaction|background|poster|xlink:href)\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlAttributes();

    [GeneratedRegex(@"\s+style\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    public static string Sanitize(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        string body = ExtractFragment(html);
        body = Comments().Replace(body, "");
        // повторяем, пока есть что удалять: защита от вложенных конструкций вида <scr<script>ipt>
        string prev;
        int guard = 0;
        do
        {
            prev = body;
            body = PairedDangerous().Replace(body, "");
            body = SingleDangerous().Replace(body, "");
        } while (body != prev && ++guard < 10);
        body = EventHandlers().Replace(body, "");
        body = UrlAttributes().Replace(body, m => SafeUrlAttribute(m.Groups[1].Value, m.Groups[2].Value));
        body = StyleAttribute().Replace(body, m => SafeStyle(m.Value));
        return body;
    }

    private static string SafeUrlAttribute(string name, string rawValue)
    {
        string v = rawValue.Trim('"', '\'');
        string decoded = System.Net.WebUtility.HtmlDecode(v);
        string compact = new string(decoded.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        bool isLink = name.Equals("href", StringComparison.OrdinalIgnoreCase);
        if (isLink)
        {
            var (decision, _, _) = LinkPolicy.Evaluate(compact);
            return decision == LinkDecision.Block && !compact.StartsWith('#') ? "" : $" href=\"{System.Net.WebUtility.HtmlEncode(decoded)}\"";
        }
        // Изображения: только встроенные данные PNG/JPEG/GIF. Внешние адреса не загружаются (DOCX-02).
        if (name.Equals("src", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(compact, @"^data:image/(png|jpeg|jpg|gif);base64,", RegexOptions.IgnoreCase))
            return $" src=\"{compact}\"";
        return "";
    }

    private static string SafeStyle(string attr)
    {
        string lower = System.Net.WebUtility.HtmlDecode(attr).ToLowerInvariant();
        return lower.Contains("expression(") || lower.Contains("url(") || lower.Contains("javascript:") || lower.Contains("behavior:")
            ? "" : attr;
    }

    /// <summary>Из CF_HTML взять только фрагмент между StartFragment/EndFragment.</summary>
    public static string ExtractFragment(string clip)
    {
        if (!clip.StartsWith("Version:", StringComparison.Ordinal)) return clip;
        var m1 = Regex.Match(clip, @"StartFragment:(\d+)");
        var m2 = Regex.Match(clip, @"EndFragment:(\d+)");
        if (!m1.Success || !m2.Success) return clip;
        // Смещения в CF_HTML — в байтах UTF-8
        byte[] bytes = Encoding.UTF8.GetBytes(clip);
        int s = int.Parse(m1.Groups[1].Value), e = int.Parse(m2.Groups[1].Value);
        if (s < 0 || e > bytes.Length || s >= e) return clip;
        return Encoding.UTF8.GetString(bytes, s, e - s);
    }

    /// <summary>Упаковать очищенный фрагмент обратно в CF_HTML для буфера обмена.</summary>
    public static string ToCfHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        string pre = "<html><body><!--StartFragment-->", post = "<!--EndFragment--></body></html>";
        int headerLen = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startHtml = headerLen;
        int startFrag = startHtml + Encoding.UTF8.GetByteCount(pre);
        int endFrag = startFrag + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFrag + Encoding.UTF8.GetByteCount(post);
        return string.Format(header, startHtml, endHtml, startFrag, endFrag) + pre + fragment + post;
    }
}
