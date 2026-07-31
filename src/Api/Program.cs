// Placeholder Api host (Day 13). No business logic yet - exists solely so the
// CD pipeline has something real to containerize and smoke-test, mirroring
// the approach used for the CI pipeline's placeholder scaffold (Day 11).
// Replace/extend as each bounded context's endpoints are implemented,
// per ADR-0003's bounded-context-first structure.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check endpoint - used by the docker-compose smoke test in CI
// (Day 13) to confirm the container actually starts and serves traffic,
// not just that it builds.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "CustomsClearance.Api" }))
    .WithName("HealthCheck");

await app.RunAsync();

// Exposed for WebApplicationFactory-based integration tests later.
public partial class Program
{
    protected Program() { }
}
