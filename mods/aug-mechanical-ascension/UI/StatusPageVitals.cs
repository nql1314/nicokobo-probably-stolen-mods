using TMPro;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Integration;
using Nicokobo.ModUtilities;
using UnityEngine;

namespace AugMechanicalAscension.UI;

// Receiver event information remains on the calendar Status tab; vitals use the HUD.
internal sealed class StatusPageVitals(NativeSessionBridge bridge, Action<string> log)
{
    private const string Marker = "NicokoboAugStatusVitals";
    private TextMeshProUGUI? _label;
    private GameObject? _statusSection;
    private IntPtr _panelPointer;
    private DateTime _nextFindUtc;
    private DateTime _nextErrorLogUtc;
    private DateTime _nextEffectRefreshUtc;
    private long _effectGeneration = -1;
    private OwnedCyberneticEffects _effects;
    private bool _textCurrent, _textReceiver;
    private string _textLocale = "";
    private int _textExposure;
    private string _textDaily = "";

    internal void Update()
    {
        try
        {
            var view = bridge.CurrentSession;
            if (view == null)
            {
                if (_label != null && _label.gameObject.activeSelf) _label.gameObject.SetActive(false);
                return;
            }
            if (_label != null && _statusSection != null && !_statusSection.activeInHierarchy)
            {
                if (_label.gameObject.activeSelf) _label.gameObject.SetActive(false);
                return;
            }
            var state = view.State;
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                store.runID != state.GameRunId ||
                store.saveSlotId != state.SaveSlotId)
            {
                if (_label != null && _label.gameObject.activeSelf) _label.gameObject.SetActive(false);
                return;
            }
            if (_label == null || NativeObject.Handle(_label) == IntPtr.Zero ||
                _label.gameObject == null ||
                NativeObject.Handle(_label.transform.parent?.gameObject) != _panelPointer)
            {
                _label = null;
                if (DateTime.UtcNow < _nextFindUtc) return;
                _nextFindUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.StatusPageFindRetrySeconds);
                TryAttach();
            }
            var label = _label;
            if (label == null) return;
            bool current = state.InitPhase == "complete" &&
                state.FailureReason == "none" &&
                StoreStation.GetDayCounter() == state.LastObservedDay;
            if (!current)
            {
                if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
                return;
            }
            bool visible = _statusSection != null &&
                NativeObject.Handle(_statusSection) != IntPtr.Zero &&
                _statusSection.activeInHierarchy;
            if (label.gameObject.activeSelf != visible)
                label.gameObject.SetActive(visible);
            if (!visible) return;
            if (_effectGeneration != view.Generation || DateTime.UtcNow >= _nextEffectRefreshUtc)
            {
                _effects = CyberneticItemOwnership.Capture(store);
                _effectGeneration = view.Generation;
                _nextEffectRefreshUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.StatusPageEffectRefreshIntervalSeconds);
            }
            string locale = ModText.LocaleCode;
            string daily = StatusPanelRules.DailyChangeLine(state, null, _effects, NicoWorkshop.HasMechanicalAscension(state));
            if (_textCurrent && _textExposure == state.Exposure && _textLocale == locale &&
                _textReceiver == _effects.CommunicationReceiver && _textDaily == daily) return;
            string text = daily + (_effects.CommunicationReceiver
                ? "\n" + StatusPanelRules.EventInformationLine(state, null) : "");
            if (label.text != text) label.text = text;
            _textExposure = state.Exposure; _textLocale = locale;
            _textReceiver = _effects.CommunicationReceiver; _textCurrent = true;
            _textDaily = daily;
        }
        catch (Exception ex)
        {
            _label = null;
            _nextFindUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.StatusPageFailureRetrySeconds);
            if (DateTime.UtcNow < _nextErrorLogUtc) return;
            _nextErrorLogUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.StatusPageErrorLogIntervalSeconds);
            log($"[ERROR] [P3] Calendar status vitals unavailable; {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TryAttach()
    {
        var calendar = AdvCalendarUIManager.Instance;
        var section = calendar?.statusSection;
        var panel = calendar?.UIPanel;
        var source = calendar?.storeAttractivenessTMP;
        var tab = calendar?.statusButton;
        if (section == null || NativeObject.Handle(section) == IntPtr.Zero ||
            panel == null || NativeObject.Handle(panel) == IntPtr.Zero ||
            source == null || NativeObject.Handle(source) == IntPtr.Zero ||
            tab == null || NativeObject.Handle(tab) == IntPtr.Zero) return;
        var parent = panel.transform;
        var found = parent.Find(Marker);
        GameObject? created = null;
        try
        {
            var target = found?.gameObject;
            if (target == null)
            {
                created = UnityEngine.Object.Instantiate(source.gameObject, parent);
                created.name = Marker;
                target = created;
            }
            var label = target.GetComponent<TextMeshProUGUI>()
                ?? throw new InvalidOperationException("Native status text template has no TMP label");
            var rect = target.GetComponent<RectTransform>()
                ?? throw new InvalidOperationException("Native status text template has no RectTransform");
            var panelRect = panel.GetComponent<RectTransform>()
                ?? throw new InvalidOperationException("Native calendar page has no RectTransform");
            var tabRect = tab.GetComponent<RectTransform>()
                ?? throw new InvalidOperationException("Native status tab has no RectTransform");
            var tabBottom = tabRect.TransformPoint(new Vector3(0f, tabRect.rect.yMin, 0f));
            var localBottom = panelRect.InverseTransformPoint(tabBottom);
            float topOffset = Mathf.Clamp(panelRect.rect.yMax - localBottom.y + 35f,
                145f, 160f);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(110f, -topOffset);
            rect.sizeDelta = new Vector2(600f, 150f);
            rect.localScale = Vector3.one;
            label.fontSize = 22f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.37f, 0.37f, 0.37f, label.color.a);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.enableAutoSizing = false;
            label.richText = true;
            label.raycastTarget = false;
            label.text = "";
            target.SetActive(true);
            _label = label;
            _textCurrent = false;
            _statusSection = section;
            _panelPointer = NativeObject.Handle(panel);
            log($"[P3] Calendar status vitals attached; topOffset={topOffset:F0}; " +
                $"panel={panelRect.rect.width:F0}x{panelRect.rect.height:F0}");
        }
        catch
        {
            if (created != null) UnityEngine.Object.Destroy(created);
            throw;
        }
    }
}
