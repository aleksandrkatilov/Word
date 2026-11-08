using System.Text;

namespace Pero.Services;

/// <summary>
/// Подсчёт слов для строки состояния (UI-06) обходом модели документа движка.
/// Модель читается через dynamic, чтобы смена внутренних типов движка не ломала сборку:
/// при ошибке возвращается null, и строка состояния показывает «—».
/// </summary>
public static class WordCounter
{
    public static int? Count(object? document)
    {
        if (document is null) return null;
        try
        {
            int words = 0;
            foreach (dynamic section in ((dynamic)document).Sections)
                words += CountBlocks(section.Blocks);
            return words;
        }
        catch
        {
            return null;
        }
    }

    private static int CountBlocks(dynamic blocks)
    {
        int words = 0;
        foreach (dynamic block in blocks)
        {
            string type = ((object)block).GetType().Name;
            if (type == "ParagraphAdv")
            {
                var sb = new StringBuilder();
                foreach (dynamic inline in block.Inlines)
                    if (((object)inline).GetType().Name == "SpanAdv") sb.Append((string)inline.Text);
                words += CountWords(sb.ToString());
            }
            else if (type == "TableAdv")
            {
                foreach (dynamic row in block.Rows)
                    foreach (dynamic cell in row.Cells)
                        words += CountBlocks(cell.Blocks);
            }
        }
        return words;
    }

    public static int CountWords(string text)
    {
        int n = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            // дефис и апостроф продолжают слово, но не начинают его
            bool letter = char.IsLetterOrDigit(c) || (inWord && c is '-' or '\'' or '’');
            if (letter && !inWord) n++;
            inWord = letter;
        }
        return n;
    }
}
