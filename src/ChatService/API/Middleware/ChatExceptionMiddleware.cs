using System.Text.Json;
using FluentValidation;

namespace ChatService.API.Middleware;

public sealed class ChatExceptionMiddleware(RequestDelegate next, ILogger<ChatExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException exception)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled chat exception.");
            await WriteAsync(context, StatusCodes.Status500InternalServerError, "An unexpected server error occurred.");
        }
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { title = message, status = statusCode }));
    }
}