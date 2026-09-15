using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CustomsClearance.Platform.Security;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers JWT Bearer authentication using symmetric-key validation
    /// bound from the "Jwt" configuration section (see JwtOptions for the
    /// local/Development vs. future Authority-based migration note).
    /// </summary>
    public static IServiceCollection AddCustomsClearanceAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwtOptions.Key))
        {
            throw new InvalidOperationException(
                $"Configuration section '{JwtOptions.SectionName}:Key' is required. " +
                "See appsettings.Development.json for the local/dev placeholder, " +
                "or adr/0011 for the production secrets-management approach.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        return services;
    }

    /// <summary>
    /// Registers ITokenIssuer (JwtTokenIssuer), for a login endpoint (Day 23)
    /// to mint tokens signed with the same "Jwt" config section
    /// AddCustomsClearanceAuthentication validates against. Separate from
    /// that method because it's a distinct responsibility (issuance vs.
    /// validation) - a host that only validates tokens issued elsewhere
    /// (e.g. once real Authority-based validation replaces the symmetric
    /// key, per JwtOptions' doc comment) would not call this.
    /// </summary>
    public static IServiceCollection AddCustomsClearanceTokenIssuance(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwtOptions.Key))
        {
            throw new InvalidOperationException(
                $"Configuration section '{JwtOptions.SectionName}:Key' is required to issue tokens. " +
                "See appsettings.Development.json for the local/dev placeholder, " +
                "or adr/0011 for the production secrets-management approach.");
        }

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        return services;
    }

    /// <summary>
    /// Registers the platform's named authorization policies
    /// (PlatformPolicies), mapped to roles per the Day 1 Permissions
    /// Matrix (PlatformRoles).
    /// </summary>
    public static IServiceCollection AddCustomsClearanceAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PlatformPolicies.RequireAdmin, policy =>
                policy.RequireRole(PlatformRoles.Admin));

            options.AddPolicy(PlatformPolicies.RequireSupervisorOrAdmin, policy =>
                policy.RequireRole(PlatformRoles.Supervisor, PlatformRoles.Admin));

            options.AddPolicy(PlatformPolicies.RequireComplianceOfficerOrAdmin, policy =>
                policy.RequireRole(PlatformRoles.ComplianceOfficer, PlatformRoles.Admin));

            options.AddPolicy(PlatformPolicies.RequireAnyPlatformRole, policy =>
                policy.RequireRole(PlatformRoles.Broker, PlatformRoles.Supervisor, PlatformRoles.ComplianceOfficer, PlatformRoles.Admin));
        });

        return services;
    }
}
