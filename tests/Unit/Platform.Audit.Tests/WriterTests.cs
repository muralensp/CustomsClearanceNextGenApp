using CustomsClearance.Platform.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CustomsClearance.Platform.Audit.Tests;

public class WriterTests
{
    [Fact]
    public async Task InMemoryAuditWriter_CapturesWrittenEvents()
    {
        var writer = new InMemoryAuditWriter();
        var evt = AuditEvent.Create(
            AuditEventCategory.BusinessAudit,
            entityType: "Commodity",
            action: "Classified",
            classification: DataClassification.Internal,
            performedBySystem: "HSClassificationEngine");

        await writer.WriteAsync(evt);

        Assert.Single(writer.Events);
        Assert.Equal(evt.AuditEventId, writer.Events.First().AuditEventId);
    }

    [Fact]
    public async Task ConsoleAuditWriter_CompletesSuccessfully()
    {
        var writer = new ConsoleAuditWriter(NullLogger<ConsoleAuditWriter>.Instance);
        var evt = AuditEvent.Create(
            AuditEventCategory.TechnicalAudit,
            entityType: "Config",
            action: "Changed",
            classification: DataClassification.Internal,
            performedBySystem: "AdminService");

        var task = writer.WriteAsync(evt);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }
}
