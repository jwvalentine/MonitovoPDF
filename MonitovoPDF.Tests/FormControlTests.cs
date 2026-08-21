using MonitovoPDF;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace MonitovoPDF.Tests;

/// <summary>
/// Covers the field types a form is mostly made of: tick boxes, dropdowns and radio buttons.
/// </summary>
/// <remarks>
/// A tick box is drawn from the template's own artwork for the state it is being put into, so
/// what these assert is that the right artwork was painted — the box is a form XObject, and a
/// page that draws it names it in a <c>Do</c> operator.
/// </remarks>
public class FormControlTests
{
    private static string Content(byte[] pdf) => PdfContent.OfFirstPage(pdf);

    /// <summary>How many XObjects the finished page draws.</summary>
    private static int Drawn(string content) =>
        System.Text.RegularExpressions.Regex.Matches(content, @"/MpState\d+ Do").Count;

    /// <summary>
    /// The artwork the page ended up painting, one entry per state that was drawn.
    /// </summary>
    /// <remarks>
    /// The artwork lives in its own object rather than in the page, so the page only names it.
    /// Reading the stream back is what shows which of a widget's states was chosen — in this
    /// fixture the ticked one fills a square and the cleared one only outlines it.
    /// </remarks>
    private static List<string> Painted(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        var xobjects = document.Pages[0].Elements
            .GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");

        var painted = new List<string>();

        foreach (var key in (xobjects?.Elements.Keys ?? []).Where(k => k.StartsWith("/MpState")).Order())
        {
            var item = xobjects!.Elements[key];
            var artwork = item is PdfReference { Value: PdfDictionary resolved } ? resolved : item as PdfDictionary;

            painted.Add(System.Text.Encoding.Latin1.GetString(artwork?.Stream?.UnfilteredValue ?? []));
        }

        return painted;
    }

    /// <summary>In this fixture, only the ticked state fills its square.</summary>
    private static bool IsTicked(string artwork) => artwork.Contains("2 2 8 8 re f", StringComparison.Ordinal);

    [Fact]
    public void ATickedBoxIsDrawnFromTheTemplatesOwnArtwork()
    {
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(), fill => fill.SetCheckbox("agree", true));

