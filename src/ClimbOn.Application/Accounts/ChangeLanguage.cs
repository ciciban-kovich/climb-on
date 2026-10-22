using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

// S104: the climber changes the stored language at any time; the setting maps as at registration.
public sealed class ChangeLanguage(IAccountStore accounts)
{
    // Returns the language now stored for the account.
    public async Task<Language> RunAsync(Guid accountId, string? setting, CancellationToken cancellationToken)
    {
        var language = Languages.FromClientSetting(setting);
        await accounts.SetLanguageAsync(accountId, language, cancellationToken);
        return language;
    }
}
