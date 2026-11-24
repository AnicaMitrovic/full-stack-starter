using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Starter.Api.Features.Todos;

namespace Starter.Tests.Security;

/// <summary>
/// Hål 1 (IDOR): the list was filtered by team, but fetching, changing or deleting a single todo by id was not.
/// A member of Sälj could read Ekonomi's payroll todo just by guessing its id.
/// The answer must be 404, not 403, so an attacker cannot even learn that the id exists.
/// </summary>
public sealed class TeamIsolationTests(SecurityApiFactory factory) : IClassFixture<SecurityApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _salj = factory.ClientFor("salj");
    private readonly HttpClient _ekonomi = factory.ClientFor("ekonomi");

    private async Task<TodoResponse> CreateEkonomiTodoAsync(string title)
    {
        var response = await _ekonomi.PostAsJsonAsync("/api/todos", new { title });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TodoResponse>(Json))!;
    }

    private async Task<TodoResponse?> EkonomiSeesAsync(int id)
    {
        var todos = await _ekonomi.GetFromJsonAsync<List<TodoResponse>>("/api/todos", Json);
        return todos!.SingleOrDefault(t => t.Id == id);
    }

    [Fact]
    [Trait("Abuse", "MB-1.1")]
    public async Task Another_team_cannot_read_a_todo_by_id()
    {
        // 202 is Ekonomi's seeded, confidential bonus todo.
        var response = await _salj.GetAsync("/api/todos/202");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Bonusunderlag", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Abuse", "MB-1.1")]
    public async Task Another_team_cannot_update_a_todo()
    {
        var todo = await CreateEkonomiTodoAsync("Lönefil till banken");

        var response = await _salj.PutAsJsonAsync($"/api/todos/{todo.Id}", new { title = "Hackad av Sälj" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Lönefil till banken", (await EkonomiSeesAsync(todo.Id))!.Title);
    }

    [Fact]
    [Trait("Abuse", "MB-1.1")]
    public async Task Another_team_cannot_mark_a_todo_done()
    {
        var todo = await CreateEkonomiTodoAsync("Stäm av kontot");

        var response = await _salj.PutAsync($"/api/todos/{todo.Id}/done", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await EkonomiSeesAsync(todo.Id))!.IsDone);
    }

    [Fact]
    [Trait("Abuse", "MB-1.1")]
    public async Task Another_team_cannot_delete_a_todo()
    {
        var todo = await CreateEkonomiTodoAsync("Bokslut");

        var response = await _salj.DeleteAsync($"/api/todos/{todo.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(await EkonomiSeesAsync(todo.Id));
    }

    [Fact]
    [Trait("Abuse", "MB-1.1")]
    public async Task The_owning_team_can_still_read_its_own_todo()
    {
        var response = await _ekonomi.GetAsync("/api/todos/202");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
