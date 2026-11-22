using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal static class EvidenceJson
{
    internal static object? Snapshot(object? value) => value switch
    {
        null => null,
        JToken token => token.DeepClone(),
        _ => JToken.FromObject(value, CreateSerializer())
    };
    // Unity's computed normalized property recursively returns another Vector2.
    // Evidence records its two original coordinates, without changing global
    // serializer settings used by the game or business Mods.
    internal static JsonSerializer CreateSerializer() => JsonSerializer.Create(new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        Converters = { new Vector2Coordinates() }
    });

    private sealed class Vector2Coordinates : JsonConverter
    {
        public override bool CanRead => false;
        public override bool CanConvert(Type objectType) => objectType == typeof(Vector2);
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is not Vector2 vector) throw new JsonSerializationException("Expected Vector2 evidence");
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(vector.x);
            writer.WritePropertyName("y"); writer.WriteValue(vector.y);
            writer.WriteEndObject();
        }
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            => throw new NotSupportedException("Evidence serializer is write-only");
    }
}
