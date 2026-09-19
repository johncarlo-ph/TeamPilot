using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TeamPilot.Application.Common.Exceptions;
using TeamPilot.Domain.Exceptions;

namespace TeamPilot.API.Middleware;

/// <summary>
/// Centralized exception handling: maps Application/Domain exceptions to consistent
/// <see cref="ProblemDetails"/> responses instead of letting each controller handle its own.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client navigated away, disconnected, or (e.g. a debounced typeahead search like
        // the "@"-mention autocomplete) superseded this request with a newer one before it
        // finished - HttpContext.RequestAborted firing mid-request is the expected, non-
        // exceptional way that ends, the same as ProjectEventsController's SSE stream already
        // treats it. The underlying exception's *type* varies by what was in flight when the
        // cancellation landed (a raw OperationCanceledException, or - for an in-flight EF Core
        // query - Microsoft.Data.SqlClient wrapping it as a SqlException instead of translating
        // it cleanly), so this checks the token rather than pattern-matching exception types.
        // There's no client left to write a response to either way.
        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(exception, "Request cancelled by the client for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        var (statusCode, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Authentication failed"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            BranchAlreadyLinkedException => (StatusCodes.Status409Conflict, "Branch already in use"),
            WorkflowLockedException => (StatusCodes.Status409Conflict, "Pipeline is locked"),
            AgentInstructionsIncompleteException => (StatusCodes.Status409Conflict, "Agent instructions incomplete"),
            InvalidWorkflowOperationException => (StatusCodes.Status409Conflict, "Invalid pipeline operation"),
            UnresolvedConflictsException => (StatusCodes.Status409Conflict, "Unresolved conflicts"),
            StaleConflictResolutionException => (StatusCodes.Status409Conflict, "Stale conflict resolutions"),
            ProjectNotReadyException => (StatusCodes.Status409Conflict, "Project not ready"),
            FileMentionProjectNotReferencedException => (StatusCodes.Status409Conflict, "Referenced project not mentioned"),
            ProjectHasActiveTicketsException => (StatusCodes.Status409Conflict, "Project has active tickets"),
            SprintHasActiveTicketsException => (StatusCodes.Status409Conflict, "Sprint has active tickets"),
            TicketNotAssignedToSprintException => (StatusCodes.Status409Conflict, "Ticket not assigned to a sprint"),
            TicketHasNoLinkedBranchException => (StatusCodes.Status409Conflict, "Ticket has no linked branch"),
            GitOperationException => (StatusCodes.Status422UnprocessableEntity, "Git operation failed"),
            LlmOperationException => (StatusCodes.Status422UnprocessableEntity, "LLM operation failed"),
            DomainException => (StatusCodes.Status409Conflict, "Invalid operation for the current state"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "{ExceptionType} while processing {Method} {Path}", exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : exception.Message,
            Instance = httpContext.Request.Path,
        };

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
