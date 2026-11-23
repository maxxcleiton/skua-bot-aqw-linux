using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Skua.Core.Models.Converters;

public class IntConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(int) || objectType == typeof(int?);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null || reader.Value is null or (object)"null")
            return objectType == typeof(int?) ? null : 0;

        if (reader.TokenType == JsonToken.Integer)
        {
            try
            {
                return Convert.ToInt32(reader.Value);
            }
            catch
            {
                return 0;
            }
        }

        if (reader.TokenType == JsonToken.Float)
        {
            try
            {
                double d = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                return unchecked((int)Math.Round(d));
            }
            catch
            {
                return 0;
            }
        }

        string str = reader.Value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(str))
            return objectType == typeof(int?) ? null : 0;

        if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            return intVal;

        if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal))
            return unchecked((int)Math.Round(dVal));

        return 0;
    }
}
