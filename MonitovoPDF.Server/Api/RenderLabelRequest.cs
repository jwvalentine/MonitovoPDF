using System.Buffers.Text;

namespace MonitovoPDF.Server.Api;

/// <summary>
/// Wire format for a label render. The template travels with the request because the service
/// stores nothing: callers keep their templates wherever they already keep them.
/// </summary>
public sealed record RenderLabelRequest
{
    /// <summary>The template PDF, base64 encoded.</summary>
    public string? Template { get; init; }

    /// <summary>Text values, keyed by the name of the template field to draw them into.</summary>
    public Dictionary<string, string>? Fields { get; init; }

    /// <summary>Images, base64 encoded, keyed by the name of the template field to draw them into.</summary>
    public Dictionary<string, string>? Images { get; init; }

    /// <summary>Barcodes to generate, keyed by the name of the template field to draw them into.</summary>
    public Dictionary<string, BarcodeRequest>? Barcodes { get; init; }
}

/// <summary>A barcode the service should generate rather than the caller supplying an image.</summary>
public sealed record BarcodeRequest
{
    /// <summary>Symbology name, such as "code128" or "qr".</summary>
    public string? Type { get; init; }

    /// <summary>The content to encode. What is valid depends on the symbology.</summary>
    public string? Value { get; init; }
}

/// <summary>A barcode that has been validated against a known symbology.</summary>
public sealed record BarcodeSpec(BarcodeType Type, string Value);

/// <summary>A request that has passed validation and been decoded.</summary>
public sealed record DecodedLabelRequest(
    byte[] Template,
    IReadOnlyDictionary<string, string> Text,
    IReadOnlyDictionary<string, byte[]> Images,
    IReadOnlyDictionary<string, BarcodeSpec> Barcodes);

public static class RenderLabelRequestDecoder
{
    /// <summary>
    /// Validates and decodes an incoming request. Every ceiling is checked against the encoded
    /// length before decoding, so an oversized payload is rejected without being expanded first.
    /// </summary>
    public static bool TryDecode(
        RenderLabelRequest request,
        RenderingOptions options,
        out DecodedLabelRequest decoded,
        out List<string> errors)
    {
        errors = [];
        decoded = null!;

        var text = request.Fields ?? [];
        var images = request.Images ?? [];
        var barcodes = request.Barcodes ?? [];

        if (string.IsNullOrWhiteSpace(request.Template))
            errors.Add("A base64-encoded template is required.");

        if (text.Count + images.Count + barcodes.Count > options.MaxFieldCount)
            errors.Add($"A request may populate at most {options.MaxFieldCount} fields.");

        // A field may be given a value once. Anything else is ambiguous about what to draw.
        var claimed = text.Keys.Concat(images.Keys).Concat(barcodes.Keys);
        foreach (var name in claimed.GroupBy(name => name, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add($"Field '{name}' is given more than one value.");
        }

        foreach (var (name, value) in text)
        {
            if (string.IsNullOrWhiteSpace(name))
                errors.Add("Field names must not be empty.");
            else if (value.Length > options.MaxTextLength)
                errors.Add($"The value for field '{name}' exceeds the {options.MaxTextLength} character limit.");
        }

        var decodedBarcodes = new Dictionary<string, BarcodeSpec>(StringComparer.Ordinal);
        foreach (var (name, barcode) in barcodes)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("Field names must not be empty.");
                continue;
            }

            if (!BarcodeTypes.TryParse(barcode?.Type, out var type))
            {
                errors.Add($"Field '{name}' asks for an unknown barcode type '{barcode?.Type}'. "
                    + $"Supported types are: {string.Join(", ", BarcodeTypes.Names)}.");
                continue;
            }

            if (string.IsNullOrEmpty(barcode!.Value))
                errors.Add($"The barcode for field '{name}' has no value.");
            else if (barcode.Value.Length > options.MaxTextLength)
                errors.Add($"The barcode value for field '{name}' exceeds the {options.MaxTextLength} character limit.");
            else
                decodedBarcodes[name] = new BarcodeSpec(type, barcode.Value);
        }

        if (errors.Count > 0)
            return false;

        if (!TryDecodeBase64(request.Template!, options.MaxTemplateBytes, out var templateBytes))
        {
            errors.Add($"The template is not valid base64, is empty, or exceeds {options.MaxTemplateBytes} bytes.");
            return false;
        }

        var decodedImages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, value) in images)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("Field names must not be empty.");
                continue;
            }

            if (!TryDecodeBase64(value, options.MaxImageBytes, out var imageBytes))
            {
                errors.Add($"The image for field '{name}' is not valid base64, is empty, or exceeds {options.MaxImageBytes} bytes.");
                continue;
            }

            decodedImages[name] = imageBytes;
        }

        if (errors.Count > 0)
            return false;

        decoded = new DecodedLabelRequest(templateBytes, text, decodedImages, decodedBarcodes);
        return true;
    }

    private static bool TryDecodeBase64(string value, int maxBytes, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Reject on the encoded length first: four base64 characters carry three bytes, so this
        // bounds the decode without ever allocating the expanded payload.
        if ((long)value.Length / 4 * 3 > maxBytes)
            return false;

        var buffer = new byte[Base64.GetMaxDecodedFromUtf8Length(value.Length)];
        if (!Convert.TryFromBase64String(value, buffer, out var written) || written == 0 || written > maxBytes)
            return false;

        bytes = buffer[..written];
        return true;
    }
}
