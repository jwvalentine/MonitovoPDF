namespace MonitovoPDF.Rendering;

/// <summary>
/// Raised when a render fails because of the caller's input — a malformed template, a field that
/// the template does not define, or a value that breaches a configured ceiling. These map to 4xx
/// responses; anything else escaping the renderer is a genuine fault.
/// </summary>
public sealed class TemplateRenderException(string message) : Exception(message);
