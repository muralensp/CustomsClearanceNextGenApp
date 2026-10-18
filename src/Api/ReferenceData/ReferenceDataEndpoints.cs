using CustomsClearance.Platform.ReferenceData;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.ReferenceData;

public sealed record CountryResponse(string Code, string Alpha3Code, string Name, bool IsActive, string Source, bool IsVerified);

public sealed record CurrencyResponse(string Code, string NumericCode, string Name, int MinorUnits, bool IsActive, string Source, bool IsVerified);

public sealed record UnitResponse(string Code, string Name, bool IsActive, string Source, bool IsVerified);

/// <summary>
/// Day 39's read-only lookup API for the seeded reference tables. Any
/// authenticated user may read (every module needs these lookups); there is
/// deliberately no write endpoint yet - editing reference data belongs to
/// Phase 1's master-data work, once real sources are chosen. Active rows
/// only unless includeInactive=true; every response carries Source and
/// IsVerified so placeholder data is visibly placeholder. Codes are matched
/// case-insensitively.
/// </summary>
public static class ReferenceDataEndpoints
{
    public static WebApplication MapReferenceDataEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reference").RequireAuthorization();

        group.MapGet("/countries", async (ReferenceDataDbContext db, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await db.Countries.AsNoTracking()
                .Where(x => includeInactive == true || x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new CountryResponse(x.Code, x.Alpha3Code, x.Name, x.IsActive, x.Source, x.IsVerified))
                .ToListAsync(ct))).WithName("ListCountries");

        group.MapGet("/countries/{code}", async (string code, ReferenceDataDbContext db, CancellationToken ct) =>
        {
            var key = code.ToUpperInvariant();
            var x = await db.Countries.AsNoTracking().SingleOrDefaultAsync(c => c.Code == key, ct);
            return x is null ? Results.NotFound() : Results.Ok(new CountryResponse(x.Code, x.Alpha3Code, x.Name, x.IsActive, x.Source, x.IsVerified));
        }).WithName("GetCountry");

        group.MapGet("/currencies", async (ReferenceDataDbContext db, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await db.Currencies.AsNoTracking()
                .Where(x => includeInactive == true || x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new CurrencyResponse(x.Code, x.NumericCode, x.Name, x.MinorUnits, x.IsActive, x.Source, x.IsVerified))
                .ToListAsync(ct))).WithName("ListCurrencies");

        group.MapGet("/currencies/{code}", async (string code, ReferenceDataDbContext db, CancellationToken ct) =>
        {
            var key = code.ToUpperInvariant();
            var x = await db.Currencies.AsNoTracking().SingleOrDefaultAsync(c => c.Code == key, ct);
            return x is null ? Results.NotFound() : Results.Ok(new CurrencyResponse(x.Code, x.NumericCode, x.Name, x.MinorUnits, x.IsActive, x.Source, x.IsVerified));
        }).WithName("GetCurrency");

        group.MapGet("/units", async (ReferenceDataDbContext db, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await db.UnitsOfMeasure.AsNoTracking()
                .Where(x => includeInactive == true || x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new UnitResponse(x.Code, x.Name, x.IsActive, x.Source, x.IsVerified))
                .ToListAsync(ct))).WithName("ListUnits");

        group.MapGet("/units/{code}", async (string code, ReferenceDataDbContext db, CancellationToken ct) =>
        {
            var key = code.ToUpperInvariant();
            var x = await db.UnitsOfMeasure.AsNoTracking().SingleOrDefaultAsync(u => u.Code == key, ct);
            return x is null ? Results.NotFound() : Results.Ok(new UnitResponse(x.Code, x.Name, x.IsActive, x.Source, x.IsVerified));
        }).WithName("GetUnit");

        return app;
    }
}
