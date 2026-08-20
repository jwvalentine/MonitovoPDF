using MonitovoPDF.Rendering;
using PdfSharp.Drawing;
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
    private static bool _fontsVerified;

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

        if (builder.Text.Count > 0)
            EnsureFontsAvailable(options);

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
            Install(new FileSystemFontResolver(directory, fallbackFamily, onWarning), force);
        }
    }

    /// <summary>
    /// Installs a font resolver, refusing to displace one this library did not put there.
    /// </summary>
    /// <remarks>
    /// Two separate constraints apply. Only one resolver exists per process, so replacing a
    /// foreign one would change how the rest of the application renders text. And PDFsharp fixes
    /// the resolver as soon as it is first used, so this cannot be called after a render at all —
    /// its own message for that is "You must not change font resolver after is was once used",
    /// which is translated here into something that says what to do.
    /// </remarks>
    private static void Install(IFontResolver resolver, bool force)
    {
        var installed = GlobalFontSettings.FontResolver;

        if (!force && installed is not null and not FileSystemFontResolver and not BundledFontResolver)
        {
            throw new InvalidOperationException(
                $"A font resolver of type {installed.GetType().FullName} is already installed. "
                + "PDFsharp allows only one per process, so replacing it would change how the "
                + "rest of the application renders text. Pass force: true to replace it anyway.");
        }

        try
        {
            GlobalFontSettings.FontResolver = resolver;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "Fonts can only be configured before the first render. PDFsharp fixes its font "
                + "resolver as soon as one is used, and will not accept another. Call "
                + "UseFontDirectory or UseBundledFonts once, at start-up.", exception);
        }

        _fontsVerified = false;
    }

    /// <summary>
    /// Draws text with the font embedded in this library, for hosts that have none of their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bundled font is DejaVu Sans, and every family a template asks for resolves to it. That
    /// makes this a working last resort rather than a substitute for real font configuration:
    /// DejaVu's metrics are not those of Arial or Helvetica, so text will occupy a different width
    /// than the template's designer saw, and shrink-to-fit may engage where it did not before. If
    /// a document's layout matters, supply the real fonts with <see cref="UseFontDirectory"/>.
    /// </para>
    /// <para>
    /// This sets process-wide state, with the same guard as <see cref="UseFontDirectory"/>.
    /// </para>
    /// </remarks>
    /// <param name="force">Replace a font resolver that this library did not install.</param>
    /// <exception cref="InvalidOperationException">
    /// Another font resolver is already installed and <paramref name="force"/> was not set.
    /// </exception>
    public static void UseBundledFonts(bool force = false)
    {
        lock (FontGate)
        {
            Install(new BundledFontResolver(), force);
        }
    }

    /// <summary>
    /// Draws text with the fonts installed on the host. Adequate on Windows; a Linux container
    /// usually has none, so <see cref="UseFontDirectory"/> or <see cref="UseBundledFonts"/> is
    /// needed there.
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
    /// Falls back to the host's fonts when the caller has configured nothing, then checks that a
    /// font can actually be had. An explicit resolver is never displaced.
    /// </summary>
    /// <remarks>
    /// The check exists because the underlying failure is otherwise a bare "No appropriate font
    /// found for family name" from deep inside the PDF engine, which says nothing about what to do
    /// next. A host with no fonts is the normal case for a slim Linux container, so this is a
    /// situation callers meet routinely and should be told how to fix.
    /// </remarks>
    private static void EnsureFontsAvailable(RenderingOptions options)
    {
        if (_fontsVerified)
            return;

        lock (FontGate)
        {
            if (_fontsVerified)
                return;

            if (GlobalFontSettings.FontResolver is null && !_platformFontsEnabled)
                UseInstalledFonts();

            try
            {
                _ = new XFont(options.DefaultFontFamily, 10);
            }
            catch (Exception exception)
            {
                throw new TemplateRenderException(
                    $"No font is available to draw text with: nothing could resolve the family "
                    + $"'{options.DefaultFontFamily}'. A host with no fonts installed is normal for a "
                    + "slim Linux container. Call MonitovoPdf.UseBundledFonts() to draw with the font "
                    + "embedded in this library, or MonitovoPdf.UseFontDirectory(path) to supply your "
                    + "own, once at start-up.", exception);
            }

            _fontsVerified = true;
        }
    }
}
