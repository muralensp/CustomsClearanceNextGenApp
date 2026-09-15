using CustomsClearance.Platform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CustomsClearance.Platform.Security.Tests;

/// <summary>
/// Shared real-pipeline test host (AddCustomsClearanceAuthentication +
/// AddCustomsClearanceAuthorization + a RequireAdmin-protected endpoint),
/// extracted from AuthenticationAuthorizationTests (Day 17) so
/// JwtTokenIssuerTests (Day 23) can prove an issued token is actually
/// accepted by this same library's Bearer validation, not just
/// well-formed.
/// </summary>
internal static class TestAuthHost
{
    public static async Task<int> InvokeSecuredEndpointAsync(string? bearerToken, string key, string issuer, string audience)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    var config = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Jwt:Key"] = key,
                            ["Jwt:Issuer"] = issuer,
                            ["Jwt:Audience"] = audience,
                        })
                        .Build();

                    services.AddRouting();
                    services.AddCustomsClearanceAuthentication(config);
                    services.AddCustomsClearanceAuthorization();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/secure", (HttpContext _) => Results.Ok())
                            .RequireAuthorization(PlatformPolicies.RequireAdmin);
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        if (bearerToken is not null)
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
        }

        var response = await client.GetAsync("/secure");
        return (int)response.StatusCode;
    }
}
