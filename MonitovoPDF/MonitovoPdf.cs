using MonitovoPDF.Rendering;
using PdfSharp.Fonts;

namespace MonitovoPDF;

/// <summary>
/// Fills template PDFs with text, images and barcodes, in process.
/// </summary>
/// <remarks>
/// <para>
/// Placeholders in a template are ordinary AcroForm fields. Their names, pages and rectangles are
/// read to find out where each value goes; the values are then drawn into the page content and
/// the form is stripped, so the finished document is flat.
/// </para>
/// <para>
/// That flatness is deliberate. A PDF whose content lives in form field values depends on the
/// viewer generating field appearances, and many do not — such a document renders correctly in
/// Acrobat and blank in several other viewers and print paths.
/// </para>
/// <example>
/// <code>
/// byte[] pdf = MonitovoPdf.Fill(templateBytes, fill =>
/// {
///     fill.SetText("part_number", "WIDGET-4471");
///     fill.SetImage("logo", logoBytes);
///     fill.SetBarcode("barcode", BarcodeType.Code128, "WIDGET-4471");
/// });
/// </code>
/// </example>
/// </remarks>
public static class MonitovoPdf
{
    private static readonly object FontGate = new();
    private static bool _platformFontsEnabled;

    /// <summary>Fills a template held in memory and returns the finished document.</summary>
    /// <param name="template">The template PDF.</param>
    /// <param name="fill">Callback that sets the values to draw.</param>
    /// <param name="options">Ceilings and defaults. The defaults are used when omitted.</param>
    /// <exception cref="TemplateRenderException">
    /// The template is unreadable, breaches a configured ceiling, does not define a field that was
    /// given a value, or was given a value the barcode symbology cannot encode.
    /// </exception>
    public static byte[] Fill(byte[] template, Action<FillBuilder> fill, RenderingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fill);

        options ??= new RenderingOptions();

        if (template.Length > options.MaxTemplateBytes)
        {
            throw new TemplateRenderException(
                $"The template is {template.Length} bytes, which exceeds the limit of {options.MaxTemplateBytes}.");
        }

        var builder = new FillBuilder();
        fill(builder);

        if (builder.Count > options.MaxFieldCount)
        {
            throw new TemplateRenderException(
                $"A render may populate at most {options.MaxFieldCount} fields, but {builder.Count} were given.");
        }

        foreach (var (field, value) in builder.Text)
        {
            if (value.Length > options.MaxTextLength)
            {
                throw new TemplateRenderException(
                    $"The value for field '{field}' exceeds the {options.MaxTextLength} character limit.");
            }
        }

        EnsureFontsAvailable();

        return new LabelRenderer(options).Render(template, builder.Text, builder.Images, builder.Barcodes);
    }

    /// <summary>Fills a template read from a stream.</summary>
    public static byte[] Fill(Stream template, Action<FillBuilder> fill, RenderingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        using var buffer = new MemoryStream();
        template.CopyTo(buffer);

        return Fill(buffer.ToArray(), fill, options);
    }

    /// <summary>Fills a template read from disk.</summary>
    public static byte[] FillFile(string templatePath, Action<FillBuilder> fill, RenderingOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);

        return Fill(File.ReadAllBytes(templatePath), fill, options);
    }

    /// <summary>Fills a template read from disk and writes the result to disk.</summary>
    public static void FillFile(string templatePath, string outputPath, Action<FillBuilder> fill,
        RenderingOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        File.WriteAllBytes(outputPath, FillFile(templatePath, fill, options));
    }

    /// <summary>
    /// Draws text with the TrueType files in <paramref name="directory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This changes process-wide state.</b> The underlying PDF engine resolves fonts through a
    /// single global hook, so a font resolver installed here applies to everything in the process
    /// that uses PDFsharp, not only to this library. If the host application uses PDFsharp with a
    /// resolver of its own, installing one here would displace it, so that case throws instead of
    /// silently taking over. Pass <paramref name="force"/> to take over deliberately.
    /// </para>
    /// <para>
    /// Face names come from file names, so <c>Arial.ttf</c> serves the "Arial" family, with
    /// optional <c>-Bold</c>, <c>-Italic</c> and <c>-BoldItalic</c> suffixes for those styles.
    /// Call this once at start-up. It is required on Linux, where a slim container has no fonts
    /// installed and text would otherwise fail to draw.
    /// </para>
    /// </remarks>
    /// <param name="directory">Directory of <c>.ttf</c> files.</param>
    /// <param name="fallbackFamily">Family to substitute when a requested one is missing.</param>
    /// <param name="onWarning">Called when a font family is substituted. Optional.</param>
    /// <param name="force">Replace a font resolver that this library did not install.</param>
    /// <exception cref="InvalidOperationException">
    /// Another font resolver is already installed and <paramref name="force"/> was not set.
    /// </exception>
    public static void UseFontDirectory(string directory, string fallbackFamily = "Arial",
        Action<string>? onWarning = null, bool force = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        lock (FontGate)
        {
            var installed = GlobalFontSettings.FontResolver;

            if (installed is not null and not FileSystemFontResolver && !force)
            {
                throw new InvalidOperationException(
                    $"A font resolver of type {installed.GetType().FullName} is already installed. "
                    + "PDFsharp allows only one per process, so replacing it would change how the "
                    + "rest of the application renders text. Pass force: true to replace it anyway.");
            }

            GlobalFontSettings.FontResolver = new FileSystemFontResolver(directory, fallbackFamily, onWarning);
        }
    }

    /// <summary>
    /// Draws text with the fonts installed on the host. Adequate on Windows; a Linux container
    /// usually has none, so <see cref="UseFontDirectory"/> is needed there.
    /// </summary>
    /// <remarks>This sets process-wide state, as <see cref="UseFontDirectory"/> does.</remarks>
    public static void UseInstalledFonts()
    {
        lock (FontGate)
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;
            _platformFontsEnabled = true;
        }
    }

    /// <summary>
    /// Falls back to the host's fonts when the caller has configured nothing, so that a first
    /// render on Windows works without ceremony. An explicit resolver is never displaced.
    /// </summary>
    private static void EnsureFontsAvailable()
    {
        if (_platformFontsEnabled || GlobalFontSettings.FontResolver is not null)
            return;

        UseInstalledFonts();
    }
}
