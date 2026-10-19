using CustomsClearance.Platform.Audit.Persistence;
using Xunit;

namespace CustomsClearance.Platform.Audit.Persistence.Tests;

public class AuditStoreGrantsTests
{
    [Fact]
    public void BuildStatements_GrantsOnlySelectAndInsert_NeverUpdateDeleteOrTruncate()
    {
        var statements = AuditStoreGrants.BuildStatements("AuditEvents", "audit_writer");

        Assert.Equal(2, statements.Count);
        Assert.Equal("REVOKE ALL ON TABLE \"AuditEvents\" FROM \"audit_writer\"", statements[0]);
        Assert.Equal("GRANT SELECT, INSERT ON TABLE \"AuditEvents\" TO \"audit_writer\"", statements[1]);
        Assert.DoesNotContain(statements, s => s.Contains("UPDATE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(statements, s => s.Contains("DELETE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(statements, s => s.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("audit_writer\"; DROP TABLE x; --")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("1abc")]
    public void BuildStatements_RejectsUnsafeRoleName(string role)
    {
        Assert.Throws<ArgumentException>(() => AuditStoreGrants.BuildStatements("AuditEvents", role));
    }

    [Fact]
    public void BuildStatements_RejectsUnsafeTableName()
    {
        Assert.Throws<ArgumentException>(() => AuditStoreGrants.BuildStatements("Audit\"Events", "audit_writer"));
    }
}
