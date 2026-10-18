using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.Configuration;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.ConfigAdmin;

/// <summary>
/// Day 36's admin API for the configuration service: create/list/get/
/// update/delete a ConfigEntry, plus a read-through-IConfigService endpoint
/// that exercises the cached read path. Gated by PlatformPolicies.RequireAdmin
/// (same as /api/rules) - config changes alter live platform behaviour.
///
/// Update and delete invalidate the IConfigService cache entry for the key,
/// so a change is visible on this host immediately; other readers otherwise
/// see it within the cache TTL. Update logs old and new value, the data
/// Day 38 will wire into the audit trail.
/// </summary>
public static class ConfigAdminEndpoints
{
    public static WebApplication MapConfigAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/config").RequireAuthorization(PlatformPolicies.RequireAdmin);

        group.MapPost("/", CreateAsync).WithName("CreateConfigEntry");
        group.MapGet("/", ListAsync).WithName("ListConfigEntries");
        group.MapGet("/{key}", GetAsync).WithName("GetConfigEntry");
        group.MapGet("/{key}/effective", GetEffectiveAsync).WithName("GetEffectiveConfigValue");
        group.MapPut("/{key}", UpdateAsync).WithName("UpdateConfigEntry");
        group.MapDelete("/{key}", DeleteAsync).WithName("DeleteConfigEntry");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateConfigRequest request,
        HttpContext http,
        ConfigDbContext dbContext,
        IConfigService configService,
        IAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 200)
        {
            throw new DomainValidationException("BR-CONFIG-001", "Key is required and must be at most 200 characters.", nameof(request.Key));
        }

        ValidateValue(request.ValueType, request.Value, nameof(request.Value));

        if (request.IsFeatureFlag && request.ValueType != ConfigValueType.Boolean)
        {
            throw new DomainValidationException("BR-CONFIG-003", "A feature flag must have ValueType Boolean.", nameof(request.ValueType));
        }

        if (await dbContext.ConfigEntries.AnyAsync(e => e.Key == request.Key, cancellationToken))
        {
            throw new DomainValidationException("BR-CONFIG-004", $"Key '{request.Key}' is already in use.", nameof(request.Key));
        }

        var entry = new ConfigEntry
        {
            Id = Guid.NewGuid(),
            Key = request.Key,
            Value = request.Value,
            ValueType = request.ValueType,
            Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category,
            IsFeatureFlag = request.IsFeatureFlag,
            Description = request.Description,
        };

        dbContext.ConfigEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Clears any negative ("key absent") cache entry from an earlier read.
        configService.Invalidate(entry.Key);

        await ConfigAudit.WriteAsync(auditWriter, http, "ConfigCreated", entry.Id,
        [
            new FieldChange(nameof(ConfigEntry.Key), null, entry.Key),
            new FieldChange(nameof(ConfigEntry.Value), null, entry.Value),
            new FieldChange(nameof(ConfigEntry.ValueType), null, entry.ValueType.ToString()),
            new FieldChange(nameof(ConfigEntry.IsFeatureFlag), null, entry.IsFeatureFlag.ToString()),
        ], cancellationToken);

        return Results.Created($"/api/config/{entry.Key}", ToResponse(entry));
    }

    private static async Task<IResult> ListAsync(ConfigDbContext dbContext, CancellationToken cancellationToken)
    {
        var entries = await dbContext.ConfigEntries
            .AsNoTracking()
            .OrderBy(e => e.Key)
            .ToListAsync(cancellationToken);

        return Results.Ok(entries.Select(ToResponse));
    }

    private static async Task<IResult> GetAsync(string key, ConfigDbContext dbContext, CancellationToken cancellationToken)
    {
        var entry = await dbContext.ConfigEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Key == key, cancellationToken);
        return entry is null ? Results.NotFound() : Results.Ok(ToResponse(entry));
    }

    private static async Task<IResult> GetEffectiveAsync(string key, IConfigService configService, CancellationToken cancellationToken)
    {
        var value = await configService.GetStringAsync(key, cancellationToken);
        return Results.Ok(new EffectiveConfigResponse(key, value, value is not null));
    }

    private static async Task<IResult> UpdateAsync(
        string key,
        UpdateConfigRequest request,
        HttpContext http,
        ConfigDbContext dbContext,
        IConfigService configService,
        IAuditWriter auditWriter,
        ILogger<ConfigEntry> logger,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.ConfigEntries.SingleOrDefaultAsync(e => e.Key == key, cancellationToken);
        if (entry is null)
        {
            return Results.NotFound();
        }

        ValidateValue(entry.ValueType, request.Value, nameof(request.Value));

        var oldValue = entry.Value;
        var oldDescription = entry.Description;
        entry.Value = request.Value;
        if (request.Description is not null)
        {
            entry.Description = request.Description;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        configService.Invalidate(key);

        logger.LogInformation("Config '{ConfigKey}' changed from '{OldValue}' to '{NewValue}'", key, oldValue, entry.Value);

        // Only genuine changes are audited; a PUT that changes nothing is not
        // an audit-worthy event.
        var changes = new List<FieldChange>();
        if (oldValue != entry.Value)
        {
            changes.Add(new FieldChange(nameof(ConfigEntry.Value), oldValue, entry.Value));
        }

        if (oldDescription != entry.Description)
        {
            changes.Add(new FieldChange(nameof(ConfigEntry.Description), oldDescription, entry.Description));
        }

        if (changes.Count > 0)
        {
            await ConfigAudit.WriteAsync(auditWriter, http, "ConfigUpdated", entry.Id, changes, cancellationToken);
        }

        return Results.Ok(new ConfigChangeResponse(key, oldValue, entry.Value, ToResponse(entry)));
    }

    private static async Task<IResult> DeleteAsync(
        string key,
        HttpContext http,
        ConfigDbContext dbContext,
        IConfigService configService,
        IAuditWriter auditWriter,
        ILogger<ConfigEntry> logger,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.ConfigEntries.SingleOrDefaultAsync(e => e.Key == key, cancellationToken);
        if (entry is null)
        {
            return Results.NotFound();
        }

        dbContext.ConfigEntries.Remove(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        configService.Invalidate(key);

        logger.LogInformation("Config '{ConfigKey}' deleted (last value '{OldValue}')", key, entry.Value);

        await ConfigAudit.WriteAsync(auditWriter, http, "ConfigDeleted", entry.Id,
        [
            new FieldChange(nameof(ConfigEntry.Key), entry.Key, null),
            new FieldChange(nameof(ConfigEntry.Value), entry.Value, null),
        ], cancellationToken);

        return Results.NoContent();
    }

    private static void ValidateValue(ConfigValueType type, string? value, string field)
    {
        if (!ConfigValueValidator.IsValid(type, value))
        {
            throw new DomainValidationException("BR-CONFIG-002", $"Value is not a valid {type}.", field);
        }
    }

    private static ConfigEntryResponse ToResponse(ConfigEntry e) => new(
        e.Id, e.Key, e.Value, e.ValueType.ToString(), e.Category, e.IsFeatureFlag, e.Description, e.CreatedAt, e.UpdatedAt);
}
