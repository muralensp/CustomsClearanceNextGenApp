using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.Logging;
using Xunit;

namespace CustomsClearance.Platform.Audit.Tests;

public class AuditEventTests
{
    [Fact]
    public void Create_StampsCorrelationIdFromAccessor()
    {
        CorrelationIdAccessor.Current = "test-corr-42";

        var evt = AuditEvent.Create(
            AuditEventCategory.BusinessAudit,
            entityType: "Declaration",
            action: "StatusChanged",
            classification: DataClassification.Confidential,
            performedBySystem: "SegregationEngine");

        Assert.Equal("test-corr-42", evt.CorrelationId);
    }

    [Fact]
    public void Create_GeneratesUniqueIdAndUtcTimestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var evt = AuditEvent.Create(
            AuditEventCategory.TechnicalAudit,
            entityType: "User",
            action: "Login",
            classification: DataClassification.Internal,
            performedBySystem: "IdentityService");

        var after = DateTimeOffset.UtcNow;

        Assert.NotEqual(Guid.Empty, evt.AuditEventId);
        Assert.InRange(evt.OccurredAt, before, after);
    }

    [Fact]
    public void Create_RequiresEitherPerformedByOrPerformedBySystem()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditEvent.Create(
                AuditEventCategory.BusinessAudit,
                entityType: "Shipment",
                action: "Created",
                classification: DataClassification.Internal));
    }

    [Fact]
    public void Create_WithFieldChanges_PreservesThem()
    {
        var changes = new[] { new FieldChange("GSTIN", "OLDVALUE123", "NEWVALUE456") };

        var evt = AuditEvent.Create(
            AuditEventCategory.BusinessAudit,
            entityType: "Party",
            action: "FieldUpdated",
            classification: DataClassification.HighlyConfidential,
            performedBy: Guid.NewGuid(),
            fieldChanges: changes);

        Assert.Single(evt.FieldChanges!);
        Assert.Equal("GSTIN", evt.FieldChanges![0].FieldName);
    }
}
