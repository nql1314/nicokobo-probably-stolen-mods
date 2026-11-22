using System.Text;
using AugMechanicalAscension.Serialization;

namespace AugMechanicalAscension.Official;

internal enum OfficialSaveReadStatus { Readable, Missing, TooLarge, ChangedDuringRead, Malformed, Unavailable, Invalid }

/// <summary>Owns a detached JSON document. Dispose after all Root element reads.
/// Readable proves a bounded file read and syntax only, not a matching run,
/// committed transaction, durable flush, or successful native reload.</summary>
internal sealed class OfficialSaveReadResult : IDisposable
{
    private JsonDocument? _document;
    internal OfficialSaveReadResult(OfficialSaveReadStatus status, int byteCount = 0,
        string? reason = null, JsonDocument? document = null)
    { Status = status; ByteCount = byteCount; Reason = reason; _document = document; }
    public OfficialSaveReadStatus Status { get; }
    public int ByteCount { get; }
    public string? Reason { get; }
    public JsonElement Root => _document?.RootElement ??
        throw new InvalidOperationException("No readable save document, or result has been disposed");
    public void Dispose() => Interlocked.Exchange(ref _document, null)?.Dispose();
}

/// <summary>Read-only ES3 text support, independent of Unity and game singletons.
/// The caller validates native type, run/slot identity, owned state and receipts.</summary>
internal static partial class OfficialSaveReadbackApi
{
    public const int DefaultMaxBytes = 33554432;
    public const int DefaultMaxDepth = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);



    public static OfficialSaveReadResult ReadBytes(ReadOnlyMemory<byte> bytes,
        int maxBytes = DefaultMaxBytes, int maxDepth = DefaultMaxDepth)
    {
        if (maxBytes <= 0 || maxDepth <= 0) return new(OfficialSaveReadStatus.Invalid);
        if (bytes.Length > maxBytes) return new(OfficialSaveReadStatus.TooLarge, bytes.Length);
        try
        {
            var document = Parse(StrictUtf8.GetString(bytes.Span).TrimStart('\uFEFF'), maxDepth);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return new(OfficialSaveReadStatus.Readable, bytes.Length, document: document);
            document.Dispose();
            return new(OfficialSaveReadStatus.Malformed, bytes.Length, "Save root is not an object");
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        { return new(OfficialSaveReadStatus.Malformed, bytes.Length, ex.GetType().Name); }
    }

    /// <summary>Parse an ES3 object or an embedded inventory JSON string. The
    /// caller owns/disposes the document. Duplicate property checks are explicit.</summary>
    public static JsonDocument Parse(string source, int maxDepth = DefaultMaxDepth)
    {
        if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        return JsonDocument.Parse(Normalize(source), new JsonDocumentOptions { MaxDepth = maxDepth });
    }

    /// <summary>False for a non-object or duplicate key; true with present=false
    /// for an absent property. Unlike GetProperty, never chooses the last duplicate.</summary>
    public static bool TryGetUniqueProperty(JsonElement parent, string name,
        out JsonElement value, out bool present)
    {
        value = default; present = false;
        if (parent.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in parent.EnumerateObject())
        {
            if (!property.NameEquals(name)) continue;
            if (present) { value = default; return false; }
            value = property.Value; present = true;
        }
        return true;
    }

    public static JsonElement RequireProperty(JsonElement parent, string name) =>
        TryGetUniqueProperty(parent, name, out var value, out bool present) && present
            ? value : throw new InvalidDataException("Missing, duplicate or invalid property: " + name);

    /// <summary>Read actual item nodes reachable from a native inventory's index-zero
    /// root. Detached nodes are not delivery evidence. Returned elements borrow the
    /// caller's document; this does not validate ownership tags or business receipts.</summary>
    public static IReadOnlyList<JsonElement> ReadInventoryItems(JsonElement graph,
        int maxNodes = 65536)
    {
        var nodes = RequireProperty(graph, "saveItems");
        if (maxNodes <= 0 || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() <= 0 || nodes.GetArrayLength() > maxNodes)
            throw new InvalidDataException("Invalid saved inventory graph");
        var result = new List<JsonElement>();
        var visited = new HashSet<int>();
        var ids = new HashSet<int>();
        var pending = new Stack<int>(); pending.Push(0);
        while (pending.TryPop(out int index))
        {
            if (index < 0 || index >= nodes.GetArrayLength() || !visited.Add(index))
                throw new InvalidDataException("Invalid saved inventory edge");
            var node = nodes[index];
            if (!TryGetUniqueProperty(node, "unitCount", out var units, out bool hasUnits) ||
                !TryGetUniqueProperty(node, "uniqueId", out var uid, out bool hasId))
                throw new InvalidDataException("Invalid saved inventory identity");
            if (index > 0 && hasUnits && units.GetInt32() > 0 && hasId && uid.GetInt32() > 0)
            {
                if (!ids.Add(uid.GetInt32()) || string.IsNullOrWhiteSpace(RequireProperty(node, "identifier").GetString()))
                    throw new InvalidDataException("Duplicate or invalid saved item identity");
                result.Add(node);
            }
            if (!TryGetUniqueProperty(node, "childItems", out var children, out bool hasChildren))
                throw new InvalidDataException("Invalid saved inventory children");
            if (!hasChildren) continue;
            if (children.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid saved inventory children");
            foreach (var child in children.EnumerateArray())
            {
                int next = child.GetInt32();
                if (next <= 0) throw new InvalidDataException("Invalid saved inventory child index");
                pending.Push(next);
            }
        }
        return result;
    }

    /// <summary>Normalize ES3 numeric dictionary keys, raw string controls and
    /// legacy backslashes only. Do not use this on ordinary mod-owned JSON.</summary>
    public static string Normalize(string source)
    {
        if(source==null)throw new ArgumentNullException(nameof(source));
        var result = new StringBuilder(source.Length);
        bool inString = false;
        for (int i = 0; i < source.Length;)
        {
            char c = source[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < source.Length)
                {
                    char next = source[i + 1];
                    bool valid = "\"\\/bfnrt".Contains(next) || next == 'u' && i + 5 < source.Length &&
                        IsHex(source[i + 2]) && IsHex(source[i + 3]) && IsHex(source[i + 4]) && IsHex(source[i + 5]);
                    if (valid) { result.Append(c).Append(next); i += 2; continue; }
                    result.Append("\\\\"); i++; continue;
                }
                if (c < 0x20) result.Append("\\u").Append(((int)c).ToString("x4"));
                else result.Append(c);
                if (c == '"') inString = false;
                i++; continue;
            }
            if (c == '"') { inString = true; result.Append(c); i++; continue; }
            result.Append(c); i++;
            if (c is not ('{' or ',')) continue;
            int start = i;
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            int digitStart = i;
            if (i < source.Length && source[i] == '-') i++;
            int digits = i;
            while (i < source.Length && source[i] is >= '0' and <= '9') i++;
            if (i == digits) { result.Append(source, start, i - start); continue; }
            int digitEnd = i;
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            if (i < source.Length && source[i] == ':')
            {
                result.Append(source, start, digitStart - start);
                result.Append('"').Append(source, digitStart, digitEnd - digitStart).Append('"');
                result.Append(source, digitEnd, i - digitEnd).Append(':'); i++;
            }
            else result.Append(source, start, i - start);
        }
        return result.ToString();
    }

    private static bool IsHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}

