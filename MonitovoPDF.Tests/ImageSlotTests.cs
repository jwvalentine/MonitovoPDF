using MonitovoPDF;
using MonitovoPDF.Rendering;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace MonitovoPDF.Tests;

/// <summary>
/// Covers replacing image placeholders addressed by position, for templates whose placeholders
/// are images rather than form fields.
/// </summary>
public class ImageSlotTests
{
    private static SyntheticTemplate.Slot[] FourSlots() =>
    [
        new("/Im0", 10, 150, 40, 40),
        new("/Im1", 60, 150, 40, 40),
        new("/Im2", 10, 100, 40, 40),
        new("/Im3", 60, 100, 40, 40),
    ];

    /// <summary>Reads a page's image XObjects back out, in the same order the library uses.</summary>
    private static List<(string Name, byte[] Data, int Width, int Height, string Subtype)> ImagesIn(
        byte[] pdf, int pageNumber = 1)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        var page = document.Pages[pageNumber - 1];
        var xobjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");

        var found = new List<(string, byte[], int, int, string)>();
        if (xobjects is null)
            return found;

        foreach (var key in xobjects.Elements.Keys.Order(StringComparer.Ordinal))
        {
            var item = xobjects.Elements[key];
            var dictionary = item is PdfReference { Value: PdfDictionary resolved } ? resolved : item as PdfDictionary;
            if (dictionary is null)
                continue;

            found.Add((
                key,
                dictionary.Stream?.Value ?? [],
                dictionary.Elements.GetInteger("/Width"),
                dictionary.Elements.GetInteger("/Height"),
                dictionary.Elements.GetName("/Subtype")));
        }

