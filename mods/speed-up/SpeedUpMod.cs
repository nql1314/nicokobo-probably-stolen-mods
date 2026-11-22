using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpeedUp
{
    public sealed class SpeedUpMod : IMod
    {
        public const string Id = "nicokobo.speed-up";
        internal static ModLog Log { get; private set; }
        GameObject host;

        public void Init(ModManifest manifest)
        {
            Log = new ModLog(manifest);
            SpeedUpSettings.Initialize(manifest.ID);
        }

        public void OnEnable()
        {
            try
            {
                host = new GameObject("Nicokobo.SpeedUp");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<SpeedUpRuntime>();
            }
            catch
            {
                OnDisable();
                throw;
            }
        }

        public void OnDisable()
        {
            if (host == null) return;
            host.SetActive(false);
            UnityEngine.Object.Destroy(host);
            host = null;
        }
    }

    // IMod has no frame callbacks; this component owns the runtime and its cleanup.
    [DefaultExecutionOrder(10000)]
    public sealed class SpeedUpRuntime : MonoBehaviour
    {
        internal static bool InGame => EmporiumEntry.Instance != null && PlayerStore.HasInstance
            && !string.IsNullOrEmpty(PlayerStore.Instance.runID);

        void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;

        void Update()
        {
            if (InGame) HotkeyHandler.Update();
            GlobalSpeed.Update();
        }

        void OnGUI()
        {
            if (InGame) SpeedNotice.OnGUI();
        }

        void OnSceneChanged(Scene previous, Scene next) => Release();

        void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Release();
        }

        static void Release()
        {
            GlobalSpeed.Release();
            ClientAnimSpeed.Release();
            SpeedNotice.Clear();
        }
    }
}
