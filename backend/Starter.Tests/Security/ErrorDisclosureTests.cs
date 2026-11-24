using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Starter.Api.Domain;
using Starter.Api.Features.Todos;

namespace Starter.Tests.Security;

/// <summary>
/// Hål 4 (information disclosure): the developer exception page ran in every environment, so any crash sent the
/// exception type, the SQL and the full stack trace (class names, file paths) to whoever caused it.
/// Outside Development a client only gets a short ProblemDetails; the whole error goes to the log.
/// </summary>
public sealed class ErrorDisclosureTests(ErrorDisclosureTests.ProductionFactory factory) : IClassFixture<ErrorDisclosureTests.ProductionFactory>
{
    private const string Secret = "SELECT * FROM Todos WHERE secret_internal_detail";

    [Fact]
    [Trait("Abuse", "MB-4.1")]
    public async Task A_crash_in_production_returns_a_short_problem_details_without_internals()
    {
        var response = await factory.ClientFor("salj").GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain(nameof(ExplodingTodoRepository), body);
        Assert.DoesNotContain("   at ", body);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(500, problem!.Status);
    }

    [Fact]
    [Trait("Abuse", "MB-4.1")]
    public async Task A_crash_in_production_is_logged_in_full()
    {
        await factory.ClientFor("salj").GetAsync("/api/todos");

        Assert.Contains(factory.Logs.Errors, e => e.Exception?.Message == Secret);
    }

    public sealed class ProductionFactory : SecurityApiFactory
    {
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureTestServices(services => services.AddScoped<ITodoRepository, ExplodingTodoRepository>());
        }
    }

    /// <summary>A repository that fails the way a real database bug would, with internals in the message.</summary>
    private sealed class ExplodingTodoRepository : ITodoRepository
    {
        private static Exception Boom() => new InvalidOperationException(Secret);

        public Task<IReadOnlyList<Todo>> ListAsync(CancellationToken cancellationToken) => throw Boom();
        public Task<IReadOnlyList<Todo>> ListOverdueAsync(DateOnly today, CancellationToken cancellationToken) => throw Boom();
        public Task<IReadOnlyList<Todo>> ListCompletedWithDueDateAsync(CancellationToken cancellationToken) => throw Boom();
        public Task<int> CountOverdueAsync(DateOnly today, CancellationToken cancellationToken) => throw Boom();
        public Task<IReadOnlyList<Todo>> SearchAsync(string query, CancellationToken cancellationToken) => throw Boom();
        public Task<Todo?> FindAsync(int id, CancellationToken cancellationToken) => throw Boom();
        public Task AddAsync(Todo todo, CancellationToken cancellationToken) => throw Boom();
        public void Remove(Todo todo) => throw Boom();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw Boom();
    }

    /// <summary>Keeps every Error-level log entry so a test can check that the full exception was logged.</summary>
    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, Exception? Exception)> Errors { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Errors);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<(string, Exception?)> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    errors.Enqueue((category, exception));
                }
            }
        }
    }
}
