using System.Globalization;
using System.Text.RegularExpressions;
using MonitovoPDF;
using MonitovoPDF.Rendering;

namespace MonitovoPDF.Tests;

/// <summary>
/// Covers honouring the font a template asks for, rather than always drawing in the configured
/// default.
/// </summary>
/// <remarks>
/// The name-handling is tested directly rather than by inspecting the rendered PDF. What a
/// finished document records as its BaseFont depends on the font file the host happens to have —
/// Arial reports "ArialMT", DejaVu reports "DejaVuSans" — so an assertion on that would pass on a
/// developer's machine and fail in CI for reasons unrelated to the behaviour under test.
/// </remarks>
public class TemplateFontTests
{
    private static readonly SyntheticTemplate.Field Title = new("part_number", 10, 60, 190, 90);

    [Theory]
    // The base-14 names are never embedded and are defined to be substituted.
    [InlineData("Helvetica", "Arial")]
    [InlineData("/Helvetica", "Arial")]
    [InlineData("Times-Roman", "Times New Roman")]
    [InlineData("Times", "Times New Roman")]
    [InlineData("Courier", "Courier New")]
    // A subset tag is six uppercase letters and a plus, and is not part of the family.
    [InlineData("ABCDEF+Arial", "Arial")]
    [InlineData("ABCDEF+Arial-Bold", "Arial")]
    [InlineData("/ABCDEF+Calibri-Italic", "Calibri")]
    // A style suffix is not part of the family either; the renderer never asks for a styled face.
    [InlineData("Arial-BoldItalic", "Arial")]
    // Anything else passes through untouched.
    [InlineData("Calibri", "Calibri")]
    [InlineData("DejaVuSans", "DejaVuSans")]
    public void BaseFontNames_ReduceToAFamily(string baseFont, string expected)
    {
        Assert.Equal(expected, LabelRenderer.NormaliseBaseFont(baseFont));
    }

    [Fact]
    public void AFontTheHostDoesNotHave_FallsBackWithoutFailing()
    {
        // A template may name anything. Refusing to render because a font is missing would be a
        // poor trade against drawing the label in something close.
        var template = SyntheticTemplate.WithFontNamed("NoSuchFontExistsAnywhere", Title);

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetText("part_number", "WIDGET-4471"));

        Assert.Contains("WIDGET-4471", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateNamingAFontTheHostHas_Renders()
    {
        // The suite's resolver answers to the default family, so a template asking for it by name
        // exercises the path where the requested family is found rather than substituted.
        var template = SyntheticTemplate.WithFontNamed(new RenderingOptions().DefaultFontFamily, Title);

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetText("part_number", "WIDGET-4471"));

        Assert.Contains("WIDGET-4471", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateNamingNoResolvableFont_StillRenders()
    {
        // WithFields references a resource the template never defines, which is the case where
        // there is nothing to honour and the configured default has to carry it.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFields(Title),
            fill => fill.SetText("part_number", "WIDGET-4471"));

        Assert.Contains("WIDGET-4471", PdfContent.OfFirstPage(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSizeTheFieldAsksFor_IsStillHonoured()
    {
        // Reading the family must not disturb reading the size out of the same string.
        var template = SyntheticTemplate.WithFontNamed("Helvetica", Title);

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetText("part_number", "OK"));

        var match = Regex.Match(PdfContent.OfFirstPage(pdf), @"/F\d+\s+([\d.]+)\s+Tf");
        Assert.True(match.Success, "The content stream contains no text-font operator.");
        Assert.Equal(9d, double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }
}
