using ZXing;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Maps a <see cref="BarcodeType"/> to the encoder that draws it and the quiet zone it needs.
/// </summary>
/// <param name="Type">The symbology.</param>
/// <param name="Name">Lowercase, punctuation-free name used by the HTTP surface.</param>
/// <param name="Format">The encoder to use.</param>
/// <param name="QuietZoneModules">
/// Blank margin the encoder includes around the symbol, in modules. Linear codes generally
/// require ten; the 2D symbologies need far less. Including it in the symbol means a template
/// author does not have to leave room for it around the field.
/// </param>
internal sealed record BarcodeSymbology(
    BarcodeType Type, string Name, BarcodeFormat Format, int QuietZoneModules)
{
    private static readonly BarcodeSymbology[] Supported =
    [
        new(BarcodeType.Code128, "code128", BarcodeFormat.CODE_128, 10),
        new(BarcodeType.Code39, "code39", BarcodeFormat.CODE_39, 10),
        new(BarcodeType.Code93, "code93", BarcodeFormat.CODE_93, 10),
        new(BarcodeType.Codabar, "codabar", BarcodeFormat.CODABAR, 10),
        new(BarcodeType.Itf, "itf", BarcodeFormat.ITF, 10),
        new(BarcodeType.Ean13, "ean13", BarcodeFormat.EAN_13, 10),
        new(BarcodeType.Ean8, "ean8", BarcodeFormat.EAN_8, 10),
        new(BarcodeType.UpcA, "upca", BarcodeFormat.UPC_A, 10),
        new(BarcodeType.UpcE, "upce", BarcodeFormat.UPC_E, 10),
        new(BarcodeType.Msi, "msi", BarcodeFormat.MSI, 10),
        new(BarcodeType.Plessey, "plessey", BarcodeFormat.PLESSEY, 10),
        new(BarcodeType.QrCode, "qr", BarcodeFormat.QR_CODE, 4),
        new(BarcodeType.DataMatrix, "datamatrix", BarcodeFormat.DATA_MATRIX, 2),
        new(BarcodeType.Aztec, "aztec", BarcodeFormat.AZTEC, 2),
        new(BarcodeType.Pdf417, "pdf417", BarcodeFormat.PDF_417, 2),
    ];

    private static readonly Dictionary<string, BarcodeSymbology> ByName =
        Supported.ToDictionary(symbology => symbology.Name, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<BarcodeType, BarcodeSymbology> ByType =
        Supported.ToDictionary(symbology => symbology.Type);

    /// <summary>Every symbology name a caller may ask for, in a stable order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Supported.Select(symbology => symbology.Name)];

    public static BarcodeSymbology For(BarcodeType type) =>
        ByType.TryGetValue(type, out var symbology)
            ? symbology
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown barcode type.");

    public static bool TryParse(string? name, out BarcodeSymbology symbology)
    {
        symbology = null!;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name!.Trim(), out symbology!);
    }
}

/// <summary>A barcode to draw into a named template field.</summary>
internal sealed record BarcodeContent(BarcodeSymbology Symbology, string Value);

/// <summary>Text to draw into a field, with any caller overrides for how it should look.</summary>
internal sealed record TextContent(string Value, TextOptions? Options);
