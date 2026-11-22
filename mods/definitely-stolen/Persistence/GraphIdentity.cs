using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DefinitelyStolen.Persistence;

internal static class GraphIdentity
{
    internal static HashSet<int> Ids(string source) => Nodes(source).Keys.ToHashSet();
    internal static IReadOnlyDictionary<int, (string Id, int Units)> Nodes(string source)
    {
        var nodes = JObject.Parse(source, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error })["saveItems"] as JArray ?? throw new InvalidDataException("Missing native graph nodes");
        var ids = new Dictionary<int, (string, int)>();
        foreach (var node in nodes)
        {
            int id = node.Value<int>("uniqueId");
            string identifier = node.Value<string>("identifier") ?? "";
            int units = node.Value<int>("unitCount");
            if (id <= 0 || units <= 0 || string.IsNullOrWhiteSpace(identifier) || !ids.TryAdd(id, (identifier, units)))
                throw new InvalidDataException("Invalid saved native graph");
        }
        if (ids.Count == 0) throw new InvalidDataException("Empty native graph");
        return ids;
    }
}
