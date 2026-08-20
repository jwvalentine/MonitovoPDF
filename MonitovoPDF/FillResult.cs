namespace MonitovoPDF;

/// <summary>Identifies one image placeholder: which page, and which position on it.</summary>
/// <param name="PageNumber">One-based page number.</param>
/// <param name="Index">One-based position among that page's image placeholders.</param>
public sealed record ImageSlotReference(int PageNumber, int Index)
{
    /// <inheritdoc />
    public override string ToString() => $"image {Index} on page {PageNumber}";
}

/// <summary>A finished document, together with what could not be filled.</summary>
/// <param name="Pdf">The rendered document.</param>
/// <param name="UnmatchedFields">
/// Names given a value that the template does not define, in the order they were set.
/// </param>
/// <param name="UnmatchedImages">
/// Image placeholders that were addressed but do not exist — a page with fewer images than
/// expected, or no images at all.
/// </param>
/// <remarks>
/// Both lists are always empty unless <see cref="RenderingOptions.OnMissingField"/> is
/// <see cref="MissingFieldBehaviour.Ignore"/>, since otherwise anything that does not match fails
/// the render. They are separate because they are addressed differently and a caller reacting to
/// one usually does something different about the other.
/// </remarks>
public sealed record FillResult(
    byte[] Pdf,
    IReadOnlyList<string> UnmatchedFields,
    IReadOnlyList<ImageSlotReference> UnmatchedImages)
{
    /// <summary>Whether everything the caller asked for was drawn.</summary>
    public bool Complete => UnmatchedFields.Count == 0 && UnmatchedImages.Count == 0;
}

/// <summary>What to do when a value is set on something the template does not define.</summary>
public enum MissingFieldBehaviour
{
    /// <summary>
    /// Fail the whole render. The default, because a partly populated document usually means the
    /// wrong template, and half a document is rarely better than none.
    /// </summary>
    Throw,

    /// <summary>
    /// Draw what can be drawn and carry on. What did not match is reported by
    /// <see cref="MonitovoPdf.FillWithReport(byte[], Action{FillBuilder}, RenderingOptions?)"/>.
    /// Useful when one set of values feeds several templates that do not all carry every field.
    /// </summary>
    Ignore,
}
