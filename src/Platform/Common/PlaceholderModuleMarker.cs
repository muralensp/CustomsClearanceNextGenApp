namespace CustomsClearance.Platform.Common;

/// <summary>
/// Placeholder scaffold marker for the Platform.Common module. No business logic yet
/// - exists solely so the CI pipeline (Day 11) has a real project to build, lint, and test
/// against, per ADR-0003's bounded-context-first structure. Remove/replace once Phase 0
/// implementation of this module begins.
/// </summary>
public static class PlaceholderModuleMarker
{
    public const string ModuleName = "Platform.Common";

    /// <summary>
    /// Trivial method (not just a const) so the Day 12 coverage pipeline has at
    /// least one real coverable line/method to measure and report on, instead
    /// of a vacuous 0-of-0 "100%" for a const-only class.
    /// </summary>
    public static string Describe() => $"Module: {ModuleName}";
}
