using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace MonitovoPDF.Rendering;

/// <summary>Reads a template's pages and fields without changing anything.</summary>
internal static class TemplateInspector
{
    public static TemplateInfo Inspect(byte[] templateBytes, RenderingOptions options)
    {
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
            if (document.PageCount > options.MaxPages)
            {
                throw new TemplateRenderException(
                    $"The template has {document.PageCount} pages, which exceeds the limit of {options.MaxPages}.");
            }

            var pages = new List<TemplatePage>();
            var numbers = new Dictionary<PdfPage, int>();

            for (var i = 0; i < document.PageCount; i++)
            {
                var page = document.Pages[i];
                numbers[page] = i + 1;

                pages.Add(new TemplatePage(
                    i + 1, page.Width.Point, page.Height.Point, page.Elements.GetInteger("/Rotate"),
                    DescribeImages(page, i + 1)));
            }

            var fields = LabelRenderer.FormOf(document) is { } form
                ? Describe(form, numbers)
                : [];

            return new TemplateInfo(pages, fields);
        }
    }

    /// <summary>
    /// Reports a page's image placeholders, and where the page draws each of them.
    /// </summary>
    /// <remarks>
    /// The positions come from reading the page's own drawing instructions, which is the only
    /// place they exist — an image object carries no position. Where that cannot be resolved the
    /// placeholder is still reported, with no placements, rather than being left out.
    /// </remarks>
    private static List<TemplateImage> DescribeImages(PdfPage page, int pageNumber)
    {
        var slots = ImageSlots.On(page);
        if (slots.Count == 0)
            return [];

        var drawn = PlacedXObjects.On(page);

        return [.. slots.Select(slot => new TemplateImage(
            slot.Index,
            slot.ResourceName,
            slot.PixelWidth,
            slot.PixelHeight,
            drawn.TryGetValue(slot.ResourceName, out var boxes)
                ? [.. boxes.Select(box => new FieldPlacement(
                    pageNumber, box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height))]
                : []))];
    }

    private static List<TemplateField> Describe(PdfAcroForm form, Dictionary<PdfPage, int> pageNumbers)
    {
        var widgetPages = new Dictionary<PdfObjectID, PdfPage>();
        foreach (var (page, _) in pageNumbers)
        {
            var annotations = page.Elements.GetArray("/Annots");
            for (var i = 0; i < (annotations?.Elements.Count ?? 0); i++)
            {
                if (annotations!.Elements[i] is PdfReference reference)
                    widgetPages[reference.ObjectID] = page;
            }
        }

        var formAppearance = form.Elements.GetString("/DA");
        var described = new List<TemplateField>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Walk(form.Fields, prefix: "");
        return described;

        void Walk(PdfAcroField.PdfAcroFieldCollection fields, string prefix)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];

                // A kid with no name of its own is a widget annotation, not a child field. The
                // PDF engine surfaces both through the same collection, and treating a widget as
                // a field would invent one that the template does not contain.
                if (field is null || string.IsNullOrEmpty(field.Name))
                    continue;

                var name = prefix.Length == 0 ? field.Name : $"{prefix}.{field.Name}";
                var hasChildFields = field.HasKids && field.Fields.Count > 0;
                var placements = PlacementsOf(field, widgetPages, pageNumbers);

                if (hasChildFields)
                    Walk(field.Fields, name);

                // A field whose kids are named child fields is a grouping rather than something
                // to fill, so it is only reported when it occupies space of its own. Kids that are
                // bare widget annotations are placements of this field, not fields themselves.
                if (placements.Count == 0 && hasChildFields)
                    continue;

                // A name repeated in the template is one field to a caller, so its placements are
                // gathered rather than reported as separate entries.
                var existing = described.FindIndex(candidate => candidate.Name == name);
                if (existing >= 0 && seen.Contains(name))
                {
                    described[existing] = described[existing] with
                    {
                        Placements = [.. described[existing].Placements, .. placements],
                    };

                    continue;
                }

                seen.Add(name);
                described.Add(Describe(field, form, name, placements, formAppearance));
            }
        }
    }

    private static TemplateField Describe(
        PdfAcroField field, PdfAcroForm form, string name,
        IReadOnlyList<FieldPlacement> placements, string? formAppearance)
    {
        var (family, size) = LabelRenderer.ReadFieldAppearance(field, form, formAppearance);

        var kind = field.Elements.GetName("/FT") switch
        {
            "/Tx" => TemplateFieldKind.Text,
            "/Btn" => TemplateFieldKind.Button,
            "/Ch" => TemplateFieldKind.Choice,
            "/Sig" => TemplateFieldKind.Signature,
            _ => TemplateFieldKind.Unknown
        };

        var alignment = field.Elements.GetInteger("/Q") switch
        {
            1 => TextAlignment.Centre,
            2 => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        return new TemplateField(
            name,
            kind,
            placements,
            family,
            size ?? 0,
            alignment,
            (field.Flags & PdfAcroFieldFlags.Multiline) != 0)
        {
            // What a field will accept is exactly what a caller needs before setting it, and is
            // the template's to say rather than the caller's to guess.
            Options = FieldAppearances.OptionsOf(field),
        };
    }

    private static List<FieldPlacement> PlacementsOf(
        PdfAcroField field,
        Dictionary<PdfObjectID, PdfPage> widgetPages,
        Dictionary<PdfPage, int> pageNumbers)
    {
        var placements = new List<FieldPlacement>();
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
            Add(field, field.Reference.ObjectID);
        }

        return placements;

        void Add(PdfDictionary widget, PdfObjectID objectId)
        {
            if (!widgetPages.TryGetValue(objectId, out var page))
                return;

            var bounds = LabelRenderer.ToCanvasRect(widget.Elements.GetRectangle("/Rect"), page);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            placements.Add(new FieldPlacement(
                pageNumbers[page], bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }
    }
}
