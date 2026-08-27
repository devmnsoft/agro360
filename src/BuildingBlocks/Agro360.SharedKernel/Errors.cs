namespace Agro360.SharedKernel;

public sealed record DomainError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null)
{
    public static readonly DomainError None = new(string.Empty, string.Empty);
}

public readonly record struct Result
{
    private Result(bool isSuccess, DomainError error)
    {
        if (isSuccess == (error != DomainError.None))
        {
            throw new ArgumentException("Resultado inconsistente.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError Error { get; }

    public static Result Success() => new(true, DomainError.None);

    public static Result Failure(DomainError error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Failure<T>(DomainError error) => new(error);
}

public readonly record struct Result<T>
{
    private readonly T? _value;

    internal Result(T value)
    {
        _value = value;
        IsSuccess = true;
        Error = DomainError.None;
    }

    internal Result(DomainError error)
    {
        _value = default;
        IsSuccess = false;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Um resultado com falha não possui valor.");

}

public class DomainException : Exception
{
    public DomainException(string message, string code = "domain_error")
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ValidationException : DomainException
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Um ou mais dados são inválidos.", "validation_error")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class NotFoundException(string resource, Guid id)
    : DomainException($"{resource} '{id}' não foi encontrado.", "resource_not_found")
{
}

public sealed class ConflictException(string message, string code = "concurrency_conflict")
    : DomainException(message, code)
{
}

public sealed class ForbiddenException(string message)
    : DomainException(message, "forbidden")
{
}

public sealed class PersistenceException(string message, Exception innerException)
    : Exception(message, innerException)
{
}
