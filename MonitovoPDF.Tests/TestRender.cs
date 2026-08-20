using MonitovoPDF;

namespace MonitovoPDF.Tests;

/// <summary>
/// Fills a template from plain dictionaries, so a test can say what it wants drawn without
/// building a callback each time.
/// </summary>
/// <remarks>
/// Everything goes through the public API rather than the renderer underneath. A test that
/// reaches past the surface consumers use can pass while the surface itself is broken.
/// </remarks>
internal static class TestRender
{
    public static byte[] Fill(
        byte[] template,
        Dictionary<string, string>? text = null,
        Dictionary<string, byte[]>? images = null,
        Dictionary<string, (BarcodeType Type, string Value)>? barcodes = null,
        RenderingOptions? options = null)
        => MonitovoPdf.Fill(template, fill => Apply(fill, text, images, barcodes), options);

    public static FillResult FillWithReport(
        byte[] template,
        Dictionary<string, string>? text = null,
        Dictionary<string, byte[]>? images = null,
        Dictionary<string, (BarcodeType Type, string Value)>? barcodes = null,
        RenderingOptions? options = null)
        => MonitovoPdf.FillWithReport(template, fill => Apply(fill, text, images, barcodes), options);

    /// <summary>Builds an options instance with one or two values changed.</summary>
    public static RenderingOptions Options(Action<RenderingOptions> configure)
    {
        var options = new RenderingOptions();
        configure(options);

        return options;
    }

    private static void Apply(
        FillBuilder fill,
        Dictionary<string, string>? text,
        Dictionary<string, byte[]>? images,
        Dictionary<string, (BarcodeType Type, string Value)>? barcodes)
    {
        foreach (var (field, value) in text ?? [])
            fill.SetText(field, value);

        foreach (var (field, value) in images ?? [])
            fill.SetImage(field, value);

        foreach (var (field, barcode) in barcodes ?? [])
            fill.SetBarcode(field, barcode.Type, barcode.Value);
    }
}
