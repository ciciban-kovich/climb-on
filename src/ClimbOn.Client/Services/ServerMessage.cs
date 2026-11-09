using ClimbOn.Client.Api.Models;
using ClimbOn.Client.Resources;
using Microsoft.Extensions.Localization;

namespace ClimbOn.Client.Services;

// What a failed call shows: the api's own code and message (C45), or a client text when no error body arrived.
public sealed record ServerMessage(string? Code, string Text)
{
    public const string UnexpectedKey = "error_unexpected";

    public const string NetworkKey = "error_network";

    // Runs an api call; null if it succeeded.
    public static async Task<ServerMessage?> RunAsync(Func<Task> call, IStringLocalizer<UiTexts> texts)
    {
        try
        {
            await call();
            return null;
        }
        catch (ErrorBody error) when (!string.IsNullOrWhiteSpace(error.MessageEscaped))
        {
            return new ServerMessage(error.Code, error.MessageEscaped);
        }
        catch (HttpRequestException)
        {
            return new ServerMessage(null, texts[NetworkKey]);
        }
        catch (Exception)
        {
            // No error body (C45): an ApiException without one, or a reply that is not the api's, such as a
            // proxy's error page or a body that does not parse.
            return new ServerMessage(null, texts[UnexpectedKey]);
        }
    }
}
