using System.Diagnostics.CodeAnalysis;

namespace GifMaker.Core;

/// <summary>
/// Result type for operations that can fail with an error message.
/// Makes illegal states unrepresentable - either success with value or failure with error.
/// </summary>
/// <remarks>
/// Uses separate structs for Success/Failure to avoid nullable reference type issues
/// with value types and ensure proper flow analysis.
/// </remarks>
public readonly struct Result<T> where T : notnull
{
    private readonly T? _value;
    private readonly string? _error;

    [MemberNotNullWhen(true, nameof(_value))]
    [MemberNotNullWhen(false, nameof(_error))]
    public bool IsSuccess { get; }

    private Result(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private Result(string error, bool _)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error, false);

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onError) =>
        IsSuccess ? onSuccess(_value) : onError(_error);

    public void Match(Action<T> onSuccess, Action<string> onError)
    {
        if (IsSuccess)
            onSuccess(_value);
        else
            onError(_error);
    }

    /// <summary>
    /// Gets the value if successful, or throws ArgumentException with error message.
    /// </summary>
    public T GetValueOrThrow() =>
        IsSuccess ? _value : throw new ArgumentException(_error);

    /// <summary>
    /// Gets the value if successful, or throws with custom exception factory.
    /// </summary>
    public T GetValueOrThrow(Func<string, Exception> exceptionFactory) =>
        IsSuccess ? _value : throw exceptionFactory(_error);
}
