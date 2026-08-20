namespace MonitovoPDF.Rendering;

/// <summary>
/// Raised when a render fails because of the input — a malformed template, a field the template
/// does not define, a value that breaches a configured ceiling, or a value the chosen barcode
/// symbology cannot encode.
/// </summary>
/// <remarks>
/// This is the exception to catch. Anything else escaping a render is a fault rather than a
/// rejected input. Where the underlying cause carries useful detail, it is kept as
/// <see cref="Exception.InnerException"/>.
/// </remarks>
public sealed class TemplateRenderException : Exception
{
    /// <summary>Creates the exception with a message describing what was wrong with the input.</summary>
    public TemplateRenderException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception, keeping the underlying failure for diagnosis.</summary>
    public TemplateRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
