using System.Globalization;
using System.IO.Compression;
using System.Text;
using Pero.Doc.Fonts;
using Pero.Doc.Layout;

namespace Pero.Doc.Render;

/// <summary>
/// Экспорт PDF из той же разметки, что на экране и при печати (PAGE-01, CORE-04). Собственная запись PDF 1.7:
/// шрифты встраиваются (Type0/Identity-H с ToUnicode — текст копируется и ищется), изображения JPEG — как есть,
/// PNG — через FlateDecode. Без сторонних библиотек.
/// </summary>
public static class PdfExport
{
    public static void Write(LayoutResult layout, PageDrawer drawer, FontCatalog fonts, Stream output, string? title = null)
    {
        var pdf = new PdfFile(fonts);
        foreach (var page in layout.Pages)
        {
            var canvas = new PdfCanvas(pdf, page.Height);
            drawer.Draw(page, canvas);
            pdf.AddPage(page.Width, page.Height, canvas);
        }
        pdf.Save(output, title);
    }

    public static void Write(LayoutResult layout, PageDrawer drawer, FontCatalog fonts, string path, string? title = null)
    {
        using var fs = File.Create(path);
        Write(layout, drawer, fonts, fs, title);
    }
}

internal sealed class PdfCanvas(PdfFile pdf, double pageHeight) : IPageCanvas
{
    public StringBuilder Content { get; } = new();
    public HashSet<string> Fonts { get; } = [];
    public HashSet<string> Images { get; } = [];
    private static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
    private double Y(double y) => pageHeight - y;
    private static string C(Rgb c) => $"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)}";

    public void FillRect(RectPt r, Rgb color) =>
        Content.Append($"{C(color)} rg {N(r.X)} {N(Y(r.Bottom))} {N(r.W)} {N(r.H)} re f\n");

    public void Line(double x1, double y1, double x2, double y2, double width, Rgb color) =>
        Content.Append($"{C(color)} RG {N(width)} w 0 J {N(x1)} {N(Y(y1))} m {N(x2)} {N(Y(y2))} l S\n");

    public void Glyphs(GlyphRunInfo run)
    {
        var (font, glyphs) = pdf.FontFor(run);
        Fonts.Add(font.Name);
        var sb = Content;
        sb.Append("BT\n").Append(C(run.Color)).Append(" rg\n");
        if (run.Font.SimulateBold) sb.Append(C(run.Color)).Append($" RG 2 Tr {N(run.SizePt / 35)} w\n");
        sb.Append($"/{font.Name} {N(run.SizePt)} Tf\n");
        double skew = run.Font.SimulateItalic ? 0.2 : 0;
        sb.Append($"1 0 {N(skew)} 1 {N(run.X)} {N(Y(run.BaselineY))} Tm\n[");
        for (int i = 0; i < glyphs.Length; i++)
        {
            ushort g = glyphs[i];
            sb.Append('<').Append(g.ToString("X4")).Append('>');
            double natural = font.Face.AdvanceUnits(g) * 1000.0 / font.Face.UnitsPerEm;
            double wanted = run.Advances[i] * 1000.0 / run.SizePt;
            double adj = natural - wanted;
            if (Math.Abs(adj) > 0.05) sb.Append(' ').Append(N(adj)).Append(' ');
        }
        sb.Append("] TJ\n");
        if (run.Font.SimulateBold) sb.Append("0 Tr\n");
        sb.Append("ET\n");
    }

    public void Image(byte[] data, RectPt r)
    {
        var img = pdf.ImageFor(data);
        if (img is null)
        {
            Content.Append($"0.85 0.85 0.85 rg {N(r.X)} {N(Y(r.Bottom))} {N(r.W)} {N(r.H)} re f\n");
            return;
        }
        Images.Add(img);
        Content.Append($"q {N(r.W)} 0 0 {N(r.H)} {N(r.X)} {N(Y(r.Bottom))} cm /{img} Do Q\n");
    }
}

