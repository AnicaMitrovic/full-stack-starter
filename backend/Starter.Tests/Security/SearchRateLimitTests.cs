using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Starter.Tests.Security;

/// <summary>
/// Hål 6 (no rate limiting): the search, the most expensive query in the API, could be called ten thousand times a
/// minute. Too many calls must get 429 Too Many Requests with a Retry-After header, and switching the X-Team header
/// must not give an attacker a fresh quota.
/// </summary>
public sealed class SearchRateLimitTests
{
    private const int Limit = 5;

    private static SecurityApiFactory NewApi() => new LowLimitFactory();

    private static async Task<HttpStatusCode[]> SearchManyTimesAsync(SecurityApiFactory api, int times, Func<int, string> team)
    {
        var codes = new HttpStatusCode[times];
        for (var i = 0; i < times; i++)
        {
            codes[i] = (await api.ClientFor(team(i)).GetAsync("/api/todos/search?q=offert")).StatusCode;
        }

        return codes;
    }

    [Fact]
    [Trait("Abuse", "MB-6.1")]
    public async Task Calls_within_the_limit_succeed()
    {
        using var api = NewApi();

        var codes = await SearchManyTimesAsync(api, Limit, _ => "salj");

        Assert.All(codes, c => Assert.Equal(HttpStatusCode.OK, c));
    }

    [Fact]
    [Trait("Abuse", "MB-6.1")]
    public async Task Too_many_searches_get_429_with_retry_after()
    {
        using var api = NewApi();
        await SearchManyTimesAsync(api, Limit, _ => "salj");

        var response = await api.ClientFor("salj").GetAsync("/api/todos/search?q=offert");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After header is missing");
    }

    [Fact]
    [Trait("Abuse", "MB-6.1")]
    public async Task Switching_the_team_header_does_not_reset_the_limit()
    {
        using var api = NewApi();
        string[] teams = ["salj", "ekonomi", "support", "utveckling"];

        var codes = await SearchManyTimesAsync(api, Limit * 3, i => teams[i % teams.Length]);

        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
    }

    private sealed class LowLimitFactory : SecurityApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimiting:Search:PermitLimit", Limit.ToString());
            builder.UseSetting("RateLimiting:Search:WindowSeconds", "60");
        }
    }
}
