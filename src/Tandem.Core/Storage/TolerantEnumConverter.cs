using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tandem.Core.Storage;

/// <summary>
/// Enums as camelCase strings. An unknown value (a typo, or a value written by a newer version)
/// reads as the enum's default instead of making the whole file unreadable.
/// </summary>
internal sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class TolerantEnumConverter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String when Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value) && Enum.IsDefined(value):
                    return value;
                case JsonTokenType.Number when reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(T), number):
                    return (T)Enum.ToObject(typeof(T), number);
                default:
                    return default;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var name = value.ToString();
            writer.WriteStringValue(name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..]);
        }
    }
}
