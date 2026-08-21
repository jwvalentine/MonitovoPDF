namespace MonitovoPDF;

/// <summary>Horizontal placement of text within its field.</summary>
public enum TextAlignment
{
    /// <summary>Against the left edge of the field.</summary>
    Left,

    /// <summary>Centred in the field.</summary>
    Centre,

    /// <summary>Against the right edge of the field.</summary>
    Right,
}

/// <summary>
/// Overrides the appearance a template field asks for, when a caller needs to.
/// </summary>
/// <remarks>
/// <para>
/// The default is to draw text exactly as the template specifies: its font, its size, its
/// alignment. That is usually what you want — the template is where a document's appearance is
/// designed, and a value drawn differently from what the designer laid out will sit wrong.
/// </para>
/// <para>
/// These overrides exist for the cases where the template cannot be changed, or where a value's
/// appearance genuinely belongs to the caller rather than the document. Anything left null keeps
/// what the field asked for.
/// </para>
/// </remarks>
public sealed record TextOptions
{
    /// <summary>Size to draw at, overriding the field's own. Shrink-to-fit still applies.</summary>
    public double? FontSizePoints { get; init; }

    /// <summary>Family to draw with, overriding the field's own.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Horizontal placement, overriding the field's own.</summary>
    public TextAlignment? Alignment { get; init; }

    /// <summary>
    /// Whether the value wraps across lines. When null, the field's own multiline flag decides,
    /// and a value containing a line break wraps regardless.
    /// </summary>
    public bool? Multiline { get; init; }

    /// <summary>Whether to draw in a bold face, overriding what the field asks for.</summary>
    public bool? Bold { get; init; }

    /// <summary>Whether to draw in an italic face, overriding what the field asks for.</summary>
    public bool? Italic { get; init; }

    /// <summary>
    /// Colour to draw in as <c>#RRGGBB</c>, overriding what the field asks for. Null keeps the
    /// field's own colour, and a field asking for none is drawn in black.
    /// </summary>
    /// <remarks>
    /// <see cref="TemplateField.Colour"/> reports a field's colour in the same form, so a value
    /// read from one template can be applied to another without conversion.
    /// </remarks>
    public string? Colour { get; init; }
}
