using Pero.Doc.Model;

namespace Pero.Doc.Editing;

/// <summary>Ввод и удаление текста, абзацы и разрывы (EDIT-01).</summary>
public sealed partial class DocEditor
{
    /// <summary>Набор текста (с заменой выделения). Подряд идущий ввод — один шаг отмены.</summary>
    public void TypeText(string text)
    {
        if (text.Length == 0) return;
        Execute(Loc.L("Ввод"), () =>
        {
            var at = DeleteSelectionCore();
            var p = P(at.Para);
            var (props, link) = InlineOps.PropsAt(p, at.Offset);
            if (PendingProps is not null) { props = PendingProps.Clone(); }
            InlineOps.InsertText(p, at.Offset, text, props, link);
            return Selection.At(new DocPos(at.Para, at.Offset + text.Replace("\r", "").Replace("\n", "").Length));
        }, coalesceTyping: Selection.IsEmpty && !text.Contains('\n'));
    }

    /// <summary>Вставка многострочного текста: строки становятся абзацами.</summary>
    public void InsertPlainText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        Execute(Loc.L("Вставка"), () =>
        {
            var at = DeleteSelectionCore();
            var props = InlineOps.PropsAt(P(at.Para), at.Offset);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) at = SplitCore(at);
                InlineOps.InsertText(P(at.Para), at.Offset, lines[i], props.Props, props.Link);
                at = new DocPos(at.Para, at.Offset + lines[i].Length);
            }
            return Selection.At(at);
        });
    }

    /// <summary>Enter — новый абзац.</summary>
    public void NewParagraph() => Execute(Loc.L("Новый абзац"), () =>
    {
        var at = DeleteSelectionCore();
        var p = P(at.Para);
        // пустой пункт списка + Enter — выход из списка, как в Word
        if (p.Length == 0 && p.Props.NumId is not null)
        {
            p.Props.NumId = null;
            p.Props.Ilvl = null;
            return Selection.At(at);
        }
        return Selection.At(SplitCore(at));
    });

    /// <summary>Shift+Enter — перенос строки.</summary>
    public void LineBreak() => InsertBreak(BreakKind.Line, Loc.L("Перенос строки"));

    /// <summary>Ctrl+Enter — разрыв страницы.</summary>
    public void PageBreak() => InsertBreak(BreakKind.Page, Loc.L("Разрыв страницы"));

    private void InsertBreak(BreakKind kind, string name) => Execute(name, () =>
    {
        var at = DeleteSelectionCore();
        var p = P(at.Para);
        var (props, _) = InlineOps.PropsAt(p, at.Offset);
        InlineOps.InsertInlines(p, at.Offset, [new Break { Kind = kind, Props = props }]);
        return Selection.At(new DocPos(at.Para, at.Offset + 1));
    });

    public void Backspace()
    {
        if (!Selection.IsEmpty) { DeleteSelection(); return; }
        var at = Selection.Active;
        var p = P(at.Para);
        if (at.Offset > 0)
        {
            Execute(Loc.L("Удаление"), () =>
            {
                InlineOps.Remove(p, at.Offset - 1, at.Offset);
                return Selection.At(new DocPos(at.Para, at.Offset - 1));
            }, coalesceTyping: true);
            return;
        }
        // в начале абзаца: сначала снять список, затем отступ первой строки, затем слить с предыдущим
        if (p.Props.NumId is not null)
        {
            Execute(Loc.L("Снять список"), () => { p.Props.NumId = null; p.Props.Ilvl = null; return Selection; });
            return;
        }
        if (at.Para == 0 || !Index.SameContainer(at.Para - 1, at.Para)) return;
        var loc = Index[at.Para];
        if (loc.Index == 0 || loc.Container[loc.Index - 1] is not Paragraph prev) return;
        Execute(Loc.L("Удаление"), () =>
        {
            int len = prev.Length;
            MergeWithNext(loc.Container, loc.Index - 1);
            return Selection.At(new DocPos(at.Para - 1, len));
        });
    }

    public void DeleteForward()
    {
        if (!Selection.IsEmpty) { DeleteSelection(); return; }
        var at = Selection.Active;
        var p = P(at.Para);
        if (at.Offset < p.Length)
        {
            Execute(Loc.L("Удаление"), () => { InlineOps.Remove(p, at.Offset, at.Offset + 1); return Selection.At(at); });
            return;
        }
        var loc = Index[at.Para];
        if (loc.Index + 1 >= loc.Container.Count || loc.Container[loc.Index + 1] is not Paragraph) return;
        Execute(Loc.L("Удаление"), () => { MergeWithNext(loc.Container, loc.Index); return Selection.At(at); });
    }

    public void DeleteSelection()
    {
        if (Selection.IsEmpty) return;
        Execute(Loc.L("Удаление"), () => Selection.At(DeleteSelectionCore()));
    }

    /// <summary>Удалить выделение внутри транзакции. Возвращает позицию начала.</summary>
    internal DocPos DeleteSelectionCore()
    {
        var s = Selection;
        if (s.IsEmpty) return s.Active;
        var (start, end) = (s.Start, s.End);
        if (start.Para == end.Para)
        {
            InlineOps.Remove(P(start.Para), start.Offset, end.Offset);
            return start;
        }
        var first = Index[start.Para];
        var last = Index[end.Para];
        if (ReferenceEquals(first.Container, last.Container))
        {
            // один контейнер: хвост первого + голова последнего, всё между удаляется (включая таблицы целиком)
            InlineOps.Remove(first.Paragraph, start.Offset, first.Paragraph.Length);
            InlineOps.Remove(last.Paragraph, 0, end.Offset);
            int from = first.Index + 1, to = last.Index;
            var keepRaw = first.Container.Skip(from).Take(to - from).OfType<RawBlock>().ToList(); // неизвестные блоки не теряем
            first.Container.RemoveRange(from, to - from);
            first.Container.InsertRange(from, keepRaw);
            Rebuild();
            // если между ними остались неизвестные блоки — абзацы не сливаем, чтобы их не потерять
            if (first.Container.IndexOf(last.Paragraph) == first.Index + 1) MergeWithNext(first.Container, first.Index);
            return start;
        }
        // разные контейнеры (часть в таблице): очищаем текст абзацев, структуру не трогаем
        for (int i = start.Para; i <= end.Para; i++)
        {
            var p = Index[i].Paragraph;
            int a = i == start.Para ? start.Offset : 0;
            int b = i == end.Para ? end.Offset : p.Length;
            InlineOps.Remove(p, a, b);
        }
        return start;
    }

    internal DocPos SplitCore(DocPos at)
    {
        var loc = Index[at.Para];
        var p = loc.Paragraph;
        bool atEnd = at.Offset >= p.Length;
        var next = InlineOps.SplitParagraph(p, at.Offset);
        next.Props.PageBreakBefore = false;
        if (atEnd && p.Props.StyleId is { } sid && Doc.Styles.ById.TryGetValue(sid, out var st) && st.Next is { } nx && nx != sid)
            next.Props.StyleId = nx == Doc.Styles.DefaultParagraphStyleId ? null : nx; // после заголовка — обычный текст
        loc.Container.Insert(loc.Index + 1, next);
        Rebuild();
        return new DocPos(at.Para + 1, 0);
    }

    private void MergeWithNext(List<Block> container, int index)
    {
        var a = (Paragraph)container[index];
        var b = (Paragraph)container[index + 1];
        a.Inlines.AddRange(b.Inlines);
        container.RemoveAt(index + 1);
        InlineOps.Normalize(a);
        Rebuild();
    }

    private void Rebuild() => Index = new ParaIndex(StoryBlocks);
}
