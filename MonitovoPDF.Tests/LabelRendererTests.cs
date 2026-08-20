using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

using MonitovoPDF.Rendering;

namespace MonitovoPDF.Tests;

public class LabelRendererTests
{
    private static readonly SyntheticTemplate.Field Title = new("title", 10, 60, 190, 90);
    private static readonly SyntheticTemplate.Field Logo = new("logo", 10, 10, 60, 50);

    static LabelRendererTests()
    {
        // Mirrors the host's fallback when no font directory is configured.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;
    }

    private static LabelRenderer CreateRenderer(Action<RenderingOptions>? configure = null)
    {
        var options = new RenderingOptions();
        configure?.Invoke(options);

        return new LabelRenderer(options);
    }

    [Fact]
    public void Render_WritesTheTextIntoThePageContent()
    {
        var template = SyntheticTemplate.WithFields(Title);

        var pdf = CreateRenderer().Render(
            template,
            new Dictionary<string, string> { ["title"] = "WIDGET-4471" },
            new Dictionary<string, byte[]>());

        // The value must live in the page content stream, not in a form field value, or viewers
        // that do not generate appearances would print a blank label.
        Assert.Contains("WIDGET-4471", ReadPageContent(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_LeavesNoInteractiveFormBehind()
    {
        var template = SyntheticTemplate.WithFields(Title, Logo);

        var pdf = CreateRenderer().Render(
            template,
            new Dictionary<string, string> { ["title"] = "WIDGET-4471" },
            new Dictionary<string, byte[]> { ["logo"] = SyntheticTemplate.SinglePixelPng() });

        using var output = new MemoryStream(pdf);
        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Modify);

        Assert.Equal(0, document.AcroForm?.Fields.Count ?? 0);

        foreach (var page in document.Pages)
        {
            var annotations = page.Elements.GetArray("/Annots");
            var widgets = 0;

            for (var i = 0; i < (annotations?.Elements.Count ?? 0); i++)
            {
                if (annotations!.Elements[i] is PdfSharp.Pdf.Advanced.PdfReference { Value: PdfDictionary dictionary }
                    && dictionary.Elements.GetName("/Subtype") == "/Widget")
                {
                    widgets++;
                }
            }

            Assert.Equal(0, widgets);
        }
    }

    [Fact]
    public void Render_DrawsTheImageIntoThePage()
    {
        var template = SyntheticTemplate.WithFields(Logo);

        var pdf = CreateRenderer().Render(
            template,
            new Dictionary<string, string>(),
            new Dictionary<string, byte[]> { ["logo"] = SyntheticTemplate.SinglePixelPng() });

        using var output = new MemoryStream(pdf);
        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Modify);

        var resources = document.Pages[0].Elements.GetDictionary("/Resources");
        var xObjects = resources?.Elements.GetDictionary("/XObject");

        Assert.NotNull(xObjects);
        Assert.NotEmpty(xObjects!.Elements.Keys);
    }

    [Fact]
    public void Render_UsesTheFontSizeTheTemplateFieldAsksFor()
    {
        var template = SyntheticTemplate.WithFields(Title);

        var pdf = CreateRenderer().Render(
            template,
            new Dictionary<string, string> { ["title"] = "OK" },
            new Dictionary<string, byte[]>());

        // The synthetic template's field carries "/Helv 9 Tf", so 9pt must survive into the output.
        Assert.Equal(9d, ReadDrawnFontSize(ReadPageContent(pdf)));
    }

    [Fact]
    public void Render_ShrinksTextThatWouldOverflowTheField()
    {
        // A value far wider than the 180pt field must still be drawn in full rather than clipped.
        var template = SyntheticTemplate.WithFields(Title);
        var value = new string('M', 200);
        var options = new RenderingOptions();

        var pdf = CreateRenderer().Render(
            template,
            new Dictionary<string, string> { ["title"] = value },
            new Dictionary<string, byte[]>());

        var content = ReadPageContent(pdf);
        var drawnSize = ReadDrawnFontSize(content);

        Assert.Contains(value, content, StringComparison.Ordinal);
        Assert.True(drawnSize < 9d, $"Expected the font to shrink below 9pt but it was {drawnSize}pt.");
        Assert.True(drawnSize >= options.MinimumFontSizePoints, "The font shrank past the configured floor.");
    }

    [Fact]
    public void Render_RejectsAFieldTheTemplateDoesNotDefine()
    {
        var template = SyntheticTemplate.WithFields(Title);

        var exception = Assert.Throws<TemplateRenderException>(() => CreateRenderer().Render(
            template,
            new Dictionary<string, string> { ["nonexistent"] = "value" },
            new Dictionary<string, byte[]>()));

        Assert.Contains("nonexistent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_LeavesTheLabelUntouchedWhenOneFieldIsUnknown()
    {
        var template = SyntheticTemplate.WithFields(Title);

        // A partially populated label is worse than a failed request, so nothing is drawn.
        Assert.Throws<TemplateRenderException>(() => CreateRenderer().Render(
            template,
            new Dictionary<string, string> { ["title"] = "WIDGET-4471", ["missing"] = "value" },
            new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void Render_RejectsATemplateThatIsNotAPdf()
    {
        var notAPdf = Encoding.ASCII.GetBytes("this is not a PDF document");

        Assert.Throws<TemplateRenderException>(() => CreateRenderer().Render(
            notAPdf,
            new Dictionary<string, string>(),
            new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void Render_RejectsATemplateWithNoFormFields()
    {
        var template = SyntheticTemplate.WithFields();

        var exception = Assert.Throws<TemplateRenderException>(() => CreateRenderer().Render(
            template,
            new Dictionary<string, string> { ["title"] = "WIDGET-4471" },
            new Dictionary<string, byte[]>()));

        Assert.Contains("field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_RejectsATemplateExceedingThePageCeiling()
    {
        var template = SyntheticTemplate.WithFields(Title);

        var exception = Assert.Throws<TemplateRenderException>(() =>
            CreateRenderer(options => options.MaxPages = 0).Render(
                template,
                new Dictionary<string, string> { ["title"] = "WIDGET-4471" },
                new Dictionary<string, byte[]>()));

        Assert.Contains("pages", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_RejectsAnImageThatCannotBeDecoded()
    {
        var template = SyntheticTemplate.WithFields(Logo);

        Assert.Throws<TemplateRenderException>(() => CreateRenderer().Render(
            template,
            new Dictionary<string, string>(),
            new Dictionary<string, byte[]> { ["logo"] = [0x00, 0x01, 0x02, 0x03] }));
    }

    /// <summary>Reads the point size out of the text-font operator, as in "/F0 9 Tf".</summary>
    private static double ReadDrawnFontSize(string content)
    {
        var match = Regex.Match(content, @"/F\d+\s+([\d.]+)\s+Tf");
        Assert.True(match.Success, "The content stream contains no text-font operator.");

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Decompresses the first page's content stream so drawn text can be asserted on.</summary>
    private static string ReadPageContent(byte[] pdf) => PdfContent.OfFirstPage(pdf);
}
