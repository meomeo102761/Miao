using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Miao.Core.Services
{
    public class StringOrArrayConverter : JsonConverter<string[]>
    {
        public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var single = reader.GetString();
                return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { single };
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                        list.Add(reader.GetString()!);
                }
                return list.ToArray();
            }

            return Array.Empty<string>();
        }

        public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var s in value) writer.WriteStringValue(s);
            writer.WriteEndArray();
        }
    }
}