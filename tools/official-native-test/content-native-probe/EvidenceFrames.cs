using System.Diagnostics;
using System.Security.Cryptography;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private readonly List<object> _evidenceFrames = new();
    private int _evidenceFrameId;
    internal IEnumerable<object?> CaptureEvidenceFrame(string label)
    {
        R.environment["screenshots"] = _evidenceFrames;
        string name = "frame-" + (++_evidenceFrameId).ToString("D2");
        string path = Path.Combine(Path.GetDirectoryName(O.Output)!, name + ".png");
        if (File.Exists(path)) throw new IOException("Evidence frame must be new: " + path);
        yield return null;
        Exception? captureError = null;
        try { ScreenCapture.CaptureScreenshot(path); }
        catch (Exception error) { captureError = error; }
        if (captureError != null)
        {
            R.Skip("evidence." + name, "Game-frame capture unavailable: " + captureError.Message, false);
            yield break;
        }
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 15)
        {
            byte[]? png = null;
            try { if (File.Exists(path)) png = File.ReadAllBytes(path); }
            catch (IOException) { }
            if (png is { Length: > 32 } && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71 &&
                png[png.Length - 8] == 73 && png[png.Length - 7] == 69 && png[png.Length - 6] == 78 && png[png.Length - 5] == 68)
            {
                int width = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];
                int height = png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23];
                using var sha = SHA256.Create();
                var frame = new { label, phase = R.phase, path, width, height, bytes = png.Length,
                    sha256 = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant(),
                    capturedUtc = DateTime.UtcNow.ToString("O"), source = "Unity ScreenCapture; game framebuffer only" };
                _evidenceFrames.Add(frame);
                R.Check("evidence." + name, "Complete PNG from the actual isolated game", frame, width > 0 && height > 0);
                yield break;
            }
            yield return null;
        }
        R.Skip("evidence." + name, "No complete rendered PNG within 15 seconds", false);
    }
}
