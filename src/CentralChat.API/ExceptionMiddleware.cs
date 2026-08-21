using CentralChat.Application;

namespace CentralChat.API;

public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { context.Response.Headers.Append("X-Correlation-Id", context.TraceIdentifier); await next(context); }
        catch (Exception ex)
        {
            var (status, title) = ex switch { ValidationException => (400, "Validation failed"), ForbiddenException => (403, "Forbidden"), NotFoundException => (404, "Not found"), ConflictException => (409, "Conflict"), _ => (500, "Unexpected server error") };
            if (status == 500) logger.LogError(ex, "Unhandled error for request {TraceId}", context.TraceIdentifier); else logger.LogInformation(ex, "Request rejected with {StatusCode}", status);
            context.Response.StatusCode = status; await Results.Problem(statusCode: status, title: title, detail: status == 500 ? "An unexpected error occurred." : ex.Message, extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }
    }
}
