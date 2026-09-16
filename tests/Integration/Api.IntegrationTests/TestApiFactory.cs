using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CustomsClearance.Api.IntegrationTests;

/// <summary>
/// Shared WebApplicationFactory customization for all integration tests in
/// this project. Explicitly blanks ConnectionStrings:DiagnosticsDb,
/// ConnectionStrings:IdentityDb (Day 23), ConnectionStrings:RulesDb
/// (Day 31), and RabbitMq:HostName (Day 24) so Program.cs's
/// `if (!string.IsNullOrWhiteSpace(...))` guards skip DB/broker
/// registration and any startup connection attempt entirely.
///
/// Why this exists (Day 18): WebApplicationFactory defaults its hosting
/// environment to "Development" regardless of whether a test explicitly
/// calls UseEnvironment("Development") - so once Program.cs started doing
/// real Postgres work under IsDevelopment(), EVERY integration test
/// (including HealthEndpointTests, which has nothing to do with
/// persistence) started failing, because there is no Postgres container
/// available in this in-process test host (per adr/0009: integration
/// tests must not require infrastructure - only the container smoke-test
/// layer, Day 13/18, has a real database). Found via a real full-suite
/// failure (6/6 failed, ~56s each hitting the connection retry/backoff)
/// before being fixed here, not anticipated in advance.
///
/// Env-var override, not ConfigureAppConfiguration: the first fix attempt
/// used builder.ConfigureAppConfiguration to blank the connection string,
/// but that STILL failed - Program.cs's top-level statements call
/// `builder.Configuration.GetConnectionString(...)` synchronously while
/// building WebApplicationBuilder itself, before WebApplicationFactory's
/// ConfigureWebHost hooks ever get a chance to run for a minimal-hosting
/// (top-level Program.cs) app. Setting the environment variable in the
/// test PROCESS, before any factory/host is created, works because
/// WebApplication.CreateBuilder's default AddEnvironmentVariables()
/// provider reads the live process environment at that same Build time.
/// </summary>
public class TestApiFactory : WebApplicationFactory<Program>
{
    static TestApiFactory()
    {
        // A single space, not "": on Windows, SetEnvironmentVariable(name, "")
        // actually DELETES the variable rather than setting it to an empty
        // value - found by adding a temporary debug log to Program.cs and
        // observing GetEnvironmentVariable still returned null/empty after
        // an explicit "" set, meaning the override was silently a no-op and
        // Program.cs kept reading the real value from
        // appsettings.Development.json. A whitespace string is a real,
        // present value that still satisfies Program.cs's
        // string.IsNullOrWhiteSpace(connectionString) guard.
        Environment.SetEnvironmentVariable("ConnectionStrings__DiagnosticsDb", " ");
        Environment.SetEnvironmentVariable("ConnectionStrings__IdentityDb", " ");
        Environment.SetEnvironmentVariable("ConnectionStrings__RulesDb", " ");
        Environment.SetEnvironmentVariable("RabbitMq__HostName", " ");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}
