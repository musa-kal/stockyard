namespace Stockyard.Domain;

/// <summary>A broken business rule, carrying one of the <see cref="ErrorNumbers"/>.</summary>
public sealed class DomainException : Exception
{
    public DomainException()
        : this(ErrorNumbers.Validation, "The request is not valid.")
    {
    }

    public DomainException(string message)
        : this(ErrorNumbers.Validation, message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorNumber = ErrorNumbers.Validation;
    }

    public DomainException(int errorNumber, string message)
        : base(message)
    {
        ErrorNumber = errorNumber;
    }

    public int ErrorNumber { get; }
}
