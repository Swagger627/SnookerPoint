namespace SnookerPoint.Application.Common;

/// <summary>
/// A lightweight success/failure result carrying friendly, user-facing error
/// messages. Kept dependency-free deliberately (no validation library) so the
/// application layer stays lean.
/// </summary>
public class OperationResult
{
    private static readonly IReadOnlyList<string> NoErrors = Array.Empty<string>();

    protected OperationResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public bool Failed => !Succeeded;

    public IReadOnlyList<string> Errors { get; }

    /// <summary>A single joined message suitable for display.</summary>
    public string ErrorMessage => string.Join(Environment.NewLine, Errors);

    public static OperationResult Success() => new(true, NoErrors);

    public static OperationResult Failure(params string[] errors) =>
        new(false, errors.Length == 0 ? new[] { "Operation failed." } : errors);

    public static OperationResult Failure(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new OperationResult(false, list.Count == 0 ? new[] { "Operation failed." } : list);
    }
}

/// <summary>An <see cref="OperationResult"/> that also carries a value on success.</summary>
public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool succeeded, T? value, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Success(T value) =>
        new(true, value, Array.Empty<string>());

    public static new OperationResult<T> Failure(params string[] errors) =>
        new(false, default, errors.Length == 0 ? new[] { "Operation failed." } : errors);

    public static new OperationResult<T> Failure(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        return new OperationResult<T>(false, default, list.Count == 0 ? new[] { "Operation failed." } : list);
    }
}
