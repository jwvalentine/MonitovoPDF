using System.Globalization;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using ZXing;
using ZXing.Common;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Fills a template PDF by drawing text and images onto its pages at the positions its form
/// fields occupy, then removing the fields so the result is flat.
/// </summary>
/// <remarks>
/// The form fields are used only as a coordinate source. PDFsharp does not generate appearance
/// streams for filled fields (empira/PDFsharp issue 64, closed as wontfix), so a document whose
/// content lives in form field values renders blank in viewers that do not build appearances
/// themselves — including the print paths a label is most likely to take. Drawing into the page
/// content stream instead produces a document that renders identically everywhere.
/// </remarks>
public sealed class LabelRenderer(IOptions<RenderingOptions> options, ILogger<LabelRenderer> logger)
{
    private readonly RenderingOptions _options = options.Value;

    private sealed record Placement(PdfPage Page, XRect Bounds, double FontSize, XStringAlignment Alignment);

    /// <summary>
    /// Draws <paramref name="textValues"/> and <paramref name="imageValues"/> into the fields of
    /// the same name and returns the finished document.
    /// </summary>
    /// <exception cref="TemplateRenderException">The template or the requested fields are unusable.</exception>
    public byte[] Render(
        byte[] templateBytes,
        IReadOnlyDictionary<string, string> textValues,
        IReadOnlyDictionary<string, byte[]> imageValues,
        IReadOnlyDictionary<string, BarcodeContent>? barcodeValues = null)
    {
        barcodeValues ??= new Dictionary<string, BarcodeContent>();

        using var input = new MemoryStream(templateBytes, writable: false);

        PdfDocument document;
        try
        {
            document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Rejected a template that could not be parsed: {Reason}.", exception.GetType().Name);
            throw new TemplateRenderException("The template is not a readable PDF document.");
        }

        using (document)
        {
            if (document.PageCount > _options.MaxPages)
            {
                throw new TemplateRenderException(
                    $"The template has {document.PageCount} pages, which exceeds the limit of {_options.MaxPages}.");
            }

            var form = document.AcroForm
                ?? throw new TemplateRenderException("The template defines no form fields to fill.");

            var fields = FlattenFields(form);
            var widgetPages = MapWidgetsToPages(document);
            var formAppearance = form.Elements.GetString("/DA");

            // Resolve every requested field before drawing anything, so an unknown name fails the
            // whole request rather than leaving a half-populated label.
            var textPlacements = Resolve(textValues.Keys, fields, widgetPages, formAppearance);
            var imagePlacements = Resolve(imageValues.Keys, fields, widgetPages, formAppearance);
            var barcodePlacements = Resolve(barcodeValues.Keys, fields, widgetPages, formAppearance);

            var canvases = new Dictionary<PdfPage, XGraphics>();
            try
            {
                foreach (var (name, placements) in textPlacements)
                {
                    foreach (var placement in placements)
                        DrawText(CanvasFor(placement.Page), placement, textValues[name]);
                }

                foreach (var (name, placements) in imagePlacements)
                {
                    foreach (var placement in placements)
                        DrawImage(CanvasFor(placement.Page), placement, imageValues[name], name);
                }

                foreach (var (name, placements) in barcodePlacements)
                {
                    foreach (var placement in placements)
                        DrawBarcode(CanvasFor(placement.Page), placement, barcodeValues[name], name);
                }
            }
            finally
            {
                foreach (var canvas in canvases.Values)
                    canvas.Dispose();
            }

            RemoveFormFields(document, form);

            using var output = new MemoryStream();
            document.Save(output, closeStream: false);
            return output.ToArray();

            XGraphics CanvasFor(PdfPage page)
            {
                if (!canvases.TryGetValue(page, out var canvas))
                {
                    // Append so the drawn content sits on top of the template artwork.
                    canvas = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    canvases[page] = canvas;
                }

                return canvas;
            }
        }
    }

    private Dictionary<string, IReadOnlyList<Placement>> Resolve(
        IEnumerable<string> names,
        IReadOnlyDictionary<string, PdfAcroField> fields,
        IReadOnlyDictionary<PdfObjectID, PdfPage> widgetPages,
        string? formAppearance)
    {
        var resolved = new Dictionary<string, IReadOnlyList<Placement>>(StringComparer.Ordinal);
        var unusable = new List<string>();

        foreach (var name in names)
        {
            if (!fields.TryGetValue(name, out var field))
            {
                unusable.Add(name);
                continue;
            }

            var placements = PlacementsFor(field, widgetPages, formAppearance);
            if (placements.Count == 0)
            {
                unusable.Add(name);
                continue;
            }

            resolved[name] = placements;
        }

        if (unusable.Count > 0)
        {
            throw new TemplateRenderException(
                $"The template has no usable field named: {string.Join(", ", unusable.Order(StringComparer.Ordinal))}.");
        }

        return resolved;
    }

