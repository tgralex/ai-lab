using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiLab.Infrastructure.Persistence;

/// <summary>
/// SQLite's EF Core provider refuses to translate ORDER BY over DateTimeOffset columns (it can't
/// prove all stored offsets are comparable). Every timestamp in this app is UTC, so storing as a
/// plain UTC DateTime — sortable — and converting back to DateTimeOffset on read is lossless and
/// sidesteps the restriction everywhere (history lists, comparison "latest run", exports, etc.)
/// without touching call sites.
/// </summary>
public sealed class DateTimeOffsetToUtcDateTimeConverter() : ValueConverter<DateTimeOffset, DateTime>(
    v => v.UtcDateTime,
    v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

public sealed class NullableDateTimeOffsetToUtcDateTimeConverter() : ValueConverter<DateTimeOffset?, DateTime?>(
    v => v.HasValue ? v.Value.UtcDateTime : null,
    v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);
