using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.NativeTest
{
    internal static class NativeProbeLaunch
    {
        internal static bool WindowReady { get; private set; }
        private static DisplayInfo _testDisplay;

        internal static IEnumerable<object> PrepareWindow(Action<object> record, bool allowPrimaryDisplay = false)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (EmporiumOption.Instance == null)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Display options not ready");
                yield return null!;
            }
            var displays = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displays);
            if (displays.Count == 0) throw new InvalidOperationException("No active display is available for the native regression window");
            int displayIndex = allowPrimaryDisplay || displays.Count == 1 ? 0 : 1;
            _testDisplay = displays[displayIndex];
            // The game reapplies its saved display option after CLI arguments.
            // Use its real option API in the disposable product only.
            EmporiumOption.Instance.fullscreenMode = 2;
            EmporiumOption.Instance.resolution = 3; // Native 1280 x 720 option.
            EmporiumOption.Instance.fpsCapWhileMinimized = false;
            EmporiumOption.Instance.ApplySetting();
            while (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width != 1280 || Screen.height != 720)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Windowed 1280x720 was not applied");
                yield return null!;
            }
            var moving = Screen.MoveMainWindowTo(in _testDisplay, new Vector2Int(64, 64));
            while (!moving.isDone)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Test-display move did not complete");
                yield return null!;
            }
            WindowReady = true;
            AssertWindow();
            string policy = displayIndex == 1 ? "windowed secondary display" :
                allowPrimaryDisplay ? "user-approved primary display" : "windowed primary display fallback";
            record(new { policy, mode = Screen.fullScreenMode.ToString(),
                Screen.width, Screen.height, selectedDisplay = displayIndex, display = _testDisplay.name,
                x = Screen.mainWindowPosition.x, y = Screen.mainWindowPosition.y,
                layout = displays.Select((display, index) => new { index, display.name, display.width, display.height }).ToArray() });
        }

        internal static void AssertWindow()
        {
            if (!WindowReady) return;
            if (Screen.fullScreenMode != FullScreenMode.Windowed || !Screen.mainWindowDisplayInfo.Equals(_testDisplay))
                throw new InvalidOperationException("Native regression left its selected windowed display");
        }

        // Scanner v2 forbids Process APIs inside Mods. The external supervisor
        // owns the process handle and publishes launch.json before Unity loads
        // Mods. This PID is supervisor evidence, not in-Mod OS introspection.
        internal static int ReadProcessId(string output, string runId, string mode, string product)
        {
            string result = Path.GetFullPath(output);
            string runDirectory = Path.GetDirectoryName(result);
            string root = Path.GetDirectoryName(runDirectory);
            JObject launch = JObject.Parse(File.ReadAllText(Path.Combine(runDirectory, "launch.json")));
            string environmentId = launch.Value<string>("environmentRunId") ?? "";
            int? pid = (int?)launch["processId"];
            if (!Guid.TryParseExact(environmentId, "D", out var environment) ||
                new DirectoryInfo(root).Name != "native-" + environment.ToString("N") ||
                product != "NicoT_" + environment.ToString("N").Substring(0, 9) ||
                launch.Value<string>("runId") != runId || launch.Value<string>("mode") != mode || pid is not > 0 ||
                !string.Equals(Path.GetFullPath(launch.Value<string>("result") ?? ""), result, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFullPath(launch.Value<string>("executable") ?? ""), Path.Combine(root, "game", "Probably Stolen.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Native probe output must bind to this supervisor's unique isolated launch");
            return pid.Value;
        }
    }
}
