using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Starter.Api.Features.Todos;

namespace Starter.Tests.Security;

/// <summary>
/// Hål 2 (mass assignment): the request DTOs accepted <c>teamId</c> and <c>isDeleted</c>, fields the form never shows.
/// Sälj could plant a todo in Ekonomi's list, move its own todos to another team, or hide them without anyone noticing.
/// The server decides team and deleted-flag; extra JSON fields are ignored.
/// </summary>
public sealed class MassAssignmentTests(SecurityApiFactory factory) : IClassFixture<SecurityApiFactory>
{
    private const int EkonomiTeamId = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _salj = factory.ClientFor("salj");
    private readonly HttpClient _ekonomi = factory.ClientFor("ekonomi");

    private static async Task<List<TodoResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TodoResponse>>("/api/todos", Json))!;

    private async Task<TodoResponse> CreateSaljTodoAsync(string title)
    {
        var response = await _salj.PostAsJsonAsync("/api/todos", new { title });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TodoResponse>(Json))!;
    }

    [Fact]
    [Trait("Abuse", "MB-2.1")]
    public async Task Create_ignores_a_team_id_sent_by_the_client()
    {
        var response = await _salj.PostAsJsonAsync("/api/todos", new { title = "Planterad hos Ekonomi", teamId = EkonomiTeamId });
        response.EnsureSuccessStatusCode();

        Assert.DoesNotContain(await ListAsync(_ekonomi), t => t.Title == "Planterad hos Ekonomi");
        Assert.Contains(await ListAsync(_salj), t => t.Title == "Planterad hos Ekonomi");
    }

    [Fact]
    [Trait("Abuse", "MB-2.1")]
    public async Task Create_ignores_an_is_deleted_flag_sent_by_the_client()
    {
        var response = await _salj.PostAsJsonAsync("/api/todos", new { title = "Gömd från början", isDeleted = true });
        response.EnsureSuccessStatusCode();

        Assert.Contains(await ListAsync(_salj), t => t.Title == "Gömd från början");
    }

    [Fact]
    [Trait("Abuse", "MB-2.1")]
    public async Task Update_cannot_move_a_todo_to_another_team()
    {
        var todo = await CreateSaljTodoAsync("Flyttas inte");

        var response = await _salj.PutAsJsonAsync($"/api/todos/{todo.Id}", new { title = "Flyttas inte", teamId = EkonomiTeamId });
        response.EnsureSuccessStatusCode();

        Assert.DoesNotContain(await ListAsync(_ekonomi), t => t.Id == todo.Id);
        Assert.Contains(await ListAsync(_salj), t => t.Id == todo.Id);
    }

    [Fact]
    [Trait("Abuse", "MB-2.1")]
    public async Task Update_cannot_silently_hide_a_todo()
    {
        var todo = await CreateSaljTodoAsync("Syns fortfarande");

        var response = await _salj.PutAsJsonAsync($"/api/todos/{todo.Id}", new { title = "Syns fortfarande", isDeleted = true });
        response.EnsureSuccessStatusCode();

        Assert.Contains(await ListAsync(_salj), t => t.Id == todo.Id);
    }
}