internal sealed class PdfFile(FontCatalog catalog)
{
    private readonly List<byte[]?> _objects = [null]; // объект 0 не используется
    private readonly List<int> _pageIds = [];
    private readonly Dictionary<FontFace, PdfFont> _fonts = [];
    private readonly Dictionary<string, (string Name, int Id)> _images = [];
    private readonly Dictionary<string, int> _fontIds = [];
    private readonly Dictionary<string, int> _imageIds = [];
    private int _pagesId;

    internal sealed class PdfFont(string name, FontFace face)
    {
        public string Name { get; } = name;
        public FontFace Face { get; } = face;
        public SortedDictionary<ushort, string> Used { get; } = [];
    }

    private int Reserve() { _objects.Add(null); return _objects.Count - 1; }
    private void Set(int id, string s) => _objects[id] = Encoding.Latin1.GetBytes(s);
    private void Set(int id, byte[] b) => _objects[id] = b;
    private int Add(string s) { int id = Reserve(); Set(id, s); return id; }

    private static byte[] MakeStream(string dict, byte[] data, bool compress)
    {
        byte[] body = data;
        if (compress)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
            body = ms.ToArray();
        }
        var head = Encoding.Latin1.GetBytes($"<< {dict}{(compress ? " /Filter /FlateDecode" : "")} /Length {body.Length} >>\nstream\n");
        var tail = Encoding.Latin1.GetBytes("\nendstream");
        return [.. head, .. body, .. tail];
    }

    private int AddStream(string dict, byte[] data, bool compress = true) { int id = Reserve(); Set(id, MakeStream(dict, data, compress)); return id; }

    /// <summary>Шрифт PDF для отрезка; запрещённые к встраиванию шрифты заменяются на встраиваемый.</summary>
    public (PdfFont Font, ushort[] Glyphs) FontFor(GlyphRunInfo run)
    {
        var face = run.Font.Face;
        ushort[] glyphs = run.Glyphs;
        if (!face.Embeddable)
        {
            var alt = catalog.Resolve(new FontKey(FontCatalog.DefaultFamily, face.Bold, face.Italic))?.Face;
            if (alt is not null && alt.Embeddable)
            {
                face = alt;
                var cps = CodePoints(run.Text);
                glyphs = cps.Select(cp => alt.GlyphId(cp)).ToArray();
                if (glyphs.Length != run.Advances.Length) Array.Resize(ref glyphs, run.Advances.Length);
            }
        }
        if (!_fonts.TryGetValue(face, out var f)) _fonts[face] = f = new PdfFont("F" + (_fonts.Count + 1), face);
        var cpsAll = CodePoints(run.Text);
        for (int i = 0; i < glyphs.Length; i++)
            if (!f.Used.ContainsKey(glyphs[i])) f.Used[glyphs[i]] = i < cpsAll.Count ? char.ConvertFromUtf32(cpsAll[i]) : "";
        return (f, glyphs);
    }

    private static List<int> CodePoints(string s)
    {
        var list = new List<int>();
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length) { list.Add(char.ConvertToUtf32(s[i], s[i + 1])); i++; }
            else list.Add(s[i]);
        }
        return list;
    }

    public string? ImageFor(byte[] data)
    {
        string key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(data));
        if (_images.TryGetValue(key, out var existing)) return existing.Name;
        int? id = PdfImages.Embed(data, AddStream);
        if (id is null) return null;
        string name = "Im" + (_images.Count + 1);
        _images[key] = (name, id.Value);
        _imageIds[name] = id.Value;
        return name;
    }

    public void AddPage(double w, double h, PdfCanvas canvas)
    {
        if (_pagesId == 0) _pagesId = Reserve();
        int content = AddStream("", Encoding.Latin1.GetBytes(canvas.Content.ToString()));
        int pageId = Reserve();
        _pageIds.Add(pageId);
        _pendingPages.Add((pageId, w, h, content, canvas.Fonts.ToList(), canvas.Images.ToList()));
    }

    private readonly List<(int Id, double W, double H, int Content, List<string> Fonts, List<string> Images)> _pendingPages = [];

    private static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    public void Save(Stream output, string? title)
    {
        if (_pagesId == 0) { _pagesId = Reserve(); }
        foreach (var f in _fonts.Values) _fontIds[f.Name] = WriteFont(f);
        foreach (var p in _pendingPages)
        {
            string fonts = string.Concat(p.Fonts.Select(n => $"/{n} {_fontIds[n]} 0 R "));
            string images = string.Concat(p.Images.Select(n => $"/{n} {_imageIds[n]} 0 R "));
            Set(p.Id, $"<< /Type /Page /Parent {_pagesId} 0 R /MediaBox [0 0 {N(p.W)} {N(p.H)}] /Contents {p.Content} 0 R " +
                      $"/Resources << /Font << {fonts}>> /XObject << {images}>> /ProcSet [/PDF /Text /ImageC /ImageB] >> >>");
        }
        if (_pendingPages.Count == 0)
        {
            int content = AddStream("", []);
            int pageId = Reserve();
            Set(pageId, $"<< /Type /Page /Parent {_pagesId} 0 R /MediaBox [0 0 595.3 841.9] /Contents {content} 0 R /Resources << >> >>");
            _pageIds.Add(pageId);
        }
        Set(_pagesId, $"<< /Type /Pages /Kids [{string.Join(" ", _pageIds.Select(i => $"{i} 0 R"))}] /Count {_pageIds.Count} >>");
        int info = Add($"<< /Producer (Pero) /Title {PdfString(title ?? "")} /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>");
        int catalogId = Add($"<< /Type /Catalog /Pages {_pagesId} 0 R >>");

        var offsets = new long[_objects.Count];
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); output.Write(b); }
        long pos = 0;
        void Wp(byte[] b) { output.Write(b); pos += b.Length; }
        var header = Encoding.Latin1.GetBytes("%PDF-1.7\n%âãÏÓ\n");
        Wp(header);
        for (int i = 1; i < _objects.Count; i++)
        {
            offsets[i] = pos;
            Wp(Encoding.Latin1.GetBytes($"{i} 0 obj\n"));
            Wp(_objects[i] ?? Encoding.Latin1.GetBytes("null"));
            Wp(Encoding.Latin1.GetBytes("\nendobj\n"));
        }
        long xref = pos;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {_objects.Count}\n0000000000 65535 f \n");
        for (int i = 1; i < _objects.Count; i++) sb.Append(offsets[i].ToString("D10")).Append(" 00000 n \n");
        sb.Append($"trailer\n<< /Size {_objects.Count} /Root {catalogId} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        W(sb.ToString());
    }

    private static string PdfString(string s)
    {
        // UTF-16BE с BOM — для русских названий
        var b = Encoding.BigEndianUnicode.GetBytes(s);
        return "<FEFF" + Convert.ToHexString(b) + ">";
    }

    private int WriteFont(PdfFont f)
    {
        var face = f.Face;
        double k = 1000.0 / face.UnitsPerEm;
        byte[] file = face.FontFileBytes();
        int fileId = face.IsCff
            ? AddStream("/Subtype /OpenType", file)
            : AddStream($"/Length1 {file.Length}", file);
        int flags = face.IsSymbol ? 4 : 32;
        if (face.Italic) flags |= 64;
        string psName = new string(face.PostScriptName.Where(c => c > 32 && c < 127 && c is not ('(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%')).ToArray());
        if (psName.Length == 0) psName = "Font" + f.Name;
        int n = int.Parse(f.Name[1..]);
        string tag = new string(Enumerable.Range(0, 6).Select(i => (char)('A' + (n / (int)Math.Pow(26, 5 - i)) % 26)).ToArray());
        string fontName = tag + "+" + psName;
        int desc = Add($"<< /Type /FontDescriptor /FontName /{fontName} /Flags {flags} " +
                       $"/FontBBox [{N(face.XMin * k)} {N(face.YMin * k)} {N(face.XMax * k)} {N(face.YMax * k)}] /ItalicAngle {face.ItalicAngle} " +
                       $"/Ascent {N(face.WinAscent * k)} /Descent {N(-face.WinDescent * k)} /CapHeight {N(face.CapHeight * k)} /StemV {(face.Bold ? 120 : 80)} " +
                       $"/{(face.IsCff ? "FontFile3" : "FontFile2")} {fileId} 0 R >>");
        var w = new StringBuilder();
        foreach (var g in f.Used.Keys) w.Append($"{g} [{N(face.AdvanceUnits(g) * k)}] ");
        string cidType = face.IsCff ? "CIDFontType0" : "CIDFontType2";
        int cid = Add($"<< /Type /Font /Subtype /{cidType} /BaseFont /{fontName} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                      $"/FontDescriptor {desc} 0 R /DW {N(face.AdvanceUnits(0) * k)} /W [{w}]{(face.IsCff ? "" : " /CIDToGIDMap /Identity")} >>");
        int toUni = AddStream("", Encoding.ASCII.GetBytes(ToUnicode(f)));
        return Add($"<< /Type /Font /Subtype /Type0 /BaseFont /{fontName} /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {toUni} 0 R >>");
    }

    private static string ToUnicode(PdfFont f)
    {
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var items = f.Used.Where(kv => kv.Value.Length > 0).ToList();
        for (int i = 0; i < items.Count; i += 100)
        {
            var chunk = items.Skip(i).Take(100).ToList();
            sb.Append($"{chunk.Count} beginbfchar\n");
            foreach (var (g, text) in chunk)
                sb.Append($"<{g:X4}> <{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text))}>\n");
            sb.Append("endbfchar\n");
        }
        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return sb.ToString();
    }
}

