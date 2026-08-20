using MonitovoPDF.Rendering;

namespace MonitovoPDF;

/// <summary>
/// Collects the values to draw into a template, each keyed by the name of the form field that
/// marks where it goes.
/// </summary>
/// <remarks>
/// A field may be given a value once. Setting the same field twice, or giving it both text and a
/// barcode, throws rather than silently letting one win.
/// </remarks>
public sealed class FillBuilder
{
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BarcodeContent> _barcodes = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, string> Text => _text;

    internal IReadOnlyDictionary<string, byte[]> Images => _images;

    internal IReadOnlyDictionary<string, BarcodeContent> Barcodes => _barcodes;

    internal int Count => _text.Count + _images.Count + _barcodes.Count;

    /// <summary>Draws <paramref name="value"/> into the field called <paramref name="field"/>.</summary>
    /// <exception cref="ArgumentException">The field name is empty, or already has a value.</exception>
    public FillBuilder SetText(string field, string value)
    {
        Claim(field);
        _text[field] = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    /// <summary>Draws an image into the field, scaled to fit and centred, keeping its aspect ratio.</summary>
    /// <exception cref="ArgumentException">The field name is empty, or already has a value.</exception>
    public FillBuilder SetImage(string field, byte[] image)
    {
        Claim(field);
        _images[field] = image ?? throw new ArgumentNullException(nameof(image));
        return this;
    }

    /// <summary>Draws an image read from a stream into the field.</summary>
    public FillBuilder SetImage(string field, Stream image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var buffer = new MemoryStream();
        image.CopyTo(buffer);
        return SetImage(field, buffer.ToArray());
    }

    /// <summary>
    /// Generates a barcode and draws it into the field as vector graphics, so the bar edges stay
    /// exact at any print resolution.
    /// </summary>
    /// <exception cref="ArgumentException">The field name is empty, or already has a value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The barcode type is not a known value.</exception>
    public FillBuilder SetBarcode(string field, BarcodeType type, string value)
    {
        Claim(field);

        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("A barcode needs a value to encode.", nameof(value));

        _barcodes[field] = new BarcodeContent(BarcodeSymbology.For(type), value);
        return this;
    }

    private void Claim(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("A field name is required.", nameof(field));

        if (_text.ContainsKey(field) || _images.ContainsKey(field) || _barcodes.ContainsKey(field))
            throw new ArgumentException($"Field '{field}' already has a value.", nameof(field));
    }
}
