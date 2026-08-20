using System.Reflection;
using PdfSharp.Fonts;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Serves the one font embedded in this assembly, so a host with no fonts installed can still
/// draw text.
/// </summary>
/// <remarks>
/// <para>
/// The font is DejaVu Sans, held as an embedded resource and served from memory — nothing is
/// written to disk. Every family resolves to it, whatever was asked for, because the point is to
/// be a working last resort rather than a font library.
/// </para>
/// <para>
/// Only the regular face is carried. The renderer never asks for bold or italic, and a second
/// face would be another 700KB in every consumer's build output for nothing.
/// </para>
/// </remarks>
internal sealed class BundledFontResolver : IFontResolver
{
    /// <summary>The family name this resolver answers to, and substitutes for anything else.</summary>
    public const string FamilyName = "DejaVu Sans";

    private const string ResourceName = "MonitovoPDF.fonts.DejaVuSans.ttf";
    private const string FaceName = "MonitovoPDF.Bundled.DejaVuSans";

    private static readonly Lazy<byte[]> Font = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(FaceName);

    public byte[]? GetFont(string faceName) =>
        faceName == FaceName ? Font.Value : null;

    private static byte[] Load()
    {
        using var stream = typeof(BundledFontResolver).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded font '{ResourceName}' is missing from the assembly. This is a packaging "
                + "fault rather than anything a caller can correct.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }
}
