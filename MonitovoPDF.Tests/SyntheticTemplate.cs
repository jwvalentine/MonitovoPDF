using System.Text;

namespace MonitovoPDF.Tests;

/// <summary>
/// Builds minimal, synthetic template PDFs in code so the test suite carries no binary fixtures
/// and no document that could contain real data.
/// </summary>
internal static class SyntheticTemplate
{
    internal sealed record Field(string Name, int Left, int Bottom, int Right, int Top);

    /// <summary>
    /// Produces a template whose fields ask for a named font, the way a real authoring tool does:
    /// the default appearance references a resource, and the resource resolves to a base font.
    /// </summary>
    /// <param name="baseFont">The <c>/BaseFont</c> value, such as "Helvetica" or "ABCDEF+Calibri".</param>
    public static byte[] WithFontNamed(string baseFont, params Field[] fields)
    {
        // The font object follows the fields, so its number is known once they are counted.
        var fontNumber = FirstFieldNumber + fields.Length;

        var bodies = Bodies(fields, "/F1", $"/DR << /Font << /F1 {fontNumber} 0 R >> >>");
        bodies.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} >>");

        return Assemble(bodies);
    }

    /// <summary>
    /// Produces a single-page PDF carrying an AcroForm with one text field per entry in
    /// <paramref name="fields"/>, each field being its own widget annotation on the page.
    /// </summary>
    /// <remarks>
    /// The default appearance names a resource the template does not define, which is the case
    /// where the renderer has nothing to resolve and falls back to its configured font.
    /// </remarks>
    public static byte[] WithFields(params Field[] fields) =>
        Assemble(Bodies(fields, "/Helv", extraFormEntries: ""));

    /// <summary>
    /// Produces a template where one field is shown in several places, as a form does when the
    /// same value belongs in more than one spot: a single field with several widget annotations.
    /// </summary>
    public static byte[] WithSharedField(
        string name, params (int Left, int Bottom, int Right, int Top)[] rectangles)
    {
        const int fieldNumber = FirstFieldNumber;
        var firstWidget = fieldNumber + 1;

        var widgetReferences = string.Join(
            " ", rectangles.Select((_, index) => $"{firstWidget + index} 0 R"));

        var bodies = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{fieldNumber} 0 R] /DA (/Helv 9 Tf 0 g) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] "
                + $"/Resources << /Font << >> >> /Annots [{widgetReferences}] >>",
            $"<< /FT /Tx /T ({name}) /DA (/Helv 9 Tf 0 g) /Kids [{widgetReferences}] >>",
        };

        bodies.AddRange(rectangles.Select(rectangle =>
            $"<< /Type /Annot /Subtype /Widget /Parent {fieldNumber} 0 R "
            + $"/Rect [{rectangle.Left} {rectangle.Bottom} {rectangle.Right} {rectangle.Top}] /F 4 >>"));

        return Assemble(bodies);
    }

    /// <summary>Produces a template with one field carrying the multiline flag.</summary>
    public static byte[] WithMultilineField(Field field)
    {
        var bodies = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{FirstFieldNumber} 0 R] /DA (/Helv 9 Tf 0 g) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] "
                + $"/Resources << /Font << >> >> /Annots [{FirstFieldNumber} 0 R] >>",
            // Bit 13 of the field flags marks a text field as holding more than one line.
            "<< /Type /Annot /Subtype /Widget /FT /Tx /Ff 4096 "
                + $"/T ({field.Name}) /Rect [{field.Left} {field.Bottom} {field.Right} {field.Top}] "
                + "/DA (/Helv 9 Tf 0 g) /F 4 >>",
        };

        return Assemble(bodies);
    }

    /// <summary>Objects 1-3 are the catalog, page tree and page; the fields follow.</summary>
    private const int FirstFieldNumber = 4;

    private static List<string> Bodies(Field[] fields, string resource, string extraFormEntries)
    {
        const int pageWidth = 200;
        const int pageHeight = 100;

        var fieldReferences = string.Join(
            " ", fields.Select((_, index) => $"{FirstFieldNumber + index} 0 R"));

        var appearance = $"{resource} 9 Tf 0 g";

        var bodies = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{fieldReferences}] "
                + $"/DA ({appearance}) {extraFormEntries} >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] "
                + $"/Resources << /Font << >> >> /Annots [{fieldReferences}] >>"
        };

        bodies.AddRange(fields.Select(field =>
            "<< /Type /Annot /Subtype /Widget /FT /Tx "
            + $"/T ({field.Name}) /Rect [{field.Left} {field.Bottom} {field.Right} {field.Top}] "
            + $"/DA ({appearance}) /F 4 >>"));

        return bodies;
    }

    /// <summary>Serialises the objects with a correct cross-reference table.</summary>
    private static byte[] Assemble(IReadOnlyList<string> bodies)
    {
        var document = new StringBuilder();
        document.Append("%PDF-1.7\n");

        var offsets = new List<int>(bodies.Count);
        for (var i = 0; i < bodies.Count; i++)
        {
            offsets.Add(document.Length);
            document.Append($"{i + 1} 0 obj\n{bodies[i]}\nendobj\n");
        }

        var startXref = document.Length;
        document.Append($"xref\n0 {bodies.Count + 1}\n");
        document.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            document.Append($"{offset:D10} 00000 n \n");

        document.Append($"trailer\n<< /Size {bodies.Count + 1} /Root 1 0 R >>\n");
        document.Append($"startxref\n{startXref}\n%%EOF\n");

        // ASCII keeps one character to one byte, so the offsets recorded above stay correct.
        return Encoding.ASCII.GetBytes(document.ToString());
    }

    /// <summary>A 1x1 pixel PNG, used wherever a test needs a decodable image.</summary>
    public static byte[] SinglePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
