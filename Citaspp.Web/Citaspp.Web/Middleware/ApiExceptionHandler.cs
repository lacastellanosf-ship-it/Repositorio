using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Citaspp;

namespace Citaspp.Web.Middleware;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "No encontrado"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Solicitud no válida"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Solicitud no válida"),
            AppointmentConflictException => (StatusCodes.Status409Conflict, "Conflicto de horario"),
            ConfigurationConflictException => (StatusCodes.Status409Conflict, "Conflicto de configuración"),
            _ => (0, (string?)null)
        };

        if (statusCode == 0)
        {
            return false;
        }

        logger.LogWarning(exception, "Solicitud API rechazada: {Title}", title);
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = title,
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
