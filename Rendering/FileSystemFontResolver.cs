using PdfSharp.Fonts;

namespace MonitovoPDF.Rendering;

/// <summary>
/// Resolves fonts from a directory of TrueType files shipped with the deployment.
/// </summary>
/// <remarks>
/// Face names come from the file names, so <c>Arial.ttf</c> serves the "Arial" family and the
/// optional <c>-Bold</c>, <c>-Italic</c> and <c>-BoldItalic</c> suffixes serve those styles. A
/// family that cannot be matched falls back to the configured default so that an unexpected font
/// in a template degrades the label rather than failing the request.
/// </remarks>
public sealed class FileSystemFontResolver : IFontResolver
{
    private readonly Dictionary<string, byte[]> _faces;
    private readonly string _fallbackFace;
    private readonly ILogger<FileSystemFontResolver> _logger;

    public FileSystemFontResolver(string directory, string defaultFamily, ILogger<FileSystemFontResolver> logger)
    {
        _logger = logger;
        _faces = Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

        if (_faces.Count == 0)
            throw new InvalidOperationException($"No .ttf files found in font directory '{directory}'.");

        _fallbackFace = _faces.ContainsKey(defaultFamily) ? defaultFamily : _faces.Keys.Order().First();

        _logger.LogInformation(
            "Loaded {FontCount} font face(s) from {FontDirectory}; fallback face is {FallbackFace}.",
            _faces.Count, directory, _fallbackFace);
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

        _logger.LogWarning("Font family {FontFamily} is not available; falling back to {FallbackFace}.",
            familyName, _fallbackFace);
        return new FontResolverInfo(_fallbackFace);
    }

    public byte[]? GetFont(string faceName) => _faces.GetValueOrDefault(faceName);
}
