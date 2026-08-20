using MonitovoPDF.Rendering;
using PdfSharp.Fonts;

namespace MonitovoPDF.Tests;

public class BarcodeRenderingTests
{
    private static readonly SyntheticTemplate.Field Slot = new("barcode", 10, 10, 190, 90);

    static BarcodeRenderingTests()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;
    }

    /// <summary>
    /// Content that is valid for each symbology. The symbologies differ sharply in what they
    /// accept — several are digits-only, and some require an exact length.
    /// </summary>
    public static TheoryData<string, string> ValidContent() => new()
    {
        { "code128", "WIDGET-4471" },
        { "code39", "WIDGET4471" },
        { "code93", "WIDGET4471" },
        { "codabar", "A12345B" },
        { "itf", "1234567890" },
        { "ean13", "590123412345" },
        { "ean8", "9638507" },
        { "upca", "01234567891" },
        { "upce", "0123456" },
        { "msi", "1234567890" },
        { "plessey", "1234567890" },
        { "qr", "https://example.invalid/w4471" },
        { "datamatrix", "WIDGET-4471" },
        { "aztec", "WIDGET-4471" },
        { "pdf417", "WIDGET-4471" },
    };

    private static LabelRenderer CreateRenderer() =>
        new(new RenderingOptions());

    private static byte[] RenderBarcode(string type, string value)
    {
        Assert.True(BarcodeSymbology.TryParse(type, out var symbology), $"'{type}' is not a known symbology.");

        return CreateRenderer().Render(
            SyntheticTemplate.WithFields(Slot),
            new Dictionary<string, string>(),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, BarcodeContent> { ["barcode"] = new(symbology, value) });
    }

    [Theory]
    [MemberData(nameof(ValidContent))]
    public void EverySupportedSymbology_RendersIntoTheTemplate(string type, string value)
    {
        var pdf = RenderBarcode(type, value);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf[..4]), StringComparison.Ordinal);

        // Bars are drawn as vector rectangles. A real symbol needs many of them, so counting
        // rectangle operators distinguishes a drawn barcode from an empty or single-block page.
        var rectangles = CountRectangles(PdfContent.OfFirstPage(pdf));

        Assert.True(rectangles >= 10, $"Expected a barcode of many rectangles but drew {rectangles}.");
    }

    /// <summary>Counts "re" rectangle operators in a content stream.</summary>
    private static int CountRectangles(string content) =>
        System.Text.RegularExpressions.Regex.Matches(content, @"(?<![A-Za-z])re(?![A-Za-z])").Count;

    [Fact]
    public void EverySymbologyInTheSupportedList_IsCovered()
    {
        // Guards against a symbology being added to the service without a test for it.
        var tested = ValidContent().Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(BarcodeSymbology.Names.Order(StringComparer.Ordinal), tested.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Barcodes_AreDrawnAsVectorsRatherThanImages()
    {
        var pdf = RenderBarcode("code128", "WIDGET-4471");

        // No XObject means nothing was rasterised: the bars are page content.
        Assert.DoesNotContain("/XObject", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("itf", "12345")]          // odd digit count
    [InlineData("itf", "ABCDEF")]         // not digits
    [InlineData("ean13", "1")]            // wrong length
    [InlineData("ean8", "123456789012")]  // wrong length
    [InlineData("upca", "12")]            // wrong length
    public void ContentInvalidForTheSymbology_IsRejected(string type, string value)
    {
        var exception = Assert.Throws<TemplateRenderException>(() => RenderBarcode(type, value));

        Assert.Contains("barcode", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(type, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABarcodeForAFieldTheTemplateDoesNotDefine_IsRejected()
    {
        Assert.True(BarcodeSymbology.TryParse("code128", out var symbology));

        var exception = Assert.Throws<TemplateRenderException>(() => CreateRenderer().Render(
            SyntheticTemplate.WithFields(Slot),
            new Dictionary<string, string>(),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, BarcodeContent> { ["nonexistent"] = new(symbology, "X") }));

        Assert.Contains("nonexistent", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CODE128")]
    [InlineData("code128")]
    [InlineData("  code128  ")]
    public void SymbologyNames_AreMatchedLenientlyOnCaseAndSpace(string name)
    {
        Assert.True(BarcodeSymbology.TryParse(name, out var symbology));
        Assert.Equal("code128", symbology.Name);
    }

    [Theory]
    [InlineData("code-128")]
    [InlineData("qrcode")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownSymbologyNames_AreRejected(string? name)
    {
        Assert.False(BarcodeSymbology.TryParse(name, out _));
    }
}
