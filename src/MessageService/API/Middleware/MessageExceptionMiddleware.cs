using System.Text.Json;
using FluentValidation;

namespace MessageService.API.Middleware;

public sealed class MessageExceptionMiddleware(RequestDelegate next, ILogger<MessageExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { title = exception.Message, status = 400 }));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled message service exception.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { title = "An unexpected server error occurred.", status = 500 }));
        }
    }
}