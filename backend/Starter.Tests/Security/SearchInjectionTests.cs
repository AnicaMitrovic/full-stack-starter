using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Starter.Api.Features.Todos;

namespace Starter.Tests.Security;

/// <summary>
/// Hål 3 (SQL injection): the search glued the user's text straight into a SQL string.
/// <c>' OR 1=1 --</c> commented out the team filter and returned every team's todos.
/// User text must only ever be a parameter, never part of the SQL statement.
/// </summary>
public sealed class SearchInjectionTests(SecurityApiFactory factory) : IClassFixture<SecurityApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _salj = factory.ClientFor("salj");

    private async Task<HttpResponseMessage> SearchAsync(string query) =>
        await _salj.GetAsync($"/api/todos/search?q={Uri.EscapeDataString(query)}");

    [Theory]
    [Trait("Abuse", "MB-3.1")]
    [InlineData("' OR 1=1 --")]
    [InlineData("%' OR TeamId = 2 --")]
    [InlineData("x' UNION SELECT * FROM Todos --")]
    public async Task Injected_sql_does_not_leak_other_teams_todos(string attack)
    {
        var response = await SearchAsync(attack);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Lönekörning", body);
        Assert.DoesNotContain("Bonusunderlag", body);
    }

    [Fact]
    [Trait("Abuse", "MB-3.1")]
    public async Task A_lone_quote_is_just_text_not_a_server_error()
    {
        var response = await SearchAsync("'");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Abuse", "MB-3.1")]
    public async Task Normal_search_still_finds_the_teams_own_todos_case_insensitively()
    {
        var results = await (await SearchAsync("volvo")).Content.ReadFromJsonAsync<List<TodoResponse>>(Json);

        Assert.Contains(results!, t => t.Id == 101);
    }
}
