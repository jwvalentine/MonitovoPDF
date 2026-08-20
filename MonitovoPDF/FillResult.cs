namespace MonitovoPDF;

/// <summary>A finished document, together with what could not be filled.</summary>
/// <param name="Pdf">The rendered document.</param>
/// <param name="UnmatchedFields">
/// Names given a value that the template does not define, in the order they were set. Always
/// empty unless <see cref="RenderingOptions.OnMissingField"/> is
/// <see cref="MissingFieldBehaviour.Ignore"/>, since otherwise a missing name throws.
/// </param>
/// <remarks>
/// This is the answer to "it rendered, but did everything I asked for actually land?" — a question
/// worth asking when one set of values is filled into templates that differ slightly, and worth
/// logging when it is not.
/// </remarks>
public sealed record FillResult(byte[] Pdf, IReadOnlyList<string> UnmatchedFields);

/// <summary>What to do when a value is set on a field the template does not define.</summary>
public enum MissingFieldBehaviour
{
    /// <summary>
    /// Fail the whole render. The default, because a partly populated document usually means the
    /// wrong template was supplied, and half a document is rarely better than none.
    /// </summary>
    Throw,

    /// <summary>
    /// Draw what can be drawn and carry on. The names that did not match are reported by
    /// <see cref="MonitovoPdf.FillWithReport(byte[], Action{FillBuilder}, RenderingOptions?)"/>.
    /// Useful when one set of values feeds several templates that do not all carry every field.
    /// </summary>
    Ignore,
}