        Assert.Equal(1, Drawn(Content(pdf)));
        Assert.True(IsTicked(Assert.Single(Painted(pdf))));
    }

    [Fact]
    public void AClearedBoxStillDrawsItsBox()
    {
        // Not the same as leaving the field alone. The outline lives in the widget, so flattening
        // would lose it — an unticked box has to be painted, or it vanishes from the document.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(), fill => fill.SetCheckbox("agree", false));

        Assert.Equal(1, Drawn(Content(pdf)));

        var artwork = Assert.Single(Painted(pdf));

        Assert.False(IsTicked(artwork));
        Assert.Contains("0 0 12 12 re S", artwork, StringComparison.Ordinal);
    }

    [Fact]
    public void TheArtworkIsMappedOntoTheWidgetsOwnRectangle()
    {
        // The artwork's coordinate space is its own — a twelve-point box here — and has to be
        // scaled and shifted onto the rectangle the widget occupies. The tick box sits at
        // (20,160) and is twelve points square, so the mapping is a pure translation.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(), fill => fill.SetCheckbox("agree", true));

        Assert.Contains("1 0 0 1 20 160 cm", Content(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void ABoxWithNoArtworkFallsBackToADrawnCross()
    {
        // Templates from tools that expect the viewer to render their controls leave the states
        // empty. Drawing nothing would lose the box; a cross survives a coarse printer.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(withArtwork: false),
            fill => fill.SetCheckbox("agree", true));

        var content = Content(pdf);

        Assert.Equal(0, Drawn(content));
        Assert.Contains(" m ", content, StringComparison.Ordinal);
        Assert.Contains(" l S ", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AChosenOptionIsDrawnAsText()
    {
        // Flattening removes the control, so what lands on the page is the value it was showing.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(), fill => fill.SetChoice("country", "Portugal"));

        Assert.Contains("(Portugal)", Content(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOptionTheFieldDoesNotOfferIsRefused()
    {
        // A form recording an answer it never offered is worse than one that fails, because it
        // looks completed. The message names what the field does offer, since that is the fix.
        var exception = Assert.Throws<TemplateRenderException>(() => MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(), fill => fill.SetChoice("country", "Atlantis")));

        Assert.Contains("Atlantis", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Ireland", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Japan", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralOptionsCanBeChosenAtOnce()
    {
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(),
            fill => fill.SetChoice("country", ["Ireland", "Japan"]));

        var content = Content(pdf);

        Assert.Contains("(Ireland)", content, StringComparison.Ordinal);
        Assert.Contains("(Japan)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void OneRadioButtonGoesOnAndTheRestGoOff()
    {
        // A group is one field with several widgets, exactly one of which may be on. Turning the
        // others off explicitly is what stops a template that shipped one selected keeping it.
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(), fill => fill.SetChoice("size", "Large"));

        var content = Content(pdf);

        // Both widgets are painted, so neither keeps whatever state the template shipped it in,
        // and exactly one of them gets the filled artwork.
        Assert.Equal(2, Drawn(content));
        Assert.Single(Painted(pdf), IsTicked);

        // Both are drawn where they belong: the small one at x = 20, the large at x = 60.
        Assert.Contains("1 0 0 1 20 80 cm", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 60 80 cm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ARadioGroupThatNamesItsButtonsByNumberStillSelectsTheRightOne()
    {
        // The other shape a radio group comes in: the values a caller would use live in /Opt on
        // the group, and the buttons answer to states named after their position. Matching the
        // chosen value against those state names finds nothing, and the failure is silent —
        // every button goes off and the form comes out with nothing selected. So the value is
        // matched to a button by position whenever the group lists its values.
        var template = SyntheticTemplate.WithNumberedRadioGroup();

        Assert.Equal(["Small", "Large"], MonitovoPdf.Inspect(template).Field("size")!.Options);

        var pdf = MonitovoPdf.Fill(template, fill => fill.SetChoice("size", "Large"));

        Assert.Equal(2, Drawn(Content(pdf)));
        Assert.Single(Painted(pdf), IsTicked);
    }

    [Fact]
    public void TheRightButtonOfANumberedGroupIsTheOneChosen()
    {
        // Selecting either end has to land on that end, or a group could pass the test above
        // while always choosing the same button.
        foreach (var (value, x) in new[] { ("Small", "20"), ("Large", "60") })
        {
            var pdf = MonitovoPdf.Fill(
                SyntheticTemplate.WithNumberedRadioGroup(), fill => fill.SetChoice("size", value));

            var content = Content(pdf);
            var names = System.Text.RegularExpressions.Regex.Matches(content, @"1 0 0 1 (\d+) 80 cm (/MpState\d+) Do")
                .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

            var painted = Painted(pdf);
            var ticked = System.Text.RegularExpressions.Regex.Match(
                names[x], @"/MpState(\d+)").Groups[1].Value;

            Assert.True(IsTicked(painted[int.Parse(ticked)]), $"'{value}' did not tick the button at x={x}.");
        }
    }

    [Fact]
    public void SelectingSomethingThatMatchesNoButtonFails()
    {
        // A button field constraining nothing has no list to validate against, so a value that
        // matches no button gets as far as drawing and then selects none of them. Checking the
        // outcome rather than the matching is what makes this hold for template shapes nobody
        // here has seen: whatever a tool calls its states, ending up with nothing selected when
        // something was asked for is a defect.
        var template = SyntheticTemplate.WithFormControls(withArtwork: false);

        var exception = Assert.Throws<TemplateRenderException>(() =>
            MonitovoPdf.Fill(template, fill => fill.SetChoice("agree", "Maybe")));

        Assert.Contains("Maybe", exception.Message, StringComparison.Ordinal);
        Assert.Contains("agree", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectReportsWhatAFieldWillAccept()
    {
        // A caller should not have to guess the options, and they belong to the template.
        var info = MonitovoPdf.Inspect(SyntheticTemplate.WithFormControls());

        Assert.Equal(["Ireland", "Portugal", "Japan"], info.Field("country")!.Options);
        Assert.Equal(["Small", "Large"], info.Field("size")!.Options);

        // A tick box constrains nothing a caller would choose between, and neither does text.
        Assert.Empty(info.Field("agree")!.Options);
    }

    [Fact]
    public void AFieldCannotBeGivenTwoKindsOfValue()
    {
        Assert.Throws<ArgumentException>(() => new FillBuilder()
            .SetCheckbox("agree", true)
            .SetText("agree", "yes"));

        Assert.Throws<ArgumentException>(() => new FillBuilder()
            .SetText("agree", "yes")
            .SetCheckbox("agree", true));
    }

    [Fact]
    public void TheFinishedDocumentIsStillFlat()
    {
        var pdf = MonitovoPdf.Fill(SyntheticTemplate.WithFormControls(), fill =>
        {
            fill.SetCheckbox("agree", true);
            fill.SetChoice("country", "Ireland");
            fill.SetChoice("size", "Small");
        });

        // No widget annotations survive, so nothing interactive is left and no viewer is asked
        // to build an appearance. The form dictionary itself stays but is emptied — PDFsharp
        // does not expose the catalog, so it cannot be unlinked.
        Assert.DoesNotContain("/Widget", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);

        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        Assert.Equal(0, document.AcroForm.Fields.Count);
    }

    [Fact]
    public void AnUnknownFieldIsReportedLikeAnyOther()
    {
        var exception = Assert.Throws<TemplateRenderException>(() => MonitovoPdf.Fill(
            SyntheticTemplate.WithFormControls(), fill => fill.SetCheckbox("nonexistent", true)));

        Assert.Contains("nonexistent", exception.Message, StringComparison.Ordinal);
    }
}
