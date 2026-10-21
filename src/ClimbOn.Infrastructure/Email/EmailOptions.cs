namespace ClimbOn.Infrastructure.Email;

// The configuration section Email (design §3 'Email'). The password is not here: it is the Key Vault
// secret email-smtp-password, read at send time (ISmtpPasswordSource).
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    // The From address.
    public string? Sender { get; set; }

    public SmtpOptions Smtp { get; set; } = new();

    // The deadline for one send attempt, password read included. The first attempt runs inside the
    // registration request, so it must not wait on MailKit's own 2-minute timeout per operation.
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? Username { get; set; }
}
