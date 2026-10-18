using System.Net;
using CustomsClearance.Api.Modules;

namespace CustomsClearance.Api.IntegrationTests;

/// <summary>
/// Guards the module-discovery mechanism that replaced the long hand-edited
/// Program.cs. A module that silently failed to load, or two modules sharing a
/// run order, would otherwise only show up as a runtime surprise.
/// </summary>
public class ApiModuleCatalogTests : IClassFixture<TestApiFactory>
{
    private static readonly string[] _expectedModules =
    [
        "Diagnostics", "Identity", "RulesEngine", "Configuration", "Notifications", "Audit", "ReferenceData",
    ];

    private readonly TestApiFactory _factory;

    public ApiModuleCatalogTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Discover_FindsEveryExistingCapability()
    {
        var names = ApiModuleCatalog.Discover().Select(m => m.Name).ToList();

        Assert.Equal(_expectedModules.OrderBy(n => n), names.OrderBy(n => n));
    }

    [Fact]
    public void Discover_ReturnsModulesInStrictlyAscendingOrder_WithUniqueNames()
    {
        var modules = ApiModuleCatalog.Discover();

        Assert.Equal(modules.Count, modules.Select(m => m.Name).Distinct().Count());
        Assert.Equal(modules.Count, modules.Select(m => m.Order).Distinct().Count());
        Assert.Equal(modules.OrderBy(m => m.Order).Select(m => m.Name), modules.Select(m => m.Name));
    }

    [Fact]
    public void Diagnostics_RunsFirst_BecauseItCreatesThePhysicalDatabase()
    {
        // The first module's schema bootstrap creates the database; every later
        // module only adds tables to it (Day 23 finding). Reordering this would
        // reintroduce "relation does not exist" on a fresh volume.
        Assert.Equal("Diagnostics", ApiModuleCatalog.Discover()[0].Name);
    }

    [Theory]
    [InlineData("/api/config")]
    [InlineData("/api/rules")]
    [InlineData("/api/audit")]
    [InlineData("/api/notifications/mine")]
    [InlineData("/api/reference/countries")]
    public async Task ModuleRoutes_AreNotMapped_WhenTheirConnectionStringIsAbsent(string path)
    {
        // TestApiFactory blanks every connection string (infrastructure-free
        // integration tests, ADR-0009). Had a module mapped its routes anyway,
        // this would be 401 (mapped, unauthenticated) or the host would not have
        // started at all (Day 18 map-time bindability lesson).
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
