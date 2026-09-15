using CustomsClearance.Platform.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Identity;

/// <summary>
/// Seeds one dev user per PRD persona (Ch.1.7) / PlatformRoles, so Day 23's
/// login endpoint has something real to authenticate against without a
/// registration flow (out of scope - a real Administration/PartyManagement
/// bounded context owns user management, later). Passwords are clearly-
/// marked dev-only placeholders, never real secrets (adr/0004 Item 5) -
/// same posture as JwtOptions.Key's Development placeholder.
/// </summary>
public static class IdentitySeeder
{
    private static readonly (string Username, string Password, string Role)[] _devUsers =
    [
        ("broker", "Broker-Dev-Pw-123!", PlatformRoles.Broker),
        ("supervisor", "Supervisor-Dev-Pw-123!", PlatformRoles.Supervisor),
        ("compliance", "Compliance-Dev-Pw-123!", PlatformRoles.ComplianceOfficer),
        ("admin", "Admin-Dev-Pw-123!", PlatformRoles.Admin),
    ];

    public static async Task SeedIfEmptyAsync(IdentityDbContext dbContext)
    {
        if (await dbContext.Users.AnyAsync())
        {
            return;
        }

        var hasher = new PasswordHasher<PlatformUser>();

        foreach (var (username, password, role) in _devUsers)
        {
            var user = new PlatformUser
            {
                Id = Guid.NewGuid(),
                Username = username,
                Role = role,
                PasswordHash = string.Empty,
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            dbContext.Users.Add(user);
        }

        await dbContext.SaveChangesAsync();
    }
}
