using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Stockyard.Infrastructure.Persistence;

/// <summary>
/// Every datetime2 column holds UTC. SQL Server does not store the kind, so values are tagged
/// as UTC when read, which keeps the "Z" suffix in API responses.
/// </summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
