using MonitovoPDF;

namespace MonitovoPDF.Tests;

/// <summary>Covers reading a template without filling it.</summary>
public class InspectTests
{
    private static readonly SyntheticTemplate.Field Title = new("part_number", 10, 60, 190, 90);
    private static readonly SyntheticTemplate.Field Slot = new("barcode", 10, 10, 90, 50);

    [Fact]
    public void EveryFieldIsReported_ByName()
    {
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFields(Title, Slot));

        Assert.Equal(["barcode", "part_number"], info.FieldNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ThePageSizeIsReported_InPointsAndMillimetres()
    {
        // The synthetic template is 200x100 points.
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFields(Title));

        var page = Assert.Single(info.Pages);
        Assert.Equal(1, page.Number);
        Assert.Equal(200, page.WidthPoints);
        Assert.Equal(100, page.HeightPoints);
        Assert.Equal(70.56, page.WidthMillimetres, 2);
        Assert.Equal(0, page.Rotation);
    }

    [Fact]
    public void AFieldReportsWhereItSits()
    {
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFields(Title));

        var placement = Assert.Single(info.Field("part_number")!.Placements);
        Assert.Equal(1, placement.PageNumber);
        Assert.Equal(10, placement.XPoints);
        Assert.Equal(180, placement.WidthPoints);
        Assert.Equal(30, placement.HeightPoints);

        // Measured from the top of the page: the field's top edge is 90 up from the bottom of a
        // 100 point page.
        Assert.Equal(10, placement.YPoints);
    }

    [Fact]
    public void AFieldShownTwice_ReportsBothPlacements()
    {
        var info = MonitovoPdf.Inspect(
            SyntheticTemplate.WithSharedField("reason", (10, 150, 190, 190), (10, 10, 190, 50)));

        Assert.Equal(2, info.Field("reason")!.Placements.Count);
    }

    [Fact]
    public void TheFontAFieldAsksForIsReported()
    {
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFontNamed("Helvetica", Title));

        var field = info.Field("part_number")!;
        Assert.Equal("Arial", field.FontFamily);
        Assert.Equal(9, field.FontSizePoints);
    }

    [Fact]
    public void AFieldNamingNoFont_ReportsNone()
    {
        // Distinguishing "asks for nothing" from "asks for the default" is the point of reporting
        // it: only the first is a template a caller may want to correct.
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFields(Title));

        Assert.Null(info.Field("part_number")!.FontFamily);
    }

    [Fact]
    public void TheFieldKindIsReported()
    {
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFields(Title));

        Assert.Equal(TemplateFieldKind.Text, info.Field("part_number")!.Kind);
    }

    [Fact]
    public void AMultilineFieldSaysSo()
    {
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithMultilineField(Title));

        Assert.True(info.Field("part_number")!.IsMultiline);
        Assert.False(MonitovoPdf.Inspect(SyntheticTemplate.WithFields(Title)).Field("part_number")!.IsMultiline);
    }

    [Fact]
    public void AnUnknownFieldNameReturnsNull()
    {
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFields(Title));

        Assert.Null(info.Field("nonexistent"));
    }

    [Fact]
    public void ATemplateWithNoFormIsReadableRatherThanAnError()
    {
        // Reporting an empty field list is more useful than throwing: "this template has no
        // fields" is exactly the answer a caller is asking for.
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFields());

        Assert.Single(info.Pages);
        Assert.Empty(info.Fields);
    }

    [Fact]
    public void AnUnreadableTemplateThrows()
    {
        Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Inspect(System.Text.Encoding.ASCII.GetBytes("not a pdf")));
    }

    [Fact]
    public void InspectingFromDiskAndFromAStreamAgree()
    {
        var template = SyntheticTemplate.WithFields(Title, Slot);
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "template.pdf");

        try
        {
            File.WriteAllBytes(path, template);

            using var stream = new MemoryStream(template);
            Assert.Equal(MonitovoPdf.InspectFile(path).FieldNames, MonitovoPdf.Inspect(stream).FieldNames);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
