using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;

namespace CustomsClearance.Platform.Notifications;

public sealed record EmailOptions(string Host, int Port, string From, bool EnableSsl);

/// <summary>
/// Sends through a real SMTP server using the BCL's SmtpClient (no new
/// package dependency). Excluded from unit coverage with the same
/// justification as RabbitMqEventBus: it is pure network I/O, and this
/// codebase has no mocking library - it is proven instead by a live SMTP
/// round trip against the Mailpit dev container (Day 37 execution log).
/// MailKit is the documented upgrade path if richer SMTP features are needed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Network I/O; proven against a real SMTP server (Mailpit) in the live run.")]
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(EmailOptions options)
    {
        _options = options;
    }

    public bool IsConfigured => true;

    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl };
        using var message = new MailMessage(_options.From, to, subject, body);
        await client.SendMailAsync(message, cancellationToken);
    }
}

/// <summary>Registered when Email:Host is absent, so the email channel records Skipped instead of crashing.</summary>
public sealed class DisabledEmailSender : IEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Email is not configured.");
}
