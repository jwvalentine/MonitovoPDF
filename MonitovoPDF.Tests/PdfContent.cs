using System.Text;
using PdfSharp.Pdf.IO;

namespace MonitovoPDF.Tests;

/// <summary>Reads the drawing operators out of a rendered PDF so tests can assert on them.</summary>
internal static class PdfContent
{
    public static string OfFirstPage(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        var builder = new StringBuilder();
        foreach (var content in document.Pages[0].Contents)
            builder.Append(Encoding.Latin1.GetString(content.Stream.UnfilteredValue));

        return builder.ToString();
    }
}
