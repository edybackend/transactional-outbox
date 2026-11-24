using Subscriptions;

namespace TransactionalOutbox.Api.Endpoints;

/// <summary>The only place where a <see cref="Failure"/> becomes an HTTP status (RFC 9457 problem details).</summary>
internal static class FailureResults
{
    public static IResult ToHttp<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Failure!);

    public static IResult ToProblem(Failure failure) => failure.Kind switch
    {
        FailureKind.Validation => Results.ValidationProblem(
            failure.Fields ?? new Dictionary<string, string[]>(), detail: failure.Message),
        FailureKind.NotFound => Results.Problem(failure.Message, statusCode: StatusCodes.Status404NotFound),
        FailureKind.Conflict => Results.Problem(failure.Message, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(failure.Message, statusCode: StatusCodes.Status500InternalServerError),
    };
}
