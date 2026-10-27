using System.Globalization;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Abstractions;
using ClimbOn.Domain.Accounts;

namespace ClimbOn.Api.Email;

// Email texts come from Resources/Texts*.resx in the recipient's language (S105). Links carry their
// token in the URL fragment, which browsers never send to the server (design §3).
public sealed class EmailContent(IConfiguration configuration) : IEmailContent
{
    public const string PublicBaseUrlKey = "PublicBaseUrl";

    public const string ConfirmationSubjectKey = "email_confirmation_subject";

    public const string ConfirmationBodyKey = "email_confirmation_body";

    public const string RegistrationAttemptSubjectKey = "registration_attempt_subject";

    public const string RegistrationAttemptBodyKey = "registration_attempt_body";

    public EmailMessage EmailConfirmation(string to, Language language, string token) =>
        new(to, Texts.Get(ConfirmationSubjectKey, language), Format(ConfirmationBodyKey, language, Link("confirm", token)));

    // S87: the login link only. The password reset link joins it with password reset (S80, M2).
    public EmailMessage RegistrationAttempt(string to, Language language) =>
        new(to, Texts.Get(RegistrationAttemptSubjectKey, language), Format(RegistrationAttemptBodyKey, language, Link("login")));

    private string Link(string path, string? token = null)
    {
        var baseUrl = configuration[PublicBaseUrlKey]
            ?? throw new InvalidOperationException($"{PublicBaseUrlKey} is not configured.");
        var link = $"{baseUrl.TrimEnd('/')}/{path}";
        return token is null ? link : $"{link}#t={token}";
    }

    private static string Format(string key, Language language, params object[] values) =>
        string.Format(CultureInfo.GetCultureInfo(language.Tag()), Texts.Get(key, language), values);
}