        return found;
    }

    [Fact]
    public void OnlyTheAddressedSlotsChange()
    {
        // Templates routinely carry fixed artwork in placeholders a caller has no interest in.
        // Rewriting those would be a change nobody asked for.
        var template = SyntheticTemplate.WithImageSlots(FourSlots());
        var before = ImagesIn(template);

        var pdf = MonitovoPdf.Fill(template, fill =>
        {
            fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng());
            fill.SetImageAt(1, 3, SyntheticTemplate.SinglePixelPng());
        });

        var after = ImagesIn(pdf);

        Assert.Equal(before.Count, after.Count);

        // Slots 2 and 4 must come through byte for byte.
        Assert.Equal(before[1].Data, after[1].Data);
        Assert.Equal(before[3].Data, after[3].Data);

        // Slots 1 and 3 must not.
        Assert.NotEqual(before[0].Data, after[0].Data);
        Assert.NotEqual(before[2].Data, after[2].Data);
    }

    [Fact]
    public void TheReplacementKeepsThePlaceholdersResourceNameAndDrawingInstruction()
    {
        // Geometry is inherited by leaving the content stream alone, so the resource name has to
        // survive: the page still says "draw /Im0 here", and only what /Im0 is has changed.
        var template = SyntheticTemplate.WithImageSlots(FourSlots());

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng()));

        Assert.Equal(
            ImagesIn(template).Select(image => image.Name),
            ImagesIn(pdf).Select(image => image.Name));

        Assert.Contains("/Im0 Do", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void AReplacementOfADifferentShapeIsAccepted()
    {
        // The placeholder's transform decides the size, so a replacement with different
        // proportions fills the same rectangle rather than being fitted or letterboxed.
        var template = SyntheticTemplate.WithImageSlots(FourSlots());
        var wide = SyntheticTemplate.StripedPng(240, 20);

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetImageAt(1, 1, wide));

        var replaced = ImagesIn(pdf)[0];
        Assert.Equal(240, replaced.Width);
        Assert.Equal(20, replaced.Height);

        // The drawing instruction still maps it onto the original 40x40 rectangle.
        Assert.Contains("40 0 0 40 10 150 cm", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void ABarcodeReplacesAPlaceholderAsVectorsRatherThanAPicture()
    {
        var template = SyntheticTemplate.WithImageSlots(FourSlots());

        var pdf = MonitovoPdf.Fill(template, fill =>
            fill.SetBarcodeAt(1, 2, BarcodeType.Code128, "SLOT-4471"));

        var replaced = ImagesIn(pdf)[1];

        // A form rather than an image, and its content is rectangles.
        Assert.Equal("/Form", replaced.Subtype);
        Assert.Contains(" re", System.Text.Encoding.Latin1.GetString(replaced.Data), StringComparison.Ordinal);
    }

    [Fact]
    public void SlotsAreNumberedByNaturalOrderOfTheirResourceName()
    {
        // A resource dictionary has no order of its own, so the rule has to be ours and has to be
        // stable. Ten must not sort between one and two.
        Assert.True(NaturalOrder.Compare("/Im2", "/Im10") < 0);
        Assert.True(NaturalOrder.Compare("/Im0", "/Im2") < 0);
        Assert.True(NaturalOrder.Compare("/Im10", "/Im9") > 0);
        Assert.Equal(0, NaturalOrder.Compare("/Im3", "/Im3"));
    }

    [Fact]
    public void TheDeclarationOrderInTheTemplateDoesNotDecideTheNumbering()
    {
        // Declared out of order on purpose: index 1 must still be /Im0.
        var template = SyntheticTemplate.WithImageSlots(
            new SyntheticTemplate.Slot("/Im2", 10, 150, 40, 40),
            new SyntheticTemplate.Slot("/Im0", 60, 150, 40, 40),
            new SyntheticTemplate.Slot("/Im10", 10, 100, 40, 40));

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng()));

        var info = MonitovoPdf.Inspect(pdf);
        var replaced = ImagesIn(pdf).Single(image => image.Name == "/Im0");
        var untouched = ImagesIn(pdf).Where(image => image.Name != "/Im0").ToList();
        var original = ImagesIn(template);

        Assert.NotEqual(original.Single(image => image.Name == "/Im0").Data, replaced.Data);
        foreach (var image in untouched)
            Assert.Equal(original.Single(candidate => candidate.Name == image.Name).Data, image.Data);

        // And the reported numbering agrees: /Im0, /Im2, /Im10.
        Assert.Equal(["/Im0", "/Im2", "/Im10"], info.Pages[0].Images.Select(image => image.ResourceName));
    }

    [Fact]
    public void TextFieldsAndImageSlotsFillInOneCall()
    {
        var template = SyntheticTemplate.WithFieldsAndImageSlots(
            [new SyntheticTemplate.Field("title", 10, 20, 190, 50)],
            [new SyntheticTemplate.Slot("/Im0", 10, 150, 40, 40),
             new SyntheticTemplate.Slot("/Im1", 60, 150, 40, 40)]);

        var pdf = MonitovoPdf.Fill(template, fill =>
        {
            fill.SetText("title", "COMBINED-4471");
            fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng());
            fill.SetBarcodeAt(1, 2, BarcodeType.Code128, "COMBINED-4471");
        });

        Assert.Contains("COMBINED-4471", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);

        var images = ImagesIn(pdf);
        Assert.NotEqual(ImagesIn(template)[0].Data, images[0].Data);
        Assert.Equal("/Form", images[1].Subtype);

        // The form is gone, so the finished document is flat.
        Assert.DoesNotContain("/Widget", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndexPastTheEndFailsTheRender()
    {
        var template = SyntheticTemplate.WithImageSlots(FourSlots());

        var exception = Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Fill(template, fill => fill.SetImageAt(1, 9, SyntheticTemplate.SinglePixelPng())));

        Assert.Contains("image 9 on page 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("4 image placeholder", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndexPastTheEndCanBeIgnoredAndIsReported()
    {
        var template = SyntheticTemplate.WithImageSlots(FourSlots());

        var result = MonitovoPdf.FillWithReport(template, fill =>
        {
            fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng());
            fill.SetImageAt(1, 9, SyntheticTemplate.SinglePixelPng());
        }, new RenderingOptions { OnMissingField = MissingFieldBehaviour.Ignore });

        var missing = Assert.Single(result.UnmatchedImages);
        Assert.Equal(new ImageSlotReference(1, 9), missing);
        Assert.False(result.Complete);

        // The one that did exist was still replaced.
        Assert.NotEqual(ImagesIn(template)[0].Data, ImagesIn(result.Pdf)[0].Data);
    }

    [Fact]
    public void APageWithNoImagesAtAllIsReportedTheSameWay()
    {
        var template = SyntheticTemplate.WithFields(new SyntheticTemplate.Field("title", 10, 60, 190, 90));

        var result = MonitovoPdf.FillWithReport(
            template,
            fill => fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng()),
            new RenderingOptions { OnMissingField = MissingFieldBehaviour.Ignore });

        Assert.Equal(new ImageSlotReference(1, 1), Assert.Single(result.UnmatchedImages));
    }

    [Fact]
    public void AddressingTheSamePlaceholderTwiceThrows()
    {
        var template = SyntheticTemplate.WithImageSlots(FourSlots());

        var exception = Assert.Throws<ArgumentException>(() => MonitovoPdf.Fill(template, fill =>
        {
            fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng());
            fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng());
        }));

        Assert.Contains("already has a replacement", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void PagesAndIndexesAreCountedFromOne(int page, int index)
    {
        var template = SyntheticTemplate.WithImageSlots(FourSlots());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MonitovoPdf.Fill(template, fill => fill.SetImageAt(page, index, SyntheticTemplate.SinglePixelPng())));
    }

    [Fact]
    public void ATemplateWithNoFormAtAllCanStillBeFilled()
    {
        // A template whose placeholders are all images has no form to strip, and asking for one
        // would rule out exactly the templates this addressing exists for.
        var template = SyntheticTemplate.WithImageSlots(FourSlots());

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetImageAt(1, 1, SyntheticTemplate.SinglePixelPng()));

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf[..4]), StringComparison.Ordinal);
    }
}
