using System.Runtime.CompilerServices;
using MonitovoPDF;

namespace MonitovoPDF.Tests;

/// <summary>
/// Gives the whole test assembly a font to draw with, on every platform.
/// </summary>
/// <remarks>
/// <para>
/// PDFsharp resolves fonts through one process-wide hook, so this runs once from a module
/// initializer rather than from each test class. Relying on the host's installed fonts is not
/// enough: the cross-platform build cannot use them on Linux, so a CI runner throws
/// "No appropriate font found" on the first render and takes most of the suite with it.
/// </para>
/// <para>
/// A single font is copied into a temporary directory rather than pointing the resolver at a
/// system font directory, because the resolver eagerly reads every <c>.ttf</c> it finds — aimed
/// at <c>C:\Windows\Fonts</c> that is hundreds of megabytes of pointless work.
/// </para>
/// </remarks>
internal static class TestFonts
{
    /// <summary>Directories worth searching when nothing is configured, in preference order.</summary>
    private static readonly string[] CandidateDirectories =
    [
        "/usr/share/fonts",             // Linux
        "/usr/local/share/fonts",       // Linux, locally installed
        "/System/Library/Fonts",        // macOS
        "/Library/Fonts",               // macOS
        @"C:\Windows\Fonts",            // Windows
    ];

    /// <summary>Font file names to prefer, so runs are as repeatable as the host allows.</summary>
    private static readonly string[] PreferredFiles =
    [
        "DejaVuSans.ttf", "Arial.ttf", "arial.ttf", "LiberationSans-Regular.ttf", "Helvetica.ttf",
    ];

    /// <summary>
    /// Bold faces to look for, so that asking for bold can actually produce a different font.
    /// </summary>
    /// <remarks>
    /// Without one, a request for bold quietly resolves to the regular face and a test asserting
    /// that bold was honoured passes whatever the code does. A weight the suite cannot tell apart
    /// is a weight the suite is not testing.
    /// </remarks>
    private static readonly string[] BoldFiles =
    [
        "DejaVuSans-Bold.ttf", "arialbd.ttf", "Arial-Bold.ttf", "LiberationSans-Bold.ttf",
    ];

    [ModuleInitializer]
    internal static void Install()
    {
        var source = Locate()
            ?? throw new InvalidOperationException(
                "No TrueType font could be found to run the tests with. Install one, or point "
                + "MONITOVO_TEST_FONTS at a directory containing a .ttf file.");

        // The resolver matches a family to a file name, so the copy is named for the family the
        // default options ask for. That keeps the tests using default options.
        var family = new RenderingOptions().DefaultFontFamily;
        var directory = Path.Combine(Path.GetTempPath(), "monitovopdf-tests", "fonts");
        Directory.CreateDirectory(directory);

        Place(source, Path.Combine(directory, family + ".ttf"));

        // The resolver names a bold face by suffix, so a bold file has to be there under that
        // name or asking for bold silently returns the regular one.
        var bold = LocateBold()
            ?? throw new InvalidOperationException(
                "No bold TrueType font could be found. The suite needs one to tell a bold face "
                + "from a regular one; install one, or point MONITOVO_TEST_FONTS at a directory "
                + $"containing one of: {string.Join(", ", BoldFiles)}.");

        Place(bold, Path.Combine(directory, family + "-Bold.ttf"));

        MonitovoPdf.UseFontDirectory(directory, family);
    }

    private static void Place(string source, string target)
    {
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
            File.Copy(source, target, overwrite: true);
    }

    private static string? LocateBold()
    {
        var configured = Environment.GetEnvironmentVariable("MONITOVO_TEST_FONTS");

        return new[] { configured }.Concat(CandidateDirectories)
            .Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            .Select(directory => BoldIn(directory!))
            .FirstOrDefault(found => found is not null);
    }

    private static string? BoldIn(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                    BoldFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("MONITOVO_TEST_FONTS");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            if (FirstFontIn(configured) is { } fromConfigured)
                return fromConfigured;
        }

        return CandidateDirectories
            .Where(Directory.Exists)
            .Select(FirstFontIn)
            .FirstOrDefault(found => found is not null);
    }

    private static string? FirstFontIn(string directory)
    {
        List<string> fonts;
        try
        {
            fonts = [.. Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories)];
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return fonts.FirstOrDefault(path => PreferredFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            ?? fonts.FirstOrDefault();
    }
}