/// <summary>Изображения для PDF: JPEG без перекодирования, PNG — через FlateDecode (с прозрачностью — маской).</summary>
internal static class PdfImages
{
    public static int? Embed(byte[] data, Func<string, byte[], bool, int> addStream)
    {
        try
        {
            if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8) return Jpeg(data, addStream);
            if (data.Length > 8 && data[0] == 0x89 && data[1] == (byte)'P') return Png(data, addStream);
        }
        catch { }
        return null;
    }

    private static int? Jpeg(byte[] d, Func<string, byte[], bool, int> addStream)
    {
        int i = 2;
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            byte m = d[i + 1];
            int len = d[i + 2] << 8 | d[i + 3];
            if (m is >= 0xC0 and <= 0xCF && m is not (0xC4 or 0xC8 or 0xCC))
            {
                int h = d[i + 5] << 8 | d[i + 6], w = d[i + 7] << 8 | d[i + 8], comps = d[i + 9];
                string cs = comps switch { 1 => "/DeviceGray", 4 => "/DeviceCMYK", _ => "/DeviceRGB" };
                string decode = comps == 4 ? " /Decode [1 0 1 0 1 0 1 0]" : "";
                return addStream($"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {cs} /BitsPerComponent 8 /Filter /DCTDecode{decode}", d, false);
            }
            i += 2 + len;
        }
        return null;
    }

    private static int? Png(byte[] d, Func<string, byte[], bool, int> addStream)
    {
        int pos = 8, w = 0, h = 0, depth = 0, type = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        var idat = new MemoryStream();
        while (pos + 8 <= d.Length)
        {
            int len = d[pos] << 24 | d[pos + 1] << 16 | d[pos + 2] << 8 | d[pos + 3];
            string tag = Encoding.ASCII.GetString(d, pos + 4, 4);
            int body = pos + 8;
            if (len < 0 || body + len > d.Length) break;
            switch (tag)
            {
                case "IHDR":
                    w = d[body] << 24 | d[body + 1] << 16 | d[body + 2] << 8 | d[body + 3];
                    h = d[body + 4] << 24 | d[body + 5] << 16 | d[body + 6] << 8 | d[body + 7];
                    depth = d[body + 8]; type = d[body + 9]; interlace = d[body + 12];
                    break;
                case "PLTE": palette = d[body..(body + len)]; break;
                case "tRNS": trns = d[body..(body + len)]; break;
                case "IDAT": idat.Write(d, body, len); break;
            }
            pos = body + len + 4;
        }
        if (w <= 0 || h <= 0 || interlace != 0) return null;
        int colors = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (colors == 0) return null;
        // без прозрачности: данные PNG передаются как есть, предиктор PNG понимает сам PDF
        if (type is 0 or 2 or 3 && trns is null)
        {
            string cs = type switch
            {
                0 => "/DeviceGray",
                2 => "/DeviceRGB",
                _ => $"[/Indexed /DeviceRGB {palette!.Length / 3 - 1} <{Convert.ToHexString(palette)}>]",
            };
            string dict = $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {cs} /BitsPerComponent {depth} " +
                          $"/Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors {colors} /BitsPerComponent {depth} /Columns {w} >>";
            return addStream(dict, idat.ToArray(), false);
        }
        if (depth != 8) return null;
        // с прозрачностью: распаковка, цвет и альфа — отдельными потоками
        byte[] raw;
        using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress))
        using (var ms = new MemoryStream()) { z.CopyTo(ms); raw = ms.ToArray(); }
        int bpp = colors, stride = w * bpp;
        var pixels = Unfilter(raw, w, h, bpp);
        bool gray = type is 0 or 4;
        int outColors = gray ? 1 : 3;
        var color = new byte[w * h * outColors];
        var alpha = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            switch (type)
            {
                case 6: color[3 * i] = pixels[4 * i]; color[3 * i + 1] = pixels[4 * i + 1]; color[3 * i + 2] = pixels[4 * i + 2]; alpha[i] = pixels[4 * i + 3]; break;
                case 4: color[i] = pixels[2 * i]; alpha[i] = pixels[2 * i + 1]; break;
                case 2: color[3 * i] = pixels[3 * i]; color[3 * i + 1] = pixels[3 * i + 1]; color[3 * i + 2] = pixels[3 * i + 2]; alpha[i] = 255; break;
                case 0: color[i] = pixels[i]; alpha[i] = 255; break;
                case 3:
                {
                    int idx = pixels[i];
                    color[3 * i] = palette![3 * idx]; color[3 * i + 1] = palette[3 * idx + 1]; color[3 * i + 2] = palette[3 * idx + 2];
                    alpha[i] = trns is not null && idx < trns.Length ? trns[idx] : (byte)255;
                    break;
                }
            }
        }
        if (type == 3) { gray = false; outColors = 3; }
        int smask = addStream($"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceGray /BitsPerComponent 8", alpha, true);
        return addStream($"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {(gray ? "/DeviceGray" : "/DeviceRGB")} /BitsPerComponent 8 /SMask {smask} 0 R", color, true);
    }

    private static byte[] Unfilter(byte[] raw, int w, int h, int bpp)
    {
        int stride = w * bpp;
        var res = new byte[stride * h];
        var prev = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            int src = y * (stride + 1);
            if (src + stride >= raw.Length + 1) break;
            byte f = raw[src];
            var cur = res.AsSpan(y * stride, stride);
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? cur[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                int v = raw[src + 1 + x];
                cur[x] = (byte)(f switch
                {
                    1 => v + a,
                    2 => v + b,
                    3 => v + (a + b) / 2,
                    4 => v + Paeth(a, b, c),
                    _ => v,
                });
            }
            cur.CopyTo(prev);
        }
        return res;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
