using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using ZainRecharge.Application.Exceptions;

namespace ZainRecharge.Api.ErrorHandling;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The caller hung up before we answered. Nobody is listening.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        (int Status, string Title, IDictionary<string, string[]>? Errors) result = exception switch
        {
            RequestValidationException e => (StatusCodes.Status400BadRequest, "One or more validation errors occurred.", e.Errors),
            RechargeNotFoundException => (StatusCodes.Status404NotFound, "Recharge not found.", null),
            SqlException or RetryLimitExceededException => (StatusCodes.Status503ServiceUnavailable, "The database is temporarily unavailable.", null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null)
        };

        if (result.Status >= 500)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path.Value);
        else
            logger.LogWarning("Request ended with {StatusCode}: {Reason}", result.Status, exception.Message);

        var problem = result.Errors is null
            ? new ProblemDetails { Status = result.Status, Title = result.Title }
            : new ValidationProblemDetails(result.Errors) { Status = result.Status, Title = result.Title };

        // Validation and not-found messages are written by us and are safe to show.
        // Everything else stays generic: the real message is in Seq, never in the response.
        problem.Detail = exception is RequestValidationException or RechargeNotFoundException
            ? exception.Message
            : "Something went wrong. Quote the traceId when you contact support.";

        if (result.Status == StatusCodes.Status503ServiceUnavailable)
            httpContext.Response.Headers.RetryAfter = "30";

        httpContext.Response.StatusCode = result.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }
}
