using System.Text;
using MonitovoPDF;
using MonitovoPDF.Rendering;

namespace MonitovoPDF.Tests;

/// <summary>Covers the public surface consumers write against.</summary>
public class MonitovoPdfTests
{
    private static readonly SyntheticTemplate.Field Title = new("part_number", 10, 60, 190, 90);
    private static readonly SyntheticTemplate.Field Slot = new("barcode", 10, 10, 90, 50);

    private static byte[] Template() => SyntheticTemplate.WithFields(Title, Slot);

    [Fact]
    public void Fill_DrawsText_ImagesAndBarcodes_InOneCall()
    {
        var pdf = MonitovoPdf.Fill(Template(), fill =>
        {
            fill.SetText("part_number", "WIDGET-4471");
            fill.SetBarcode("barcode", BarcodeType.Code128, "WIDGET-4471");
        });

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]), StringComparison.Ordinal);
        Assert.Contains("WIDGET-4471", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_AcceptsAStream()
    {
        using var stream = new MemoryStream(Template());

        var pdf = MonitovoPdf.Fill(stream, fill => fill.SetText("part_number", "FROM-STREAM"));

        Assert.Contains("FROM-STREAM", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void FillFile_ReadsAndWritesDisk()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var templatePath = Path.Combine(directory, "template.pdf");
            var outputPath = Path.Combine(directory, "label.pdf");
            File.WriteAllBytes(templatePath, Template());

            MonitovoPdf.FillFile(templatePath, outputPath, fill => fill.SetText("part_number", "FROM-DISK"));

            Assert.True(File.Exists(outputPath));
            Assert.Contains("FROM-DISK", PdfContent.OfFirstPage(File.ReadAllBytes(outputPath)), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Fill_RejectsAFieldTheTemplateDoesNotDefine()
    {
        var exception = Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Fill(Template(), fill => fill.SetText("nonexistent", "x")));

        Assert.Contains("nonexistent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_KeepsTheUnderlyingFailureWhenATemplateIsUnreadable()
    {
        var exception = Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Fill(Encoding.ASCII.GetBytes("not a pdf"), fill => { }));

        // A library caller can only diagnose this if the real cause survives.
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Fill_RejectsATemplateOverTheConfiguredCeiling()
    {
        var template = Template();
        var options = new RenderingOptions { MaxTemplateBytes = template.Length - 1 };

        var exception = Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Fill(template, fill => fill.SetText("part_number", "x"), options));

        Assert.Contains("exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fill_RejectsMoreFieldsThanTheCeilingAllows()
    {
        var options = new RenderingOptions { MaxFieldCount = 1 };

        Assert.Throws<TemplateRenderException>(() => MonitovoPdf.Fill(Template(), fill =>
        {
            fill.SetText("part_number", "a");
            fill.SetBarcode("barcode", BarcodeType.Code128, "b");
        }, options));
    }

    [Fact]
    public void Fill_RejectsATextValueOverTheLengthCeiling()
    {
        var options = new RenderingOptions { MaxTextLength = 4 };

        Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Fill(Template(), fill => fill.SetText("part_number", "far too long"), options));
    }

    [Fact]
    public void Fill_RejectsAValueTheSymbologyCannotEncode()
    {
        var exception = Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Fill(Template(), fill => fill.SetBarcode("barcode", BarcodeType.Itf, "ABC")));

        Assert.Contains("itf", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Fill_RequiresATemplateAndACallback()
    {
        Assert.Throws<ArgumentNullException>(() => MonitovoPdf.Fill((byte[])null!, fill => { }));
        Assert.Throws<ArgumentNullException>(() => MonitovoPdf.Fill(Template(), null!));
    }

    [Fact]
    public void FillBuilder_RefusesToGiveAFieldTwoValues()
    {
        var exception = Assert.Throws<ArgumentException>(() => MonitovoPdf.Fill(Template(), fill =>
        {
            fill.SetText("part_number", "first");
            fill.SetBarcode("part_number", BarcodeType.Code128, "second");
        }));

        Assert.Contains("already has a value", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FillBuilder_RefusesAnEmptyFieldName(string field)
    {
        Assert.Throws<ArgumentException>(() =>
            MonitovoPdf.Fill(Template(), fill => fill.SetText(field, "x")));
    }

    [Fact]
    public void FillBuilder_RefusesABarcodeWithNoValue()
    {
        Assert.Throws<ArgumentException>(() =>
            MonitovoPdf.Fill(Template(), fill => fill.SetBarcode("barcode", BarcodeType.Code128, "")));
    }

    [Fact]
    public void EveryBarcodeType_HasAName_AndRoundTrips()
    {
        foreach (var type in Enum.GetValues<BarcodeType>())
        {
            var name = BarcodeTypes.NameOf(type);

            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.True(BarcodeTypes.TryParse(name, out var parsed), $"'{name}' did not parse back.");
            Assert.Equal(type, parsed);
        }
    }

    [Fact]
    public void BarcodeTypeNames_CoverEveryEnumValue()
    {
        Assert.Equal(Enum.GetValues<BarcodeType>().Length, BarcodeTypes.Names.Count);
    }

    [Theory]
    [InlineData("CODE128")]
    [InlineData("  qr  ")]
    public void BarcodeTypeNames_AreMatchedLeniently(string name)
    {
        Assert.True(BarcodeTypes.TryParse(name, out _));
    }

    [Theory]
    [InlineData("code-128")]
    [InlineData("nonsense")]
    [InlineData(null)]
    public void UnknownBarcodeTypeNames_AreRejected(string? name)
    {
        Assert.False(BarcodeTypes.TryParse(name, out _));
    }
}
