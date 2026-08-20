using System.ComponentModel.DataAnnotations;

namespace MonitovoPDF.Server;

/// <summary>
/// Bounds that belong to the HTTP host rather than to rendering itself.
/// </summary>
/// <remarks>
/// An in-process caller of the library controls its own request size and cancellation, so these
/// have no meaning there. They exist because a network boundary needs its own ceilings.
/// </remarks>
public sealed class ServerOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Server";

    /// <summary>
    /// Largest accepted request body, enforced before the body is buffered. The outermost bound;
    /// the rendering ceilings apply to the decoded payload within it.
    /// </summary>
    [Range(1024, 209_715_200)]
    public long MaxRequestBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Ceiling on how long a caller waits for a render.
    /// </summary>
    /// <remarks>
    /// This bounds the response, not the work: a render is synchronous and CPU-bound, so it
    /// carries on after a timeout. The real defence against a pathological template is the input
    /// size and page-count ceilings in <c>Rendering</c>.
    /// </remarks>
    [Range(100, 600_000)]
    public int RenderTimeoutMilliseconds { get; set; } = 15_000;
}
