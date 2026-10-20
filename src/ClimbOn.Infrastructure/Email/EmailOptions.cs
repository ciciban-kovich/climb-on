namespace ClimbOn.Infrastructure.Email;

// The configuration section Email (design §3 'Email'). The password is not here: it is the Key Vault
// secret email-smtp-password, read at send time (ISmtpPasswordSource).
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    // The From address.
    public string? Sender { get; set; }

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? Username { get; set; }
}
