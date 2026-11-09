using Pero.Doc.Model;

namespace Pero.Doc.Editing;

/// <summary>Низкоуровневые операции над содержимым абзаца: разрез по смещению, вставка, удаление, слияние прогонов.</summary>
internal static class InlineOps
{
    /// <summary>Разрезать содержимое так, чтобы смещение пришлось на границу элементов. Возвращает индекс элемента после границы.</summary>
    public static int SplitAt(Paragraph p, int offset)
    {
        int pos = 0;
        for (int i = 0; i < p.Inlines.Count; i++)
        {
            var inl = p.Inlines[i];
            int len = inl.Length;
            if (offset == pos) return i;
            if (offset < pos + len)
            {
                if (inl is Run r)
                {
                    int cut = offset - pos;
                    var tail = (Run)r.Clone();
                    tail.Text = r.Text[cut..];
                    r.Text = r.Text[..cut];
                    p.Inlines.Insert(i + 1, tail);
                    return i + 1;
                }
                return i + 1; // внутри объекта длины 1 быть не может; на всякий случай — после него
            }
            pos += len;
        }
        return p.Inlines.Count;
    }

    /// <summary>Свойства, которые получит текст, набранный в смещении (как в Word: от предыдущего символа).</summary>
    public static (RunProps Props, LinkRef? Link) PropsAt(Paragraph p, int offset)
    {
        int pos = 0;
        Inline? before = null, after = null;
        foreach (var inl in p.Inlines)
        {
            if (inl.Length == 0) continue;
            if (pos < offset) before = inl;
            else { after ??= inl; break; }
            pos += inl.Length;
        }
        var src = before ?? after;
        RunProps props = src switch
        {
            Run r => r.Props,
            TabChar t => t.Props,
            Break b => b.Props,
            InlineImage im => im.Props,
            _ => MarkProps(p),
        };
        // ссылка продолжается, только если курсор внутри неё
        LinkRef? link = before?.Link is { } l && after?.Link is { } l2 && l.SameAs(l2) ? l : null;
        return (props.Clone(), link);
    }

    private static RunProps MarkProps(Paragraph p) =>
        p.Props.MarkRunProps is { } m ? Docx.DocxReader.ReadRunProps(m) : new RunProps();

    public static void InsertInlines(Paragraph p, int offset, IEnumerable<Inline> items)
    {
        int idx = SplitAt(p, offset);
        p.Inlines.InsertRange(idx, items);
        Normalize(p);
    }

    public static void InsertText(Paragraph p, int offset, string text, RunProps props, LinkRef? link)
    {
        var items = new List<Inline>();
        var sb = new System.Text.StringBuilder();
        void Flush() { if (sb.Length > 0) { items.Add(new Run { Text = sb.ToString(), Props = props.Clone(), Link = link }); sb.Clear(); } }
        foreach (char c in text)
        {
            if (c == '\t') { Flush(); items.Add(new TabChar { Props = props.Clone(), Link = link }); }
            else if (c == '\v') { Flush(); items.Add(new Break { Kind = BreakKind.Line, Props = props.Clone(), Link = link }); }
            else if (c != '\r' && c != '\n') sb.Append(c);
        }
        Flush();
        InsertInlines(p, offset, items);
    }

    /// <summary>Удалить диапазон [from, to) внутри абзаца. Возвращает удалённые элементы.</summary>
    public static List<Inline> Remove(Paragraph p, int from, int to)
    {
        if (to <= from) return [];
        int a = SplitAt(p, from);
        int b = SplitAt(p, to);
        // невидимые служебные элементы (закладки, коды полей) на границах не удаляем
        var removed = p.Inlines.GetRange(a, b - a);
        var keep = removed.Where(i => i is RawInline { Visible: false }).ToList();
        p.Inlines.RemoveRange(a, b - a);
        p.Inlines.InsertRange(a, keep);
        Normalize(p);
        return removed.Except(keep).ToList();
    }

    /// <summary>Применить изменение свойств к элементам в диапазоне [from, to).</summary>
    public static void ApplyProps(Paragraph p, int from, int to, Action<RunProps> change)
    {
        if (to <= from) return;
        int a = SplitAt(p, from);
        int b = SplitAt(p, to);
        for (int i = a; i < b; i++)
        {
            switch (p.Inlines[i])
            {
                case Run r: change(r.Props); break;
                case TabChar t: change(t.Props); break;
                case Break br: change(br.Props); break;
                case InlineImage im: change(im.Props); break;
            }
        }
        Normalize(p);
    }

    public static void SetLink(Paragraph p, int from, int to, LinkRef? link)
    {
        if (to <= from) return;
        int a = SplitAt(p, from);
        int b = SplitAt(p, to);
        for (int i = a; i < b; i++) p.Inlines[i].Link = link;
        Normalize(p);
    }

    /// <summary>Слить соседние прогоны с одинаковыми свойствами и ссылкой, убрать пустые.</summary>
    public static void Normalize(Paragraph p)
    {
        for (int i = p.Inlines.Count - 1; i >= 0; i--)
        {
            if (p.Inlines[i] is Run { Text.Length: 0 }) { p.Inlines.RemoveAt(i); continue; }
            if (i > 0 && p.Inlines[i] is Run r && p.Inlines[i - 1] is Run prev &&
                prev.Props.Equals(r.Props) && LinkEq(prev.Link, r.Link))
            {
                prev.Text += r.Text;
                p.Inlines.RemoveAt(i);
            }
        }
    }

    private static bool LinkEq(LinkRef? a, LinkRef? b) => a is null ? b is null : a.SameAs(b);

    /// <summary>Содержимое абзаца после смещения переносится в новый абзац.</summary>
    public static Paragraph SplitParagraph(Paragraph p, int offset)
    {
        int idx = SplitAt(p, offset);
        var next = new Paragraph { Props = p.Props.Clone() };
        next.Inlines.AddRange(p.Inlines.Skip(idx));
        p.Inlines.RemoveRange(idx, p.Inlines.Count - idx);
        Normalize(p);
        Normalize(next);
        return next;
    }
}
