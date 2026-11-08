using Pero.Doc.Model;

namespace Pero.Doc.Editing;

/// <summary>Фрагмент документа для буфера обмена «Пера»: абзацы с оформлением, первый и последний — частично.</summary>
public sealed class Fragment
{
    public List<Block> Blocks { get; } = [];
    public string PlainText { get; init; } = "";
    /// <summary>Фрагмент — часть одного абзаца (вставляется в строку, без нового абзаца).</summary>
    public bool Inline => Blocks.Count == 1 && Blocks[0] is Paragraph;
}

/// <summary>Буфер обмена, поиск и замена, перемещение по словам (EDIT-01, FIND-01).</summary>
public sealed partial class DocEditor
{
    /// <summary>Скопировать выделение как фрагмент с оформлением.</summary>
    public Fragment? CopyFragment()
    {
        var s = Selection;
        if (s.IsEmpty) return null;
        var f = new Fragment { PlainText = SelectedText() };
        for (int i = s.Start.Para; i <= s.End.Para; i++)
        {
            var src = P(i);
            int a = i == s.Start.Para ? s.Start.Offset : 0;
            int b = i == s.End.Para ? s.End.Offset : src.Length;
            var copy = (Paragraph)src.Clone();
            if (b < copy.Length) InlineOps.Remove(copy, b, copy.Length);
            if (a > 0) InlineOps.Remove(copy, 0, a);
            // невидимые служебные элементы (закладки, поля) по краям не переносим
            copy.Inlines.RemoveAll(x => x is RawInline { Visible: false });
            f.Blocks.Add(copy);
        }
        return f;
    }

    /// <summary>Вставить фрагмент «Пера» в позицию курсора одним шагом отмены.</summary>
    public void PasteFragment(Fragment f)
    {
        if (f.Blocks.Count == 0) return;
        Execute("Вставка", () =>
        {
            var at = DeleteSelectionCore();
            var paras = f.Blocks.OfType<Paragraph>().Select(p => (Paragraph)p.Clone()).ToList();
            if (paras.Count == 0) return Selection.At(at);
            var target = P(at.Para);
            if (paras.Count == 1)
            {
                var items = paras[0].Inlines;
                int len = paras[0].Length;
                InlineOps.InsertInlines(target, at.Offset, items);
                return Selection.At(new DocPos(at.Para, at.Offset + len));
            }
            // несколько абзацев: первый дополняет текущий, последний — начало хвоста, средние вставляются целиком
            var tailPos = SplitCore(at);
            var head = P(at.Para);
            InlineOps.InsertInlines(head, head.Length, paras[0].Inlines);
            var loc = Index[tailPos.Para];
            int insertAt = loc.Index;
            for (int i = 1; i < paras.Count - 1; i++) loc.Container.Insert(insertAt++, paras[i]);
            Rebuild();
            var tail = loc.Paragraph;
            var last = paras[^1];
            InlineOps.InsertInlines(tail, 0, last.Inlines);
            tail.Props = last.Props.Clone();
            int tailIndex = Index.IndexOf(tail);
            return Selection.At(new DocPos(tailIndex, last.Length));
        });
    }

    // ── поиск и замена ───────────────────────────────────────────────────────

    /// <summary>Все вхождения текста в редактируемой части документа.</summary>
    public List<Selection> FindAll(string query, bool matchCase = false)
    {
        var res = new List<Selection>();
        if (string.IsNullOrEmpty(query)) return res;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        for (int i = 0; i < Index.Count; i++)
        {
            string t = Index[i].Paragraph.Text;
            int from = 0;
            while (from <= t.Length - query.Length)
            {
                int k = t.IndexOf(query, from, cmp);
                if (k < 0) break;
                res.Add(new Selection(new DocPos(i, k), new DocPos(i, k + query.Length)));
                from = k + Math.Max(1, query.Length);
            }
        }
        return res;
    }

    /// <summary>Следующее (или предыдущее) вхождение от курсора; поиск по кругу.</summary>
    public Selection? FindNext(string query, bool forward = true, bool matchCase = false)
    {
        var all = FindAll(query, matchCase);
        if (all.Count == 0) return null;
        var cur = Selection;
        var hit = forward
            ? all.FirstOrDefault(m => m.Start >= cur.End && !(m.Start == cur.Start && m.End == cur.End), all[0])
            : all.LastOrDefault(m => m.End <= cur.Start, all[^1]);
        Select(hit);
        return hit;
    }

    /// <summary>Заменить текущее выделение, если оно совпадает с запросом, и перейти к следующему.</summary>
    public bool ReplaceCurrent(string query, string replacement, bool matchCase = false)
    {
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        if (!Selection.IsEmpty && string.Equals(SelectedText(), query, cmp))
        {
            InsertPlainText(replacement);
            FindNext(query, true, matchCase);
            return true;
        }
        FindNext(query, true, matchCase);
        return false;
    }

