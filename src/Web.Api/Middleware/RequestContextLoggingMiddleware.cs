using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Logging;

namespace Web.Api.Middleware;

public class RequestContextLoggingMiddleware(RequestDelegate next, ILogger<RequestContextLoggingMiddleware> logger)
{
    private const string CorrelationIdHeaderName = "Correlation-Id";

    public Task Invoke(HttpContext context)
    {
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = GetCorrelationId(context)
        }))
        {
            return next.Invoke(context);
        }
    }

    private static string GetCorrelationId(HttpContext context)
    {
        context.Request.Headers.TryGetValue(
            CorrelationIdHeaderName,
            out StringValues correlationId);

        return correlationId.FirstOrDefault() ?? context.TraceIdentifier;
    }
}
