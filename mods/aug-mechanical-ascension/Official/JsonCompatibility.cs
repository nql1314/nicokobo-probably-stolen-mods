using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

// A small adapter preserves the legacy codecs' exact names and validation while
// using the Newtonsoft assembly supplied by the game. No System.Text.Json DLL.
namespace AugMechanicalAscension.Serialization;

internal enum JsonValueKind { Undefined, Object, Array, String, Number, True, False, Null }
internal sealed class JsonNamingPolicy
{ internal static readonly JsonNamingPolicy CamelCase = new(); internal string ConvertName(string name)=>new CamelCaseNamingStrategy().GetPropertyName(name,false); }
internal sealed class JsonSerializerOptions
{
    internal JsonNamingPolicy? PropertyNamingPolicy { get; set; }
    internal bool PropertyNameCaseInsensitive { get; set; }
    internal int MaxDepth { get; set; } = 64;
    internal bool WriteIndented { get; set; }
}
internal struct JsonDocumentOptions { internal int MaxDepth { get; set; } }
internal sealed class JsonException : Exception
{ internal JsonException(string message, Exception? inner = null) : base(message, inner) {} }
internal readonly struct JsonProperty(string name, JToken value)
{ internal string Name => name; internal JsonElement Value => new(value); internal bool NameEquals(string other)=>name==other; }
internal readonly struct JsonElement(JToken? value)
{
    internal JToken? Token => value;
    internal JsonValueKind ValueKind => value?.Type switch
    {
        JTokenType.Object => JsonValueKind.Object, JTokenType.Array => JsonValueKind.Array,
        JTokenType.String => JsonValueKind.String, JTokenType.Integer or JTokenType.Float => JsonValueKind.Number,
        JTokenType.Boolean => value.Value<bool>() ? JsonValueKind.True : JsonValueKind.False,
        JTokenType.Null => JsonValueKind.Null, _ => JsonValueKind.Undefined
    };
    internal bool TryGetProperty(string name, out JsonElement element)
    { var found = (value as JObject)?.Property(name, StringComparison.Ordinal); element = new(found?.Value); return found != null; }
    internal JsonElement GetProperty(string name) => TryGetProperty(name, out var found) ? found : throw new KeyNotFoundException(name);
    internal IEnumerable<JsonProperty> EnumerateObject() => value is JObject obj ? obj.Properties().Select(p => new JsonProperty(p.Name,p.Value)) : throw new JsonException("Not an object");
    internal IEnumerable<JsonElement> EnumerateArray() => value is JArray array ? array.Select(p => new JsonElement(p)) : throw new JsonException("Not an array");
    internal int GetArrayLength() => (value as JArray)?.Count ?? throw new JsonException("Not an array");
    internal JsonElement this[int index] => new(value![index]);
    internal string? GetString() => value?.Type == JTokenType.Null ? null : value?.Type == JTokenType.String ? value.Value<string>() : throw new JsonException("Not a string");
    internal bool TryGetInt32(out int result) => int.TryParse(value?.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && value?.Type == JTokenType.Integer;
    internal bool TryGetInt64(out long result) => long.TryParse(value?.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && value?.Type == JTokenType.Integer;
    internal int GetInt32() => TryGetInt32(out int result) ? result : throw new JsonException("Not Int32");
    internal long GetInt64() => TryGetInt64(out long result) ? result : throw new JsonException("Not Int64");
    internal bool TryGetDouble(out double result) => double.TryParse(value?.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && ValueKind == JsonValueKind.Number;
    internal double GetDouble() => TryGetDouble(out double result) ? result : throw new JsonException("Not a number");
    internal bool GetBoolean() => value?.Type == JTokenType.Boolean ? value.Value<bool>() : throw new JsonException("Not a boolean");
    internal string GetRawText() => value?.ToString(Formatting.None) ?? "null";
}
internal sealed class JsonDocument(JToken root) : IDisposable
{
    internal JsonElement RootElement => new(root);
    internal static JsonDocument Parse(string json, JsonDocumentOptions options = default)
    {
        try
        {
            StrictJsonSyntax.Validate(json,options.MaxDepth>0?options.MaxDepth:64);
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = options.MaxDepth > 0 ? options.MaxDepth : 64 };
            var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new JsonException("Trailing JSON content");
            return new(token);
        }
        catch (Newtonsoft.Json.JsonException ex) { throw new JsonException(ex.Message,ex); }
    }
    public void Dispose() {}
}
internal static class JsonSerializer
{
    private static JsonSerializerSettings Settings(JsonSerializerOptions? options) => new()
    {
        ContractResolver = options?.PropertyNamingPolicy == null ? new DefaultContractResolver() : new CamelCasePropertyNamesContractResolver(),
        MaxDepth = options?.MaxDepth ?? 64, DateParseHandling = DateParseHandling.None,
        Formatting = options?.WriteIndented == true ? Formatting.Indented : Formatting.None,
        Culture = CultureInfo.InvariantCulture
    };
    internal static string Serialize<T>(T value, JsonSerializerOptions? options = null)
    { try { return JsonConvert.SerializeObject(value,Settings(options)); } catch (Newtonsoft.Json.JsonException ex) { throw new JsonException(ex.Message,ex); } }
    internal static T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json,new() { MaxDepth = options?.MaxDepth ?? 64 });
            var serializer = Newtonsoft.Json.JsonSerializer.Create(Settings(options));
            var token = document.RootElement.Token!;
            if (options?.PropertyNameCaseInsensitive != true) ExactNames(token,typeof(T),serializer.ContractResolver);
            return token.ToObject<T>(serializer);
        }
        catch (Newtonsoft.Json.JsonException ex) { throw new JsonException(ex.Message,ex); }
    }
    private static void ExactNames(JToken token, Type type, IContractResolver resolver)
    {
        var nullable=Nullable.GetUnderlyingType(type);
        if(token.Type==JTokenType.Null)
        { if(type.IsValueType&&nullable==null)throw new JsonException("Null for a non-nullable value");return; }
        type=nullable??type;
        if(type==typeof(string)&&token.Type!=JTokenType.String)throw new JsonException("Expected JSON string");
        if(type==typeof(bool)&&token.Type!=JTokenType.Boolean)throw new JsonException("Expected JSON boolean");
        if(type.IsEnum||type==typeof(int)||type==typeof(long)||type==typeof(short)||type==typeof(byte)||type==typeof(uint)||type==typeof(ulong))
        {
            if(token.Type!=JTokenType.Integer)throw new JsonException("Expected JSON integer");
            try { Convert.ChangeType(((JValue)token).Value,type.IsEnum?Enum.GetUnderlyingType(type):type,CultureInfo.InvariantCulture); }
            catch(Exception ex)when(ex is OverflowException or InvalidCastException or FormatException){throw new JsonException("Integer outside target range",ex);}
        }
        if((type==typeof(double)||type==typeof(float)||type==typeof(decimal))&&token.Type is not(JTokenType.Integer or JTokenType.Float))throw new JsonException("Expected JSON number");
        var contract = resolver.ResolveContract(type);
        if (contract is JsonObjectContract obj && token is JObject data)
        {
            foreach (var property in data.Properties().ToArray())
            {
                var match = obj.Properties.FirstOrDefault(p => p.PropertyName == property.Name && !p.Ignored);
                if (match == null) { property.Remove(); continue; }
                if (match.PropertyType != null) ExactNames(property.Value,match.PropertyType,resolver);
            }
        }
        else if (contract is JsonArrayContract array && token is JArray items)
            foreach (var item in items) ExactNames(item,array.CollectionItemType ?? typeof(object),resolver);
        else if (contract is JsonDictionaryContract dict && token is JObject entries)
            foreach (var entry in entries.Properties()) ExactNames(entry.Value,dict.DictionaryValueType ?? typeof(object),resolver);
    }
}

