using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.ReferenceData.Tests;

public class DatasetSanityTests
{
    [Fact]
    public void Countries_HaveWellFormedUniqueCodes_AndIncludeIndia()
    {
        var file = ReferenceDataSeeder.Load<CountrySeed>("countries.json");

        Assert.All(file.Items, c =>
        {
            Assert.Matches("^[A-Z]{2}$", c.Code);
            Assert.Matches("^[A-Z]{3}$", c.Alpha3Code);
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
        });
        Assert.Equal(file.Items.Count, file.Items.Select(c => c.Code).Distinct().Count());
        Assert.Equal(file.Items.Count, file.Items.Select(c => c.Alpha3Code).Distinct().Count());
        Assert.Contains(file.Items, c => c is { Code: "IN", Alpha3Code: "IND" });
    }

    [Fact]
    public void Currencies_HaveWellFormedUniqueCodes_AndInrIsTwoDecimalPlaces()
    {
        var file = ReferenceDataSeeder.Load<CurrencySeed>("currencies.json");

        Assert.All(file.Items, c =>
        {
            Assert.Matches("^[A-Z]{3}$", c.Code);
            Assert.Matches("^[0-9]{3}$", c.NumericCode);
            Assert.InRange(c.MinorUnits, 0, 4);
        });
        Assert.Equal(file.Items.Count, file.Items.Select(c => c.Code).Distinct().Count());
        Assert.Equal(file.Items.Count, file.Items.Select(c => c.NumericCode).Distinct().Count());
        Assert.Equal(("356", 2), file.Items.Where(c => c.Code == "INR").Select(c => (c.NumericCode, c.MinorUnits)).Single());
        Assert.Equal(0, file.Items.Single(c => c.Code == "JPY").MinorUnits);
    }

    [Fact]
    public void Units_HaveWellFormedUniqueCodes_AndIncludeCommonUqcs()
    {
        var file = ReferenceDataSeeder.Load<UnitSeed>("units.json");

        Assert.All(file.Items, u =>
        {
            Assert.Matches("^[A-Z]{3}$", u.Code);
            Assert.False(string.IsNullOrWhiteSpace(u.Name));
        });
        Assert.Equal(file.Items.Count, file.Items.Select(u => u.Code).Distinct().Count());
        foreach (var expected in new[] { "KGS", "NOS", "MTR", "LTR", "PCS", "SET" })
        {
            Assert.Contains(file.Items, u => u.Code == expected);
        }
    }

    [Theory]
    [InlineData("countries.json")]
    [InlineData("currencies.json")]
    [InlineData("units.json")]
    public void EveryDataset_IsMarkedPlaceholderAndUnverified(string fileName)
    {
        var raw = ReadRaw(fileName);

        Assert.Matches(new Regex("\"source\":\\s*\"Placeholder-Day39\""), raw);
        Assert.Matches(new Regex("\"isVerified\":\\s*false"), raw);
    }

    private static string ReadRaw(string fileName)
    {
        var assembly = typeof(ReferenceDataSeeder).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}

public class ReferenceDataSeederTests
{
    private static ReferenceDataDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<ReferenceDataDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public async Task SeedAsync_InsertsEveryRow_TaggedAsUnverifiedPlaceholder()
    {
        using var db = NewDb(nameof(SeedAsync_InsertsEveryRow_TaggedAsUnverifiedPlaceholder));

        var result = await ReferenceDataSeeder.SeedAsync(db);

        Assert.Equal(ReferenceDataSeeder.Load<CountrySeed>("countries.json").Items.Count, result.Countries);
        Assert.Equal(ReferenceDataSeeder.Load<CurrencySeed>("currencies.json").Items.Count, result.Currencies);
        Assert.Equal(ReferenceDataSeeder.Load<UnitSeed>("units.json").Items.Count, result.Units);
        Assert.Equal(result.Countries, await db.Countries.CountAsync());
        Assert.All(await db.Countries.ToListAsync(), c =>
        {
            Assert.Equal("Placeholder-Day39", c.Source);
            Assert.False(c.IsVerified);
            Assert.True(c.IsActive);
        });
        Assert.All(await db.Currencies.ToListAsync(), c => Assert.False(c.IsVerified));
        Assert.All(await db.UnitsOfMeasure.ToListAsync(), u => Assert.False(u.IsVerified));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var db = NewDb(nameof(SeedAsync_IsIdempotent));
        await ReferenceDataSeeder.SeedAsync(db);
        var counts = (await db.Countries.CountAsync(), await db.Currencies.CountAsync(), await db.UnitsOfMeasure.CountAsync());

        var second = await ReferenceDataSeeder.SeedAsync(db);

        Assert.Equal(new SeedResult(0, 0, 0), second);
        Assert.Equal(counts, (await db.Countries.CountAsync(), await db.Currencies.CountAsync(), await db.UnitsOfMeasure.CountAsync()));
    }

    [Fact]
    public async Task SeedAsync_NeverOverwritesOrReactivatesEditedRows()
    {
        using var db = NewDb(nameof(SeedAsync_NeverOverwritesOrReactivatesEditedRows));
        await ReferenceDataSeeder.SeedAsync(db);

        var india = await db.Countries.SingleAsync(c => c.Code == "IN");
        india.Name = "Bharat (edited)";
        india.IsActive = false;
        india.IsVerified = true;
        await db.SaveChangesAsync();

        await ReferenceDataSeeder.SeedAsync(db);

        var after = await db.Countries.AsNoTracking().SingleAsync(c => c.Code == "IN");
        Assert.Equal("Bharat (edited)", after.Name);
        Assert.False(after.IsActive);
        Assert.True(after.IsVerified);
    }

    [Fact]
    public async Task SeedAsync_InsertsOnlyTheMissingCodes()
    {
        using var db = NewDb(nameof(SeedAsync_InsertsOnlyTheMissingCodes));
        await ReferenceDataSeeder.SeedAsync(db);
        db.Currencies.Remove(await db.Currencies.SingleAsync(c => c.Code == "SAR"));
        await db.SaveChangesAsync();

        var result = await ReferenceDataSeeder.SeedAsync(db);

        Assert.Equal(new SeedResult(0, 1, 0), result);
        Assert.True(await db.Currencies.AnyAsync(c => c.Code == "SAR"));
    }

    [Fact]
    public void Model_DeclaresUniqueCodeIndexOnEveryTable()
    {
        using var db = NewDb(nameof(Model_DeclaresUniqueCodeIndexOnEveryTable));

        foreach (var type in new[] { typeof(Country), typeof(Currency), typeof(UnitOfMeasure) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(ReferenceEntity.Code));
        }
    }
}
