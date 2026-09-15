using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;

namespace Stockyard.Api.Errors;

/// <summary>Turns known failures into RFC 9457 Problem Details through <see cref="ErrorMapping"/>.</summary>
public sealed class StockyardExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var (errorNumber, detail) = exception switch
        {
            DomainException domain => (domain.ErrorNumber, domain.Message),
            SqlException sql => (sql.Number, (string?)null),
            DbUpdateException { InnerException: SqlException sql } => (sql.Number, null),
            BadHttpRequestException bad => (ErrorNumbers.Validation, bad.Message),
            _ => (0, null),
        };

        if (ErrorMapping.Map(errorNumber) is not { } mapped)
        {
            return false;
        }

        httpContext.Response.StatusCode = mapped.Status;
        var problem = new ProblemDetails
        {
            Status = mapped.Status,
            Title = mapped.Title,
            Detail = detail,
        };
        problem.Extensions["errorNumber"] = errorNumber;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
