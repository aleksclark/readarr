using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer
{
    public class PolymorphicWriteOnlyJsonConverter<T> : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Cannot deserialize abstract/interface types - skip the JSON token
            if (typeToConvert.IsAbstract || typeToConvert.IsInterface)
            {
                reader.Skip();
                return default;
            }

            // Create options without this converter to avoid infinite recursion
            var newOptions = new JsonSerializerOptions(options);
            newOptions.Converters.Remove(this);
            return JsonSerializer.Deserialize<T>(ref reader, newOptions);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
