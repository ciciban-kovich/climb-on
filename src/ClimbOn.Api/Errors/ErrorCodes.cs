using Microsoft.AspNetCore.Http;

namespace ClimbOn.Api.Errors;

// Codes the api itself emits (C45). Each is also a key in Resources/Texts*.resx.
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string RequestFailed = "request_failed";
    public const string InternalError = "internal_error";
    public const string ServiceUnavailable = "service_unavailable";
    public const string DatabaseUnavailable = "database_unavailable";

    public static IReadOnlyList<string> All { get; } =
    [
        ValidationFailed, NotFound, MethodNotAllowed, UnsupportedMediaType, RequestFailed,
        InternalError, ServiceUnavailable, DatabaseUnavailable,
    ];

    // The code for an error status that carries no code of its own.
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ValidationFailed,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status503ServiceUnavailable => ServiceUnavailable,
        < 500 => RequestFailed,
        _ => InternalError,
    };
}
