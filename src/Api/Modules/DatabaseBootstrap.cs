using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// The Development-time "make my tables exist" logic that used to be copied
/// seven times in Program.cs, one block per DbContext. Behaviour is unchanged;
/// the findings that shaped it are kept here, once.
/// </summary>
internal static class DatabaseBootstrap
{
    private const int _maxAttempts = 5;

    /// <summary>
    /// Runs <paramref name="action"/>, retrying with a 2s backoff. Found for
    /// real (Day 18): docker-compose's list-form depends_on only waits for the
    /// postgres container to START, not to accept connections, and an
    /// unhandled connection failure here crashed the whole Api before it could
    /// serve /health. Fixed at the orchestration level too (healthcheck +
    /// service_healthy); this retry stays as defence in depth. The last
    /// attempt's exception is allowed to propagate.
    /// </summary>
    public static async Task WithRetryAsync(WebApplication app, string database, Func<Task> action)
    {
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < _maxAttempts)
            {
                app.Logger.LogWarning(
                    ex,
                    "{Database} not ready (attempt {Attempt}/{MaxAttempts}), retrying in 2s...",
                    database,
                    attempt,
                    _maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>
    /// EnsureCreatedAsync, then CreateTablesAsync when the database already
    /// existed. Found for real (Day 23): EnsureCreatedAsync only creates the
    /// physical DATABASE - it no-ops (adds no tables) once the database exists,
    /// which it always does after the first module ran, so a later module's
    /// tables silently never appeared ("relation Users does not exist").
    /// "Already exists" is tolerated so a restart against a persisted volume
    /// is fine.
    ///
    /// <paramref name="createTablesIfDatabaseExists"/> is false only for the
    /// first module (Diagnostics), which always had plain EnsureCreated
    /// behaviour; kept as it was rather than changed in a refactor.
    /// </summary>
    public static async Task EnsureSchemaAsync(DbContext db, bool createTablesIfDatabaseExists = true)
    {
        var created = await db.Database.EnsureCreatedAsync();
        if (created || !createTablesIfDatabaseExists)
        {
            return;
        }

        var databaseCreator = (RelationalDatabaseCreator)db.GetService<IDatabaseCreator>();
        try
        {
            await databaseCreator.CreateTablesAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateTable)
        {
            // Already created by a previous run against this same persisted
            // volume - fine, not an error.
        }
    }
}
