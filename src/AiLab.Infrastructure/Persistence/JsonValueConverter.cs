using System.Text.Json;
using AiLab.Core.Requests;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiLab.Infrastructure.Persistence;

/// <summary>Factory for EF Core value converters/comparers that store a list/dictionary/complex-object property as a JSON text column.</summary>
public static class JsonValueConverter
{
    private static readonly JsonSerializerOptions Options = new();

    public static ValueConverter<IReadOnlyList<string>, string> StringList { get; } = new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<List<string>>(v, Options) ?? new List<string>());

    public static ValueComparer<IReadOnlyList<string>> StringListComparer { get; } = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        v => v.ToList());

    public static ValueConverter<IReadOnlyList<Guid>, string> GuidList { get; } = new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<List<Guid>>(v, Options) ?? new List<Guid>());

    public static ValueComparer<IReadOnlyList<Guid>> GuidListComparer { get; } = new(
        (a, b) => (a ?? new List<Guid>()).SequenceEqual(b ?? new List<Guid>()),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        v => v.ToList());

    public static ValueConverter<IReadOnlyDictionary<string, string>, string> StringDictionary { get; } = new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, Options) ?? new Dictionary<string, string>());

    public static ValueComparer<IReadOnlyDictionary<string, string>> StringDictionaryComparer { get; } = new(
        (a, b) => (a ?? new Dictionary<string, string>()).OrderBy(p => p.Key).SequenceEqual((b ?? new Dictionary<string, string>()).OrderBy(p => p.Key)),
        v => v.Aggregate(0, (hash, kvp) => HashCode.Combine(hash, kvp.Key.GetHashCode(), kvp.Value.GetHashCode())),
        v => v.ToDictionary(p => p.Key, p => p.Value));

    public static ValueConverter<IReadOnlyList<InputBinding>, string> InputBindingList { get; } = new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<List<InputBinding>>(v, Options) ?? new List<InputBinding>());

    public static ValueComparer<IReadOnlyList<InputBinding>> InputBindingListComparer { get; } = new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        v => JsonSerializer.Serialize(v, Options).GetHashCode(),
        v => v.ToList());

    public static ValueConverter<T, string> ForObject<T>()
        where T : class =>
        new(
            v => JsonSerializer.Serialize(v, Options),
            v => DeserializeRequired<T>(v));

    public static ValueComparer<T> ObjectComparer<T>()
        where T : class =>
        new(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);

    private static T DeserializeRequired<T>(string json)
        where T : class =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from stored JSON.");

    public static ValueConverter<T?, string?> ForNullableObject<T>()
        where T : class =>
        new(
            v => v == null ? null : JsonSerializer.Serialize(v, Options),
            v => v == null ? null : JsonSerializer.Deserialize<T>(v, Options));
}
