using Microsoft.AspNetCore.Http;
using Stockyard.Api.Errors;
using Stockyard.Domain;

namespace Stockyard.UnitTests.Api;

public sealed class ErrorMappingTests
{
    [Theory]
    [InlineData(ErrorNumbers.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorNumbers.OverReceipt, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorNumbers.IdempotencyKeyConflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorNumbers.InsufficientStock, StatusCodes.Status409Conflict)]
    [InlineData(ErrorNumbers.InvalidState, StatusCodes.Status409Conflict)]
    [InlineData(ErrorNumbers.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorMapping.UniqueIndexViolation, StatusCodes.Status409Conflict)]
    [InlineData(ErrorMapping.PrimaryKeyViolation, StatusCodes.Status409Conflict)]
    [InlineData(ErrorMapping.StockLockTimeout, StatusCodes.Status503ServiceUnavailable)]
    public void MapsEveryKnownNumber(int errorNumber, int expectedStatus)
    {
        var mapped = ErrorMapping.Map(errorNumber);

        mapped.ShouldNotBeNull();
        mapped.Value.Status.ShouldBe(expectedStatus);
        mapped.Value.Title.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1205)]
    [InlineData(547)]
    public void LeavesUnknownNumbersToTheDefaultHandler(int errorNumber)
    {
        ErrorMapping.Map(errorNumber).ShouldBeNull();
    }
}
