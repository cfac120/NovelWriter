using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// 宽松字符串转换器：把任意 JSON 值（null / 数字 / 数组 / 对象 / 字符串）转为字符串。
/// 解决 LLM 输出字段类型不稳定的问题（如 "character_involvement": ["CHAR_001"]）。
/// </summary>
public sealed class LooseStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.True:
            case JsonTokenType.False:
                return reader.GetBoolean() ? "true" : "false";
            case JsonTokenType.Number:
                // 整数/浮点数 —— 直接读出原始文本
                if (reader.TryGetInt64(out var l)) return l.ToString();
                if (reader.TryGetDecimal(out var dec)) return dec.ToString();
                return reader.GetDouble().ToString();
            case JsonTokenType.StartArray:
                // 数组：序列化为 JSON 字符串
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    return doc.RootElement.GetRawText();
                }
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    return doc.RootElement.GetRawText();
                }
            default:
                return reader.GetString();
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>
/// 宽松整数转换器：把 JSON 数字（含浮点）/ 数字字符串 / null 都安全转为 int。
/// </summary>
public sealed class LooseIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return 0;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i)) return i;
                if (reader.TryGetInt64(out var l)) return (int)l;
                if (reader.TryGetDecimal(out var dec)) return (int)dec;
                return (int)reader.GetDouble();
            case JsonTokenType.String:
                var s = reader.GetString();
                return int.TryParse(s, out var n) ? n : 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
