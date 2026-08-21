namespace MonitovoPDF;

/// <summary>What a template looks like: its pages and the fields it defines.</summary>
/// <remarks>
/// Returned by <see cref="MonitovoPdf.Inspect(byte[], RenderingOptions?)"/>. Reading a template before filling it
/// answers the questions that otherwise only surface as a failed render: whether the page is the
/// size expected, and what a field is actually called.
/// </remarks>
public sealed record TemplateInfo(
    IReadOnlyList<TemplatePage> Pages,
    IReadOnlyList<TemplateField> Fields)
{
    /// <summary>Finds a field by name, or null when the template has no such field.</summary>
    public TemplateField? Field(string name) =>
        Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <summary>Every field name, in the order the template defines them.</summary>
    public IReadOnlyList<string> FieldNames => [.. Fields.Select(candidate => candidate.Name)];
}

/// <summary>One page of a template.</summary>
/// <param name="Number">One-based page number.</param>
/// <param name="WidthPoints">Page width in points, 72 to the inch.</param>
/// <param name="HeightPoints">Page height in points.</param>
/// <param name="Rotation">Degrees the page is rotated for display: 0, 90, 180 or 270.</param>
/// <param name="Images">The image placeholders the page draws, in the order they are addressed.</param>
public sealed record TemplatePage(
    int Number, double WidthPoints, double HeightPoints, int Rotation,
    IReadOnlyList<TemplateImage> Images)
{
    /// <summary>Page width in millimetres, for comparing against a physical size.</summary>
    public double WidthMillimetres => WidthPoints * 25.4 / 72;

    /// <summary>Page height in millimetres.</summary>
    public double HeightMillimetres => HeightPoints * 25.4 / 72;
}

/// <summary>What a template field is called, where it sits, and how it asks to be drawn.</summary>
/// <param name="Name">The name a value is keyed by when filling.</param>
/// <param name="Kind">The field type the template declares.</param>
/// <param name="Placements">Where the field appears. A field may have more than one placement.</param>
/// <param name="FontFamily">Family the field asks for, or null when it names none.</param>
/// <param name="FontSizePoints">Size the field asks for; zero means auto-size.</param>
/// <param name="Alignment">Horizontal placement the field asks for.</param>
/// <param name="IsMultiline">Whether the field is flagged to hold more than one line.</param>
public sealed record TemplateField(
    string Name,
    TemplateFieldKind Kind,
    IReadOnlyList<FieldPlacement> Placements,
    string? FontFamily,
    double FontSizePoints,
    TextAlignment Alignment,
    bool IsMultiline)
{
    /// <summary>
    /// The values this field accepts, for a dropdown, list box or set of radio buttons.
    /// </summary>
    /// <remarks>
    /// Empty for a field that does not constrain its value, which includes every text field and
    /// a combo box a person may type into. These come from the template rather than the caller,
    /// so this is the list <see cref="FillBuilder.SetChoice(string, string)"/> will accept.
    /// </remarks>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>Where one occurrence of a field sits on a page.</summary>
/// <param name="PageNumber">One-based page number.</param>
/// <param name="XPoints">Distance from the left edge of the page, in points.</param>
/// <param name="YPoints">Distance from the top edge of the page, in points.</param>
/// <param name="WidthPoints">Width in points.</param>
/// <param name="HeightPoints">Height in points.</param>
public sealed record FieldPlacement(
    int PageNumber, double XPoints, double YPoints, double WidthPoints, double HeightPoints);

/// <summary>The field types a template may declare.</summary>
public enum TemplateFieldKind
{
    /// <summary>The template declares no type, or one that is not recognised.</summary>
    Unknown,

    /// <summary>A text field.</summary>
    Text,

    /// <summary>A button, including check boxes and radio buttons.</summary>
    Button,

    /// <summary>A list or combo box.</summary>
    Choice,

    /// <summary>A signature field.</summary>
    Signature,
}

/// <summary>
/// An image placeholder on a page: the position it is addressed by, the name the PDF knows it by,
/// its pixel size, and where the page draws it.
/// </summary>
/// <param name="Index">
/// One-based position among the page's images, ordered by resource name with embedded numbers
/// compared as numbers. This is the number <see cref="FillBuilder.SetImageAt(int, int, byte[])"/>
/// takes.
/// </param>
/// <param name="ResourceName">The name the page's own instructions use, such as <c>/Im0</c>.</param>
/// <param name="PixelWidth">Width of the stored image in pixels, not the size it is drawn at.</param>
/// <param name="PixelHeight">Height of the stored image in pixels.</param>
/// <param name="Placements">
/// Where the page draws it, in points from the top-left. Usually one, occasionally several if the
/// page draws the same image more than once, and empty when the position could not be worked out
/// from the page's instructions.
/// </param>
public sealed record TemplateImage(
    int Index,
    string ResourceName,
    int PixelWidth,
    int PixelHeight,
    IReadOnlyList<FieldPlacement> Placements);
