using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Starter.Api.Features.Teams;

namespace Starter.Tests.Security;

/// <summary>
/// Starts the real API in memory with its own seeded SQLite file, so the security tests attack
/// exactly what a curl call would hit. Each test class gets a fresh database.
/// </summary>
public class SecurityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"starter-security-{Guid.NewGuid()}.db");

    /// <summary>An HTTP client that says it belongs to <paramref name="team"/> via the X-Team header.</summary>
    public HttpClient ClientFor(string team)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TeamContext.HeaderName, team);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }
}
