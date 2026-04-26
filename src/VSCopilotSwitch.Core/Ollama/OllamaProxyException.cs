namespace VSCopilotSwitch.Core.Ollama;

public enum OllamaProxyErrorKind
{
    InvalidRequest,
    ModelNotFound,
    AmbiguousModel,
    ProviderUnauthorized,
    ProviderRateLimited,
    ProviderTimeout,
    ProviderUnavailable,
    UpstreamError
}

public sealed class OllamaProxyException : Exception
{
    public OllamaProxyException(OllamaProxyErrorKind kind, string code, string publicMessage, Exception? innerException = null)
        : base(publicMessage, innerException)
    {
        Kind = kind;
        Code = code;
        PublicMessage = publicMessage;
    }

    public OllamaProxyErrorKind Kind { get; }

    public string Code { get; }

    public string PublicMessage { get; }
}
