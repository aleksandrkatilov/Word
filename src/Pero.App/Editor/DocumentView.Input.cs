using System.Windows;
using System.Windows.Input;
using Pero.Doc.Editing;
using Pero.Doc.Layout;
using Pero.Doc.Model;

namespace Pero.Editor;

public sealed partial class DocumentView
{
    private double? _desiredX;          // желаемая колонка при движении вверх/вниз, пункты
    private bool _dragging;
    private DocPos _dragAnchor;
    private ResizeState? _resize;

    private sealed class ResizeState(LineBox line, Seg seg, DocPos pos)
    {
        public LineBox Line { get; } = line;
        public Seg Seg { get; } = seg;
        public DocPos Pos { get; } = pos;
        public double NewW { get; set; } = seg.Width;
        public double NewH { get; set; } = seg.Height;
    }

    private HeaderFooter? Story => Editor.Story;
    private int? PagePref => Editor.Story is null ? null : StoryPage;

    /// <summary>Правка разрешена; иначе — сообщение окну.</summary>
    private bool CanEdit()
    {
        if (!IsReadOnly) return true;
        ReadOnlyAttempt?.Invoke(this, EventArgs.Empty);
        return false;
    }

    // ── клавиатура ───────────────────────────────────────────────────────────

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        string text = e.Text;
        if (string.IsNullOrEmpty(text) || e.Handled) return;
        // управляющие символы обрабатываются в OnPreviewKeyDown
        text = new string(text.Where(c => c >= ' ' || c == '\t').ToArray());
        if (text.Length == 0 || text == "\t") return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        e.Handled = true;
        if (!CanEdit()) return;
        Editor.TypeText(text);
        _desiredX = null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool shift = mods.HasFlag(ModifierKeys.Shift), ctrl = mods.HasFlag(ModifierKeys.Control), alt = mods.HasFlag(ModifierKeys.Alt);
        if (alt) return;
        var sel = Editor.Selection;
        var pos = sel.Active;

        void Move(DocPos to, bool keepDesired = false)
        {
            Editor.Select(shift ? new Selection(sel.Anchor, to) : Selection.At(to));
            if (!keepDesired) _desiredX = null;
            ScrollToCaret();
            e.Handled = true;
        }

