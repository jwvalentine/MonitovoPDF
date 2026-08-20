namespace MonitovoPDF;

/// <summary>
/// How a barcode is drawn, beyond the bars themselves.
/// </summary>
/// <remarks>
/// <para>
/// A barcode's value printed as readable text below the bars is what somebody falls back to when
/// a scanner is not to hand or the symbol has been scuffed: they read the number and key it in.
/// A label carrying its number only as bars has no fallback at all. Human-readable interpretation
/// is normal practice for printed barcodes, and part of the symbology specification for some of
/// them, so this is worth turning on for anything a person will handle.
/// </para>
/// <para>
/// It is off by default because it changes the geometry rather than adding to it. The text is
/// drawn inside the space the barcode was already given, so the bars become shorter to make room.
/// Shorter bars are marginally harder to scan at an angle, which makes this a deliberate trade
/// rather than a free improvement.
/// </para>
/// </remarks>
public sealed record BarcodeOptions
{
    /// <summary>Whether the value is printed as readable text below the bars.</summary>
    public bool ShowValue { get; init; }

    /// <summary>
    /// How much of the barcode's height the readable text is given, as a fraction of it. Null
    /// uses <see cref="RenderingOptions.BarcodeCaptionHeightFraction"/>.
    /// </summary>
    public double? CaptionHeightFraction { get; init; }

    /// <summary>Family the readable text is drawn in. Null uses the default font.</summary>
    public string? CaptionFontFamily { get; init; }

    /// <summary>
    /// Size the readable text is drawn at, in points. Null derives it from the space reserved,
    /// which keeps the text in proportion with the barcode at whatever size the barcode lands.
    /// </summary>
    /// <remarks>
    /// A caption too wide for the barcode is shrunk until it fits, whichever way its size was
    /// arrived at, because a caption running past the bars is worse than a small one.
    /// </remarks>
    public double? CaptionFontSizePoints { get; init; }
}
