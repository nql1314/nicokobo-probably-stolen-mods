using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

// A resumable experiment is deliberately not a passing acceptance result.
// Only the explicitly bound native breeding experiment may cross this gate.
internal static class ProbeContinuations
{
    internal static bool BreedingCheckpoint(JObject source)
    {
        try
        {
            var p = (JObject)source["persistence"]!;
            var b = (JObject)p["nativeBreeding"]!;
            var checkpoint = (JObject)b["checkpoint"]!;
            var checks = (JArray)source["checks"]!;
            string raw = (string)b["checkpointJson"]!;
            return (string?)source["mode"] is "breeding-save" or "breeding-reload" &&
                (bool?)source["executed"] == true && (bool?)source["executedChecksPassed"] == true &&
                source["error"]?.Type is null or JTokenType.Null && (string?)source["phase"] == "completed" &&
                checks.Count > 0 && checks.OfType<JObject>().All(c => (string?)c["status"] != "failed") &&
                checks.OfType<JObject>().Any(c => ((string?)c["name"])?.StartsWith("native-breeding.checkpoint-real-es3.", StringComparison.Ordinal) == true && (bool?)c["passed"] == true) &&
                checks.OfType<JObject>().Any(c => (string?)c["name"] == "persistence.actual-es3-carrier-readback" && (bool?)c["passed"] == true) &&
                (int?)b["schema"] == 1 && (string?)b["goal"] == "rat_800g" && (bool?)b["resumeAllowed"] == true &&
                (string?)b["status"] is "in-progress-not-tested" or "claimed-awaiting-independent-reload" &&
                (string?)b["sourceRunId"] == (string?)source["runId"] && (string?)b["sourceProcessToken"] == (string?)source["processToken"] &&
                (string?)b["gameRunId"] == (string?)p["gameRunId"] && (int?)b["slotId"] == (int?)p["slotId"] &&
                (string?)b["persistentDataPath"] == (string?)p["persistentDataPath"] && (int?)b["day"] == (int?)p["day"] && (int?)b["cash"] == (int?)p["cash"] &&
                ((string?)p["saveSha256"])?.Length == 64 && (string?)b["es3Sha256AtReadback"] == (string?)p["saveSha256"] &&
                (string?)b["es3PathAtReadback"] == (string?)p["saveFile"] && b["items"] is JArray { Count: > 0 } &&
                (int?)checkpoint["schema"] == 1 && (string?)checkpoint["goal"] == "rat_800g" &&
                (string?)checkpoint["status"] == (string?)b["status"] &&
                (string?)checkpoint["lastCheckpointRunId"] == (string?)source["runId"] &&
                (string?)checkpoint["lastCheckpointProcessToken"] == (string?)source["processToken"] &&
                (string?)checkpoint["gameRunId"] == (string?)p["gameRunId"] && (int?)checkpoint["slotId"] == (int?)p["slotId"] &&
                (int?)checkpoint["day"] == (int?)p["day"] && (int?)checkpoint["cash"] == (int?)p["cash"] &&
                !string.IsNullOrEmpty(raw) && (string?)p["modData"]?["nicokobo.content_native_probe:breeding-progress"] == raw &&
                JToken.DeepEquals(JToken.Parse(raw), checkpoint);
        }
        catch { return false; }
    }
}
