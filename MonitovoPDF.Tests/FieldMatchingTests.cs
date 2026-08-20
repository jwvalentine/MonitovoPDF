using System.Text.RegularExpressions;
using MonitovoPDF;

namespace MonitovoPDF.Tests;

/// <summary>
/// Covers which fields a value reaches, and what happens when it reaches none.
/// </summary>
public class FieldMatchingTests
{
    private static readonly SyntheticTemplate.Field Title = new("title", 10, 150, 190, 190);
    private static readonly SyntheticTemplate.Field Second = new("second", 10, 10, 190, 50);

    private static int TimesDrawn(byte[] pdf, string value) =>
        Regex.Matches(PdfContent.OfFirstPage(pdf), Regex.Escape(value)).Count;

    [Fact]
    public void AFieldWithSeveralWidgets_IsDrawnInEveryPlaceItAppears()
    {
        // One field shown twice is the ordinary way a form repeats a value, and both places have
        // to be filled or the document is visibly half done.
        var template = SyntheticTemplate.WithSharedField("reason", (10, 150, 190, 190), (10, 10, 190, 50));

        var pdf = TestRender.Fill(template, new Dictionary<string, string> { ["reason"] = "REPEATED" });

        Assert.Equal(2, TimesDrawn(pdf, "REPEATED"));
    }

    [Fact]
    public void SeparateFieldsSharingAName_AreAllDrawn()
    {
        // Some authoring tools emit genuinely separate field objects under one name. Keeping only
        // one of them would draw the value in a single place and silently miss the rest — the
        // worst kind of wrong, because the document looks plausible.
        var template = SyntheticTemplate.WithFields(
            new SyntheticTemplate.Field("reason", 10, 150, 190, 190),
            new SyntheticTemplate.Field("reason", 10, 10, 190, 50));

        var pdf = TestRender.Fill(template, new Dictionary<string, string> { ["reason"] = "DUPLICATED" });

        Assert.Equal(2, TimesDrawn(pdf, "DUPLICATED"));
    }

    [Fact]
    public void AMissingField_FailsTheWholeRenderByDefault()
    {
        var exception = Assert.Throws<TemplateRenderException>(() => TestRender.Fill(
            SyntheticTemplate.WithFields(Title),
            new Dictionary<string, string> { ["title"] = "PRESENT", ["absent"] = "MISSING" }));

        Assert.Contains("absent", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OnMissingField", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingField_CanBeIgnored_AndTheRestStillDrawn()
    {
        var pdf = TestRender.Fill(
            SyntheticTemplate.WithFields(Title),
            new Dictionary<string, string> { ["title"] = "PRESENT", ["absent"] = "MISSING" },
            options: TestRender.Options(o => o.OnMissingField = MissingFieldBehaviour.Ignore));

        var content = PdfContent.OfFirstPage(pdf);
        Assert.Contains("PRESENT", content, StringComparison.Ordinal);
        Assert.DoesNotContain("MISSING", content, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoredFields_AreReported()
    {
        // Ignoring a name quietly would turn a wrong template into a silently wrong document, so
        // the caller is told exactly what did not land.
        var result = TestRender.FillWithReport(
            SyntheticTemplate.WithFields(Title),
            new Dictionary<string, string> { ["title"] = "PRESENT", ["absent"] = "x", ["also_absent"] = "y" },
            options: TestRender.Options(o => o.OnMissingField = MissingFieldBehaviour.Ignore));

        Assert.Equal(["absent", "also_absent"], result.UnmatchedFields.Order(StringComparer.Ordinal));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(result.Pdf[..4]), StringComparison.Ordinal);
    }

    [Fact]
    public void WhenEverythingMatches_NothingIsReported()
    {
        var result = TestRender.FillWithReport(
            SyntheticTemplate.WithFields(Title, Second),
            new Dictionary<string, string> { ["title"] = "A", ["second"] = "B" });

        Assert.Empty(result.UnmatchedFields);
    }

    [Fact]
    public void AMissingImageOrBarcodeField_IsReportedToo()
    {
        var result = TestRender.FillWithReport(
            SyntheticTemplate.WithFields(Title),
            images: new Dictionary<string, byte[]> { ["no_logo"] = SyntheticTemplate.SinglePixelPng() },
            barcodes: new Dictionary<string, (BarcodeType, string)> { ["no_code"] = (BarcodeType.Code128, "X") },
            options: TestRender.Options(o => o.OnMissingField = MissingFieldBehaviour.Ignore));

        Assert.Equal(["no_code", "no_logo"], result.UnmatchedFields.Order(StringComparer.Ordinal));
    }
}
