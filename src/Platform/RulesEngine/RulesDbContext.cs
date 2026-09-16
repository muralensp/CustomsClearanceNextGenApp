using CustomsClearance.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Day 31's real schema for ADR-0002/ADR-0013's Rule/RuleVersion/
/// RuleCondition model. Same real-PostgreSQL-via-EnsureCreatedAsync
/// pattern as DiagnosticsDbContext (Day 13/18) and IdentityDbContext
/// (Day 23) - no EF Core Migrations pipeline exists anywhere in this
/// codebase yet (a deliberate, named gap - see those contexts' own doc
/// comments), so schema creation is proven the same way: a real CREATE
/// TABLE against docker-compose's postgres service.
/// </summary>
public sealed class RulesDbContext : PlatformDbContext
{
    public RulesDbContext(DbContextOptions<RulesDbContext> options) : base(options)
    {
    }

    public DbSet<Rule> Rules => Set<Rule>();

    public DbSet<RuleVersion> RuleVersions => Set<RuleVersion>();

    public DbSet<RuleCondition> RuleConditions => Set<RuleCondition>();

    public DbSet<DecisionTrace> DecisionTraces => Set<DecisionTrace>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Rule>(rule =>
        {
            // RuleCode is the compliance-admin-facing business identifier
            // (Day 34 admin API); must be unique so a Rule can be looked up
            // by it without ambiguity.
            rule.HasIndex(r => r.RuleCode).IsUnique();
            rule.Property(r => r.Category).HasConversion<string>();

            rule.HasMany(r => r.Versions)
                .WithOne(v => v.Rule)
                .HasForeignKey(v => v.RuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RuleVersion>(version =>
        {
            // Data invariant per ADR-0013 (enforced by the Day 34 admin API,
            // not the database): no two versions of the same Rule may share
            // a VersionNumber. A unique composite index catches the one
            // sub-case a database constraint CAN enforce cheaply; the
            // effective-date-window non-overlap invariant is more than an
            // index can express and stays an application-level check.
            version.HasIndex(v => new { v.RuleId, v.VersionNumber }).IsUnique();

            version.HasMany(v => v.Conditions)
                .WithOne(c => c.RuleVersion)
                .HasForeignKey(c => c.RuleVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RuleCondition>(condition =>
        {
            condition.Property(c => c.Operator).HasConversion<string>();
        });

        modelBuilder.Entity<DecisionTrace>(trace =>
        {
            // Every row from one evaluation is looked up together (Day 33's
            // "decision trace visible for a test rule evaluation"
            // deliverable) - an index on EvaluationId makes that a real
            // index scan, not a table scan.
            trace.HasIndex(t => t.EvaluationId);
            trace.Property(t => t.SubjectType).HasConversion<string>();

            // No FK-driven cascade delete here (unlike Rule -> RuleVersion
            // -> RuleCondition): a DecisionTrace is an audit record that
            // must survive independent of the Rule/RuleVersion it
            // referenced (ADR-0004/PRD Ch.5.13). SetNull on delete rather
            // than Restrict, since the trace's own StepName/Outcome/
            // FactsSnapshotJson remain meaningful even if the referenced
            // RuleId/RuleVersionId can no longer be resolved.
            trace.HasOne<Rule>()
                .WithMany()
                .HasForeignKey(t => t.RuleId)
                .OnDelete(DeleteBehavior.SetNull);

            trace.HasOne<RuleVersion>()
                .WithMany()
                .HasForeignKey(t => t.RuleVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
