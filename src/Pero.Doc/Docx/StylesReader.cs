using System.Xml.Linq;
using Pero.Doc.Model;
using static Pero.Doc.Docx.Ns;

namespace Pero.Doc.Docx;

internal static class StylesReader
{
    public static StyleSheet Read(PackageStore store)
    {
        var sheet = new StyleSheet();
        ReadThemeFonts(store, sheet);
        string? part = store.FindRelTarget(RelStyles);
        var xml = part is null ? null : store.LoadXml(part);
        if (xml?.Root is not { } root) return sheet;

        var defaults = root.Element(W + "docDefaults");
        sheet.DefaultRun = DocxReader.ReadRunProps(defaults?.Element(W + "rPrDefault")?.Element(W + "rPr"));
        sheet.DefaultPara = DocxReader.ReadParaProps(defaults?.Element(W + "pPrDefault")?.Element(W + "pPr"));

        foreach (var s in root.Elements(W + "style"))
        {
            string? id = (string?)s.Attribute(W + "styleId");
            if (id is null) continue;
            sheet.ById[id] = new Style
            {
                Id = id,
                Name = (string?)s.Element(W + "name")?.Attribute(W + "val") ?? id,
                Type = (string?)s.Attribute(W + "type") switch
                {
                    "character" => StyleType.Character,
                    "table" => StyleType.Table,
                    "numbering" => StyleType.Numbering,
                    _ => StyleType.Paragraph,
                },
                BasedOn = (string?)s.Element(W + "basedOn")?.Attribute(W + "val"),
                Next = (string?)s.Element(W + "next")?.Attribute(W + "val"),
                IsDefault = (string?)s.Attribute(W + "default") is "1" or "true" or "on",
                Run = DocxReader.ReadRunProps(s.Element(W + "rPr")),
                Para = DocxReader.ReadParaProps(s.Element(W + "pPr")),
            };
        }
        return sheet;
    }

    private static void ReadThemeFonts(PackageStore store, StyleSheet sheet)
    {
        string? part = store.FindRelTarget(RelTheme);
        var theme = part is null ? null : store.LoadXml(part);
        var fonts = theme?.Descendants(A + "fontScheme").FirstOrDefault();
        if (fonts is null) return;
        sheet.MajorFont = (string?)fonts.Element(A + "majorFont")?.Element(A + "latin")?.Attribute("typeface") ?? sheet.MajorFont;
        sheet.MinorFont = (string?)fonts.Element(A + "minorFont")?.Element(A + "latin")?.Attribute("typeface") ?? sheet.MinorFont;
    }

    public static Numbering ReadNumbering(PackageStore store)
    {
        var num = new Numbering();
        string? part = store.FindRelTarget(RelNumbering);
        var xml = part is null ? null : store.LoadXml(part);
        if (xml?.Root is not { } root) return num;
        foreach (var a in root.Elements(W + "abstractNum"))
        {
            if (Int(a, "abstractNumId") is not int id) continue;
            var levels = new List<NumLevel>();
            foreach (var l in a.Elements(W + "lvl").OrderBy(l => Int(l, "ilvl") ?? 0))
            {
                var ind = l.Element(W + "pPr")?.Element(W + "ind");
                levels.Add(new NumLevel
                {
                    Format = (string?)l.Element(W + "numFmt")?.Attribute(W + "val") ?? "decimal",
                    Text = (string?)l.Element(W + "lvlText")?.Attribute(W + "val") ?? "",
                    Start = Int(l.Element(W + "start"), "val") ?? 1,
                    IndLeft = Int(ind, "left") ?? Int(ind, "start"),
                    IndHanging = Int(ind, "hanging"),
                    Font = (string?)l.Element(W + "rPr")?.Element(W + "rFonts")?.Attribute(W + "ascii"),
                });
            }
            num.Abstract[id] = levels;
        }
        foreach (var n in root.Elements(W + "num"))
        {
            if (Int(n, "numId") is not int nid || Int(n.Element(W + "abstractNumId"), "val") is not int aid) continue;
            num.NumToAbstract[nid] = aid;
            foreach (var o in n.Elements(W + "lvlOverride"))
                if (Int(o, "ilvl") is int lv && Int(o.Element(W + "startOverride"), "val") is int st)
                    num.StartOverride[(nid, lv)] = st;
        }
        return num;
    }
}