    private List<Placement> PlacementsFor(
        PdfAcroField field,
        IReadOnlyDictionary<PdfObjectID, PdfPage> widgetPages,
        string? formAppearance)
    {
        var fontSize = ReadFontSize(field, formAppearance);
        var alignment = field.Elements.GetInteger("/Q") switch
        {
            1 => XStringAlignment.Center,
            2 => XStringAlignment.Far,
            _ => XStringAlignment.Near
        };

        var placements = new List<Placement>();
        var kids = field.Elements.GetArray("/Kids");

        if (kids is not null && kids.Elements.Count > 0)
        {
            for (var i = 0; i < kids.Elements.Count; i++)
            {
                if (kids.Elements[i] is PdfReference { Value: PdfDictionary widget } reference)
                    Add(widget, reference.ObjectID);
            }
        }
        else if (field.Reference is not null)
        {
            // A field with no kids carries its own widget annotation.
            Add(field, field.Reference.ObjectID);
        }

        return placements;

        void Add(PdfDictionary widget, PdfObjectID objectId)
        {
            if (!widgetPages.TryGetValue(objectId, out var page))
                return;

            var bounds = ToCanvasRect(widget.Elements.GetRectangle("/Rect"), page);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            placements.Add(new Placement(page, bounds, fontSize, alignment));
        }
    }

