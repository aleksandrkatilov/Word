using System.Xml.Linq;

namespace Pero.Doc.Docx;

internal static class Ns
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    public static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace CT = "http://schemas.openxmlformats.org/package/2006/content-types";
    public static readonly XNamespace Xml = XNamespace.Xml;

    public const string RelOfficeDoc = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string RelHyperlink = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    public const string RelImage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    public const string RelStyles = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    public const string RelNumbering = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    public const string RelSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    public const string RelHeader = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
    public const string RelFooter = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";
    public const string RelTheme = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";

    /// <summary>Порядок дочерних элементов w:pPr по схеме — Word отвергает файл при нарушении порядка.</summary>
    public static readonly string[] PPrOrder =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers",
        "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct", "autoSpaceDE",
        "autoSpaceDN", "bidi", "adjustRightInd", "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents",
        "suppressOverlap", "jc", "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId", "cnfStyle",
        "rPr", "sectPr", "pPrChange",
    ];

    /// <summary>Порядок дочерних элементов w:rPr по схеме.</summary>
    public static readonly string[] RPrOrder =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss",
        "imprint", "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs",
        "highlight", "u", "effect", "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout",
        "specVanish", "oMath", "rPrChange",
    ];

    /// <summary>Порядок дочерних элементов w:sectPr.</summary>
    public static readonly string[] SectPrOrder =
    [
        "headerReference", "footerReference", "footnotePr", "endnotePr", "type", "pgSz", "pgMar", "paperSrc", "pgBorders",
        "lnNumType", "pgNumType", "cols", "formProt", "vAlign", "noEndnote", "titlePg", "textDirection", "bidi", "rtlGutter",
        "docGrid", "printerSettings", "sectPrChange",
    ];

    public static int OrderIndex(string[] order, XElement e)
    {
        int i = Array.IndexOf(order, e.Name.LocalName);
        return i < 0 ? order.Length : i;
    }

    public static int? Int(XElement? e, string attr)
    {
        var v = (string?)e?.Attribute(W + attr);
        return v is not null && int.TryParse(v, out int n) ? n : null;
    }

    /// <summary>Логическое свойство OOXML: элемент без val или val=true/1/on — истина.</summary>
    public static bool? OnOff(XElement? e)
    {
        if (e is null) return null;
        var v = (string?)e.Attribute(W + "val");
        return v is null || v is "1" or "true" or "on";
    }
}
