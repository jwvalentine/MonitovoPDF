using Microsoft.Extensions.Options;
using MonitovoPDF;
using MonitovoPDF.Server;
using MonitovoPDF.Server.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<RenderingOptions>()
    .Bind(builder.Configuration.GetSection(RenderingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var options = context.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>()
        ?? new ServerOptions();

    kestrel.Limits.MaxRequestBodySize = options.MaxRequestBytes;
});

var app = builder.Build();

var renderingOptions = app.Services.GetRequiredService<IOptions<RenderingOptions>>().Value;
var serverOptions = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

// Fonts are process-wide in the underlying PDF engine, so they are configured once at start-up.
// Without this the cross-platform build has no fonts at all on Linux and text would not draw.
if (!string.IsNullOrWhiteSpace(renderingOptions.FontDirectory))
{
    MonitovoPdf.UseFontDirectory(
        renderingOptions.FontDirectory,
        renderingOptions.DefaultFontFamily,
        warning => app.Logger.LogWarning("{FontWarning}", warning));
}
else
{
    MonitovoPdf.UseInstalledFonts();

    if (!OperatingSystem.IsWindows())
    {
        app.Logger.LogWarning(
            "Rendering:FontDirectory is not set and this host is not Windows. A Linux container has no "
            + "fonts installed, so text will fail to draw until a font directory is configured.");
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/v1/labels", async (
    RenderLabelRequest request,
    IOptions<RenderingOptions> rendering,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!RenderLabelRequestDecoder.TryDecode(request, rendering.Value, out var decoded, out var errors))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });

    var timeout = TimeSpan.FromMilliseconds(serverOptions.RenderTimeoutMilliseconds);

    try
    {
        // The render is synchronous and CPU-bound, so this bounds how long a caller waits rather
        // than aborting the work. The input ceilings are the real defence.
        var render = Task.Run(() => MonitovoPdf.Fill(decoded.Template, fill =>
        {
            foreach (var (field, value) in decoded.Text)
                fill.SetText(field, value);

            foreach (var (field, image) in decoded.Images)
                fill.SetImage(field, image);

            foreach (var (field, barcode) in decoded.Barcodes)
                fill.SetBarcode(field, barcode.Type, barcode.Value);
        }, rendering.Value), cancellationToken);

        var pdf = await render.WaitAsync(timeout, cancellationToken);

        logger.LogInformation(
            "Rendered a label from a {TemplateBytes} byte template into {PdfBytes} bytes across {FieldCount} field(s).",
            decoded.Template.Length, pdf.Length,
            decoded.Text.Count + decoded.Images.Count + decoded.Barcodes.Count);

        return Results.File(pdf, "application/pdf", "label.pdf");
    }
    catch (TemplateRenderException exception)
    {
        logger.LogWarning("Rejected a render: {Reason}.", exception.GetType().Name);
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["template"] = [exception.Message] });
    }
    catch (TimeoutException)
    {
        logger.LogWarning("A render exceeded the {TimeoutMilliseconds} ms ceiling.",
            serverOptions.RenderTimeoutMilliseconds);

        return Results.Problem("The render exceeded the time allowed for it.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.Run();

/// <summary>Exposed so the test project can host the application in memory.</summary>
public partial class Program;
