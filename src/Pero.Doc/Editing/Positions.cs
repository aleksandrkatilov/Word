using Pero.Doc.Model;

namespace Pero.Doc.Editing;

/// <summary>Позиция: номер абзаца в сквозном порядке документа (включая ячейки таблиц) и смещение в нём.</summary>
public readonly record struct DocPos(int Para, int Offset) : IComparable<DocPos>
{
    public int CompareTo(DocPos o) => Para != o.Para ? Para.CompareTo(o.Para) : Offset.CompareTo(o.Offset);
    public static bool operator <(DocPos a, DocPos b) => a.CompareTo(b) < 0;
    public static bool operator >(DocPos a, DocPos b) => a.CompareTo(b) > 0;
    public static bool operator <=(DocPos a, DocPos b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DocPos a, DocPos b) => a.CompareTo(b) >= 0;
}

/// <summary>Выделение: якорь и активный конец (где курсор).</summary>
public readonly record struct Selection(DocPos Anchor, DocPos Active)
{
    public DocPos Start => Anchor <= Active ? Anchor : Active;
    public DocPos End => Anchor <= Active ? Active : Anchor;
    public bool IsEmpty => Anchor == Active;
    public static Selection At(DocPos p) => new(p, p);
}

/// <summary>Где находится абзац: список-контейнер, индекс в нём и ячейка таблицы (если есть).</summary>
public sealed record ParaLocation(Paragraph Paragraph, List<Block> Container, int Index, TableCell? Cell, Table? Table, int Row, int CellIndex, int Depth);

/// <summary>Сквозной перечень абзацев документа; строится заново после каждой правки.</summary>
public sealed class ParaIndex
{
    public List<ParaLocation> Items { get; } = [];

    public ParaIndex(Document doc) => Walk(doc.Body, null, null, -1, -1, 0);

    private void Walk(List<Block> blocks, TableCell? cell, Table? table, int row, int ci, int depth)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p) Items.Add(new ParaLocation(p, blocks, i, cell, table, row, ci, depth));
            else if (blocks[i] is Table t)
                for (int r = 0; r < t.Rows.Count; r++)
                    for (int c = 0; c < t.Rows[r].Cells.Count; c++)
                        Walk(t.Rows[r].Cells[c].Blocks, t.Rows[r].Cells[c], t, r, c, depth + 1);
        }
    }

    public int Count => Items.Count;
    public ParaLocation this[int i] => Items[i];
    public int IndexOf(Paragraph p) => Items.FindIndex(l => ReferenceEquals(l.Paragraph, p));

    public DocPos Clamp(DocPos p)
    {
        int para = Math.Clamp(p.Para, 0, Count - 1);
        return new DocPos(para, Math.Clamp(p.Offset, 0, Items[para].Paragraph.Length));
    }

    public DocPos DocumentStart => new(0, 0);
    public DocPos DocumentEnd => new(Count - 1, Items[^1].Paragraph.Length);

    /// <summary>Абзацы в одном контейнере (одна ячейка или тело).</summary>
    public bool SameContainer(int a, int b) => ReferenceEquals(Items[a].Container, Items[b].Container);
}
