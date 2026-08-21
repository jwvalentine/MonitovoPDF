using System.Globalization;
using System.Text.RegularExpressions;
using MonitovoPDF;
using MonitovoPDF.Rendering;

namespace MonitovoPDF.Tests;

/// <summary>
/// Covers drawing a value the way the field asks for it: in its weight and in its colour.
/// </summary>
/// <remarks>
/// Both used to be discarded. A field the designer set in bold was drawn in the regular face and
/// a field set in red was drawn in black — the value was right and the document was not what
/// anyone drew, which is a difference that only shows when the result is put beside the design.
/// </remarks>
public partial class FieldStyleTests
{
    [GeneratedRegex(@"(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) rg")]
    private static partial Regex FillColour();

    private static byte[] Fill(string appearance, string baseFont = "Helvetica", TextOptions? options = null) =>
        MonitovoPdf.Fill(
            SyntheticTemplate.WithAppearance(appearance, baseFont),
            fill => fill.SetText("value", "Sample", options));

    /// <summary>The last non-black fill colour the page sets, as (r, g, b) from 0 to 1.</summary>
    private static (double R, double G, double B)? ColourIn(byte[] pdf)
    {
        var matches = FillColour().Matches(PdfContent.OfFirstPage(pdf))
            .Select(match => (
                R: double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                G: double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                B: double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .Where(colour => colour != (0, 0, 0))
            .ToList();

        return matches.Count > 0 ? matches[^1] : null;
    }

    [Theory]
    [InlineData("Helvetica", "Arial", false, false)]
    [InlineData("Helvetica-Bold", "Arial", true, false)]
    [InlineData("Helvetica-Oblique", "Arial", false, true)]
    [InlineData("Helvetica-BoldOblique", "Arial", true, true)]
    [InlineData("ABCDEF+Calibri-Italic", "Calibri", false, true)]
    [InlineData("Arial,Bold", "Arial", true, false)]
    [InlineData("Times-Roman", "Times New Roman", false, false)]
    public void AWeightIsReadOutOfTheBaseFontName(string baseFont, string family, bool bold, bool italic)
    {
        // A base font name carries a subset tag, a family and a style. "Times-Roman" is the trap:
        // its dash is part of the name rather than a style, so the alias table decides first.
        var (read, isBold, isItalic) = LabelRenderer.SplitBaseFont(baseFont);

        Assert.Equal(family, read);
        Assert.Equal(bold, isBold);
        Assert.Equal(italic, isItalic);
    }

    [Fact]
    public void AFieldAskingForBoldIsDrawnInBold()
    {
        // The two documents differ only in the weight the field asked for, so a different font
        // resource is the evidence that a different face was actually used.
        var regular = Fill("/F1 9 Tf 0 g");
        var bold = Fill("/F1 9 Tf 0 g", "Helvetica-Bold");

        Assert.Contains("(Sample)", PdfContent.OfFirstPage(bold), StringComparison.Ordinal);

        Assert.Equal("Arial", FontDescriptor(regular));
        Assert.Equal("Arial,Bold", FontDescriptor(bold));
    }

    /// <summary>
    /// The face actually embedded in the output, such as "Arial" or "Arial,Bold".
    /// </summary>
    /// <remarks>
    /// Only the subset-tagged names count. The template's own font object survives into the
    /// output and names whatever the designer asked for, so matching the first name in the file
    /// reports what the template wanted rather than what was drawn — which passes whether or not
    /// the weight was honoured. The six-letter tag is dropped because it varies per document.
    /// </remarks>
    private static string FontDescriptor(byte[] pdf)
    {
        var raw = System.Text.Encoding.Latin1.GetString(pdf);

        var embedded = Regex.Matches(raw, @"/BaseFont\s*/[A-Z]{6}\+([A-Za-z0-9,\-]+)")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(embedded);

        return string.Join(",", embedded);
    }

    [Theory]
    [InlineData("/F1 9 Tf 1 0 0 rg", 1, 0, 0)]          // red, as a viewer would set it
    [InlineData("/F1 9 Tf 0 0 1 rg", 0, 0, 1)]          // blue
    [InlineData("/F1 9 Tf 0.5 g", 0.5, 0.5, 0.5)]       // mid grey, one operand
    [InlineData("/F1 9 Tf 0 1 1 0 k", 1, 0, 0)]         // red again, in the four inks a press uses
    public void AFieldAskingForAColourIsDrawnInIt(string appearance, double r, double g, double b)
    {
        var colour = ColourIn(Fill(appearance));

        Assert.NotNull(colour);
        Assert.Equal(r, colour!.Value.R, precision: 2);
        Assert.Equal(g, colour.Value.G, precision: 2);
        Assert.Equal(b, colour.Value.B, precision: 2);
    }

    [Fact]
    public void AFieldAskingForNoColourIsStillDrawnInBlack()
    {
        // The behaviour every existing template relies on, pinned so honouring colour cannot
        // change what a template that never mentioned one produces.
        Assert.Null(ColourIn(Fill("/F1 9 Tf 0 g")));
        Assert.Null(ColourIn(Fill("/F1 9 Tf")));
    }

    [Fact]
    public void ACallerCanOverrideBothWithoutTouchingTheTemplate()
    {
        var pdf = Fill("/F1 9 Tf 0 g", "Helvetica",
            new TextOptions { Bold = true, Colour = "#00FF00" });

        var colour = ColourIn(pdf);

        Assert.NotNull(colour);
        Assert.Equal(0, colour!.Value.R, precision: 2);
        Assert.Equal(1, colour.Value.G, precision: 2);
        Assert.Equal(0, colour.Value.B, precision: 2);
    }

    [Fact]
    public void ACallerCanTurnOffAWeightTheTemplateAsksFor()
    {
        // Honouring the template is the default, so a caller who needs otherwise has to be able
        // to say so — the same escape the family and size overrides have always offered.
        var honoured = Fill("/F1 9 Tf 0 g", "Helvetica-Bold");
        var overridden = Fill("/F1 9 Tf 0 g", "Helvetica-Bold", new TextOptions { Bold = false });

        Assert.Equal("Arial,Bold", FontDescriptor(honoured));
        Assert.Equal("Arial", FontDescriptor(overridden));
    }

    [Fact]
    public void AColourThatIsNotAColourIsRefused()
    {
        // Ignoring it would draw in black, and a value drawn in black when a colour was asked
        // for is the kind of wrong that only shows up once it is printed.
        foreach (var bad in new[] { "red", "#FFF", "#GGGGGG", "FF0000", "" })
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                new FillBuilder().SetText("value", "Sample", new TextOptions { Colour = bad }));

            Assert.Contains("#RRGGBB", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void InspectReportsTheWeightAndColourSoTheyCanBeCheckedBeforeFilling()
    {
        // The point of reporting them: a template estate can be checked for reliance on either
        // without filling anything, which is how to find out whether honouring them changes
        // documents that were signed off under the old behaviour.
        // A field naming black and a field naming nothing both render black, but they are
        // reported apart: this describes what the template says, not what it comes to.
        var plain = MonitovoPdf.Inspect(SyntheticTemplate.WithAppearance("/F1 9 Tf 0 g")).Field("value")!;

        Assert.False(plain.IsBold);
        Assert.False(plain.IsItalic);
        Assert.Equal("#000000", plain.Colour);

        var silent = MonitovoPdf.Inspect(SyntheticTemplate.WithAppearance("/F1 9 Tf")).Field("value")!;

        Assert.Null(silent.Colour);

        var styled = MonitovoPdf.Inspect(
            SyntheticTemplate.WithAppearance("/F1 9 Tf 0.8 0 0 rg", "Helvetica-BoldOblique")).Field("value")!;

        Assert.True(styled.IsBold);
        Assert.True(styled.IsItalic);
        Assert.Equal("#CC0000", styled.Colour);
    }

    [Fact]
    public void AWrappedValueKeepsTheWeightAndColourToo()
    {
        // Wrapping goes down a different drawing path, which is exactly where a style gets
        // dropped without anyone noticing.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithAppearance("/F1 9 Tf 1 0 0 rg", "Helvetica-Bold"),
            fill => fill.SetText("value", "One two three four five six seven eight nine ten",
                new TextOptions { Multiline = true }));

        var colour = ColourIn(pdf);

        Assert.NotNull(colour);
        Assert.Equal(1, colour!.Value.R, precision: 2);
        Assert.Equal(0, colour.Value.G, precision: 2);
    }
}
