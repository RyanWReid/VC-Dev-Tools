namespace VCDevTool.API.Middleware
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private const string CorrelationIdHeaderName = "X-Correlation-ID";

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = GetOrCreateCorrelationId(context);
            
            // Add to response headers
            context.Response.Headers[CorrelationIdHeaderName] = correlationId;
            
            // Add to context items for use by other middleware/controllers
            context.Items["CorrelationId"] = correlationId;
            
            // Add to logging scope
            using var scope = context.RequestServices.GetRequiredService<ILogger<CorrelationIdMiddleware>>()
                .BeginScope(new Dictionary<string, object> { { "CorrelationId", correlationId } });
            
            await _next(context);
        }

        private static string GetOrCreateCorrelationId(HttpContext context)
        {
            // Check if correlation ID is provided in request headers
            if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationId) 
                && !string.IsNullOrWhiteSpace(correlationId))
            {
                return correlationId!;
            }

            // Generate new correlation ID
            return Guid.NewGuid().ToString();
        }
    }

    public static class CorrelationIdMiddlewareExtensions
    {
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CorrelationIdMiddleware>();
        }
    }
} 