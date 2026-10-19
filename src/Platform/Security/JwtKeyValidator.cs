using System.Text;
using Microsoft.Extensions.Hosting;

namespace CustomsClearance.Platform.Security;

/// <summary>
/// Day 41 (ADR-0036): fail-fast guard so a host outside Development cannot
/// silently run with the committed placeholder signing key. That key is in a
/// public repository (Gateway appsettings.json, Api appsettings.Development.json),
/// so anyone can mint a valid token with it; it must never validate tokens
/// anywhere real. Development is deliberately left untouched so local runs,
/// docker-compose and the integration tests keep working with no secrets setup.
/// </summary>
public static class JwtKeyValidator
{
    /// <summary>HMAC-SHA256 needs a key of at least 256 bits; 32 UTF-8 bytes is that floor.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>Marker text of every committed placeholder key (case-insensitive).</summary>
    private static readonly string[] _placeholderMarkers = ["LOCAL-DEV-ONLY", "PLACEHOLDER", "NEVER-USE-IN-PRODUCTION"];

    /// <exception cref="InvalidOperationException">The key is unacceptable for this environment.</exception>
    public static void EnsureAcceptable(JwtOptions options, IHostEnvironment? environment)
    {
        if (environment is not null && environment.IsDevelopment())
        {
            return;
        }

        var key = options.Key;
        var environmentName = environment?.EnvironmentName ?? "(unknown)";

        if (Array.Exists(_placeholderMarkers, m => key.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Jwt:Key is the committed development placeholder, which is not allowed in the '{environmentName}' environment. " +
                "Supply a real signing key from a managed secrets store (see adr/0004 Item 5, adr/0036), e.g. the Jwt__Key environment variable.");
        }

        if (Encoding.UTF8.GetByteCount(key) < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:Key is shorter than the required {MinimumKeyLength} bytes for HMAC-SHA256 in the '{environmentName}' environment.");
        }
    }
}
