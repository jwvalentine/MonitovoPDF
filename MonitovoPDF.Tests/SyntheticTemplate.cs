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
    /// Produces a single-page PDF carrying an AcroForm with one text field per entry in
    /// <paramref name="fields"/>, each field being its own widget annotation on the page.
    /// </summary>
    public static byte[] WithFields(params Field[] fields)
    {
        const int pageWidth = 200;
        const int pageHeight = 100;

        // Objects 1-3 are the catalog, page tree and page; the fields follow from object 4.
        var firstFieldNumber = 4;
        var fieldReferences = string.Join(
            " ", fields.Select((_, index) => $"{firstFieldNumber + index} 0 R"));

        var bodies = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{fieldReferences}] /DA (/Helv 9 Tf 0 g) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] "
                + $"/Resources << /Font << >> >> /Annots [{fieldReferences}] >>"
        };

        bodies.AddRange(fields.Select(field =>
            "<< /Type /Annot /Subtype /Widget /FT /Tx "
            + $"/T ({field.Name}) /Rect [{field.Left} {field.Bottom} {field.Right} {field.Top}] "
            + "/DA (/Helv 9 Tf 0 g) /F 4 >>"));

        return Assemble(bodies);
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
