using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Small dependency-free JSON reader/writer used by the frozen Interaction
    /// contract. It rejects duplicate object keys and trailing input so Host
    /// responses cannot be interpreted ambiguously.
    /// </summary>
    public static class InteractionJson
    {
        public static IDictionary<string, object> ParseObject(string json)
        {
            object value = new Parser(json).ParseDocument();
            var result = value as IDictionary<string, object>;
            if (result == null)
            {
                throw new FormatException("The JSON document must be an object.");
            }

            return result;
        }

        public static string RequireString(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (value == null ||
                !value.TryGetValue(propertyName, out raw) ||
                !(raw is string) ||
                string.IsNullOrWhiteSpace((string)raw))
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' must be a non-empty string."
                );
            }

            return ((string)raw).Trim();
        }

        public static string OptionalString(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (value == null || !value.TryGetValue(propertyName, out raw) ||
                raw == null)
            {
                return null;
            }

            var text = raw as string;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        public static bool? OptionalBoolean(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (value == null || !value.TryGetValue(propertyName, out raw) ||
                raw == null)
            {
                return null;
            }

            if (!(raw is bool))
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' must be a boolean."
                );
            }

            return (bool)raw;
        }

        public static int RequireInt32(
            IDictionary<string, object> value,
            string propertyName)
        {
            long number = RequireInt64(value, propertyName);
            if (number < int.MinValue || number > int.MaxValue)
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' is outside Int32 range."
                );
            }

            return (int)number;
        }

        public static long RequireInt64(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (value == null || !value.TryGetValue(propertyName, out raw))
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' is required."
                );
            }

            if (raw is long)
            {
                return (long)raw;
            }

            throw new FormatException(
                "JSON property '" + propertyName + "' must be an integer."
            );
        }

        public static IDictionary<string, object> RequireObject(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (value == null ||
                !value.TryGetValue(propertyName, out raw) ||
                !(raw is IDictionary<string, object>))
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' must be an object."
                );
            }

            return (IDictionary<string, object>)raw;
        }

        public static IList<object> RequireArray(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (value == null ||
                !value.TryGetValue(propertyName, out raw) ||
                !(raw is IList<object>))
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' must be an array."
                );
            }

            return (IList<object>)raw;
        }

        public static IReadOnlyList<string> OptionalStringArray(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (value == null || !value.TryGetValue(propertyName, out raw) ||
                raw == null)
            {
                return Array.Empty<string>();
            }

            var array = raw as IList<object>;
            if (array == null)
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' must be an array."
                );
            }

            var result = new List<string>(array.Count);
            for (int index = 0; index < array.Count; index++)
            {
                var item = array[index] as string;
                if (string.IsNullOrWhiteSpace(item))
                {
                    throw new FormatException(
                        "JSON property '" + propertyName +
                        "' must contain only non-empty strings."
                    );
                }

                result.Add(item.Trim());
            }

            return result.AsReadOnly();
        }

        public static void AppendQuoted(StringBuilder builder, string value)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20 ||
                            character == '\u2028' || character == '\u2029')
                        {
                            builder.Append("\\u");
                            builder.Append(
                                ((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture
                                )
                            );
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }

            builder.Append('"');
        }

        public static void AppendNullableString(
            StringBuilder builder,
            string value)
        {
            if (value == null)
            {
                builder.Append("null");
            }
            else
            {
                AppendQuoted(builder, value);
            }
        }

        public static void AppendFiniteDouble(StringBuilder builder, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "JSON numbers must be finite."
                );
            }

            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public static string NormalizeObjectJson(string json)
        {
            string normalized = string.IsNullOrWhiteSpace(json)
                ? "{}"
                : json.Trim();
            ParseObject(normalized);
            return normalized;
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                if (json == null)
                {
                    throw new ArgumentNullException(nameof(json));
                }

                this.json = json;
            }

            public object ParseDocument()
            {
                SkipWhitespace();
                object value = ParseValue();
                SkipWhitespace();
                if (index != json.Length)
                {
                    throw Error("Trailing JSON content is not allowed.");
                }

                return value;
            }

            private object ParseValue()
            {
                if (index >= json.Length)
                {
                    throw Error("Unexpected end of JSON input.");
                }

                switch (json[index])
                {
                    case '{':
                        return ParseObjectValue();
                    case '[':
                        return ParseArray();
                    case '"':
                        return ParseString();
                    case 't':
                        ConsumeLiteral("true");
                        return true;
                    case 'f':
                        ConsumeLiteral("false");
                        return false;
                    case 'n':
                        ConsumeLiteral("null");
                        return null;
                    default:
                        return ParseNumber();
                }
            }

            private IDictionary<string, object> ParseObjectValue()
            {
                index++;
                SkipWhitespace();
                var result = new Dictionary<string, object>(
                    StringComparer.Ordinal
                );
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    if (index >= json.Length || json[index] != '"')
                    {
                        throw Error("A JSON object key must be a string.");
                    }

                    string key = ParseString();
                    SkipWhitespace();
                    Require(':');
                    SkipWhitespace();
                    object value = ParseValue();
                    if (result.ContainsKey(key))
                    {
                        throw Error("Duplicate JSON object key '" + key + "'.");
                    }

                    result.Add(key, value);
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Require(',');
                    SkipWhitespace();
                }
            }

            private IList<object> ParseArray()
            {
                index++;
                SkipWhitespace();
                var result = new List<object>();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Require(',');
                    SkipWhitespace();
                }
            }

            private string ParseString()
            {
                Require('"');
                var builder = new StringBuilder();
                while (index < json.Length)
                {
                    char character = json[index++];
                    if (character == '"')
                    {
                        return builder.ToString();
                    }

                    if (character < 0x20)
                    {
                        throw Error("Unescaped control character in JSON string.");
                    }

                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (index >= json.Length)
                    {
                        throw Error("Incomplete JSON escape sequence.");
                    }

                    char escape = json[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw Error("Invalid JSON escape sequence.");
                    }
                }

                throw Error("Unterminated JSON string.");
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > json.Length)
                {
                    throw Error("Incomplete JSON unicode escape.");
                }

                int value = 0;
                for (int offset = 0; offset < 4; offset++)
                {
                    char character = json[index++];
                    int digit;
                    if (character >= '0' && character <= '9')
                    {
                        digit = character - '0';
                    }
                    else if (character >= 'a' && character <= 'f')
                    {
                        digit = character - 'a' + 10;
                    }
                    else if (character >= 'A' && character <= 'F')
                    {
                        digit = character - 'A' + 10;
                    }
                    else
                    {
                        throw Error("Invalid JSON unicode escape.");
                    }

                    value = (value << 4) | digit;
                }

                return (char)value;
            }

            private object ParseNumber()
            {
                int start = index;
                if (TryConsume('-') && index >= json.Length)
                {
                    throw Error("Incomplete JSON number.");
                }

                if (index < json.Length && json[index] == '0')
                {
                    index++;
                    if (index < json.Length && char.IsDigit(json[index]))
                    {
                        throw Error("JSON numbers cannot contain leading zeroes.");
                    }
                }
                else
                {
                    ConsumeDigits(required: true);
                }

                bool integer = true;
                if (TryConsume('.'))
                {
                    integer = false;
                    ConsumeDigits(required: true);
                }

                if (index < json.Length &&
                    (json[index] == 'e' || json[index] == 'E'))
                {
                    integer = false;
                    index++;
                    if (index < json.Length &&
                        (json[index] == '+' || json[index] == '-'))
                    {
                        index++;
                    }
                    ConsumeDigits(required: true);
                }

                string token = json.Substring(start, index - start);
                long integerValue;
                if (integer && long.TryParse(
                        token,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out integerValue))
                {
                    return integerValue;
                }

                double floatingValue;
                if (!double.TryParse(
                        token,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out floatingValue) ||
                    double.IsNaN(floatingValue) ||
                    double.IsInfinity(floatingValue))
                {
                    throw Error("Invalid or non-finite JSON number.");
                }

                return floatingValue;
            }

            private void ConsumeDigits(bool required)
            {
                int start = index;
                while (index < json.Length && char.IsDigit(json[index]))
                {
                    index++;
                }

                if (required && start == index)
                {
                    throw Error("A JSON number requires a digit.");
                }
            }

            private void ConsumeLiteral(string literal)
            {
                if (index + literal.Length > json.Length ||
                    string.CompareOrdinal(
                        json,
                        index,
                        literal,
                        0,
                        literal.Length
                    ) != 0)
                {
                    throw Error("Invalid JSON literal.");
                }

                index += literal.Length;
            }

            private bool TryConsume(char expected)
            {
                if (index < json.Length && json[index] == expected)
                {
                    index++;
                    return true;
                }

                return false;
            }

            private void Require(char expected)
            {
                if (!TryConsume(expected))
                {
                    throw Error("Expected '" + expected + "'.");
                }
            }

            private void SkipWhitespace()
            {
                while (index < json.Length)
                {
                    char character = json[index];
                    if (character != ' ' && character != '\t' &&
                        character != '\r' && character != '\n')
                    {
                        break;
                    }

                    index++;
                }
            }

            private FormatException Error(string message)
            {
                return new FormatException(
                    message + " Position: " + index.ToString(
                        CultureInfo.InvariantCulture
                    ) + "."
                );
            }
        }
    }
}
