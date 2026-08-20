using Microsoft.Extensions.Options;
using MonitovoPDF.Api;
using MonitovoPDF.Rendering;
using PdfSharp.Fonts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<RenderingOptions>()
    .Bind(builder.Configuration.GetSection(RenderingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<LabelRenderer>();

builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var options = context.Configuration.GetSection(RenderingOptions.SectionName).Get<RenderingOptions>()
        ?? new RenderingOptions();

    kestrel.Limits.MaxRequestBodySize = options.MaxRequestBytes;
});

var app = builder.Build();

var renderingOptions = app.Services.GetRequiredService<IOptions<RenderingOptions>>().Value;

// PDFsharp resolves fonts through a single global hook, so it is installed once at startup.
// Without it the Core build has no fonts at all on Linux and text would not draw.
if (!string.IsNullOrWhiteSpace(renderingOptions.FontDirectory))
{
    GlobalFontSettings.FontResolver = new FileSystemFontResolver(
        renderingOptions.FontDirectory,
        renderingOptions.DefaultFontFamily,
        app.Services.GetRequiredService<ILogger<FileSystemFontResolver>>());
}
else
{
    // Fall back to the fonts installed on the host, which is enough for development on Windows.
    GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;

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
    LabelRenderer renderer,
    IOptions<RenderingOptions> options,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!RenderLabelRequestDecoder.TryDecode(request, options.Value, out var decoded, out var errors))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. errors] });

    var timeout = TimeSpan.FromMilliseconds(options.Value.RenderTimeoutMilliseconds);

    try
    {
        // The render itself is synchronous and CPU-bound, so this bounds how long a caller waits
        // rather than aborting the work. The real defence against a pathological template is the
        // input size and page-count ceilings applied above.
        var render = Task.Run(
            () => renderer.Render(decoded.Template, decoded.Text, decoded.Images),
            cancellationToken);

        var pdf = await render.WaitAsync(timeout, cancellationToken);

        logger.LogInformation(
            "Rendered a label from a {TemplateBytes} byte template into {PdfBytes} bytes across {FieldCount} field(s).",
            decoded.Template.Length, pdf.Length, decoded.Text.Count + decoded.Images.Count);

        return Results.File(pdf, "application/pdf", "label.pdf");
    }
    catch (TemplateRenderException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["template"] = [exception.Message] });
    }
    catch (TimeoutException)
    {
        logger.LogWarning("A render exceeded the {TimeoutMilliseconds} ms ceiling.", options.Value.RenderTimeoutMilliseconds);
        return Results.Problem("The render exceeded the time allowed for it.", statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.Run();

/// <summary>Exposed so the test project can host the application in memory.</summary>
public partial class Program;