        switch (key)
        {
            case Key.Left:
                if (!shift && !sel.IsEmpty) { Move(sel.Start); return; }
                Move(ctrl ? Editor.StepWord(pos, -1) : Editor.StepChar(pos, -1));
                return;
            case Key.Right:
                if (!shift && !sel.IsEmpty) { Move(sel.End); return; }
                Move(ctrl ? Editor.StepWord(pos, 1) : Editor.StepChar(pos, 1));
                return;
            case Key.Up:
            case Key.Down:
            {
                int dir = key == Key.Up ? -1 : 1;
                if (ctrl)
                {
                    // Ctrl+↑/↓ — к началу абзаца
                    var target = dir < 0
                        ? (pos.Offset > 0 ? pos with { Offset = 0 } : new DocPos(Math.Max(0, pos.Para - 1), 0))
                        : new DocPos(Math.Min(Editor.Index.Count - 1, pos.Para + 1), 0);
                    Move(target);
                    return;
                }
                var c = Layout.Caret(Story, pos, PagePref);
                double x = _desiredX ?? c?.X ?? 0;
                var to = Layout.Vertical(Story, pos, dir, x, PagePref)
                         ?? (dir < 0 ? new DocPos(pos.Para, 0) : Editor.Index.Clamp(new DocPos(pos.Para, int.MaxValue)));
                _desiredX = x;
                Move(to, keepDesired: true);
                return;
            }
            case Key.Home:
                Move(ctrl ? Editor.Index.DocumentStart : Layout.LineHome(Story, pos, PagePref) ?? pos with { Offset = 0 });
                return;
            case Key.End:
                Move(ctrl ? Editor.Index.DocumentEnd : Layout.LineEndPos(Story, pos, PagePref) ?? Editor.Index.Clamp(pos with { Offset = int.MaxValue }));
                return;
            case Key.PageUp:
            case Key.PageDown:
            {
                if (Caret is not { } c) return;
                var v = ToView(c.Page, c.X, c.Y + c.Height / 2);
                double dy = (key == Key.PageUp ? -1 : 1) * _viewport.Height * 0.9;
                SetVerticalOffset(_offset.Y + dy);
                var hit = ToPage(new Point(v.X, v.Y));
                var to = Layout.Hit(Story, hit.Page, hit.X, hit.Y) ?? pos;
                Move(to);
                return;
            }
            case Key.Back:
                e.Handled = true;
                if (!CanEdit()) return;
                if (ctrl) Editor.DeleteWord(-1); else Editor.Backspace();
                _desiredX = null;
                return;
            case Key.Delete:
                if (shift) return; // Shift+Delete — «Вырезать» в окне
                e.Handled = true;
                if (!CanEdit()) return;
                if (ctrl) Editor.DeleteWord(1); else Editor.DeleteForward();
                _desiredX = null;
                return;
            case Key.Enter:
                e.Handled = true;
                if (!CanEdit()) return;
                if (ctrl) Editor.PageBreak();
                else if (shift) Editor.LineBreak();
                else Editor.NewParagraph();
                _desiredX = null;
                return;
            case Key.Tab:
                e.Handled = true;
                if (!CanEdit()) return;
                HandleTab(shift);
                return;
            case Key.Escape:
                if (Editor.Story is not null) { ExitStory(); e.Handled = true; }
                else if (!sel.IsEmpty) { Editor.Select(Selection.At(pos)); e.Handled = true; }
                return;
        }
    }

    private void HandleTab(bool back)
    {
        if (Editor.CurrentCell() is var (t, r, c))
        {
            // переход по ячейкам таблицы; Tab в последней ячейке добавляет строку
            int row = r, cell = c + (back ? -1 : 1);
            if (cell >= t.Rows[row].Cells.Count) { row++; cell = 0; }
            if (cell < 0) { row--; cell = row >= 0 ? t.Rows[row].Cells.Count - 1 : 0; }
            if (row < 0) return;
            if (row >= t.Rows.Count)
            {
                Editor.InsertRow(below: true);
                return;
            }
            var first = t.Rows[row].Cells[cell].Blocks.OfType<Paragraph>().FirstOrDefault();
            var last = t.Rows[row].Cells[cell].Blocks.OfType<Paragraph>().LastOrDefault();
            if (first is null || last is null) return;
            int a = Editor.Index.IndexOf(first), b = Editor.Index.IndexOf(last);
            Editor.Select(new Selection(new DocPos(a, 0), new DocPos(b, last.Length)));
            ScrollToCaret();
            return;
        }
        var sel = Editor.Selection;
        var p = Editor.Index[sel.Active.Para].Paragraph;
        // в начале пункта списка (или на нескольких абзацах) Tab меняет уровень/отступ
        if (p.Props.NumId is not null && sel.Active.Offset == 0 || sel.Start.Para != sel.End.Para)
        {
            Editor.Indent(back ? -360 : 360);
            return;
        }
        if (back) return;
        Editor.TypeText("\t");
    }

    // ── мышь ─────────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        var (page, x, y) = ToPage(p);
        if (Layout.Pages.Count == 0) return;
        e.Handled = true;

        // колонтитулы: двойной щелчок по области колонтитула — правка; щелчок по тексту — возврат
        var zone = Layout.ZoneAt(page, y);
        if (Editor.Story is null && zone is HfKind kind && e.ClickCount == 2)
        {
            EnterHeaderFooter(kind, page);
            return;
        }
        if (Editor.Story is not null && zone is null)
        {
            if (e.ClickCount < 2 && !IsInStoryZone(page, y)) { ExitStory(); }
        }
        else if (Editor.Story is not null && zone is HfKind z2 && z2 != Editor.Story.Kind && e.ClickCount == 2)
        {
            EnterHeaderFooter(z2, page);
            return;
        }
        if (Editor.Story is not null) StoryPage = page;

        // ссылка: Ctrl+щелчок
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Layout.LinkAt(Story, page, x, y) is { } link)
        {
            LinkClicked?.Invoke(this, link);
            return;
        }
        // изображение: выделение и изменение размера за маркер
        if (SelectedImage() is { } selImg && selImg.Line.Page == page && OnHandle(selImg, x, y))
        {
            _resize = new ResizeState(selImg.Line, selImg.Seg, Editor.Selection.Start);
            CaptureMouse();
            return;
        }
        if (Layout.ImageAt(Story, page, x, y) is { } img)
        {
            var at = new DocPos(img.Line.Para, img.Seg.Start);
            Editor.Select(new Selection(at, at with { Offset = at.Offset + 1 }));
            return;
        }

        var hit = Layout.Hit(Story, page, x, y);
        if (hit is null) return;
        var pos = hit.Value;
        if (e.ClickCount == 2) { Editor.Select(Editor.WordAt(pos)); return; }
        if (e.ClickCount >= 3) { Editor.Select(Editor.ParagraphAt(pos)); return; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Editor.Select(new Selection(Editor.Selection.Anchor, pos));
        else Editor.Select(Selection.At(pos));
        _dragAnchor = Editor.Selection.Anchor;
        _dragging = true;
        _desiredX = null;
        CaptureMouse();
    }

    private bool IsInStoryZone(int page, double y)
    {
        if (Editor.Story is null) return true;
        var pg = Layout.Pages[page];
        return Editor.Story.Kind == HfKind.Header ? y < pg.Body.Y : y > pg.Body.Bottom;
    }

    private bool OnHandle((LineBox Line, Seg Seg) img, double x, double y)
    {
        double h = HandleSize / Scale;
        double right = img.Seg.X + img.Seg.Width, bottom = img.Line.Y + img.Line.Baseline;
        return Math.Abs(x - right) <= h && Math.Abs(y - bottom) <= h;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (Layout.Pages.Count == 0) return;
        var (page, x, y) = ToPage(p);
        if (_resize is { } rz && e.LeftButton == MouseButtonState.Pressed)
        {
            // пропорционально: ширина по горизонтали мыши
            double w = Math.Max(8, x - rz.Seg.X);
            double ratio = rz.Seg.Height / Math.Max(1, rz.Seg.Width);
            rz.NewW = w;
            rz.NewH = w * ratio;
            InvalidateVisual();
            return;
        }
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
        {
            if (p.Y < 0) LineUp(); else if (p.Y > _viewport.Height) LineDown();
            var hit = Layout.Hit(Story, page, x, y);
            if (hit is { } pos) Editor.Select(new Selection(_dragAnchor, pos));
            return;
        }
        // курсор мыши
        if (SelectedImage() is { } si && si.Line.Page == page && OnHandle(si, x, y)) Cursor = Cursors.SizeNWSE;
        else if (Layout.ImageAt(Story, page, x, y) is not null) Cursor = Cursors.Arrow;
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Layout.LinkAt(Story, page, x, y) is not null) Cursor = Cursors.Hand;
        else Cursor = Cursors.IBeam;
        ToolTip = Layout.LinkAt(Story, page, x, y) is { } l ? Loc.F("{0}\nCtrl+щелчок — открыть ссылку", l.Url ?? "#" + l.Anchor) : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_resize is { } rz)
        {
            _resize = null;
            ReleaseMouseCapture();
            if (CanEdit() && Math.Abs(rz.NewW - rz.Seg.Width) > 0.5)
            {
                Editor.ResizeImage(rz.Pos, (long)Math.Round(rz.NewW * 12700));
                Editor.Select(new Selection(rz.Pos, rz.Pos with { Offset = rz.Pos.Offset + 1 }));
            }
            InvalidateVisual();
            return;
        }
        if (_dragging) { _dragging = false; ReleaseMouseCapture(); }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();
        if (Layout.Pages.Count == 0) return;
        var (page, x, y) = ToPage(e.GetPosition(this));
        var hit = Layout.Hit(Story, page, x, y);
        // щелчок вне выделения переносит курсор (как в Word); внутри — выделение сохраняется для команд меню
        if (hit is { } pos)
        {
            var s = Editor.Selection;
            bool inside = !s.IsEmpty && pos >= s.Start && pos <= s.End;
            if (!inside)
            {
                if (Layout.ImageAt(Story, page, x, y) is { } img)
                {
                    var at = new DocPos(img.Line.Para, img.Seg.Start);
                    Editor.Select(new Selection(at, at with { Offset = at.Offset + 1 }));
                }
                else Editor.Select(Selection.At(pos));
            }
        }
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        Zoom = Math.Round((Zoom + (e.Delta > 0 ? 0.1 : -0.1)) * 10) / 10;
        e.Handled = true;
    }

    /// <summary>Ctrl+щелчок по ссылке.</summary>
    public event EventHandler<LinkRef>? LinkClicked;

    // ── колонтитулы ──────────────────────────────────────────────────────────

    public void EnterHeaderFooter(HfKind kind, int page)
    {
        if (!CanEdit()) return;
        StoryPage = Math.Clamp(page, 0, Math.Max(0, Layout.Pages.Count - 1));
        Editor.OpenHeaderFooter(kind, StoryPage);
        _pageCache.Clear();
        Relayout();
        StoryChanged?.Invoke(this, EventArgs.Empty);
        ScrollToCaret();
    }

    public void ExitStory()
    {
        if (Editor.Story is null) return;
        Editor.EnterStory(null, Layout.Hit(null, StoryPage, Layout.Pages[StoryPage].Body.X, Layout.Pages[StoryPage].Body.Y));
        _pageCache.Clear();
        InvalidateVisual();
        StoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
