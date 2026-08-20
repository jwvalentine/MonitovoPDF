using System.Globalization;
using System.Text.RegularExpressions;
using MonitovoPDF;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace MonitovoPDF.Tests;

/// <summary>
/// Covers printing a barcode's value as readable text below the bars, which is what somebody
/// falls back to when a scanner is not to hand or the symbol has been damaged.
/// </summary>
public partial class BarcodeCaptionTests
{
    private const string Value = "47028538";

    private static readonly SyntheticTemplate.Field Slot = new("barcode", 10, 10, 190, 90);

    [GeneratedRegex(@"(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) re")]
    private static partial Regex Rectangle();

    [GeneratedRegex(@"(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) cm")]
    private static partial Regex Transform();

    [GeneratedRegex(@"(-?[\d.]+) (-?[\d.]+) Td")]
    private static partial Regex TextPosition();

    [GeneratedRegex(@"/\w+ (-?[\d.]+) Tf")]
    private static partial Regex FontSize();

    private static double Number(Group group) =>
        double.Parse(group.Value, CultureInfo.InvariantCulture);

    /// <summary>The (x, y, width, height) of every rectangle a content stream fills.</summary>
    private static List<(double X, double Y, double Width, double Height)> Rectangles(string content) =>
    [
        .. Rectangle().Matches(content).Select(match => (
            Number(match.Groups[1]), Number(match.Groups[2]),
            Number(match.Groups[3]), Number(match.Groups[4]))),
    ];

    /// <summary>The drawing instructions of the form XObject that replaced a placeholder.</summary>
    private static string FormContent(byte[] pdf, string resourceName = "/Im0")
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        var xobjects = document.Pages[0].Elements
            .GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;

        var item = xobjects.Elements[resourceName];
        var form = item is PdfReference { Value: PdfDictionary resolved } ? resolved : (PdfDictionary)item!;

        Assert.Equal("/Form", form.Elements.GetName("/Subtype"));