    /// <summary>Заменить все вхождения одним шагом отмены. Возвращает число замен.</summary>
    public int ReplaceAll(string query, string replacement, bool matchCase = false)
    {
        var all = FindAll(query, matchCase);
        if (all.Count == 0) return 0;
        Execute("Заменить все", () =>
        {
            for (int k = all.Count - 1; k >= 0; k--)
            {
                var m = all[k];
                var p = P(m.Start.Para);
                var (props, link) = InlineOps.PropsAt(p, m.Start.Offset + 1);
                InlineOps.Remove(p, m.Start.Offset, m.End.Offset);
                InlineOps.InsertText(p, m.Start.Offset, replacement.Replace("\r", "").Replace("\n", " "), props, link);
            }
            return Selection.At(Index.Clamp(all[0].Start));
        });
        return all.Count;
    }

    // ── перемещение ──────────────────────────────────────────────────────────

    /// <summary>Соседняя позиция по символу (с переходом между абзацами).</summary>
    public DocPos StepChar(DocPos p, int dir)
    {
        if (dir > 0)
        {
            if (p.Offset < P(p.Para).Length) return p with { Offset = p.Offset + 1 + (IsPair(p.Para, p.Offset) ? 1 : 0) };
            return p.Para + 1 < Index.Count ? new DocPos(p.Para + 1, 0) : p;
        }
        if (p.Offset > 0) return p with { Offset = p.Offset - 1 - (p.Offset >= 2 && IsPair(p.Para, p.Offset - 2) ? 1 : 0) };
        return p.Para > 0 ? new DocPos(p.Para - 1, P(p.Para - 1).Length) : p;
    }

    private bool IsPair(int para, int offset)
    {
        string t = P(para).Text;
        return offset + 1 < t.Length && char.IsSurrogatePair(t[offset], t[offset + 1]);
    }

    private static int Cls(char c) => char.IsLetterOrDigit(c) || c == '_' ? 1 : char.IsWhiteSpace(c) ? 0 : 2;

    /// <summary>Начало следующего / предыдущего слова (Ctrl+стрелки).</summary>
    public DocPos StepWord(DocPos p, int dir)
    {
        string t = P(p.Para).Text;
        int i = p.Offset;
        if (dir > 0)
        {
            if (i >= t.Length) return StepChar(p, 1);
            int c = Cls(t[i]);
            while (i < t.Length && Cls(t[i]) == c && c != 0) i++;
            while (i < t.Length && Cls(t[i]) == 0) i++;
            return p with { Offset = i };
        }
        if (i == 0) return StepChar(p, -1);
        while (i > 0 && Cls(t[i - 1]) == 0) i--;
        if (i > 0)
        {
            int c = Cls(t[i - 1]);
            while (i > 0 && Cls(t[i - 1]) == c) i--;
        }
        return p with { Offset = i };
    }

    /// <summary>Слово под позицией (двойной щелчок).</summary>
    public Selection WordAt(DocPos p)
    {
        string t = P(p.Para).Text;
        if (t.Length == 0) return Selection.At(p);
        int i = Math.Min(p.Offset, t.Length - 1);
        if (i > 0 && p.Offset == t.Length) i = t.Length - 1;
        int c = Cls(t[i]);
        int a = i, b = i + 1;
        while (a > 0 && Cls(t[a - 1]) == c) a--;
        while (b < t.Length && Cls(t[b]) == c) b++;
        if (c == 1) while (b < t.Length && t[b] == ' ') b++; // как в Word — со следующим пробелом
        return new Selection(new DocPos(p.Para, a), new DocPos(p.Para, b));
    }

    /// <summary>Абзац под позицией (тройной щелчок).</summary>
    public Selection ParagraphAt(DocPos p) => new(new DocPos(p.Para, 0), new DocPos(p.Para, P(p.Para).Length));

    /// <summary>Удалить слово слева/справа от курсора (Ctrl+Backspace / Ctrl+Delete).</summary>
    public void DeleteWord(int dir)
    {
        if (!Selection.IsEmpty) { DeleteSelection(); return; }
        var from = Selection.Active;
        var to = StepWord(from, dir);
        if (to == from) return;
        Select(new Selection(from, to));
        DeleteSelection();
    }

    /// <summary>Число слов в основном тексте (Инструменты → Число слов).</summary>
    public int WordCount()
    {
        int n = 0;
        foreach (var p in Doc.AllParagraphs())
        {
            bool inWord = false;
            foreach (char c in p.Text)
            {
                bool w = char.IsLetterOrDigit(c) || c is '-' or '\'' or '’';
                if (w && !inWord) n++;
                inWord = w;
            }
        }
        return n;
    }
}
