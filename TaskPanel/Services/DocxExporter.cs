using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TaskPanel.Services;

/// <summary>
/// Writes a minimal, valid .docx (Word Open XML) report. Hand-built with
/// System.IO.Compression + System.Xml.Linq so the app doesn't need an
/// external document-generation package.
/// </summary>
public static class DocxExporter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Export(string filePath, IReadOnlyList<(string ListName, IReadOnlyList<string> Tasks)> groups, DateTime exportedAt)
    {
        var body = new XElement(W + "body");

        body.Add(Paragraph("Master Tasks — Archive Report", bold: true, sizeHalfPoints: 32));
        body.Add(Paragraph($"Exported {exportedAt:dddd, d MMMM yyyy 'at' h:mm tt}", italic: true, sizeHalfPoints: 20));
        body.Add(Paragraph(string.Empty));

        foreach (var (listName, tasks) in groups)
        {
            body.Add(Paragraph(listName, bold: true, sizeHalfPoints: 26));
            foreach (var task in tasks)
                body.Add(Paragraph("•  " + task, sizeHalfPoints: 22));
            body.Add(Paragraph(string.Empty));
        }

        // Minimal section properties (Letter page size) — required for a well-formed document.
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", 12240), new XAttribute(W + "h", 15840))));

        var documentXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName), body));

        if (File.Exists(filePath)) File.Delete(filePath);

        using var fileStream = new FileStream(filePath, FileMode.CreateNew);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create);

        WriteXml(zip, "[Content_Types].xml", ContentTypesXml());
        WriteXml(zip, "_rels/.rels", RelsXml());
        WriteXml(zip, "word/document.xml", documentXml);
    }

    private static XElement Paragraph(string text, bool bold = false, bool italic = false, int? sizeHalfPoints = null)
    {
        var runProps = new XElement(W + "rPr");
        if (bold) runProps.Add(new XElement(W + "b"));
        if (italic) runProps.Add(new XElement(W + "i"));
        if (sizeHalfPoints.HasValue) runProps.Add(new XElement(W + "sz", new XAttribute(W + "val", sizeHalfPoints.Value)));

        var run = new XElement(W + "r",
            runProps,
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));

        return new XElement(W + "p", run);
    }

    private static void WriteXml(ZipArchive zip, string entryName, XDocument doc)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
        });
        doc.Save(writer);
    }

    private static XDocument ContentTypesXml()
    {
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))));
    }

    private static XDocument RelsXml()
    {
        XNamespace r = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(r + "Relationships",
                new XElement(r + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "word/document.xml"))));
    }
}
