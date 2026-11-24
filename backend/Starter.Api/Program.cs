using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Starter.Api.Data;
using Starter.Api.Features.Teams;
using Starter.Api.Features.Todos;

const string FrontendCorsPolicy = "frontend";
const string SearchRateLimitPolicy = TodosController.SearchRateLimitPolicy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<TeamContext>();
builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddScoped<TodoService>();

// The customer works in Swedish time, wherever the server runs (kravspec: "Svensk tid, alltid").
var deadlineTimeZone = TimeZoneInfo.FindSystemTimeZoneById(builder.Configuration["Deadlines:TimeZone"] ?? "Europe/Stockholm");
builder.Services.AddSingleton(sp => new DeadlineCalendar(sp.GetRequiredService<TimeProvider>(), deadlineTimeZone));

// Only our own frontend may call the API from a browser, not every website a user happens to visit.
var frontendOrigin = builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173";
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy => policy.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod()));

// Search is the most expensive query: cap it per client IP. The X-Team header is not used as the key,
// because anyone can change it and get a fresh quota.
var searchPermitLimit = builder.Configuration.GetValue("RateLimiting:Search:PermitLimit", 30);
var searchWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:Search:WindowSeconds", 60));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(SearchRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = searchPermitLimit, Window = searchWindow, QueueLimit = 0 }));
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Good enough for a starter. A real project uses EF Core migrations instead.
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    // Stack traces are for the developer's own machine only.
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
}
else
{
    // Everyone else gets a short ProblemDetails (from AddProblemDetails); the full exception is logged.
    app.UseExceptionHandler();
}

app.UseCors(FrontendCorsPolicy);
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed so integration tests can start the app with WebApplicationFactory.</summary>
public partial class Program;
