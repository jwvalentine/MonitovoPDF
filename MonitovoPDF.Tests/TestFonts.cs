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

        var target = Path.Combine(directory, family + ".ttf");
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
            File.Copy(source, target, overwrite: true);

        MonitovoPdf.UseFontDirectory(directory, family);
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
