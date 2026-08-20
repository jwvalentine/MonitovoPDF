using PdfSharp.Fonts;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Resolves fonts from a directory of TrueType files shipped with the application.
/// </summary>
/// <remarks>
/// Face names come from the file names, so <c>Arial.ttf</c> serves the "Arial" family and the
/// optional <c>-Bold</c>, <c>-Italic</c> and <c>-BoldItalic</c> suffixes serve those styles. A
/// family that cannot be matched falls back to the configured default, so an unexpected font in a
/// template degrades the document rather than failing the render.
/// </remarks>
internal sealed class FileSystemFontResolver : IFontResolver
{
    private readonly Dictionary<string, byte[]> _faces;
    private readonly string _fallbackFace;
    private readonly Action<string>? _onWarning;

    public FileSystemFontResolver(string directory, string fallbackFamily, Action<string>? onWarning = null)
    {
        _onWarning = onWarning;

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Font directory '{directory}' does not exist.");

        _faces = Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path)!,
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

        if (_faces.Count == 0)
            throw new InvalidOperationException($"No .ttf files found in font directory '{directory}'.");

        _fallbackFace = _faces.ContainsKey(fallbackFamily) ? fallbackFamily : _faces.Keys.Order().First();
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var suffix = (isBold, isItalic) switch
        {
            (true, true) => "-BoldItalic",
            (true, false) => "-Bold",
            (false, true) => "-Italic",
            _ => ""
        };

        if (_faces.ContainsKey(familyName + suffix))
            return new FontResolverInfo(familyName + suffix);

        // Fall back to the regular weight of the same family before giving up on the family.
        if (_faces.ContainsKey(familyName))
            return new FontResolverInfo(familyName);

        _onWarning?.Invoke($"Font family {familyName} is not available; falling back to {_fallbackFace}.");
        return new FontResolverInfo(_fallbackFace);
    }

    public byte[]? GetFont(string faceName) => _faces.GetValueOrDefault(faceName);
}