        return System.Text.Encoding.Latin1.GetString(form.Stream.UnfilteredValue);
    }

    private static byte[] FillField(BarcodeOptions? options, RenderingOptions? rendering = null) =>
        MonitovoPdf.Fill(
            SyntheticTemplate.WithFields(Slot),
            fill => fill.SetBarcode("barcode", BarcodeType.Code128, Value, options),
            rendering);

    [Fact]
    public void ByDefaultTheValueIsPrintedOnlyAsBars()
    {
        // The behaviour that shipped before this existed, pinned so it cannot drift: a caller who
        // says nothing gets bars alone, and every point of the field's height goes to them.
        var content = PdfContent.OfFirstPage(FillField(options: null));

        Assert.DoesNotContain($"({Value})", content, StringComparison.Ordinal);
        Assert.Equal(80, Rectangles(content).Max(bar => bar.Height), precision: 3);
    }

    [Fact]
    public void TheValueIsPrintedBelowTheBars()
    {
        var content = PdfContent.OfFirstPage(FillField(new BarcodeOptions { ShowValue = true }));

        Assert.Contains($"({Value}) Tj", content, StringComparison.Ordinal);

        // Both are in page coordinates, which run upwards, so the text sits at the lower number.
        var lowestBar = Rectangles(content).Min(bar => bar.Y);
        var text = Number(TextPosition().Match(content).Groups[2]);

        Assert.True(text < lowestBar, $"The value was drawn at {text}, not below the bars at {lowestBar}.");
    }

    [Theory]
    [InlineData(null, 64)]     // the default fifth of an eighty-point field
    [InlineData(0.25, 60)]
    [InlineData(0.5, 40)]
    public void TheBarsGiveUpExactlyTheHeightTheValueTakes(double? fraction, double expected)
    {
        // The value is drawn inside the space the field gave the barcode rather than beside it,
        // so this is height taken from the bars. A caller choosing the share is choosing that.
        var content = PdfContent.OfFirstPage(FillField(
            new BarcodeOptions { ShowValue = true, CaptionHeightFraction = fraction }));

        Assert.Equal(expected, Rectangles(content).Max(bar => bar.Height), precision: 3);
    }

    [Fact]
    public void TheDefaultShareIsConfigurable()
    {
        var content = PdfContent.OfFirstPage(FillField(
            new BarcodeOptions { ShowValue = true },
            TestRender.Options(options => options.BarcodeCaptionHeightFraction = 0.4)));

        Assert.Equal(48, Rectangles(content).Max(bar => bar.Height), precision: 3);
    }

    [Fact]
    public void APlaceholderBarcodeAlsoCarriesItsValue()
    {
        // A placeholder addressed by position, rather than a named field. The bars live in a form
        // XObject that inherits the placeholder's geometry; the value is drawn onto the page.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithTransformedImageSlot("120 0 0 60 40 100"),
            fill => fill.SetBarcodeAt(1, 1, BarcodeType.Code128, Value, new BarcodeOptions { ShowValue = true }));

        Assert.Contains($"({Value}) Tj", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);

        // The form is the unit square, so a fifth held back leaves the bars starting at 0.2.
        Assert.Equal(0.2, Rectangles(FormContent(pdf)).Min(bar => bar.Y), precision: 3);
    }

    [Fact]
    public void APlaceholderValueInheritsTheRotationOfItsPlaceholder()
    {
        // A quarter turn: the placeholder's own width runs up the page. The value has to turn
        // with it, or a barcode the template stood on its side gets a caption lying on its back.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithTransformedImageSlot("0 60 -90 0 150 40"),
            fill => fill.SetBarcodeAt(1, 1, BarcodeType.Code128, Value, new BarcodeOptions { ShowValue = true }));

        var (a, b, c, d) = CaptionTransform(pdf);

        // A quarter turn puts the whole of the transform into its off-diagonal terms.
        Assert.Equal(0, a, precision: 3);
        Assert.Equal(0, d, precision: 3);
        Assert.Equal(1, Math.Abs(b), precision: 3);
        Assert.Equal(1, Math.Abs(c), precision: 3);
    }

    /// <summary>
    /// The transform in force where the value is drawn.
    /// </summary>
    /// <remarks>
    /// The template's own instructions come first and the value is drawn into a stream appended
    /// after them, so the last transform in the page is the one that positions the value.
    /// </remarks>
    private static (double A, double B, double C, double D) CaptionTransform(byte[] pdf)
    {
        var transforms = Transform().Matches(PdfContent.OfFirstPage(pdf));
        Assert.NotEmpty(transforms);

        var last = transforms[^1];

        return (Number(last.Groups[1]), Number(last.Groups[2]),
            Number(last.Groups[3]), Number(last.Groups[4]));
    }

    [Fact]
    public void AStretchedPlaceholderDoesNotStretchTheValue()
    {
        // A placeholder five times wider than it is tall. Left alone, drawing inside it would
        // widen every glyph by that same five times, which is what a caption baked into a
        // stretched picture does. Measuring each of the placeholder's axes separately is what
        // undoes it, and is the whole reason the transform is recovered rather than the bounds.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithTransformedImageSlot("200 0 0 40 0 0"),
            fill => fill.SetBarcodeAt(1, 1, BarcodeType.Code128, Value, new BarcodeOptions { ShowValue = true }));

        var (a, _, _, d) = CaptionTransform(pdf);

        // Equal scale on both axes is exactly the absence of stretch. The placeholder is five to
        // one, so a transform carrying its proportions through would show it here.
        Assert.Equal(Math.Abs(a), Math.Abs(d), precision: 3);
        Assert.Equal(1, Math.Abs(a), precision: 3);
    }

    [Fact]
    public void AValueTooWideForTheBarsIsShrunkUntilItFits()
    {
        // A caption running out past the bars it belongs to is worse than a small one.
        var narrow = new SyntheticTemplate.Field("barcode", 10, 10, 60, 90);

        var content = PdfContent.OfFirstPage(MonitovoPdf.Fill(
            SyntheticTemplate.WithFields(narrow),
            fill => fill.SetBarcode(
                "barcode", BarcodeType.Code128, "4702853890210", new BarcodeOptions { ShowValue = true })));

        // Sized from the reserved band it would be 12.8 points, which will not fit fifty across.
        Assert.True(Number(FontSize().Match(content).Groups[1]) < 12.8);
    }

    [Fact]
    public void TheSizeCanBeSetOutright()
    {
        var content = PdfContent.OfFirstPage(FillField(
            new BarcodeOptions { ShowValue = true, CaptionFontSizePoints = 7 }));

        Assert.Equal(7, Number(FontSize().Match(content).Groups[1]), precision: 3);
    }

    [Fact]
    public void AShareOutsideWhatLeavesAScannableBarcodeIsRejected()
    {
        // Far likelier to be a mistaken unit — a point size, or a percentage — than an intent.
        foreach (var fraction in new[] { 0d, -0.2, 0.75, 20 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FillBuilder().SetBarcode(
                "barcode", BarcodeType.Code128, Value,
                new BarcodeOptions { ShowValue = true, CaptionHeightFraction = fraction }));
        }
    }

    [Fact]
    public void APlaceholderThePageNeverDrawsCannotCarryAValue()
    {
        // A resource a page declares but never draws has no position to inherit. Saying so beats
        // dropping the value silently, when the value is the whole point of asking.
        var template = SyntheticTemplate.WithTransformedImageSlot("1 0 0 1 0 0", drawIt: false);

        var exception = Assert.Throws<TemplateRenderException>(() => MonitovoPdf.Fill(
            template,
            fill => fill.SetBarcodeAt(1, 1, BarcodeType.Code128, Value, new BarcodeOptions { ShowValue = true })));

        Assert.Contains("does not draw", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATwoDimensionalSymbolAlsoMakesRoom()
    {
        byte[] Fill(BarcodeOptions? options) => MonitovoPdf.Fill(
            SyntheticTemplate.WithTransformedImageSlot("80 0 0 80 40 40"),
            fill => fill.SetBarcodeAt(1, 1, BarcodeType.QrCode, Value, options));

        var captioned = Fill(new BarcodeOptions { ShowValue = true });
        Assert.Contains($"({Value}) Tj", PdfContent.OfFirstPage(captioned), StringComparison.Ordinal);

        // A quiet zone means the lowest module is not at the very bottom of either symbol, so the
        // module size is what shows the room being made: a fifth held back squeezes every row.
        var plain = Rectangles(FormContent(Fill(options: null))).Min(module => module.Height);
        var squeezed = Rectangles(FormContent(captioned)).Min(module => module.Height);

        Assert.Equal(plain * 0.8, squeezed, precision: 4);
        Assert.True(Rectangles(FormContent(captioned)).Min(module => module.Y) >= 0.2);
    }

    [Fact]
    public void DrawingIntoAFieldDoesNotRenumberThePlaceholders()
    {
        // Drawing an image into a form field adds an image to the page's resources under a name
        // the engine chooses. Counted as a placeholder, it shifts the position of every real one
        // — and filling the wrong placeholder produces a document that still renders perfectly.
        var template = SyntheticTemplate.WithFieldsAndImageSlots(
            [new SyntheticTemplate.Field("photo", 10, 20, 80, 50)],
            [new SyntheticTemplate.Slot("/Im0", 10, 150, 40, 40),
             new SyntheticTemplate.Slot("/Im1", 60, 150, 40, 40)]);

        var pdf = MonitovoPdf.Fill(template, fill =>
        {
            fill.SetImage("photo", SyntheticTemplate.SinglePixelPng());
            fill.SetBarcodeAt(1, 2, BarcodeType.Code128, Value);
        });

        // The second placeholder is /Im1. The barcode must be there and nowhere else.
        Assert.Equal("/Form", Subtype(pdf, "/Im1"));
        Assert.Equal("/Image", Subtype(pdf, "/Im0"));
    }

    private static string Subtype(byte[] pdf, string resourceName)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        var xobjects = document.Pages[0].Elements
            .GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;

        var item = xobjects.Elements[resourceName];
        var dictionary = item is PdfReference { Value: PdfDictionary resolved } ? resolved : (PdfDictionary)item!;

        return dictionary.Elements.GetName("/Subtype");
    }
}
