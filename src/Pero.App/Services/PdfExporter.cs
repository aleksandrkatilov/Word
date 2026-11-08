using Pero.Core.Files;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocToPDFConverter;
using Syncfusion.Pdf;
using DocIOFormat = Syncfusion.DocIO.FormatType;

namespace Pero.Services;

/// <summary>
/// Экспорт PDF (PRINT-01) через DocIO из DOCX, который записал редактор.
/// Ограничение этапа 0: PDF вёрстается движком DocIO, а не экранной разметкой SfRichTextBoxAdv,
/// поэтому PAGE-03 («печать и PDF используют ту же модель разметки») нужно проверить замером
/// на эталонах; альтернатива — печать через «Microsoft Print to PDF» из самого редактора.
/// </summary>
public static class PdfExporter
{
    public static void Export(string docxPath, string pdfPath)
    {
        WordDocument? word = null;
        PdfDocument? pdf = null;
        try
        {
            word = new WordDocument(docxPath, DocIOFormat.Docx);
            var converter = new DocToPDFConverter();
            converter.Settings.EmbedFonts = true;          // встраивать подмножества шрифтов с учётом прав
            converter.Settings.AutoDetectComplexScript = true;
            pdf = converter.ConvertToPDF(word);
            var doc = pdf;
            AtomicFileWriter.Write(pdfPath, s => doc.Save(s));
        }
        finally
        {
            pdf?.Close(true);
            word?.Close();
        }
    }
}
