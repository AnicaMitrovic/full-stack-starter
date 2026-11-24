using System.Net;

namespace Starter.Tests.Security;

/// <summary>
/// Hål 5 (CORS): the API allowed any origin, so any web page a team member visited could call the API from
/// their browser and read the answer. Only our own frontend may do that.
/// </summary>
public sealed class CorsTests(SecurityApiFactory factory) : IClassFixture<SecurityApiFactory>
{
    private const string OwnFrontend = "http://localhost:5173";
    private const string EvilSite = "https://evil.example";

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/todos");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "x-team");
        return await factory.CreateClient().SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetFromAsync(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/todos");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Team", "ekonomi");
        return await factory.CreateClient().SendAsync(request);
    }

    [Fact]
    [Trait("Abuse", "MB-5.1")]
    public async Task A_foreign_website_is_not_allowed_to_read_the_api()
    {
        var response = await GetFromAsync(EvilSite);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    [Trait("Abuse", "MB-5.1")]
    public async Task A_foreign_website_fails_the_preflight()
    {
        var response = await PreflightAsync(EvilSite);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    [Trait("Abuse", "MB-5.1")]
    public async Task Our_own_frontend_is_allowed()
    {
        var preflight = await PreflightAsync(OwnFrontend);
        var response = await GetFromAsync(OwnFrontend);

        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal(OwnFrontend, Assert.Single(preflight.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(OwnFrontend, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }
}
