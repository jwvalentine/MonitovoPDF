using System.ComponentModel.DataAnnotations;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Ceilings and defaults for template rendering. The service accepts untrusted documents, so
/// every bound is explicit and configuration-driven rather than baked into the code.
/// </summary>
public sealed class RenderingOptions : IValidatableObject
{
    public const string SectionName = "Rendering";

    /// <summary>Largest accepted template, measured after base64 decoding.</summary>
    [Range(1024, 104_857_600)]
    public int MaxTemplateBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Largest accepted image, measured after base64 decoding.</summary>
    [Range(1024, 104_857_600)]
    public int MaxImageBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// Largest accepted request body. Enforced by the server before the body is buffered, so it
    /// is the outermost bound; the per-item limits apply to the decoded payload within it.
    /// </summary>
    [Range(1024, 209_715_200)]
    public long MaxRequestBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Maximum number of fields a single request may populate.</summary>
    [Range(1, 1000)]
    public int MaxFieldCount { get; set; } = 100;

    /// <summary>Maximum length of a single text value.</summary>
    [Range(1, 100_000)]
    public int MaxTextLength { get; set; } = 4096;

    /// <summary>Templates with more pages than this are rejected.</summary>
    [Range(1, 1000)]
    public int MaxPages { get; set; } = 10;

    /// <summary>Wall-clock ceiling for a single render.</summary>
    [Range(100, 600_000)]
    public int RenderTimeoutMilliseconds { get; set; } = 15_000;

    /// <summary>Font used when a template field does not name one.</summary>
    [Required(AllowEmptyStrings = false)]
    public string DefaultFontFamily { get; set; } = "Arial";

    /// <summary>Size used when a template field does not specify one, or specifies auto-size.</summary>
    [Range(1.0, 400.0)]
    public double DefaultFontSizePoints { get; set; } = 10;

    /// <summary>Text too wide for its field is shrunk to fit, but never below this size.</summary>
    [Range(1.0, 400.0)]
    public double MinimumFontSizePoints { get; set; } = 5;

    /// <summary>
    /// Directory of TrueType files to draw text with. PDFsharp's Core build loads no fonts on its
    /// own and a Linux container usually has none installed, so a deployment must ship the fonts
    /// it needs. When empty, the platform font resolver is used, which is adequate on Windows.
    /// </summary>
    public string? FontDirectory { get; set; }

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
