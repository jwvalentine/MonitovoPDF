namespace MonitovoPDF;

/// <summary>
/// A barcode symbology the library can draw.
/// </summary>
/// <remarks>
/// Values are encoded exactly as given: no check digit is calculated. A caller using a
/// symbology that carries one — the retail codes, or ITF-14 — must supply a correct digit, or
/// the result is a barcode that scans cleanly and carries the wrong number.
/// </remarks>
public enum BarcodeType
{
    /// <summary>Code 128. Full ASCII, and the usual choice for a general-purpose label.</summary>
    Code128,

    /// <summary>Code 39. Uppercase letters, digits, and <c>- . $ / + %</c> and space.</summary>
    Code39,

    /// <summary>Code 93. Uppercase letters, digits, and some punctuation.</summary>
    Code93,

    /// <summary>Codabar. Digits, delimited by start and stop characters A to D.</summary>
    Codabar,

    /// <summary>Interleaved 2 of 5. Digits only, in an even count.</summary>
    Itf,

    /// <summary>EAN-13. Twelve or thirteen digits.</summary>
    Ean13,

    /// <summary>EAN-8. Seven or eight digits.</summary>
    Ean8,

    /// <summary>UPC-A. Eleven or twelve digits.</summary>
    UpcA,

    /// <summary>UPC-E. Seven or eight digits.</summary>
    UpcE,

    /// <summary>MSI. Digits only. Not verified against a scanner — see the README.</summary>
    Msi,

    /// <summary>Plessey. Digits only. Not verified against a scanner — see the README.</summary>
    Plessey,

    /// <summary>QR Code. Any text.</summary>
    QrCode,

    /// <summary>Data Matrix. Any text.</summary>
    DataMatrix,

    /// <summary>Aztec. Any text.</summary>
    Aztec,

    /// <summary>PDF417. Any text.</summary>
    Pdf417,
}
