using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using ZXing;
using ZXing.Common;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Builds a barcode as a form XObject sized to the unit square, so it can stand in for an image
/// placeholder without losing its edges.
/// </summary>
/// <remarks>
/// <para>
/// A page draws an image by mapping the unit square onto wherever the image belongs, through the
/// transform in force at the drawing operator. A form XObject whose bounding box is that same unit
/// square is drawn by exactly the same operator under exactly the same transform, so swapping one
/// for the other inherits the placeholder's position and size precisely.
/// </para>
/// <para>
/// The gain is that the bars stay vector. Substituting a rasterised barcode would fix its
/// resolution at the moment of filling, and a barcode that is resampled on its way to a printer is
/// a barcode that stops scanning.
/// </para>
/// </remarks>
internal static class BarcodeForm
{
    /// <param name="document">The document the form is added to.</param>
    /// <param name="content">The barcode to encode and draw.</param>
    /// <param name="fieldDescription">How to name the placeholder if the value will not encode.</param>
    /// <param name="reservedBelow">
    /// The share of the box to leave empty at the bottom, for a readable value drawn there. The
    /// value itself is drawn onto the page rather than into this form, because a form built by
    /// hand has no font to draw with and a barcode caption in a substituted font is one that
    /// might not be there at all on the machine doing the printing.
    /// </param>
    public static PdfDictionary Build(
        PdfDocument document, BarcodeContent content, string fieldDescription, double reservedBelow)
    {
        var matrix = Encode(content, fieldDescription);
        var drawing = Draw(matrix, reservedBelow);

        var form = new PdfDictionary(document);
        form.Elements["/Type"] = new PdfName("/XObject");
        form.Elements["/Subtype"] = new PdfName("/Form");
        form.Elements["/FormType"] = new PdfInteger(1);
        form.Elements["/Resources"] = new PdfDictionary(document);

        // The unit square, so the placeholder's own transform decides the size on the page.
        form.Elements["/BBox"] = new PdfArray(document,
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(1), new PdfInteger(1));

        form.CreateStream(Encoding.ASCII.GetBytes(drawing));
        document.Internals.AddObject(form);

        return form;
    }

    private static BitMatrix Encode(BarcodeContent content, string fieldDescription)
    {
        try
        {
            var writer = new BarcodeWriterGeneric
            {
                Format = content.Symbology.Format,
                Options = new EncodingOptions
                {
                    Width = 0,
                    Height = 0,
                    Margin = content.Symbology.QuietZoneModules,
                    PureBarcode = true,
                },
            };

            return writer.Encode(content.Value);
        }
        catch (Exception exception)
        {
            throw new TemplateRenderException(
                $"The value for {fieldDescription} is not valid for {content.Symbology.Name}: {exception.Message}",
                exception);
        }
    }

    /// <summary>Writes the symbol as filled rectangles across the unit square.</summary>
    private static string Draw(BitMatrix matrix, double reservedBelow)
    {
        var drawing = new StringBuilder("0 g\n");

        var moduleWidth = 1d / matrix.Width;
        var rows = matrix.Height;

        // Whatever is set aside for a readable value comes out of the bottom, so the symbol keeps
        // the top of the box and the two do not overlap.
        var available = 1d - reservedBelow;

        // A linear symbology carries nothing vertically, so its bars run the full height they have.
        var moduleHeight = rows == 1 ? available : available / rows;

        for (var row = 0; row < rows; row++)
        {
            // Image space runs top down and form space runs bottom up, so the first row of the
            // symbol belongs at the top of the box. A 2D symbol drawn the other way up is mirrored.
            var y = rows == 1 ? reservedBelow : 1d - ((row + 1) * moduleHeight);

            foreach (var (start, length) in RunsIn(matrix, row))
            {
                drawing
                    .Append(Number(start * moduleWidth)).Append(' ')
                    .Append(Number(y)).Append(' ')
                    .Append(Number(length * moduleWidth)).Append(' ')
                    .Append(Number(moduleHeight)).Append(" re").Append('\n');
            }
        }

        return drawing.Append("f\n").ToString();
    }

    private static IEnumerable<(int Start, int Length)> RunsIn(BitMatrix matrix, int row)
    {
        var x = 0;
        while (x < matrix.Width)
        {
            if (!matrix[x, row])
            {
                x++;
                continue;
            }

            var start = x;
            while (x < matrix.Width && matrix[x, row])
                x++;

            yield return (start, x - start);
        }
    }

    private static string Number(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
