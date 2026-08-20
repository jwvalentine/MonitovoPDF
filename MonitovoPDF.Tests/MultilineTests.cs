using System.Text.RegularExpressions;
using MonitovoPDF;

namespace MonitovoPDF.Tests;

/// <summary>
/// Covers values that occupy more than one line.
/// </summary>
/// <remarks>
/// Counting the text-showing operators is what distinguishes wrapping from not wrapping: a single
/// operator means everything was drawn as one run, which for a list is a line of run-together text
/// spilling out of its field.
/// </remarks>
public class MultilineTests
{
    private static readonly SyntheticTemplate.Field Box = new("notes", 10, 10, 190, 190);

    private static int LinesDrawn(byte[] pdf) =>
        Regex.Matches(PdfContent.OfFirstPage(pdf), @"\bTj\b|\bTJ\b").Count;

    [Fact]
    public void AValueWithLineBreaks_IsDrawnAsSeveralLines()
    {
        var pdf = TestRender.Fill(
            SyntheticTemplate.WithFields(Box),
            new Dictionary<string, string> { ["notes"] = "first\nsecond\nthird" });

        Assert.Equal(3, LinesDrawn(pdf));

        var content = PdfContent.OfFirstPage(pdf);
        Assert.Contains("first", content, StringComparison.Ordinal);
        Assert.Contains("second", content, StringComparison.Ordinal);
        Assert.Contains("third", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsLineBreaks_AreTreatedTheSame()
    {
        var pdf = TestRender.Fill(
            SyntheticTemplate.WithFields(Box),
            new Dictionary<string, string> { ["notes"] = "first\r\nsecond" });

        Assert.Equal(2, LinesDrawn(pdf));
    }

    [Fact]
    public void AFieldFlaggedMultiline_WrapsLongTextOnWordBoundaries()
    {
        // No line breaks in the value at all: the wrapping has to come from the field's own flag.
        var words = string.Join(" ", Enumerable.Repeat("wrapping", 40));

        var pdf = TestRender.Fill(
            SyntheticTemplate.WithMultilineField(Box),
            new Dictionary<string, string> { ["notes"] = words });

        Assert.True(LinesDrawn(pdf) > 1, "A long value in a multiline field was drawn as one line.");
    }

    [Fact]
    public void ASingleLineFieldDoesNotWrap()
    {
        // Without the flag and without line breaks, the existing behaviour stands: one line,
        // shrunk to fit rather than reflowed.
        var words = string.Join(" ", Enumerable.Repeat("wrapping", 40));

        var pdf = TestRender.Fill(
            SyntheticTemplate.WithFields(Box),
            new Dictionary<string, string> { ["notes"] = words });

        Assert.Equal(1, LinesDrawn(pdf));
    }

    [Fact]
    public void WrappingCanBeAskedForExplicitly()
    {
        var words = string.Join(" ", Enumerable.Repeat("wrapping", 40));

        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithFields(Box),
            fill => fill.SetText("notes", words, new TextOptions { Multiline = true }));

        Assert.True(LinesDrawn(pdf) > 1, "Multiline was requested and ignored.");
    }

    [Fact]
    public void WrappingCanBeTurnedOffForAFieldThatAsksForIt()
    {
        var pdf = MonitovoPdf.Fill(
            SyntheticTemplate.WithMultilineField(Box),
            fill => fill.SetText("notes", "one two three", new TextOptions { Multiline = false }));

        Assert.Equal(1, LinesDrawn(pdf));
    }

    [Fact]
    public void MoreLinesThanFit_AreClippedRatherThanDrawnOutsideTheField()
    {
        // A short field and far too much text: whatever is drawn must stay inside the box, since
        // text running off a document is worse than text that is cut short.
        var shallow = new SyntheticTemplate.Field("notes", 10, 150, 190, 190);
        var many = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"line{i}"));

        var pdf = TestRender.Fill(
            SyntheticTemplate.WithFields(shallow),
            new Dictionary<string, string> { ["notes"] = many });

        Assert.True(LinesDrawn(pdf) < 60, "Every line was drawn despite the field being far too short.");
        Assert.True(LinesDrawn(pdf) > 0, "Nothing was drawn at all.");
    }
}