internal static class StrictJsonSyntax
{
    internal static void Validate(string source,int maxDepth)
    {
        int at=0;
        void White(){while(at<source.Length&&source[at] is ' ' or '\t' or '\r' or '\n')at++;}
        void Fail()=>throw new JsonException("Invalid JSON syntax at character "+at);
        void String()
        {
            if(at>=source.Length||source[at++]!='"'){Fail();return;}
            while(at<source.Length)
            {
                char c=source[at++];if(c=='"')return;if(c<32){Fail();return;}
                if(c!='\\')continue;if(at>=source.Length){Fail();return;}
                char escape=source[at++];
                if(escape=='u'){for(int i=0;i<4;i++)if(at>=source.Length||!Uri.IsHexDigit(source[at++])){Fail();return;}}
                else if(escape is not('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't')){Fail();return;}
            }
            Fail();
        }
        void Value(int depth)
        {
            if(depth>maxDepth){Fail();return;}White();if(at>=source.Length){Fail();return;}
            char c=source[at];
            if(c=='"'){String();return;}
            if(c=='{'||c=='[')
            {
                bool obj=c=='{';char end=obj?'}':']';at++;White();if(at<source.Length&&source[at]==end){at++;return;}
                while(true)
                {
                    if(obj){White();String();White();if(at>=source.Length||source[at++]!=':'){Fail();return;}}
                    Value(depth+1);White();if(at>=source.Length){Fail();return;}
                    char separator=source[at++];if(separator==end)return;if(separator!=','){Fail();return;}
                }
            }
            foreach(string literal in new[]{"true","false","null"})
                if(source.Length-at>=literal.Length&&string.CompareOrdinal(source,at,literal,0,literal.Length)==0){at+=literal.Length;return;}
            if(c=='-'){at++;if(at>=source.Length){Fail();return;}}
            if(source[at]=='0')at++;
            else if(source[at] is >= '1' and <= '9'){while(at<source.Length&&source[at] is >= '0' and <= '9')at++;}
            else {Fail();return;}
            if(at<source.Length&&source[at]=='.'){at++;int start=at;while(at<source.Length&&source[at] is >= '0' and <= '9')at++;if(at==start){Fail();return;}}
            if(at<source.Length&&source[at] is 'e' or 'E'){at++;if(at<source.Length&&source[at] is '+' or '-')at++;int start=at;while(at<source.Length&&source[at] is >= '0' and <= '9')at++;if(at==start){Fail();return;}}
        }
        Value(0);White();if(at!=source.Length)Fail();
    }
}
