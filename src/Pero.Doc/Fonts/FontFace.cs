using System.Buffers.Binary;
using System.Text;

namespace Pero.Doc.Fonts;

/// <summary>
/// Начертание шрифта TrueType/OpenType (FONT-01): имена, метрики, ширины и таблица символов → глифы.
/// Читается из файла .ttf/.otf или грани коллекции .ttc. Одни и те же данные используют
/// разметка (ширины), экран (номера глифов) и PDF (встраивание шрифта).
/// </summary>
public sealed class FontFace
{
    public string Path { get; }
    public int FaceIndex { get; }
    public string Family { get; private set; } = "";
    /// <summary>Все имена семейства из таблицы name (английские и локализованные), включая типографские.</summary>
    public HashSet<string> FamilyNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string SubFamily { get; private set; } = "";
    public string PostScriptName { get; private set; } = "";
    public bool Bold { get; private set; }
    public bool Italic { get; private set; }
    public int Weight { get; private set; } = 400;
    public int UnitsPerEm { get; private set; } = 1000;
    public int WinAscent { get; private set; }
    public int WinDescent { get; private set; }
    public int HheaAscent { get; private set; }
    public int HheaDescent { get; private set; }
    public int HheaLineGap { get; private set; }
    public int CapHeight { get; private set; }
    public short XMin { get; private set; }
    public short YMin { get; private set; }
    public short XMax { get; private set; }
    public short YMax { get; private set; }
    public int ItalicAngle { get; private set; }
    public bool IsCff { get; private set; }
    /// <summary>Разрешено ли встраивание (OS/2 fsType: не «ограниченная лицензия»).</summary>
    public bool Embeddable { get; private set; } = true;
    public int NumGlyphs { get; private set; }
    public bool IsSymbol { get; private set; }

    private byte[]? _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private ushort[]? _advances;
    private Dictionary<int, ushort>? _cmap;
    private readonly object _lock = new();

    private FontFace(string path, int faceIndex) { Path = path; FaceIndex = faceIndex; }

