using System.ComponentModel.DataAnnotations;

namespace MonitovoPDF;

/// <summary>
/// Ceilings and defaults for template rendering.
/// </summary>
/// <remarks>
/// Templates are frequently untrusted, even in process, so every bound is explicit rather than
/// baked into the code. The defaults are deliberately conservative; raise them knowingly.
/// </remarks>
public sealed class RenderingOptions : IValidatableObject
{
    /// <summary>Configuration section this binds to when hosted.</summary>
    public const string SectionName = "Rendering";

    /// <summary>Largest accepted template.</summary>
    [Range(1024, 104_857_600)]
    public int MaxTemplateBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Largest accepted image.</summary>
    [Range(1024, 104_857_600)]
    public int MaxImageBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Maximum number of fields a single render may populate.</summary>
    [Range(1, 1000)]
    public int MaxFieldCount { get; set; } = 100;

    /// <summary>Maximum length of a single text or barcode value.</summary>
    [Range(1, 100_000)]
    public int MaxTextLength { get; set; } = 4096;

    /// <summary>What to do when a value is set on a field the template does not define.</summary>
    public MissingFieldBehaviour OnMissingField { get; set; } = MissingFieldBehaviour.Throw;

    /// <summary>Templates with more pages than this are rejected.</summary>
    [Range(1, 1000)]
    public int MaxPages { get; set; } = 10;

    /// <summary>Font used to draw text.</summary>
    [Required(AllowEmptyStrings = false)]
    public string DefaultFontFamily { get; set; } = "Arial";

    /// <summary>Size used when a field does not specify one, or specifies auto-size.</summary>
    [Range(1.0, 400.0)]
    public double DefaultFontSizePoints { get; set; } = 10;

    /// <summary>Text too wide for its field is shrunk to fit, but never below this size.</summary>
    [Range(1.0, 400.0)]
    public double MinimumFontSizePoints { get; set; } = 5;

    /// <summary>
    /// How much of a barcode's height is given to its readable value, when one is shown.
    /// </summary>
    /// <remarks>
    /// The text is drawn inside the barcode's own space rather than beside it, so this is height
    /// taken away from the bars. Raising it buys legibility at the cost of bar height, and bar
    /// height is what lets a scanner read a symbol that is not squarely presented to it.
    /// </remarks>
    [Range(0.05, 0.5)]
    public double BarcodeCaptionHeightFraction { get; set; } = 0.2;

    /// <summary>
    /// Directory of TrueType files to draw text with. Applying it installs a process-wide font
    /// resolver — see <see cref="MonitovoPdf.UseFontDirectory"/>, which is the supported way to
    /// set it and explains why the scope matters.
    /// </summary>
    public string? FontDirectory { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinimumFontSizePoints > DefaultFontSizePoints)
        {
            yield return new ValidationResult(
                $"{nameof(MinimumFontSizePoints)} must not exceed {nameof(DefaultFontSizePoints)}.",
                [nameof(MinimumFontSizePoints), nameof(DefaultFontSizePoints)]);
        }

        if (!string.IsNullOrWhiteSpace(FontDirectory) && !Directory.Exists(FontDirectory))
        {
            yield return new ValidationResult(
                $"{nameof(FontDirectory)} '{FontDirectory}' does not exist.",
                [nameof(FontDirectory)]);
        }
    }
}
