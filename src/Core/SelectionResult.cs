namespace GifMaker.Core;

/// <summary>
/// Discriminated union for area selection outcomes.
/// Makes illegal states unrepresentable.
/// </summary>
internal abstract record SelectionResult
{
    private SelectionResult() { }

    /// <summary>Selection completed successfully.</summary>
    public sealed record Success(Rectangle Region) : SelectionResult;

    /// <summary>User cancelled the selection.</summary>
    public sealed record Cancelled : SelectionResult;

    /// <summary>Selection failed with an error.</summary>
    public sealed record Failed(SelectionError Error) : SelectionResult;
}

/// <summary>
/// Structured error type for selection failures.
/// Separates user-facing message from diagnostic details.
/// </summary>
internal readonly record struct SelectionError
{
    /// <summary>User-safe message (no implementation details).</summary>
    public string UserMessage { get; }

    /// <summary>Full diagnostic details for logging only.</summary>
    public string DiagnosticDetails { get; }

    /// <summary>
    /// Creates error with message (diagnostic details default to message).
    /// </summary>
    public SelectionError(string message) : this(message, message) { }

    private SelectionError(string userMessage, string diagnosticDetails)
    {
        UserMessage = userMessage;
        DiagnosticDetails = diagnosticDetails;
    }

    /// <summary>
    /// Creates error from exception, sanitizing the user-facing message.
    /// </summary>
    public static SelectionError FromException(Exception ex) => ex switch
    {
        InvalidOperationException => new("Selection tool unavailable", ex.ToString()),
        TimeoutException => new("Selection timed out", ex.ToString()),
        _ => new("Selection failed unexpectedly", ex.ToString())
    };
}
