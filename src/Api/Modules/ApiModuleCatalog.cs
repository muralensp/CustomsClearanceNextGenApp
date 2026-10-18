using System.Reflection;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Finds every <see cref="IApiModule"/> in this assembly. Reflection is used
/// deliberately: an explicit list in Program.cs is exactly the shared line two
/// parallel changes would both edit (a guaranteed merge conflict). Discovery is
/// by assembly scan, so a new module is picked up with no edit to Program.cs.
/// </summary>
public static class ApiModuleCatalog
{
    public static IReadOnlyList<IApiModule> Discover() => Discover(typeof(IApiModule).Assembly);

    public static IReadOnlyList<IApiModule> Discover(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IApiModule).IsAssignableFrom(t))
            .Select(t => (IApiModule)(Activator.CreateInstance(t)
                ?? throw new InvalidOperationException($"Could not create API module {t.FullName}.")))
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
}
