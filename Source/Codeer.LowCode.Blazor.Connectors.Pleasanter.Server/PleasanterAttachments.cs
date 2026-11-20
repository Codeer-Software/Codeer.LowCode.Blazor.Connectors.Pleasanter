using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Connectors.Pleasanter.Server
{
    /// <summary>
    /// 添付の列 (AttachmentsA など) の値の読み解き。値はプリザンターの AttachmentsHash の 1 列分と同じ形の JSON の配列の文字。
    /// 読むと [{"Guid","Name","Size",...}]。書くときに意味を持つのは、Base64 を持つ要素 (追加: Name・ContentType・Base64) と
    /// Deleted が true の要素 (削除: Guid) だけ。それ以外の要素 (読んだままのファイル) は送らない。
    /// </summary>
    static class PleasanterAttachments
    {
        static readonly Regex GuidPattern = new("^[0-9A-Fa-f]{32}$", RegexOptions.CultureInvariant);

        /// <summary>送るときに ContentType が無いファイルの種類 (プリザンターは無いと text/plain にする)。</summary>
        const string DefaultContentType = "application/octet-stream";

        /// <summary>1 列分の変更 (足すファイルと消すファイルの Guid)。</summary>
        internal sealed class Change
        {
            public List<JsonObject> Additions { get; } = new();
            public List<string> Deletions { get; } = new();
            public bool IsEmpty => Additions.Count == 0 && Deletions.Count == 0;
        }

        /// <summary>プリザンターの添付の Guid の形 (32 桁の 16 進)。API の URL に入れる前に確かめる。</summary>
        internal static bool IsGuid(string? text) => text != null && GuidPattern.IsMatch(text);

        /// <summary>
        /// 書き込みの値 → 変更。null・空の文字は変更なし。形が不正 (JSON の配列でない・要素が JSON のオブジェクトでない・追加に Name か Base64 が無い / 読めない・
        /// 削除に Guid が無い・Base64 と Deleted の両方を持つ) なら PleasanterException。
        /// </summary>
        internal static Change ParseChange(string table, string column, object? value)
        {
            var change = new Change();
            if (value == null) return change;
            if (value is not string text) throw Invalid(table, column, "the value is not text");
            if (string.IsNullOrWhiteSpace(text)) return change;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(text);
            }
            catch (JsonException e)
            {
                throw new PleasanterException(Message(table, column, "the value is not JSON"), PleasanterErrorKind.InvalidRequest, e);
            }
            if (root is not JsonArray array) throw Invalid(table, column, "the value is not a JSON array");

            foreach (var node in array)
            {
                if (node is not JsonObject item) throw Invalid(table, column, "an element is not a JSON object");
                var deleted = item["Deleted"] is JsonValue d && d.TryGetValue<bool>(out var flag) && flag;
                var hasBase64 = item["Base64"] != null;
                if (deleted && hasBase64) throw Invalid(table, column, "an element has both Base64 and Deleted");
                if (deleted)
                {
                    var guid = GetString(item, "Guid");
                    if (!IsGuid(guid)) throw Invalid(table, column, "an element to delete has no valid Guid");
                    if (!change.Deletions.Contains(guid!, StringComparer.OrdinalIgnoreCase)) change.Deletions.Add(guid!);
                    continue;
                }
                if (!hasBase64) continue;

                var name = GetString(item, "Name");
                var base64 = GetString(item, "Base64");
                if (string.IsNullOrEmpty(name)) throw Invalid(table, column, "an element to add has no Name");
                if (base64 == null) throw Invalid(table, column, $"the Base64 of '{name}' is not text");
                if (!IsBase64(base64)) throw Invalid(table, column, $"the Base64 of '{name}' cannot be read");
                var contentType = GetString(item, "ContentType");
                //送るのは決まった 3 つだけ (Guid・Action などを送らない)
                change.Additions.Add(new JsonObject
                {
                    ["Name"] = name,
                    ["ContentType"] = string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType,
                    ["Base64"] = base64,
                });
            }
            return change;
        }

        /// <summary>読んだ値 (プリザンターが返した並び) にあるファイルの Guid。読めなければ空。</summary>
        internal static List<string> ReadGuids(string? stored)
            => ReadFiles(stored).Select(e => e.Guid).ToList();

        /// <summary>読んだ値にあるファイル (Guid と名前)。Guid の無い要素・Deleted が true の要素は入れない。読めなければ空。</summary>
        internal static List<(string Guid, string? Name)> ReadFiles(string? stored)
        {
            var list = new List<(string, string?)>();
            if (string.IsNullOrWhiteSpace(stored)) return list;
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(stored);
            }
            catch (JsonException)
            {
                return list;
            }
            if (root is not JsonArray array) return list;
            foreach (var item in array.OfType<JsonObject>())
            {
                if (item["Deleted"] is JsonValue d && d.TryGetValue<bool>(out var flag) && flag) continue;
                var guid = GetString(item, "Guid");
                if (IsGuid(guid)) list.Add((guid!, GetString(item, "Name")));
            }
            return list;
        }

        /// <summary>送る 1 列分の配列 (追加 + 消す Guid)。</summary>
        internal static JsonArray ToRequest(IEnumerable<JsonObject> additions, IEnumerable<string> deletions)
        {
            var array = new JsonArray();
            foreach (var addition in additions) array.Add(addition.DeepClone());
            foreach (var guid in deletions) array.Add(new JsonObject { ["Guid"] = guid, ["Deleted"] = true });
            return array;
        }

        static bool IsBase64(string text)
        {
            if (text.Length % 4 != 0) return false;
            var buffer = new byte[text.Length / 4 * 3];
            return Convert.TryFromBase64String(text, buffer, out _);
        }

        static string? GetString(JsonObject item, string name)
            => item[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        static string Message(string table, string column, string reason)
            => $"The value of the attachments column '{column}' of Pleasanter table '{table}' is invalid: {reason}. " +
               "Write a JSON array of files to add ({\"Name\",\"ContentType\",\"Base64\"}) and files to delete ({\"Guid\",\"Deleted\":true}).";

        static PleasanterException Invalid(string table, string column, string reason)
            => new(Message(table, column, reason), PleasanterErrorKind.InvalidRequest);
    }
}
