using ZXing;

namespace MonitovoPDF.Rendering;

/// <summary>
/// A barcode symbology the service can draw, and the quiet zone it needs.
/// </summary>
/// <param name="Name">The value callers send in a request, lowercase and punctuation-free.</param>
/// <param name="Format">The encoder to use.</param>
/// <param name="QuietZoneModules">
/// Blank margin the encoder includes around the symbol, measured in modules. Linear codes
/// generally require ten; the 2D symbologies need far less. Including it in the symbol means a
/// template author does not have to leave room for it around the field.
/// </param>
public sealed record BarcodeSymbology(string Name, BarcodeFormat Format, int QuietZoneModules)
{
    private static readonly BarcodeSymbology[] Supported =
    [
        new("code128", BarcodeFormat.CODE_128, 10),
        new("code39", BarcodeFormat.CODE_39, 10),
        new("code93", BarcodeFormat.CODE_93, 10),
        new("codabar", BarcodeFormat.CODABAR, 10),
        new("itf", BarcodeFormat.ITF, 10),
        new("ean13", BarcodeFormat.EAN_13, 10),
        new("ean8", BarcodeFormat.EAN_8, 10),
        new("upca", BarcodeFormat.UPC_A, 10),
        new("upce", BarcodeFormat.UPC_E, 10),
        new("msi", BarcodeFormat.MSI, 10),
        new("plessey", BarcodeFormat.PLESSEY, 10),
        new("qr", BarcodeFormat.QR_CODE, 4),
        new("datamatrix", BarcodeFormat.DATA_MATRIX, 2),
        new("aztec", BarcodeFormat.AZTEC, 2),
        new("pdf417", BarcodeFormat.PDF_417, 2),
    ];

    private static readonly Dictionary<string, BarcodeSymbology> ByName =
        Supported.ToDictionary(symbology => symbology.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every symbology name a caller may ask for, in a stable order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Supported.Select(symbology => symbology.Name)];

    public static bool TryParse(string? name, out BarcodeSymbology symbology)
    {
        symbology = null!;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out symbology!);
    }
}

/// <summary>A barcode to draw into a named template field.</summary>
public sealed record BarcodeContent(BarcodeSymbology Symbology, string Value);
