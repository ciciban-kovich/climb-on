namespace ClimbOn.Domain.Errors;

// What kind of refusal an error is; the api maps each kind to an HTTP status.
public enum DomainErrorKind
{
    Invalid,
    NotFound,
    Conflict,
    Forbidden,
}

// A rule refused the request. Code is stable and language-independent (C45); it is also the key of
// the message text in the api's resources.
public sealed class DomainError : Exception
{
    public DomainError(string code, DomainErrorKind kind = DomainErrorKind.Invalid)
        : base(code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        Code = code;
        Kind = kind;
    }

    public string Code { get; }

    public DomainErrorKind Kind { get; }
}
