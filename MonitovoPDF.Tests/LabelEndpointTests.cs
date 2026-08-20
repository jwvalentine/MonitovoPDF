using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MonitovoPDF.Tests;

public class LabelEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly SyntheticTemplate.Field Title = new("title", 10, 60, 190, 90);
    private static readonly SyntheticTemplate.Field Logo = new("logo", 10, 10, 60, 50);

    private static string Template(params SyntheticTemplate.Field[] fields) =>
        Convert.ToBase64String(SyntheticTemplate.WithFields(fields));

    [Fact]
    public async Task Post_ReturnsAPdfForAValidRequest()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/v1/labels", new
        {
            template = Template(Title, Logo),
            fields = new { title = "WIDGET-4471" },
            images = new { logo = Convert.ToBase64String(SyntheticTemplate.SinglePixelPng()) }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(body[..4]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_RejectsAMissingTemplate()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/v1/labels", new
        {
            fields = new { title = "WIDGET-4471" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_RejectsATemplateThatIsNotBase64()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/v1/labels", new
        {
            template = "not base64 at all!!",
            fields = new { title = "WIDGET-4471" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_RejectsAFieldTheTemplateDoesNotDefine()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/v1/labels", new
        {
            template = Template(Title),
            fields = new { nonexistent = "value" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nonexistent", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_RejectsAFieldGivenBothTextAndAnImage()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/v1/labels", new
        {
            template = Template(Title),
            fields = new { title = "WIDGET-4471" },
            images = new { title = Convert.ToBase64String(SyntheticTemplate.SinglePixelPng()) }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReportsOk()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
