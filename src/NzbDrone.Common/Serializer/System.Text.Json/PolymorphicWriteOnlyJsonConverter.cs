using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer
{
    public class PolymorphicWriteOnlyJsonConverter<T> : JsonConverter<T>
    {
        [ThreadStatic]
        private static bool _isReading;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Cannot deserialize abstract/interface types - skip the JSON token
            if (typeToConvert.IsAbstract || typeToConvert.IsInterface)
            {
                reader.Skip();
                return default;
            }

            // Prevent infinite recursion - this converter is invoked via [JsonConverter] attribute
            // on the type, so it will be called again when we try to deserialize
            if (_isReading)
            {
                // Already in a read call - use default deserialization by reading as JsonElement
                using var doc = JsonDocument.ParseValue(ref reader);
                return default;
            }

            try
            {
                _isReading = true;
                return JsonSerializer.Deserialize<T>(ref reader, options);
            }
            finally
            {
                _isReading = false;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
