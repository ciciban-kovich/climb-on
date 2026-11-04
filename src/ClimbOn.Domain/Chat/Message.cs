using System.Buffers;
using System.Text;
using ClimbOn.Domain.Errors;

namespace ClimbOn.Domain.Chat;

// A chat message as stored (S9). Text is kept exactly as written, as plain text (S115). Read state (S75) arrives with
// S73.
public sealed record Message(Guid Id, Guid ConnectionId, Guid SenderId, string Text, DateTimeOffset SentAt)
{
    public const string EmptyCode = "message_empty";
    public const string TooLongCode = "message_too_long";
    public const string InvalidCode = "message_invalid";

    // In code points, counted exactly as written, without trimming or normalising (S119).
    public const int MaximumLength = 4000;

    public static IReadOnlyList<string> Codes { get; } = [EmptyCode, TooLongCode, InvalidCode];

    // The text is not trimmed or normalised: what is stored is what was sent (S115).
    public static Message Write(Guid id, Guid connectionId, Guid senderId, string? text, DateTimeOffset now) =>
        new(id, connectionId, senderId, Ensure(text), now);

    // S119: blank text is refused; so is text PostgreSQL cannot store as text: a lone surrogate or a NUL.
    private static string Ensure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainError(EmptyCode);
        }

        var length = 0;
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var consumed) != OperationStatus.Done || rune.Value == 0)
            {
                throw new DomainError(InvalidCode);
            }

            length++;
            rest = rest[consumed..];
        }

        return length > MaximumLength ? throw new DomainError(TooLongCode) : text;
    }
}
