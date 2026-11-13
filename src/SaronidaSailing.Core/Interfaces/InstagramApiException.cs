namespace SaronidaSailing.Core.Interfaces;

public enum InstagramApiErrorKind
{
    TokenInvalid,
    Transient,
    Other
}

public sealed class InstagramApiException(
    InstagramApiErrorKind kind,
    string message,
    int? errorCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public InstagramApiErrorKind Kind { get; } = kind;

    public int? ErrorCode { get; } = errorCode;
}
