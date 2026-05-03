using Microsoft.AspNetCore.Http;
using VSCopilotSwitch.Core.Ollama;

public static class OpenAiErrorMapper
{
    public static (int StatusCode, OpenAiErrorResponse Response) Map(Exception exception)
    {
        if (exception is OllamaProxyException proxyException)
        {
            var statusCode = ToStatusCode(proxyException.Kind);
            var type = ToOpenAiErrorType(statusCode);
            return (statusCode, new OpenAiErrorResponse(new OpenAiErrorBody(
                proxyException.PublicMessage,
                type,
                proxyException.Code)));
        }

        return (StatusCodes.Status500InternalServerError, new OpenAiErrorResponse(new OpenAiErrorBody(
            "请求处理失败，请稍后重试。",
            "api_error",
            "internal_error")));
    }

    private static int ToStatusCode(OllamaProxyErrorKind kind)
        => kind switch
        {
            OllamaProxyErrorKind.InvalidRequest => StatusCodes.Status400BadRequest,
            OllamaProxyErrorKind.ModelNotFound => StatusCodes.Status404NotFound,
            OllamaProxyErrorKind.AmbiguousModel => StatusCodes.Status409Conflict,
            OllamaProxyErrorKind.ProviderUnauthorized => StatusCodes.Status401Unauthorized,
            OllamaProxyErrorKind.ProviderRateLimited => StatusCodes.Status429TooManyRequests,
            OllamaProxyErrorKind.ProviderTimeout => StatusCodes.Status504GatewayTimeout,
            // Copilot 当前会把 503 统一显示成上游限流；Provider 网络不可用用 502 保留真实故障语义。
            OllamaProxyErrorKind.ProviderUnavailable => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status502BadGateway
        };

    private static string ToOpenAiErrorType(int statusCode)
        => statusCode switch
        {
            StatusCodes.Status400BadRequest => "invalid_request_error",
            StatusCodes.Status401Unauthorized => "authentication_error",
            StatusCodes.Status404NotFound => "not_found_error",
            StatusCodes.Status409Conflict => "invalid_request_error",
            StatusCodes.Status429TooManyRequests => "rate_limit_error",
            StatusCodes.Status504GatewayTimeout => "timeout_error",
            _ => "api_error"
        };
}
