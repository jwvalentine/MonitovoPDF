using System.Globalization;
using System.Text;
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
internal sealed class LabelRenderer(RenderingOptions? options = null)
{
    private readonly RenderingOptions _options = options ?? new RenderingOptions();

    private sealed record Placement(
        PdfPage Page, XRect Bounds, string FontFamily, double FontSize, XStringAlignment Alignment,
        bool IsMultiline);

    /// <summary>Families already found to be unavailable, so each is only attempted once.</summary>
    private readonly HashSet<string> _unavailableFamilies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The base-14 PostScript names, mapped to the families a host is likely to actually have.
    /// </summary>
    /// <remarks>
    /// These fourteen are never embedded in a PDF; the specification expects the consumer to
    /// substitute, which is exactly what a viewer does. Without this every template written by
    /// LibreOffice or Acrobat would ask for "Helvetica", find nothing, and warn about a
    /// substitution that is entirely normal.
    /// </remarks>
    private static readonly Dictionary<string, string> BaseFontAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = "Arial",
        ["Times"] = "Times New Roman",
        ["Times-Roman"] = "Times New Roman",
        ["Courier"] = "Courier New",
    };

    /// <summary>
    /// Draws <paramref name="textValues"/> and <paramref name="imageValues"/> into the fields of
    /// the same name and returns the finished document.
    /// </summary>
    /// <exception cref="TemplateRenderException">The template or the requested fields are unusable.</exception>
    public (byte[] Pdf, IReadOnlyList<string> Unmatched, IReadOnlyList<ImageSlotReference> UnmatchedImages) Render(
        byte[] templateBytes,
        IReadOnlyDictionary<string, TextContent> textValues,
        IReadOnlyDictionary<string, byte[]> imageValues,
        IReadOnlyDictionary<string, BarcodeContent>? barcodeValues = null,
        IReadOnlyDictionary<(int Page, int Index), ImageSlotContent>? slotValues = null)
    {
        barcodeValues ??= new Dictionary<string, BarcodeContent>();
        slotValues ??= new Dictionary<(int, int), ImageSlotContent>();

        using var input = new MemoryStream(templateBytes, writable: false);

        PdfDocument document;
        try
        {
            document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        }
        catch (Exception exception)
        {
            throw new TemplateRenderException("The template is not a readable PDF document.", exception);
        }

        using (document)
        {
            if (document.PageCount > _options.MaxPages)
            {
                throw new TemplateRenderException(
                    $"The template has {document.PageCount} pages, which exceeds the limit of {_options.MaxPages}.");
            }

            var addressesFields =
                textValues.Count > 0 || imageValues.Count > 0 || barcodeValues.Count > 0;

            // A template whose placeholders are all images has no form, and that is only a problem
            // for a caller who asked for a field by name.
            var form = FormOf(document);
            if (form is null && addressesFields)
                throw new TemplateRenderException("The template defines no form fields to fill.");

            var unmatched = new List<string>();
            var unmatchedImages = new List<ImageSlotReference>();

            var textPlacements = new Dictionary<string, IReadOnlyList<Placement>>(StringComparer.Ordinal);
            var imagePlacements = new Dictionary<string, IReadOnlyList<Placement>>(StringComparer.Ordinal);
            var barcodePlacements = new Dictionary<string, IReadOnlyList<Placement>>(StringComparer.Ordinal);

            if (form is not null)
            {
                var fields = FlattenFields(form);
                var widgetPages = MapWidgetsToPages(document);
                var formAppearance = form.Elements.GetString("/DA");

                // Resolve every requested field before drawing anything, so an unknown name fails
                // the whole request rather than leaving a half-populated document.
                textPlacements = Resolve(textValues.Keys, fields, widgetPages, form, formAppearance, unmatched);
                imagePlacements = Resolve(imageValues.Keys, fields, widgetPages, form, formAppearance, unmatched);
                barcodePlacements = Resolve(barcodeValues.Keys, fields, widgetPages, form, formAppearance, unmatched);
            }

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

            // Placeholder replacement happens after drawing, and touches only the resource
            // dictionary: the page's own instructions decide where the replacement lands.
            ReplaceImageSlots(document, slotValues, unmatchedImages);

            if (form is not null)
                RemoveFormFields(document, form);

            using var output = new MemoryStream();
            document.Save(output, closeStream: false);
            return (output.ToArray(), unmatched, unmatchedImages);

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
        IReadOnlyDictionary<string, List<PdfAcroField>> fields,
        IReadOnlyDictionary<PdfObjectID, PdfPage> widgetPages,
        PdfAcroForm form,
        string? formAppearance,
        List<string> unmatched)
    {
        var resolved = new Dictionary<string, IReadOnlyList<Placement>>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            var placements = new List<Placement>();

            if (fields.TryGetValue(name, out var sharing))
            {
                // Every field of this name contributes, so a value reaches all of them.
                foreach (var field in sharing)
                    placements.AddRange(PlacementsFor(field, widgetPages, form, formAppearance));
            }

            if (placements.Count == 0)
            {
                unmatched.Add(name);
                continue;
            }

            resolved[name] = placements;
        }

        if (unmatched.Count > 0 && _options.OnMissingField == MissingFieldBehaviour.Throw)
        {
            throw new TemplateRenderException(
                $"The template has no usable field named: {string.Join(", ", unmatched.Order(StringComparer.Ordinal))}. "
                + "Set RenderingOptions.OnMissingField to Ignore to draw what does match instead.");
        }

        return resolved;
    }

    private List<Placement> PlacementsFor(
        PdfAcroField field,
        IReadOnlyDictionary<PdfObjectID, PdfPage> widgetPages,
        PdfAcroForm form,
        string? formAppearance)
    {
        var (family, fontSize) = ReadAppearance(field, form, formAppearance);
        var multiline = (field.Flags & PdfAcroFieldFlags.Multiline) != 0;
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

            placements.Add(new Placement(page, bounds, family, fontSize, alignment, multiline));
        }
    }

    /// <summary>
    /// Reads the font a field asks for out of its default-appearance string, which looks like
    /// "/Helv 9 Tf 0 g": a resource name, a size, and the Tf operator.
    /// </summary>
    /// <remarks>
    /// The resource name is a key into a font dictionary, not a family name, so it has to be
    /// looked up to find what the template actually wants. Honouring it matters because the
    /// template is the designer's intent — a form laid out for Helvetica and drawn in something
    /// wider will wrap or shrink where the designer expected it to fit.
    /// </remarks>
    private (string Family, double Size) ReadAppearance(
        PdfAcroField field, PdfAcroForm form, string? formAppearance)
    {
        var (family, size) = ReadFieldAppearance(field, form, formAppearance);

        return (family ?? _options.DefaultFontFamily, size ?? _options.DefaultFontSizePoints);
    }

    /// <summary>
    /// Reads what a field asks for without substituting anything, so a caller inspecting a
    /// template can tell "asks for nothing" apart from "asks for the default".
    /// </summary>
    internal static (string? Family, double? Size) ReadFieldAppearance(
        PdfAcroField field, PdfAcroForm form, string? formAppearance)
    {
        var appearance = field.Elements.GetString("/DA");
        if (string.IsNullOrWhiteSpace(appearance))
            appearance = formAppearance;

        if (string.IsNullOrWhiteSpace(appearance))
            return (null, null);

        var tokens = appearance.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var operatorIndex = Array.IndexOf(tokens, "Tf");
        if (operatorIndex < 1)
            return (null, null);

        var parsed = double.TryParse(
            tokens[operatorIndex - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var size);

        // A size of zero means auto-size, which shrink-to-fit already covers.
        double? resolvedSize = parsed && size > 0 ? size : null;

        var resource = operatorIndex >= 2 ? tokens[operatorIndex - 2] : null;

        return (ResolveFamily(resource, field, form), resolvedSize);
    }

    /// <summary>
    /// Turns a default-appearance resource name such as "/Helv" into a font family, by looking it
    /// up in the default resources the field or the form carries.
    /// </summary>
    private static string? ResolveFamily(string? resource, PdfAcroField field, PdfAcroForm form)
    {
        if (string.IsNullOrWhiteSpace(resource) || resource[0] != '/')
            return null;

        // A field may carry its own resources; otherwise the form's apply.
        var fonts = Resources(field) ?? Resources(form);
        var font = fonts?.Elements.GetDictionary(resource);

        var baseFont = font?.Elements.GetName("/BaseFont");

        return string.IsNullOrWhiteSpace(baseFont) ? null : NormaliseBaseFont(baseFont);

        static PdfDictionary? Resources(PdfDictionary source) =>
            source.Elements.GetDictionary("/DR")?.Elements.GetDictionary("/Font");
    }

    /// <summary>
    /// Reduces a PDF <c>/BaseFont</c> name to the font family to ask a resolver for.
    /// </summary>
    /// <remarks>
    /// A base font name carries more than a family. "ABCDEF+Arial-Bold" is a six-letter subset
    /// tag, the family, and a style — and the renderer never asks for a styled face, so only the
    /// middle part is wanted. The base-14 names are then mapped to something a host is likely to
    /// have, because those are defined to be substituted rather than embedded.
    /// </remarks>
    internal static string NormaliseBaseFont(string baseFont)
    {
        var name = baseFont.TrimStart('/');

        // A subset tag is exactly six uppercase letters followed by a plus.
        if (name.Length > 7 && name[6] == '+')
            name = name[7..];

        // Split off a style suffix, unless the whole name is one the alias table knows.
        var dash = name.IndexOf('-');
        if (dash > 0 && !BaseFontAliases.ContainsKey(name))
            name = name[..dash];

        return BaseFontAliases.GetValueOrDefault(name, name);
    }

    /// <summary>
    /// Builds a font, falling back to the configured default when the template asks for one the
    /// host does not have. Each missing family is attempted once and then remembered, because the
    /// underlying failure is an exception and a label may have many fields.
    /// </summary>
    private XFont CreateFont(string family, double size)
    {
        if (!_unavailableFamilies.Contains(family))
        {
            try
            {
                return new XFont(family, size);
            }
            catch (Exception)
            {
                _unavailableFamilies.Add(family);
            }
        }

        return new XFont(_options.DefaultFontFamily, size);
    }

    /// <summary>
    /// Converts a PDF rectangle, whose origin is the bottom-left of the page, into the top-left
    /// origin an <see cref="XGraphics"/> canvas draws in.
    /// </summary>
    internal static XRect ToCanvasRect(PdfRectangle rect, PdfPage page)
    {
        var left = Math.Min(rect.X1, rect.X2);
        var right = Math.Max(rect.X1, rect.X2);
        var bottom = Math.Min(rect.Y1, rect.Y2);
        var top = Math.Max(rect.Y1, rect.Y2);

        return new XRect(left, page.Height.Point - top, right - left, top - bottom);
    }

    private void DrawText(XGraphics canvas, Placement placement, TextContent content)
    {
        var value = content.Value;
        if (value.Length == 0)
            return;

        var overrides = content.Options;
        var family = overrides?.FontFamily ?? placement.FontFamily;
        var size = overrides?.FontSizePoints ?? placement.FontSize;
        var alignment = overrides?.Alignment is { } requested ? Convert(requested) : placement.Alignment;

        // A value spanning lines has to wrap whatever the field says, or the extra lines are lost.
        var multiline = overrides?.Multiline ?? (placement.IsMultiline || value.Contains('\n'));

        if (multiline)
        {
            DrawWrapped(canvas, placement, value, family, size, alignment);
            return;
        }

        var font = FitToWidth(canvas, value, family, size, placement.Bounds.Width);
        var format = new XStringFormat { Alignment = alignment, LineAlignment = XLineAlignment.Center };

        canvas.DrawString(value, font, XBrushes.Black, placement.Bounds, format);
    }

    private static XStringAlignment Convert(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Centre => XStringAlignment.Center,
        TextAlignment.Right => XStringAlignment.Far,
        _ => XStringAlignment.Near
    };

    /// <summary>
    /// Draws a value across as many lines as it needs, shrinking until the whole block fits the
    /// field rather than only its widest line.
    /// </summary>
    private void DrawWrapped(
        XGraphics canvas, Placement placement, string value, string family, double size, XStringAlignment alignment)
    {
        var bounds = placement.Bounds;
        var font = CreateFont(family, size);
        var lines = Wrap(canvas, value, font, bounds.Width);

        // Shrink on height as well as width: wrapping trades one for the other, so a block can fit
        // every line individually and still overflow the bottom of the field.
        while (size > _options.MinimumFontSizePoints
               && lines.Count * font.GetHeight() > bounds.Height)
        {
            size -= 0.5;
            font = CreateFont(family, size);
            lines = Wrap(canvas, value, font, bounds.Width);
        }

        var lineHeight = font.GetHeight();
        var block = Math.Min(lines.Count * lineHeight, bounds.Height);
        var y = bounds.Y + ((bounds.Height - block) / 2);

        var format = new XStringFormat { Alignment = alignment, LineAlignment = XLineAlignment.Near };

        foreach (var line in lines)
        {
            // Stop at the bottom edge rather than drawing outside the field.
            if (y + lineHeight > bounds.Y + bounds.Height + 0.01)
                break;

            canvas.DrawString(line, font, XBrushes.Black, new XRect(bounds.X, y, bounds.Width, lineHeight), format);
            y += lineHeight;
        }
    }

    /// <summary>Breaks a value into lines that fit a width, honouring the line breaks it already has.</summary>
    private static List<string> Wrap(XGraphics canvas, string value, XFont font, double width)
    {
        var lines = new List<string>();

        foreach (var paragraph in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = new StringBuilder();

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";

                if (line.Length > 0 && canvas.MeasureString(candidate, font).Width > width)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    /// <summary>
    /// Shrinks the font until the value fits the field width, down to the configured floor. A
    /// label that silently clips a part number is worse than one set slightly smaller.
    /// </summary>
    private XFont FitToWidth(XGraphics canvas, string value, string family, double size, double width)
    {
        while (size > _options.MinimumFontSizePoints)
        {
            var candidate = CreateFont(family, size);
            if (canvas.MeasureString(value, candidate).Width <= width)
                return candidate;

            size -= 0.5;
        }

        return CreateFont(family, _options.MinimumFontSizePoints);
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
            throw new TemplateRenderException(
                $"The image supplied for field '{fieldName}' could not be decoded.", exception);
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
            throw new TemplateRenderException(
                $"The value for field '{fieldName}' is not valid for {content.Symbology.Name}: {exception.Message}",
                exception);
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

    /// <summary>
    /// Returns the document's interactive form, or null when it has none.
    /// </summary>
    /// <remarks>
    /// The engine throws rather than returning null for a document without a form, and its type
    /// says the value is never null, so the absence has to be caught rather than tested for. A
    /// template whose placeholders are all images legitimately has no form.
    /// </remarks>
    internal static PdfAcroForm? FormOf(PdfDocument document)
    {
        try
        {
            return document.AcroForm;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Collects every field by name. A name maps to a list, not a single field, because a template
    /// may define the same name more than once and a value is meant to reach all of them.
    /// </summary>
    /// <remarks>
    /// Usually repetition is one field with several widget annotations, which is the ordinary way
    /// a form shows a value in more than one place. But some authoring tools emit genuinely
    /// separate field objects sharing a name, and keeping only one of those would silently drop a
    /// placement — the value would be drawn in one spot and quietly missing from another.
    /// </remarks>
    private static Dictionary<string, List<PdfAcroField>> FlattenFields(PdfAcroForm form)
    {
        var map = new Dictionary<string, List<PdfAcroField>>(StringComparer.Ordinal);
        Walk(form.Fields, prefix: "");
        return map;

        void Walk(PdfAcroField.PdfAcroFieldCollection fields, string prefix)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];

                // A kid with no name of its own is a widget annotation rather than a child
                // field; its placement belongs to the parent.
                if (field is null || string.IsNullOrEmpty(field.Name))
                    continue;

                var name = prefix.Length == 0 ? field.Name : $"{prefix}.{field.Name}";

                if (!map.TryGetValue(name, out var sharing))
                    map[name] = sharing = [];

                sharing.Add(field);

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
    /// Swaps replacements into the image placeholders that were addressed, leaving the rest of the
    /// page exactly as the template had it.
    /// </summary>
    /// <remarks>
    /// Placeholders that were not addressed are not touched at all — templates routinely carry
    /// fixed artwork in the slots a caller has no interest in, and rewriting those would be a
    /// change nobody asked for.
    /// </remarks>
    private void ReplaceImageSlots(
        PdfDocument document,
        IReadOnlyDictionary<(int Page, int Index), ImageSlotContent> slots,
        List<ImageSlotReference> unmatched)
    {
        if (slots.Count == 0)
            return;

        var byPage = slots.GroupBy(entry => entry.Key.Page).OrderBy(group => group.Key);

        foreach (var group in byPage)
        {
            var available = group.Key >= 1 && group.Key <= document.PageCount
                ? ImageSlots.On(document.Pages[group.Key - 1])
                : [];

            foreach (var (key, content) in group.OrderBy(entry => entry.Key.Index))
            {
                var slot = available.FirstOrDefault(candidate => candidate.Index == key.Index);
                var description = $"image {key.Index} on page {key.Page}";

                if (slot is null)
                {
                    if (_options.OnMissingField == MissingFieldBehaviour.Throw)
                    {
                        throw new TemplateRenderException(
                            $"The template has no {description}; that page has "
                            + $"{available.Count} image placeholder(s). Set RenderingOptions."
                            + "OnMissingField to Ignore to skip it instead.");
                    }

                    unmatched.Add(new ImageSlotReference(key.Page, key.Index));
                    continue;
                }

                var page = document.Pages[key.Page - 1];

                ImageSlots.Replace(page, slot, content.Barcode is { } barcode
                    ? BarcodeForm.Build(document, barcode, description)
                    : BuildImage(document, content.Image!, description));
            }
        }
    }

    /// <summary>Turns supplied bytes into an image object the page can draw.</summary>
    private static PdfDictionary BuildImage(PdfDocument document, byte[] bytes, string description)
    {
        using var stream = new MemoryStream(bytes, writable: false);

        XImage image;
        try
        {
            image = XImage.FromStream(stream);
        }
        catch (Exception exception)
        {
            throw new TemplateRenderException(
                $"The image supplied for {description} could not be decoded.", exception);
        }

        using (image)
        {
            // An image object standing in for an image object: the page keeps drawing an image,
            // exactly as the template intended, and the engine handles the encoding.
            var replacement = new PdfImage(document, image);
            document.Internals.AddObject(replacement);

            return replacement;
        }
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
