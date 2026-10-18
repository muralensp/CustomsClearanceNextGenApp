using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.ReferenceData;

public sealed record SeedFile<T>(string Source, bool IsVerified, IReadOnlyList<T> Items);

public sealed record CountrySeed(string Code, string Alpha3Code, string Name);

public sealed record CurrencySeed(string Code, string NumericCode, string Name, int MinorUnits);

public sealed record UnitSeed(string Code, string Name);

public sealed record SeedResult(int Countries, int Currencies, int Units);

/// <summary>
/// Loads the embedded JSON datasets and inserts any row whose Code is not
/// already present. Insert-missing-only: running it on every startup is
/// safe, new codes added to the files propagate, and rows someone has since
/// edited or deactivated are never overwritten.
/// </summary>
public static class ReferenceDataSeeder
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static SeedFile<T> Load<T>(string fileName)
    {
        var assembly = typeof(ReferenceDataSeeder).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<SeedFile<T>>(stream, _json)
            ?? throw new InvalidOperationException($"Reference data file '{fileName}' is empty.");
    }

    public static async Task<SeedResult> SeedAsync(ReferenceDataDbContext db, CancellationToken cancellationToken = default)
    {
        var countries = Load<CountrySeed>("countries.json");
        var currencies = Load<CurrencySeed>("currencies.json");
        var units = Load<UnitSeed>("units.json");

        var existingCountries = (await db.Countries.Select(c => c.Code).ToListAsync(cancellationToken)).ToHashSet();
        var newCountries = countries.Items.Where(i => !existingCountries.Contains(i.Code)).ToList();
        db.Countries.AddRange(newCountries.Select(i => new Country
        {
            Id = Guid.NewGuid(),
            Code = i.Code,
            Alpha3Code = i.Alpha3Code,
            Name = i.Name,
            Source = countries.Source,
            IsVerified = countries.IsVerified,
        }));

        var existingCurrencies = (await db.Currencies.Select(c => c.Code).ToListAsync(cancellationToken)).ToHashSet();
        var newCurrencies = currencies.Items.Where(i => !existingCurrencies.Contains(i.Code)).ToList();
        db.Currencies.AddRange(newCurrencies.Select(i => new Currency
        {
            Id = Guid.NewGuid(),
            Code = i.Code,
            NumericCode = i.NumericCode,
            Name = i.Name,
            MinorUnits = i.MinorUnits,
            Source = currencies.Source,
            IsVerified = currencies.IsVerified,
        }));

        var existingUnits = (await db.UnitsOfMeasure.Select(u => u.Code).ToListAsync(cancellationToken)).ToHashSet();
        var newUnits = units.Items.Where(i => !existingUnits.Contains(i.Code)).ToList();
        db.UnitsOfMeasure.AddRange(newUnits.Select(i => new UnitOfMeasure
        {
            Id = Guid.NewGuid(),
            Code = i.Code,
            Name = i.Name,
            Source = units.Source,
            IsVerified = units.IsVerified,
        }));

        await db.SaveChangesAsync(cancellationToken);
        return new SeedResult(newCountries.Count, newCurrencies.Count, newUnits.Count);
    }
}
