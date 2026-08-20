using MonitovoPDF.Server.Api;

namespace MonitovoPDF.Tests;

public class RenderLabelRequestDecoderTests
{
    private static readonly string ValidTemplate =
        Convert.ToBase64String(SyntheticTemplate.WithFields(new SyntheticTemplate.Field("title", 10, 60, 190, 90)));

    private static bool TryDecode(RenderLabelRequest request, out List<string> errors, Action<RenderingOptions>? configure = null)
    {
        var options = new RenderingOptions();
        configure?.Invoke(options);

        return RenderLabelRequestDecoder.TryDecode(request, options, out _, out errors);
    }

    [Fact]
    public void Decodes_AWellFormedRequest()
    {
        var accepted = TryDecode(
            new RenderLabelRequest
            {
                Template = ValidTemplate,
                Fields = new Dictionary<string, string> { ["title"] = "WIDGET-4471" }
            },
            out var errors);

        Assert.True(accepted);
        Assert.Empty(errors);
    }

    [Fact]
    public void Rejects_ATemplateOverTheSizeCeiling()
    {
        // Derived from the fixture so the ceiling is always just under the real template size.
        var oneByteShort = Convert.FromBase64String(ValidTemplate).Length - 1;

        var accepted = TryDecode(
            new RenderLabelRequest { Template = ValidTemplate },
            out var errors,
            options => options.MaxTemplateBytes = oneByteShort);

        Assert.False(accepted);
        Assert.Contains(errors, error => error.Contains("template", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_AnImageOverTheSizeCeiling()
    {
        var accepted = TryDecode(
            new RenderLabelRequest
            {
                Template = ValidTemplate,
                Images = new Dictionary<string, string> { ["logo"] = Convert.ToBase64String(new byte[4096]) }
            },
            out var errors,
            options => options.MaxImageBytes = 1024);

        Assert.False(accepted);
        Assert.Contains(errors, error => error.Contains("logo", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_MoreFieldsThanTheCeilingAllows()
    {
        var fields = Enumerable.Range(0, 5).ToDictionary(i => $"field{i}", _ => "value");

        var accepted = TryDecode(
            new RenderLabelRequest { Template = ValidTemplate, Fields = fields },
            out var errors,
            options => options.MaxFieldCount = 4);

        Assert.False(accepted);
        Assert.Contains(errors, error => error.Contains("at most 4 fields", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_ATextValueOverTheLengthCeiling()
    {
        var accepted = TryDecode(
            new RenderLabelRequest
            {
                Template = ValidTemplate,
                Fields = new Dictionary<string, string> { ["title"] = new('x', 100) }
            },
            out var errors,
            options => options.MaxTextLength = 10);

        Assert.False(accepted);
        Assert.Contains(errors, error => error.Contains("title", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_AFieldGivenBothTextAndAnImage()
    {
        var accepted = TryDecode(
            new RenderLabelRequest
            {
                Template = ValidTemplate,
                Fields = new Dictionary<string, string> { ["title"] = "WIDGET-4471" },
                Images = new Dictionary<string, string> { ["title"] = Convert.ToBase64String(new byte[16]) }
            },
            out var errors);

        Assert.False(accepted);
        Assert.Contains(errors, error => error.Contains("more than one value", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all!!")]
    public void Rejects_AnUnusableTemplate(string? template)
    {
        var accepted = TryDecode(new RenderLabelRequest { Template = template }, out var errors);

        Assert.False(accepted);
        Assert.NotEmpty(errors);
    }
}
