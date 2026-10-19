using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Audit.Persistence;

/// <summary>
/// Day 41 (ADR-0036, closing the gap ADR-0032 left open): database-level
/// append-only enforcement via a RESTRICTED RUNTIME ROLE, not a trigger.
/// The role the application connects with is granted INSERT and SELECT on the
/// audit table and nothing else, so an UPDATE, DELETE or TRUNCATE is refused by
/// the database itself ("permission denied"), whatever the application code
/// does. Only standard SQL GRANT/REVOKE is used, so the approach carries over
/// to SQL Server (ADR-0001); a trigger would not.
///
/// The statements must run on a connection that OWNS the table (schema
/// bootstrap / migration time) - never on the restricted runtime connection.
/// Creating the role itself is provisioning (vendor-specific), deliberately not
/// done here. Residual: the table owner and any superuser can still alter rows.
/// </summary>
public static partial class AuditStoreGrants
{
    // Identifiers cannot be parameterised in DDL, so the role name is restricted
    // to a safe shape instead of being escaped.
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex SafeIdentifier();

    /// <summary>The idempotent GRANT/REVOKE statements for <paramref name="roleName"/> on <paramref name="tableName"/>.</summary>
    /// <exception cref="ArgumentException">A name is not a plain SQL identifier.</exception>
    public static IReadOnlyList<string> BuildStatements(string tableName, string roleName)
    {
        if (!SafeIdentifier().IsMatch(tableName))
        {
            throw new ArgumentException($"'{tableName}' is not a plain SQL identifier.", nameof(tableName));
        }

        if (!SafeIdentifier().IsMatch(roleName))
        {
            throw new ArgumentException($"'{roleName}' is not a plain SQL identifier.", nameof(roleName));
        }

        return
        [
            // Revoke first so re-running after a loosened grant tightens it again.
            $"REVOKE ALL ON TABLE \"{tableName}\" FROM \"{roleName}\"",
            $"GRANT SELECT, INSERT ON TABLE \"{tableName}\" TO \"{roleName}\"",
        ];
    }

    /// <summary>
    /// Applies the restricted grants for <paramref name="runtimeRole"/> on the
    /// audit table, using <paramref name="ownerContext"/> (an owner connection).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Executes DDL against a real database; statements are unit-tested via BuildStatements, behaviour proven live with psql (Day 41 log).")]
    public static async Task ApplyAsync(AuditDbContext ownerContext, string runtimeRole, CancellationToken cancellationToken = default)
    {
        var tableName = ownerContext.Model.FindEntityType(typeof(AuditEventRecord))!.GetTableName()!;
        foreach (var statement in BuildStatements(tableName, runtimeRole))
        {
            // Statements are built from validated identifiers only (BuildStatements).
#pragma warning disable EF1002 // identifiers cannot be parameterised; validated by SafeIdentifier
            await ownerContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
#pragma warning restore EF1002
        }
    }
}
