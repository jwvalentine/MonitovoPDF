using MonitovoPDF.Rendering;

namespace MonitovoPDF;

/// <summary>
/// Maps <see cref="BarcodeType"/> to and from short names, for callers driving the library from
/// configuration, a command line or a request body.
/// </summary>
public static class BarcodeTypes
{
    /// <summary>Every name <see cref="TryParse"/> accepts, in a stable order.</summary>
    public static IReadOnlyList<string> Names => BarcodeSymbology.Names;

    /// <summary>
    /// Parses a short name such as "code128", "qr" or "datamatrix". Matching ignores case and
    /// surrounding whitespace.
    /// </summary>
    public static bool TryParse(string? name, out BarcodeType type)
    {
        if (BarcodeSymbology.TryParse(name, out var symbology))
        {
            type = symbology.Type;
            return true;
        }

        type = default;
        return false;
    }

    /// <summary>Returns the short name for a type, as used by <see cref="TryParse"/>.</summary>
    public static string NameOf(BarcodeType type) => BarcodeSymbology.For(type).Name;
}
