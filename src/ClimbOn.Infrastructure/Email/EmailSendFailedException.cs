using System.Globalization;

namespace ClimbOn.Infrastructure.Email;

// Carries the failure's exception type and SMTP status code only. The original exception is neither
// thrown on nor attached, because its message can carry the recipient address (C32).
public sealed class EmailSendFailedException(string exceptionType, int? statusCode)
    : Exception($"Sending email failed: {exceptionType}, SMTP status {statusCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}.")
{
    public string ExceptionType { get; } = exceptionType;

    public int? StatusCode { get; } = statusCode;
}
