using System.Globalization;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Offline scalar/JSON diagnostic only. No Unity/game assembly is loaded, no
// game process or save is changed, and this never replaces native ES3 evidence.
internal static class Program
{
    private static readonly JArray Checks = new();
    private static void Check(string name, bool pass, object? observed = null)
    { Checks.Add(new JObject { ["name"] = name, ["passed"] = pass, ["observed"] = observed == null ? null : JToken.FromObject(observed) }); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static JToken Value(JToken token, params string[] path)
    { foreach (string segment in path) token = token[segment] ?? throw new InvalidDataException("Missing " + segment); return token; }
    private static JObject FailedCheck(JObject result, string name) => ((JArray)result["checks"]!).OfType<JObject>().Single(check => (string?)check["name"] == name);
    private static JArray Rehydrate(JArray source, bool widen)
    {
        var copy = (JArray)source.DeepClone();
        foreach (JObject item in copy)
            foreach (JProperty tag in ((JObject)item["tags"]!).Properties().ToArray())
            {
                float number = tag.Value["number"]!.Value<float>();
                var factory = widen ? JObject.FromObject(new { number = (double)number }) : JObject.FromObject(new { number });
                // Rebuild the object: replacing an equal scalar in an existing
                // JObject can preserve its old CLR payload type as a no-op.
                tag.Value = new JObject(((JObject)tag.Value).Properties().Select(property => new JProperty(property.Name,
                    property.Name == "number" ? factory["number"]!.DeepClone() : property.Value.DeepClone())));
            }
        return copy;
    }
    private static List<JObject> Differences(JToken? left, JToken? right, string path = "$")
    {
        var result = new List<JObject>();
        if (left is JObject a && right is JObject b)
        {
            foreach (string name in a.Properties().Select(property => property.Name).Concat(b.Properties().Select(property => property.Name)).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
                result.AddRange(Differences(a[name], b[name], path + "." + name));
        }
        else if (left is JArray c && right is JArray d)
        {
            if (c.Count != d.Count) result.Add(new JObject { ["path"] = path + ".length", ["expected"] = c.Count, ["observed"] = d.Count });
            for (int index = 0; index < Math.Min(c.Count, d.Count); index++) result.AddRange(Differences(c[index], d[index], path + "[" + index + "]"));
        }
        else if (!JToken.DeepEquals(left, right)) result.Add(new JObject { ["path"] = path, ["expected"] = left?.DeepClone(), ["observed"] = right?.DeepClone(),
            ["expectedMissing"] = left == null, ["observedMissing"] = right == null });
        return result;
    }
    private static int Main(string[] args)
    {
        if (args.Length is not (3 or 4)) throw new ArgumentException("Args: <actual-achievements-reload-result.json> <new-report.json> <actual-game-Newtonsoft.Json.dll> [actual-deviceflows-reload-result.json]");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]), gameLibrary = Path.GetFullPath(args[2]);
        if (File.Exists(output) || source == output) throw new InvalidOperationException("Report must be a distinct new file");
        string sourceHash = Hash(source), libraryHash = Hash(gameLibrary);
        var assembly = typeof(JToken).Assembly;
        Check("actual-game-newtonsoft-bytes", Hash(assembly.Location) == libraryHash, new { source = gameLibrary, sourceSha256 = libraryHash,
            loaded = assembly.Location, loadedSha256 = Hash(assembly.Location), assembly = assembly.FullName, mvid = assembly.ManifestModule.ModuleVersionId });
        var original = JObject.Parse(File.ReadAllText(source));
        var failure = FailedCheck(original, "achievements.reload.exact-rewards-and-qualifications");
        var expected = (JArray)failure["expected"]!; var observed = (JArray)failure["observed"]!;
        Check("original-native-failure-retained", (bool?)failure["passed"] == false, new { originalRun = (string?)original["runId"], originalRuntimePassed = (bool?)failure["passed"] });
        Check("actual-fourteen-items", expected.Count == 14 && observed.Count == 14, new { expected = expected.Count, observed = observed.Count });
        Check("both-json-reparsed-equal", JToken.DeepEquals(expected, observed));
        var legacy = Rehydrate(observed, false); var widened = Rehydrate(observed, true);
        Check("old-live-single-vs-baseline-double-reproduces-false", !JToken.DeepEquals(expected, legacy));
        var freshBaseline = JArray.Parse(widened.ToString(Formatting.None));
        Check("new-double-projection-fresh-roundtrip-exact", JToken.DeepEquals(widened, freshBaseline));
        Check("old-truncated-baseline-remains-rejected", !JToken.DeepEquals(expected, widened));
        var samples = new JArray(); int numberFields = 0;
        for (int itemIndex = 0; itemIndex < observed.Count; itemIndex++)
        {
            JObject item = (JObject)observed[itemIndex];
            foreach (JProperty tag in ((JObject)item["tags"]!).Properties())
            {
                numberFields++;
                float number = tag.Value["number"]!.Value<float>();
                var oldToken = (JValue)legacy[itemIndex]!["tags"]![tag.Name]!["number"]!;
                var newToken = (JValue)widened[itemIndex]!["tags"]![tag.Name]!["number"]!;
                Check("number-token-types." + item["uid"] + "." + tag.Name, oldToken.Value is float && newToken.Value is double);
                if (number == 0) continue;
                var oldFactory = JObject.FromObject(new { number });
                var fixedFactory = JObject.FromObject(new { number = (double)number });
                Check("actual-FromObject-type-and-value." + tag.Name,
                    ((JValue)oldFactory["number"]!).Value is float && ((JValue)fixedFactory["number"]!).Value is double &&
                    JToken.DeepEquals(oldFactory["number"], oldToken) && JToken.DeepEquals(fixedFactory["number"], newToken));
                Check("explicit-double-bit-exact-roundtrip." + tag.Name,
                    BitConverter.DoubleToInt64Bits((double)number) == BitConverter.DoubleToInt64Bits(JObject.Parse(fixedFactory.ToString(Formatting.None))["number"]!.Value<double>()));
                int bits = BitConverter.SingleToInt32Bits(number);
                bool upperRejected = false, lowerRejected = false;
                foreach (int direction in new[] { -1, 1 })
                {
                    float changed = BitConverter.Int32BitsToSingle(bits + direction);
                    var mutation = (JArray)widened.DeepClone(); mutation[itemIndex]!["tags"]![tag.Name]!["number"] = (double)changed;
                    bool rejected = !JToken.DeepEquals(freshBaseline, mutation);
                    Check("one-single-ulp-still-rejected." + tag.Name + "." + direction, rejected,
                        new { originalSingleBits = bits.ToString("X8"), changedSingleBits = BitConverter.SingleToInt32Bits(changed).ToString("X8"), original = (double)number, changed = (double)changed });
                    if (direction > 0) upperRejected = rejected; else lowerRejected = rejected;
                }
                samples.Add(new JObject { ["uid"] = item["uid"]!.DeepClone(), ["id"] = item["id"]!.DeepClone(), ["tag"] = tag.Name,
                    ["originalJsonNumber"] = tag.Value["number"]!.DeepClone(), ["singleBitsHex"] = bits.ToString("X8"),
                    ["singleRoundtripText"] = number.ToString("R", CultureInfo.InvariantCulture), ["exactSingleWidenedDouble"] = (double)number,
                    ["oldUnderlyingType"] = oldToken.Value!.GetType().FullName, ["newUnderlyingType"] = newToken.Value!.GetType().FullName,
                    ["parsedBaselineUnderlyingType"] = ((JValue)expected[itemIndex]!["tags"]![tag.Name]!["number"]!).Value!.GetType().FullName,
                    ["oldSerialized"] = oldFactory.ToString(Formatting.None), ["newSerialized"] = fixedFactory.ToString(Formatting.None),
                    ["oldComparisonPassed"] = JToken.DeepEquals(Value(expected[itemIndex]!, "tags", tag.Name, "number"), oldToken),
                    ["newFreshComparisonPassed"] = JToken.DeepEquals(Value(freshBaseline[itemIndex]!, "tags", tag.Name, "number"), newToken),
                    ["upperOneUlpRejected"] = upperRejected, ["lowerOneUlpRejected"] = lowerRejected });
            }
        }
        Check("five-real-nonzero-single-samples", samples.Count == 5, samples.Count);
        // All non-number properties are still compared; none is removed or ignored.
        foreach (JObject item in observed)
        {
            int index = observed.IndexOf(item);
            foreach (JProperty property in item.Properties().Where(property => property.Name != "tags"))
                Check("other-item-field-unchanged." + item["uid"] + "." + property.Name, JToken.DeepEquals(property.Value, widened[index]![property.Name]));
            foreach (JProperty tag in ((JObject)item["tags"]!).Properties())
                foreach (JProperty property in ((JObject)tag.Value).Properties().Where(property => property.Name != "number"))
                    Check("other-tag-field-unchanged." + item["uid"] + "." + tag.Name + "." + property.Name,
                        JToken.DeepEquals(property.Value, widened[index]!["tags"]![tag.Name]![property.Name]));
        }
        foreach (string field in new[] { "uid", "count", "value" })
        {
            var mutation = (JArray)widened.DeepClone(); mutation[0]![field] = mutation[0]![field]!.Value<long>() + 1;
            Check("other-item-mutation-rejected." + field, !JToken.DeepEquals(freshBaseline, mutation));
        }
        foreach (string field in new[] { "enabled", "integer", "doubleNumber", "text" })
        {
            var mutation = (JArray)widened.DeepClone(); var tag = (JObject)((JObject)mutation[0]!["tags"]!).Properties().First().Value;
            tag[field] = field switch { "enabled" => JToken.FromObject(!tag[field]!.Value<bool>()), "integer" => JToken.FromObject(tag[field]!.Value<int>() + 1),
                "doubleNumber" => JToken.FromObject(tag[field]!.Value<double>() + .5d), _ => JToken.FromObject(tag[field]!.Value<string>() + "changed") };
            Check("other-tag-mutation-rejected." + field, !JToken.DeepEquals(freshBaseline, mutation));
        }
        var report = new JObject { ["kind"] = "offline-game-newtonsoft-scalar-projection-check", ["sourceResult"] = source,
            ["sourceResultSha256"] = sourceHash, ["originalNativeRunStillFailed"] = true, ["library"] = JObject.FromObject(new { original = gameLibrary, originalSha256 = libraryHash,
                loaded = assembly.Location, loadedSha256 = Hash(assembly.Location), mvid = assembly.ManifestModule.ModuleVersionId }),
            ["numberFields"] = numberFields, ["samples"] = samples };
        report["offlineRuntime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        report["legacyOfflineReformattedJsonEqualsOriginalMonoJson"] = JToken.DeepEquals(observed, JArray.Parse(legacy.ToString(Formatting.None)));
        report["formattingScope"] = "This offline CLR's Single R formatter can differ from the native Mono result. Original JSON literals recover the original Single bits; typed FromObject/DeepEquals and widened-Double bit-exact roundtrip are checked. No native replay is claimed.";
        if (args.Length == 4)
        {
            string devicePath = Path.GetFullPath(args[3]); string deviceHash = Hash(devicePath);
            var deviceResult = JObject.Parse(File.ReadAllText(devicePath)); var deviceFailure = FailedCheck(deviceResult, "deviceflows.reload-native-uids-content-pages-and-ras-ports");
            JToken deviceExpected = deviceFailure["expected"]!, deviceObserved = deviceFailure["observed"]!;
            var differences = Differences(deviceExpected, deviceObserved);
            report["deviceFlowsReadOnly"] = new JObject { ["file"] = devicePath, ["sha256"] = deviceHash, ["nativeRuntimePassed"] = deviceFailure["passed"]!.DeepClone(),
                ["reparsedDeepEquals"] = JToken.DeepEquals(deviceExpected, deviceObserved), ["differenceCount"] = differences.Count, ["differences"] = new JArray(differences) };
            Check("deviceflows-has-separate-real-projection-differences", !JToken.DeepEquals(deviceExpected, deviceObserved) && differences.Count == 22);
            Check("deviceflows-original-result-unchanged", Hash(devicePath) == deviceHash);
        }
        Check("original-result-unchanged", Hash(source) == sourceHash);
        report["checks"] = Checks; report["checksCount"] = Checks.Count; report["failedCount"] = Checks.Count(check => (bool?)check["passed"] != true);
        report["passed"] = (int)report["failedCount"]! == 0;
        File.WriteAllText(output, report.ToString(Formatting.Indented));
        Console.WriteLine($"Checks={Checks.Count}; failures={report["failedCount"]}; real samples={samples.Count}; report={output}");
        foreach (JToken sample in samples) Console.WriteLine($"{sample["tag"]}: json={sample["originalJsonNumber"]}, SingleExact={sample["exactSingleWidenedDouble"]}, oldEqual={sample["oldComparisonPassed"]}, newEqual={sample["newFreshComparisonPassed"]}");
        return (bool)report["passed"]! ? 0 : 1;
    }
}
