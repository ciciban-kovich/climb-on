namespace ClimbOn.Api.Errors;

// The body of every error response (C45).
public sealed record ErrorBody(string Code, string Message);
