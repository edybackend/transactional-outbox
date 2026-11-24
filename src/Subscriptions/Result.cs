namespace Subscriptions;

public enum FailureKind
{
    /// <summary>The request is malformed (400).</summary>
    Validation,

    /// <summary>The subscription does not exist (404).</summary>
    NotFound,

    /// <summary>The request conflicts with the current state, such as cancelling twice (409).</summary>
    Conflict,
}

/// <summary>Why a use case did not succeed. <see cref="Fields"/> lists errors per field for validation failures.</summary>
public sealed record Failure(FailureKind Kind, string Message, IReadOnlyDictionary<string, string[]>? Fields = null);

/// <summary>Outcome of a use case: either a value or a <see cref="Subscriptions.Failure"/>. Expected failures are not exceptions.</summary>
public sealed class Result<T>
{
    private Result(T? value, Failure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(Failure failure) => new(default, failure);
}
