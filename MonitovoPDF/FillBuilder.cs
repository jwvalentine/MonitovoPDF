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
    private readonly Dictionary<string, TextContent> _text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BarcodeContent> _barcodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Page, int Index), ImageSlotContent> _slots = [];

    internal IReadOnlyDictionary<string, TextContent> Text => _text;

    internal IReadOnlyDictionary<string, byte[]> Images => _images;

    internal IReadOnlyDictionary<string, BarcodeContent> Barcodes => _barcodes;

    internal IReadOnlyDictionary<string, FieldState> States => _states;

    internal IReadOnlyDictionary<(int Page, int Index), ImageSlotContent> Slots => _slots;

    internal int Count =>
        _text.Count + _images.Count + _barcodes.Count + _slots.Count + _states.Count;

    /// <summary>Draws <paramref name="value"/> into the field called <paramref name="field"/>.</summary>
    /// <remarks>
    /// The value is drawn as the template field specifies unless <paramref name="options"/> says
    /// otherwise. A field that appears more than once in the template is drawn in every place it
    /// appears.
    /// </remarks>
    /// <exception cref="ArgumentException">The field name is empty, or already has a value.</exception>
    public FillBuilder SetText(string field, string value, TextOptions? options = null)
    {
        Claim(field);
        ArgumentNullException.ThrowIfNull(value);

        // A colour that cannot be read would otherwise be ignored, and a value drawn in black
        // when a colour was asked for is the kind of wrong that only shows up in print.
        if (options?.Colour is { } colour && LabelRenderer.ParseColour(colour) is null)
        {
            throw new ArgumentException(
                $"'{colour}' is not a colour. Give it as #RRGGBB, for example #C00000.", nameof(options));
        }

        _text[field] = new TextContent(value, options);
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
    public FillBuilder SetBarcode(string field, BarcodeType type, string value, BarcodeOptions? options = null)
    {
        Claim(field);
        Check(value, options);

        _barcodes[field] = new BarcodeContent(BarcodeSymbology.For(type), value, options);
        return this;
    }

    /// <summary>
    /// Ticks or clears a tick box.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The box is drawn as the template draws it — its own tick, in its own box, at its own
    /// weight — rather than as a character this library chooses. A box the template gives no
    /// artwork for falls back to a drawn outline and cross.
    /// </para>
    /// <para>
    /// Clearing a box is not the same as leaving it alone. A box left alone keeps whatever state
    /// the template shipped it in, which for some templates is ticked.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The field name is empty, or already has a value.</exception>
    public FillBuilder SetCheckbox(string field, bool ticked)
    {
        Claim(field);
        _states[field] = new FieldState(ticked ? null : FieldAppearances.OffState, Ticked: ticked);

        return this;
    }

    /// <summary>
    /// Selects one of the options a dropdown, list box or set of radio buttons offers.
    /// </summary>
    /// <remarks>
    /// The options belong to the template rather than to the caller: a dropdown carries its own
    /// list, and a set of radio buttons is defined by the states its buttons answer to. So this
    /// chooses from what is already there, and a value that is not among them is refused rather
    /// than drawn — a form recording an answer it does not offer is worse than one that fails.
    /// <see cref="TemplateField.Options"/> reports what a given field will accept.
    /// </remarks>
    /// <exception cref="ArgumentException">The field name or value is empty, or already set.</exception>
    public FillBuilder SetChoice(string field, string value)
    {
        Claim(field);
        ArgumentException.ThrowIfNullOrEmpty(value);

        _states[field] = new FieldState(value, Ticked: null);
        return this;
    }

    /// <summary>
    /// Selects several options at once, for a list box that permits more than one.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// No values were given, the field name is empty, or the field already has a value.
    /// </exception>
    public FillBuilder SetChoice(string field, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var chosen = values.ToList();
        if (chosen.Count == 0 || chosen.Any(string.IsNullOrEmpty))
            throw new ArgumentException("A choice needs at least one non-empty value.", nameof(values));

        Claim(field);
        _states[field] = new FieldState(chosen[0], Ticked: null) { Values = chosen };

        return this;
    }

    /// <summary>
    /// Replaces a page's image placeholder, addressed by its position among the page's images.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For templates whose placeholders are images rather than form fields. The replacement
    /// inherits the placeholder's position and size exactly, and is stretched to fill it whatever
    /// its own proportions — the page's drawing instructions are not altered, only what they draw.
    /// </para>
    /// <para>
    /// Placeholders are numbered from 1, in order of their resource name with embedded numbers
    /// compared as numbers. <see cref="MonitovoPdf.Inspect(byte[], RenderingOptions?)"/> reports
    /// the numbering for a given template, which is worth checking rather than assuming.
    /// </para>
    /// </remarks>
    /// <param name="pageNumber">One-based page number.</param>
    /// <param name="imageIndex">One-based position among that page's image placeholders.</param>
    /// <param name="image">The replacement image.</param>
    /// <exception cref="ArgumentOutOfRangeException">A page or index below one.</exception>
    /// <exception cref="ArgumentException">That placeholder already has a replacement.</exception>
    public FillBuilder SetImageAt(int pageNumber, int imageIndex, byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ClaimSlot(pageNumber, imageIndex);

        _slots[(pageNumber, imageIndex)] = new ImageSlotContent(image, null);
        return this;
    }

    /// <summary>Replaces a page's image placeholder with an image read from a stream.</summary>
    public FillBuilder SetImageAt(int pageNumber, int imageIndex, Stream image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var buffer = new MemoryStream();
        image.CopyTo(buffer);

        return SetImageAt(pageNumber, imageIndex, buffer.ToArray());
    }

    /// <summary>
    /// Draws a barcode into a page's image placeholder, addressed by position.
    /// </summary>
    /// <remarks>
    /// The barcode replaces the placeholder as vector graphics rather than as a picture, so its
    /// edges stay exact at any resolution while still inheriting the placeholder's geometry.
    /// </remarks>
    public FillBuilder SetBarcodeAt(
        int pageNumber, int imageIndex, BarcodeType type, string value, BarcodeOptions? options = null)
    {
        ClaimSlot(pageNumber, imageIndex);
        Check(value, options);

        _slots[(pageNumber, imageIndex)] =
            new ImageSlotContent(null, new BarcodeContent(BarcodeSymbology.For(type), value, options));

        return this;
    }

    /// <summary>Rejects a barcode request that cannot produce anything sensible.</summary>
    private static void Check(string value, BarcodeOptions? options)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("A barcode needs a value to encode.", nameof(value));

        // A caption taking more than half the barcode's height leaves too little bar to scan, and
        // is far likelier to be a mistaken unit — a point size, or a percentage — than an intent.
        if (options?.CaptionHeightFraction is { } fraction && fraction is <= 0 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                fraction,
                "The readable value may take between none and half of a barcode's height.");
        }
    }

    private void ClaimSlot(int pageNumber, int imageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(imageIndex, 1);

        if (_slots.ContainsKey((pageNumber, imageIndex)))
        {
            throw new ArgumentException(
                $"Image {imageIndex} on page {pageNumber} already has a replacement.", nameof(imageIndex));
        }
    }

    private void Claim(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("A field name is required.", nameof(field));

        if (_text.ContainsKey(field) || _images.ContainsKey(field)
            || _barcodes.ContainsKey(field) || _states.ContainsKey(field))
            throw new ArgumentException($"Field '{field}' already has a value.", nameof(field));
    }
}
