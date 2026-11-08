using Pero.Doc.Model;

namespace Pero.Doc.Editing;

/// <summary>
/// Редактор документа (CORE-01): все изменения — транзакции с отменой и повтором.
/// Снимок делается один раз на транзакцию; подряд идущий ввод в одном абзаце сливается в один шаг.
/// </summary>
public sealed partial class DocEditor
{
    private sealed record Snapshot(Document Doc, Selection Selection, string Name);

    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private string? _lastName;
    private DocPos _lastTypingEnd;
    private DateTime _lastTypingAt;

    public Document Doc { get; private set; }
    public ParaIndex Index { get; private set; }
    public Selection Selection { get; private set; }
    /// <summary>Оформление для следующего ввода при пустом выделении (после Ctrl+B без выделения).</summary>
    public RunProps? PendingProps { get; private set; }
    public bool IsDirty { get; private set; }
    public int UndoLimit { get; set; } = 200;

    public event EventHandler? Changed;
    public event EventHandler? SelectionChanged;

    public DocEditor(Document doc)
    {
        Doc = doc;
        Index = new ParaIndex(doc);
        Selection = Selection.At(Index.DocumentStart);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.TryPeek(out var s) ? s.Name : null;

    public void MarkSaved() => IsDirty = false;

    public void Select(Selection s)
    {
        var c = new Selection(Index.Clamp(s.Anchor), Index.Clamp(s.Active));
        if (c == Selection) return;
        Selection = c;
        PendingProps = null;
        _lastName = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll() => Select(new Selection(Index.DocumentStart, Index.DocumentEnd));

    /// <summary>Выполнить изменение как транзакцию. Функция возвращает новое выделение.</summary>
    public void Execute(string name, Func<Selection> change, bool coalesceTyping = false)
    {
        bool merge = coalesceTyping && _lastName == name && Selection.IsEmpty && Selection.Active == _lastTypingEnd &&
                     (DateTime.UtcNow - _lastTypingAt).TotalSeconds < 3 && _undo.Count > 0;
        Document? before = merge ? null : Doc.Clone();
        var selBefore = Selection;
        Selection after;
        try { after = change(); }
        catch
        {
            if (before is not null) { Doc = before; Index = new ParaIndex(Doc); }
            throw;
        }
        if (before is not null)
        {
            _undo.Push(new Snapshot(before, selBefore, name));
            Trim();
        }
        _redo.Clear();
        Index = new ParaIndex(Doc);
        Selection = new Selection(Index.Clamp(after.Anchor), Index.Clamp(after.Active));
        _lastName = coalesceTyping ? name : null;
        _lastTypingEnd = Selection.Active;
        _lastTypingAt = DateTime.UtcNow;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Trim()
    {
        if (_undo.Count <= UndoLimit) return;
        var keep = _undo.Take(UndoLimit).Reverse().ToList();
        _undo.Clear();
        foreach (var s in keep) _undo.Push(s);
    }

    public void Undo() => Swap(_undo, _redo);
    public void Redo() => Swap(_redo, _undo);

    private void Swap(Stack<Snapshot> from, Stack<Snapshot> to)
    {
        if (from.Count == 0) return;
        var s = from.Pop();
        to.Push(new Snapshot(Doc, Selection, s.Name));
        Doc = s.Doc;
        Index = new ParaIndex(Doc);
        Selection = new Selection(Index.Clamp(s.Selection.Anchor), Index.Clamp(s.Selection.Active));
        _lastName = null;
        PendingProps = null;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal Paragraph P(int para) => Index[para].Paragraph;

    /// <summary>Абзацы, затронутые выделением.</summary>
    public IEnumerable<Paragraph> SelectedParagraphs()
    {
        for (int i = Selection.Start.Para; i <= Selection.End.Para; i++) yield return Index[i].Paragraph;
    }

    /// <summary>Текст выделения: абзацы через перевод строки, ячейки — через табуляцию.</summary>
    public string SelectedText()
    {
        var s = Selection;
        var sb = new System.Text.StringBuilder();
        for (int i = s.Start.Para; i <= s.End.Para; i++)
        {
            string t = P(i).Text;
            int a = i == s.Start.Para ? s.Start.Offset : 0;
            int b = i == s.End.Para ? s.End.Offset : t.Length;
            sb.Append(t.AsSpan(a, Math.Max(0, b - a)).ToString().Replace('￼', ' ').Replace('\n', '\v'));
            if (i < s.End.Para) sb.Append(Index[i].Cell is not null && Index[i + 1].Cell is not null && !ReferenceEquals(Index[i].Cell, Index[i + 1].Cell) ? "\t" : "\r\n");
        }
        return sb.ToString();
    }
}
