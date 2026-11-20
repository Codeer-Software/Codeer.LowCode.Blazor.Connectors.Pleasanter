using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>値の変換 (本体のパラメータ・プリザンターの JSON・突き合わせの文字)。</summary>
    static class PleasanterValues
    {
        /// <summary>プリザンターに送る日時の書式 (秒まで。プリザンターの API は秒までを返す)。</summary>
        internal const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

        static readonly Regex NumberText = new(@"^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)([eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant);
        static readonly Regex DateTimeText = new(@"^([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?)(Z|[+-][0-9]{2}:[0-9]{2})?$", RegexOptions.CultureInvariant);
        static readonly string[] DateTimeFormats = { "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" };

        /// <summary>数値を decimal にする。decimal に入らない値 (桁あふれ・NaN・無限大) は PleasanterException。</summary>
        internal static decimal ToDecimal(object value)
        {
            try
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is OverflowException or FormatException or InvalidCastException)
            {
                throw new PleasanterException($"The value '{Convert.ToString(value, CultureInfo.InvariantCulture)}' is not a number that Pleasanter can store.", PleasanterErrorKind.InvalidRequest, e);
            }
        }

        /// <summary>数値の文字 (末尾の 0 を落とす: 1.0 と 1 を同じにする)。指数表記にならない。</summary>
        internal static string FormatNumber(decimal value) => (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

        /// <summary>突き合わせ・Id に使う値の文字 (数値は末尾の 0 を落とす)。</summary>
        internal static string KeyText(object value)
        {
            switch (value)
            {
                case decimal d:
                    return FormatNumber(d);
                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double:
                    return KeyText(ToDecimal(value));
                case DateOnly date:
                    return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case DateTime dateTime:
                    return dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
                case DateTimeOffset offset:
                    return KeyText(offset.DateTime);
                case bool b:
                    return b ? "true" : "false";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        static bool IsNumber(object value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

        /// <summary>
        /// 拡張 SQL のパラメータ (Params の値) にする。数値・真偽は JSON の数値・真偽、日付と日時は yyyy-MM-ddTHH:mm:ss (プリザンターが日時として受け取る)、
        /// 時刻は HH:mm:ss、それ以外は文字。null は JSON の null (プリザンターは null のパラメータをエラーにする)。
        /// </summary>
        internal static System.Text.Json.Nodes.JsonNode? ToExtendedSqlParameter(object? value) => value switch
        {
            null or DBNull => null,
            bool b => b,
            _ when IsNumber(value) => ToDecimal(value),
            DateTime dateTime => dateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
            DateOnly date => date.ToDateTime(TimeOnly.MinValue).ToString(DateTimeFormat, CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.DateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };

        /// <summary>拡張 SQL の結果の値: 整数は long、それ以外の数値は decimal、文字・真偽はそのまま、null は null。</summary>
        internal static object? FromExtendedSqlValue(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.TryGetDecimal(out var number) ? number : element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };

        /// <summary>
        /// 本体のパラメータの値を列の種類の比較用の値にする (数値 = decimal・日付と日時 = DateTime・真偽 = bool・文字 = string)。
        /// 列の種類で表せない値 (数値の列に数値でない文字など) は false。
        /// </summary>
        internal static bool TryConvert(PleasanterColumnKind kind, object value, out object converted)
        {
            converted = value;
            switch (kind)
            {
                case PleasanterColumnKind.Number:
                    if (value is bool flag) { converted = flag ? 1m : 0m; return true; }
                    if (IsNumber(value))
                    {
                        try { converted = ToDecimal(value); return true; }
                        catch (PleasanterException) { return false; }
                    }
                    if (value is string text && NumberText.IsMatch(text.Trim()) && decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        converted = number;
                        return true;
                    }
                    return false;
                case PleasanterColumnKind.Date:
                case PleasanterColumnKind.DateTime:
                    switch (value)
                    {
                        case DateTime dateTime: converted = dateTime; return true;
                        case DateOnly date: converted = date.ToDateTime(TimeOnly.MinValue); return true;
                        case DateTimeOffset offset: converted = offset.DateTime; return true;
                        case string s when TryParseDateTime(s, out var parsed): converted = parsed; return true;
                        case string s when DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d): converted = d.ToDateTime(TimeOnly.MinValue); return true;
                    }
                    return false;
                case PleasanterColumnKind.Bool:
                    switch (value)
                    {
                        case bool b: converted = b; return true;
                        case string s when s is "1" or "0" || bool.TryParse(s, out _): converted = s == "1" || (s != "0" && bool.Parse(s)); return true;
                    }
                    if (IsNumber(value))
                    {
                        var d = ToDecimal(value);
                        if (d is 0m or 1m) { converted = d == 1m; return true; }
                    }
                    return false;
                default:
                    converted = value switch
                    {
                        bool b => b ? "1" : "0",
                        _ when IsNumber(value) => FormatNumber(ToDecimal(value)),
                        DateOnly or DateTime or DateTimeOffset => KeyText(value),
                        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                    };
                    return true;
            }
        }

        static bool TryParseDateTime(string text, out DateTime value)
        {
            value = default;
            var match = DateTimeText.Match(text);
            return match.Success && DateTime.TryParseExact(match.Groups[1].Value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        /// <summary>
        /// 読んだ値 (<see cref="ConvertResult"/> の結果) を比較用の値にする (日付は DateTime)。
        /// </summary>
        internal static object? ToComparable(object? value) => value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : value;

        /// <summary>比較用の値どうしの比較 (数値は数値・日時は日時・文字は序数・真偽は false &lt; true)。</summary>
        internal static int Compare(object a, object b)
        {
            a = ToComparable(a)!;
            b = ToComparable(b)!;
            if (a is decimal da && b is decimal db) return da.CompareTo(db);
            if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
            if (a.GetType() == b.GetType() && a is IComparable comparable) return comparable.CompareTo(b);
            return string.CompareOrdinal(KeyText(a), KeyText(b));
        }

        /// <summary>
        /// プリザンターが返した値 → 列の種類の .NET の値。文字の "" と、日付の空の日付 (emptyDate) は null。
        /// 数値 = JSON の数値か数値の文字 / 日時 = yyyy-MM-ddTHH:mm:ss[.小数秒] (時差の部分があれば捨てて壁時計の値) / 真偽 = true / false。
        /// 読めない値は PleasanterException (別の値や文字のままにしない)。
        /// </summary>
        internal static object? ConvertResult(string table, PleasanterColumnDefinition column, JsonElement element, DateTime emptyDate)
        {
            //チェックの列は null を持たない (View の "false" は値の無い行も含む) ので、値が無ければ false
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return column.Kind == PleasanterColumnKind.Bool ? false : null;
            var text = element.ValueKind == JsonValueKind.String ? element.GetString()! : null;
            if (text != null && text.Length == 0) return null;
            switch (column.Kind)
            {
                case PleasanterColumnKind.String:
                    if (text != null) return text;
                    if (element.ValueKind == JsonValueKind.Number) return element.GetRawText();
                    break;
                case PleasanterColumnKind.Number:
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var d)) return d;
                    if (text != null && NumberText.IsMatch(text) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                    break;
                case PleasanterColumnKind.Date:
                case PleasanterColumnKind.DateTime:
                    if (text != null && TryParseDateTime(text, out var dateTime))
                    {
                        if (dateTime == emptyDate) return null;
                        return column.Kind == PleasanterColumnKind.Date ? DateOnly.FromDateTime(dateTime) : dateTime;
                    }
                    break;
                case PleasanterColumnKind.Bool:
                    if (element.ValueKind is JsonValueKind.True or JsonValueKind.False) return element.GetBoolean();
                    break;
                case PleasanterColumnKind.Attachments:
                    //ファイルの並び (AttachmentsHash の 1 列分) をそのまま JSON の文字で返す。ファイルが無ければ "[]"
                    if (element.ValueKind == JsonValueKind.Array) return element.GetRawText();
                    if (text != null) return text;
                    break;
            }
            var raw = text ?? element.GetRawText();
            if (100 < raw.Length) raw = raw[..100] + "...";
            var expected = column.Kind switch
            {
                PleasanterColumnKind.Number => "a number",
                PleasanterColumnKind.Date or PleasanterColumnKind.DateTime => "a date (yyyy-MM-ddTHH:mm:ss)",
                PleasanterColumnKind.Bool => "true or false",
                PleasanterColumnKind.Attachments => "a list of files",
                _ => "text",
            };
            throw new PleasanterException($"Pleasanter returned a value that cannot be read as {expected} (table '{table}', column '{column.Name}'): '{raw}'.", PleasanterErrorKind.Unknown);
        }

        /// <summary>プリザンターの行 → 列名と .NET の値 (定義にある列だけ。行に無い列は null)。</summary>
        internal static Dictionary<string, object?> ToValues(PleasanterTableDefinition definition, JsonElement row, DateTime emptyDate)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in definition.Columns)
            {
                JsonElement element = default;
                var found = column.HashName is { } hash
                    ? row.TryGetProperty(hash, out var container) && container.ValueKind == JsonValueKind.Object && container.TryGetProperty(column.Name, out element)
                    : row.TryGetProperty(column.Name, out element);
                values[column.Name] = found ? ConvertResult(definition.Name, column, element, emptyDate) : column.Kind == PleasanterColumnKind.Bool ? false : null;
            }
            return values;
        }
    }
}
