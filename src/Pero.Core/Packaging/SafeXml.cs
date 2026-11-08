using System.Xml;

namespace Pero.Core.Packaging;

/// <summary>XML-чтение без DTD, внешних сущностей и сетевых обращений (SEC-01, DOCX-02).</summary>
public static class SafeXml
{
    public static XmlReaderSettings Settings(long maxChars = 512L * 1024 * 1024) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = maxChars,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    public static XmlReader Create(Stream stream, long maxChars = 512L * 1024 * 1024) =>
        XmlReader.Create(stream, Settings(maxChars));
}