    /// <summary>
    /// Reads the point size out of a default-appearance string such as "/Helv 9 Tf 0 g", falling
    /// back to the configured default when it is absent or set to auto-size.
    /// </summary>
    private double ReadFontSize(PdfAcroField field, string? formAppearance)
    {
        var appearance = field.Elements.GetString("/DA");
        if (string.IsNullOrWhiteSpace(appearance))
            appearance = formAppearance;

        if (string.IsNullOrWhiteSpace(appearance))
            return _options.DefaultFontSizePoints;

        var tokens = appearance.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var operatorIndex = Array.IndexOf(tokens, "Tf");
        if (operatorIndex < 1)
            return _options.DefaultFontSizePoints;

        var parsed = double.TryParse(
            tokens[operatorIndex - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var size);

        // A size of zero means auto-size, which shrink-to-fit already covers.
        return parsed && size > 0 ? size : _options.DefaultFontSizePoints;
    }

    /// <summary>
    /// Converts a PDF rectangle, whose origin is the bottom-left of the page, into the top-left
    /// origin an <see cref="XGraphics"/> canvas draws in.
    /// </summary>
    private static XRect ToCanvasRect(PdfRectangle rect, PdfPage page)
    {
        var left = Math.Min(rect.X1, rect.X2);
        var right = Math.Max(rect.X1, rect.X2);
        var bottom = Math.Min(rect.Y1, rect.Y2);
        var top = Math.Max(rect.Y1, rect.Y2);

        return new XRect(left, page.Height.Point - top, right - left, top - bottom);
    }

    private void DrawText(XGraphics canvas, Placement placement, string value)
    {
        if (value.Length == 0)
            return;

        var font = FitToWidth(canvas, value, placement);
        var format = new XStringFormat { Alignment = placement.Alignment, LineAlignment = XLineAlignment.Center };

        canvas.DrawString(value, font, XBrushes.Black, placement.Bounds, format);
    }

    /// <summary>
    /// Shrinks the font until the value fits the field width, down to the configured floor. A
    /// label that silently clips a part number is worse than one set slightly smaller.
    /// </summary>
    private XFont FitToWidth(XGraphics canvas, string value, Placement placement)
    {
        var size = placement.FontSize;

        while (size > _options.MinimumFontSizePoints)
        {
            var candidate = new XFont(_options.DefaultFontFamily, size);
            if (canvas.MeasureString(value, candidate).Width <= placement.Bounds.Width)
                return candidate;

            size -= 0.5;
        }

        return new XFont(_options.DefaultFontFamily, _options.MinimumFontSizePoints);
    }

    private void DrawImage(XGraphics canvas, Placement placement, byte[] value, string fieldName)
    {
        using var stream = new MemoryStream(value, writable: false);

        XImage image;
        try
        {
            image = XImage.FromStream(stream);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Rejected an image for field {FieldName}: {Reason}.", fieldName, exception.GetType().Name);
            throw new TemplateRenderException($"The image supplied for field '{fieldName}' could not be decoded.");
        }

        using (image)
        {
            // Fit within the field and centre it, preserving the aspect ratio. Pixel dimensions are
            // used rather than point dimensions so that a DPI value embedded in the image cannot
            // change how large it lands on the label.
            var scale = Math.Min(
                placement.Bounds.Width / image.PixelWidth,
                placement.Bounds.Height / image.PixelHeight);

            var width = image.PixelWidth * scale;
            var height = image.PixelHeight * scale;
            var x = placement.Bounds.X + ((placement.Bounds.Width - width) / 2);
            var y = placement.Bounds.Y + ((placement.Bounds.Height - height) / 2);

            canvas.DrawImage(image, x, y, width, height);
        }
    }

    /// <summary>
    /// Draws a barcode as vector rectangles rather than as a rasterised image, so the bar edges
    /// stay exact at any print resolution. A scaled bitmap can blur enough at a label printer's
    /// resolution to cost a scan.
    /// </summary>
    private void DrawBarcode(XGraphics canvas, Placement placement, BarcodeContent content, string fieldName)
    {
        BitMatrix matrix;
        try
        {
            var writer = new BarcodeWriterGeneric
            {
                Format = content.Symbology.Format,
                Options = new EncodingOptions
                {
                    // Zero asks for the symbol's natural module size rather than a scaled
                    // bitmap, which keeps the drawing compact and the modules exact.
                    Width = 0,
                    Height = 0,
                    Margin = content.Symbology.QuietZoneModules,
                    PureBarcode = true,
                },
            };

            matrix = writer.Encode(content.Value);
        }
        catch (Exception exception)
        {
            // The reason is returned to the caller, who supplied the value, but never logged.
            logger.LogWarning("Rejected a {Symbology} barcode for field {FieldName}: {Reason}.",
                content.Symbology.Name, fieldName, exception.GetType().Name);

            throw new TemplateRenderException(
                $"The value for field '{fieldName}' is not valid for {content.Symbology.Name}: {exception.Message}");
        }

        var bounds = placement.Bounds;

        if (matrix.Height == 1)
        {
            // A linear symbology carries no information vertically, so the bars fill the field.
            var moduleWidth = bounds.Width / matrix.Width;

            foreach (var (start, length) in RunsIn(matrix, row: 0))
                canvas.DrawRectangle(XBrushes.Black, bounds.X + (start * moduleWidth), bounds.Y, length * moduleWidth, bounds.Height);

            return;
        }

        // A 2D symbology must keep its aspect ratio, so it is fitted and centred instead.
        var module = Math.Min(bounds.Width / matrix.Width, bounds.Height / matrix.Height);
        var originX = bounds.X + ((bounds.Width - (matrix.Width * module)) / 2);
        var originY = bounds.Y + ((bounds.Height - (matrix.Height * module)) / 2);

        for (var y = 0; y < matrix.Height; y++)
        {
            foreach (var (start, length) in RunsIn(matrix, y))
            {
                // A hair of overlap stops rasterisers drawing seams between adjacent modules.
                canvas.DrawRectangle(XBrushes.Black,
                    originX + (start * module), originY + (y * module),
                    length * module, module * 1.02);
            }
        }
    }

    /// <summary>Yields the runs of set modules in one row, as (start, length) pairs.</summary>
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

    private static Dictionary<string, PdfAcroField> FlattenFields(PdfAcroForm form)
    {
        var map = new Dictionary<string, PdfAcroField>(StringComparer.Ordinal);
        Walk(form.Fields, prefix: "");
        return map;

        void Walk(PdfAcroField.PdfAcroFieldCollection fields, string prefix)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (field is null)
                    continue;

                var name = prefix.Length == 0 ? field.Name : $"{prefix}.{field.Name}";
                map[name] = field;

                // Kids are either child fields, which are named, or bare widget annotations,
                // which are not and are handled as placements of this field.
                if (field.HasKids && field.Fields.Count > 0)
                    Walk(field.Fields, name);
            }
        }
    }

    private static Dictionary<PdfObjectID, PdfPage> MapWidgetsToPages(PdfDocument document)
    {
        var map = new Dictionary<PdfObjectID, PdfPage>();

        foreach (var page in document.Pages)
        {
            var annotations = page.Elements.GetArray("/Annots");
            if (annotations is null)
                continue;

            for (var i = 0; i < annotations.Elements.Count; i++)
            {
                if (annotations.Elements[i] is PdfReference reference)
                    map[reference.ObjectID] = page;
            }
        }

        return map;
    }

    /// <summary>
    /// Strips the interactive form so the output is flat: the drawn content is the only content,
    /// and nothing depends on a viewer generating field appearances.
    /// </summary>
    private static void RemoveFormFields(PdfDocument document, PdfAcroForm form)
    {
        foreach (var page in document.Pages)
        {
            var annotations = page.Elements.GetArray("/Annots");
            if (annotations is null)
                continue;

            for (var i = annotations.Elements.Count - 1; i >= 0; i--)
            {
                var annotation = annotations.Elements[i] switch
                {
                    PdfReference { Value: PdfDictionary dictionary } => dictionary,
                    PdfDictionary dictionary => dictionary,
                    _ => null
                };

                if (annotation?.Elements.GetName("/Subtype") == "/Widget")
                    annotations.Elements.RemoveAt(i);
            }
        }

        // PDFsharp does not expose the document catalog, so the form is emptied rather than
        // unlinked. With no field entries and no widget annotations left, nothing interactive
        // survives into the output and no viewer is asked to build an appearance.
        form.Elements["/Fields"] = new PdfArray(document);
        form.Elements.Remove("/NeedAppearances");
    }
}
