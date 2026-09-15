using CustomsClearance.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Identity;

/// <summary>
/// Day 23 minimal Identity user store, proven against real PostgreSQL
/// (docker-compose), same "not a real bounded context" placeholder status
/// as Diagnostics/DiagnosticsDbContext (Day 13/18) - a real user/identity
/// bounded context (Administration or PartyManagement, both still empty)
/// is later, real-scope work. This exists only to make login + role-based
/// token issuance provable end-to-end now, per adr/0017.
/// </summary>
public sealed class PlatformUser : AuditableEntity<Guid>
{
    public required string Username { get; set; }

    /// <summary>ASP.NET Core Identity PasswordHasher output (PBKDF2), never plaintext.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>One of CustomsClearance.Platform.Security.PlatformRoles.</summary>
    public required string Role { get; set; }
}

public sealed class IdentityDbContext : PlatformDbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    public DbSet<PlatformUser> Users => Set<PlatformUser>();
}
