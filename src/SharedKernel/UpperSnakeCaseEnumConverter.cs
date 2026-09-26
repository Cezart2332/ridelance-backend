using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel;

/// <summary>
/// Serializează un enum ca text UPPER_SNAKE_CASE (<c>NeedsReview</c> → <c>"NEEDS_REVIEW"</c>),
/// forma din contractul API al modulului de contabilitate.
/// </summary>
/// <remarks>
/// Se pune pe tipul enum-ului, nu global: restul API-ului serializează enum-urile ca înainte, iar
/// un convertor global le-ar fi schimbat forma pentru toate endpoint-urile existente. Un membru
/// cu <see cref="JsonStringEnumMemberNameAttribute"/> își păstrează numele explicit
/// (de ex. <c>100_PERCENT</c>, care nu poate fi identificator C#).
/// </remarks>
public sealed class UpperSnakeCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false)
    where TEnum : struct, Enum;
