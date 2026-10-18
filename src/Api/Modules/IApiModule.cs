using CustomsClearance.Platform.Messaging;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// One self-contained capability of the Api host (Identity, Rules admin,
/// Configuration, ...). Program.cs discovers every implementation via
/// <see cref="ApiModuleCatalog"/> and calls these hooks in order, so adding a
/// capability means adding a new module file - not editing Program.cs. That is
/// what lets two people (or two agents) add capabilities in parallel without
/// both editing the same lines of the same shared file.
///
/// Every hook is called for every module regardless of whether the module is
/// enabled; a module decides for itself (almost always "is my connection
/// string present?" - the convention since Day 18, ADR-0009) and does nothing
/// when disabled. Keeping that decision inside the module guarantees the
/// service registration, the schema bootstrap and the route mapping can never
/// disagree about it (the Day 18 Minimal API map-time bindability lesson).
/// </summary>
public interface IApiModule
{
    /// <summary>Short stable name, used in logs.</summary>
    string Name { get; }

    /// <summary>
    /// Run order among modules (ascending). Only matters where one module's
    /// start-up depends on another's: Diagnostics (10) creates the physical
    /// database that every later module's schema bootstrap then adds tables to.
    /// Leave gaps so a new module can slot in without renumbering.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Register this module's domain event payload types with the RabbitMQ
    /// consumer (only used when a real broker is configured). Default: none.
    /// </summary>
    void RegisterEventTypes(EventTypeRegistry registry)
    {
    }

    /// <summary>Register DbContexts, handlers and other services.</summary>
    void ConfigureServices(WebApplicationBuilder builder);

    /// <summary>
    /// Development-only start-up: create this module's tables and seed data.
    /// Called by Program.cs only when the environment is Development.
    /// Default: nothing to do.
    /// </summary>
    Task InitializeAsync(WebApplication app) => Task.CompletedTask;

    /// <summary>Map this module's HTTP routes.</summary>
    void MapEndpoints(WebApplication app);
}