    /// <summary>
    /// Прочитать все грани файла (для .ttc — несколько). Читаются только каталог и малые таблицы
    /// (имена, метрики): сканирование сотен системных шрифтов не загружает их целиком.
    /// Повреждённые файлы пропускаются.
    /// </summary>
    public static List<FontFace> Load(string path)
    {
        var res = new List<FontFace>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            var head = ReadAt(fs, 0, 12);
            if (head.Length < 12) return res;
            if (Encoding.ASCII.GetString(head, 0, 4) == "ttcf")
            {
                int n = Math.Min((int)U32(head, 8), 64);
                var offs = ReadAt(fs, 12, 4 * n);
                for (int i = 0; i < n; i++)
                {
                    var f = new FontFace(path, i);
                    if (f.Parse(fs, (int)U32(offs, 4 * i))) res.Add(f);
                }
            }
            else
            {
                var f = new FontFace(path, 0);
                if (f.Parse(fs, 0)) res.Add(f);
            }
        }
        catch { }
        return res;
    }

    private static byte[] ReadAt(Stream s, long offset, int len)
    {
        if (offset < 0 || len <= 0 || offset >= s.Length) return [];
        len = (int)Math.Min(len, s.Length - offset);
        var b = new byte[len];
        s.Position = offset;
        s.ReadExactly(b);
        return b;
    }

    private bool Parse(Stream fs, int offset)
    {
        var dirHead = ReadAt(fs, offset, 12);
        int num = U16(dirHead, 4);
        if (num is <= 0 or > 200) return false;
        var dir = ReadAt(fs, offset + 12, 16 * num);
        for (int i = 0; i < num; i++)
        {
            int rec = 16 * i;
            if (rec + 16 > dir.Length) break;
            string tag = Encoding.ASCII.GetString(dir, rec, 4);
            long off = U32(dir, rec + 8), len = U32(dir, rec + 12);
            if (off + len > fs.Length) continue;
            _tables[tag] = ((int)off, (int)len);
        }
        if (!_tables.ContainsKey("head") || !_tables.ContainsKey("hhea") || !_tables.ContainsKey("hmtx") || !_tables.ContainsKey("cmap")) return false;
        IsCff = _tables.ContainsKey("CFF ") || _tables.ContainsKey("CFF2");
        byte[] T(string tag) => _tables.TryGetValue(tag, out var t) ? ReadAt(fs, t.Offset, Math.Min(t.Length, 1 << 20)) : [];

        var h = T("head");
        UnitsPerEm = Math.Max(16, U16(h, 18));
        XMin = S16(h, 36); YMin = S16(h, 38); XMax = S16(h, 40); YMax = S16(h, 42);
        int macStyle = U16(h, 44);
        Bold = (macStyle & 1) != 0;
        Italic = (macStyle & 2) != 0;

        var hh = T("hhea");
        HheaAscent = S16(hh, 4); HheaDescent = -S16(hh, 6); HheaLineGap = S16(hh, 8);
        _numHMetrics = U16(hh, 34);
        var mp = T("maxp");
        NumGlyphs = U16(mp, 4);
        var post = T("post");
        if (post.Length >= 8) ItalicAngle = (int)Math.Round(BinaryPrimitives.ReadInt32BigEndian(post.AsSpan(4)) / 65536.0);

        WinAscent = HheaAscent; WinDescent = HheaDescent;
        var os2 = T("OS/2");
        if (os2.Length >= 78)
        {
            Weight = U16(os2, 4);
            int fsType = U16(os2, 8);
            Embeddable = (fsType & 0x000F) != 0x0002; // 2 — встраивание запрещено
            int fsSel = U16(os2, 62);
            Italic |= (fsSel & 1) != 0;
            Bold = (fsSel & 0x20) != 0 || Weight >= 600 || Bold;
            WinAscent = U16(os2, 74);
            WinDescent = U16(os2, 76);
            if (os2.Length >= 90 && U16(os2, 0) >= 2) CapHeight = S16(os2, 88);
        }
        if (WinAscent + WinDescent <= 0) { WinAscent = HheaAscent; WinDescent = HheaDescent; }
        if (CapHeight == 0) CapHeight = (int)(UnitsPerEm * 0.7);
        ReadNames(T("name"));
        return Family.Length > 0;
    }

    private int _numHMetrics;

    private void ReadNames(byte[] d)
    {
        if (d.Length < 6) return;
        int count = U16(d, 2), strOff = U16(d, 4);
        string? fam = null, famTypo = null, sub = null, subTypo = null;
        int famScore = -1;
        for (int i = 0; i < count; i++)
        {
            int r = 6 + 12 * i;
            if (r + 12 > d.Length) break;
            int platform = U16(d, r), enc = U16(d, r + 2), lang = U16(d, r + 4), id = U16(d, r + 6), len = U16(d, r + 8), off = U16(d, r + 10);
            if (platform == 3 && enc == 0) IsSymbol = true;
            if (id is not (1 or 2 or 6 or 16 or 17)) continue;
            int s = strOff + off;
            if (s + len > d.Length) continue;
            string? v = platform switch
            {
                3 or 0 => Encoding.BigEndianUnicode.GetString(d, s, len),
                1 when enc == 0 => Encoding.Latin1.GetString(d, s, len),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(v)) continue;
            v = v.Trim('\0', ' ');
            bool english = platform == 3 && lang == 0x409 || platform == 1 && lang == 0;
            switch (id)
            {
                case 1:
                    FamilyNames.Add(v);
                    int score = english ? 2 : 1;
                    if (score > famScore) { fam = v; famScore = score; }
                    break;
                case 16: FamilyNames.Add(v); if (english || famTypo is null) famTypo = v; break;
                case 2: if (english || sub is null) sub = v; break;
                case 17: if (english || subTypo is null) subTypo = v; break;
                case 6: if (PostScriptName.Length == 0 || english) PostScriptName = v; break;
            }
        }
        Family = fam ?? famTypo ?? "";
        SubFamily = sub ?? subTypo ?? "";
        if (PostScriptName.Length == 0) PostScriptName = Family.Replace(" ", "") + (SubFamily.Length > 0 ? "-" + SubFamily.Replace(" ", "") : "");
    }

    // ── данные по требованию ─────────────────────────────────────────────────

    private byte[] Data
    {
        get
        {
            lock (_lock) return _data ??= File.ReadAllBytes(Path);
        }
    }

    private void EnsureMetrics()
    {
        if (_advances is not null) return;
        lock (_lock)
        {
            if (_advances is not null) return;
            var d = Data;
            int nh = _numHMetrics;
            int ng = Math.Max(NumGlyphs, nh);
            var adv = new ushort[Math.Max(1, ng)];
            int hm = _tables["hmtx"].Offset;
            ushort last = 0;
            for (int i = 0; i < adv.Length; i++)
            {
                if (i < nh && hm + 4 * i + 2 <= d.Length) last = (ushort)U16(d, hm + 4 * i);
                adv[i] = last;
            }
            _cmap = ReadCmap(d);
            _advances = adv;
        }
    }

    private Dictionary<int, ushort> ReadCmap(byte[] d)
    {
        var map = new Dictionary<int, ushort>();
        var (o, _) = _tables["cmap"];
        int n = U16(d, o + 2);
        int best = -1, bestScore = -1;
        for (int i = 0; i < n; i++)
        {
            int r = o + 4 + 8 * i;
            int platform = U16(d, r), enc = U16(d, r + 2);
            int sub = o + (int)U32(d, r + 4);
            if (sub + 2 > d.Length) continue;
            int fmt = U16(d, sub);
            int score = (platform, enc, fmt) switch
            {
                (3, 10, 12) => 5,
                (0, _, 12) => 4,
                (3, 1, 4) => 3,
                (0, _, 4) => 2,
                (3, 0, 4) => 1,
                _ => -1,
            };
            if (score > bestScore) { bestScore = score; best = sub; }
        }
        if (best < 0) return map;
        int format = U16(d, best);
        if (format == 4)
        {
            int segX2 = U16(d, best + 6);
            int ends = best + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
            for (int s = 0; s < segX2 / 2; s++)
            {
                int end = U16(d, ends + 2 * s), start = U16(d, starts + 2 * s);
                short delta = S16(d, deltas + 2 * s);
                int rangeOff = U16(d, ranges + 2 * s);
                if (start > end) continue;
                for (int c = start; c <= end && c != 0xFFFF; c++)
                {
                    int g;
                    if (rangeOff == 0) g = (c + delta) & 0xFFFF;
                    else
                    {
                        int at = ranges + 2 * s + rangeOff + 2 * (c - start);
                        if (at + 2 > d.Length) continue;
                        g = U16(d, at);
                        if (g != 0) g = (g + delta) & 0xFFFF;
                    }
                    if (g != 0) map[c] = (ushort)g;
                }
            }
        }
        else if (format == 12)
        {
            int groups = (int)U32(d, best + 12);
            for (int i = 0; i < groups; i++)
            {
                int r = best + 16 + 12 * i;
                if (r + 12 > d.Length) break;
                uint start = U32(d, r), end = U32(d, r + 4), g = U32(d, r + 8);
                if (end - start > 0x20000) continue;
                for (uint c = start; c <= end; c++) map[(int)c] = (ushort)(g + c - start);
            }
        }
        // символьный шрифт (Symbol, Wingdings): коды 0xF000+ доступны и по младшему байту
        if (IsSymbol)
            foreach (var kv in map.Where(kv => kv.Key >= 0xF000 && kv.Key <= 0xF0FF).ToList())
                map.TryAdd(kv.Key - 0xF000, kv.Value);
        return map;
    }

    public bool HasGlyph(int codepoint)
    {
        EnsureMetrics();
        return _cmap!.ContainsKey(codepoint);
    }

    public ushort GlyphId(int codepoint)
    {
        EnsureMetrics();
        return _cmap!.TryGetValue(codepoint, out var g) ? g : (ushort)0;
    }

    /// <summary>Ширина глифа в единицах em.</summary>
    public int AdvanceUnits(ushort gid)
    {
        EnsureMetrics();
        return gid < _advances!.Length ? _advances[gid] : _advances[^1];
    }

    public double Advance(int codepoint, double sizePt) => AdvanceUnits(GlyphId(codepoint)) * sizePt / UnitsPerEm;

    public IReadOnlyDictionary<int, ushort> CharacterMap { get { EnsureMetrics(); return _cmap!; } }

    /// <summary>Файл шрифта для встраивания: для грани коллекции собирается отдельный TTF.</summary>
    public byte[] FontFileBytes()
    {
        var d = Data;
        bool ttc = d.Length > 4 && d[0] == (byte)'t' && d[1] == (byte)'t' && d[2] == (byte)'c' && d[3] == (byte)'f';
        if (!ttc) return d;
        var tags = _tables.Keys.OrderBy(t => t, StringComparer.Ordinal).ToList();
        int headerLen = 12 + 16 * tags.Count;
        var ms = new MemoryStream();
        var hdr = new byte[headerLen];
        BinaryPrimitives.WriteUInt32BigEndian(hdr, IsCff ? 0x4F54544Fu : 0x00010000u);
        BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(4), (ushort)tags.Count);
        int pow = 1, log = 0;
        while (pow * 2 <= tags.Count) { pow *= 2; log++; }
        BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(6), (ushort)(pow * 16));
        BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(8), (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(10), (ushort)(tags.Count * 16 - pow * 16));
        ms.Write(hdr);
        int pos = headerLen;
        for (int i = 0; i < tags.Count; i++)
        {
            var (off, len) = _tables[tags[i]];
            int rec = 12 + 16 * i;
            Encoding.ASCII.GetBytes(tags[i]).CopyTo(hdr, rec);
            BinaryPrimitives.WriteUInt32BigEndian(hdr.AsSpan(rec + 4), Checksum(d, off, len));
            BinaryPrimitives.WriteUInt32BigEndian(hdr.AsSpan(rec + 8), (uint)pos);
            BinaryPrimitives.WriteUInt32BigEndian(hdr.AsSpan(rec + 12), (uint)len);
            ms.Write(d, off, len);
            int pad = (4 - len % 4) % 4;
            for (int k = 0; k < pad; k++) ms.WriteByte(0);
            pos += len + pad;
        }
        var res = ms.ToArray();
        hdr.CopyTo(res, 0);
        return res;
    }

    private static uint Checksum(byte[] d, int off, int len)
    {
        uint sum = 0;
        for (int i = 0; i < len; i += 4)
        {
            uint v = 0;
            for (int k = 0; k < 4; k++) v = (v << 8) | (i + k < len ? d[off + i + k] : (byte)0);
            sum += v;
        }
        return sum;
    }

    public override string ToString() => $"{Family} {SubFamily} ({System.IO.Path.GetFileName(Path)}#{FaceIndex})";

    private static int U16(byte[] d, int o) => o + 2 <= d.Length ? BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o)) : 0;
    private static short S16(byte[] d, int o) => o + 2 <= d.Length ? BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(o)) : (short)0;
    private static uint U32(byte[] d, int o) => o + 4 <= d.Length ? BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o)) : 0;
}
