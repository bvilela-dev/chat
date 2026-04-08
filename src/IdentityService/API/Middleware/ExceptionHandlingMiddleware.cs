using System.Text.Json;
using FluentValidation;
using IdentityService.Application;

namespace IdentityService.API.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message, exception.Errors.Select(error => error.ErrorMessage));
        }
        catch (ConflictException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, exception.Message, null);
        }
        catch (UnauthorizedException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, exception.Message, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception.");
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected server error occurred.", null);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, IEnumerable<string>? details)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            title,
            status = statusCode,
            errors = details?.ToArray()
        });
        await context.Response.WriteAsync(payload);
    }
}