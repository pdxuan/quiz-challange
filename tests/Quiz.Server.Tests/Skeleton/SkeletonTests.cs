using System.Net;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Skeleton;

public class SkeletonTests
{
    [Fact]
    public async Task OpenApiDocument_InDevelopment_Returns200WithDocument()
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    public async Task SwaggerUi_InDevelopment_Returns200WithUi(string path)
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    public async Task ApiDocs_InProduction_Return404(string path)
    {
        using var factory = new QuizApiFactory { Environment = "Production" };
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(QuizApiFactory.SpaMarker, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/play/ABC234")]
    [InlineData("/host/ABC234")]
    public async Task ClientRoute_ReturnsSpaIndex(string path)
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(QuizApiFactory.SpaMarker, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/api")]
    [InlineData("/hubs/nope")]
    public async Task UnknownServerPath_Returns404ProblemDetails(string path)
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(QuizApiFactory.SpaMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_Returns200Healthy()
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
