using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Dedicated overlay canvas for replay controls so clicks work above battle UI
/// and while Time.timeScale is 0. Collapsed by default to a bottom-right tab;
/// hover or click expands the full bar so the hand stays visible.
/// </summary>
public class ReplayControls : MonoBehaviour
{
    Canvas _overlayCanvas;
    GameObject _panel;
    GameObject _tab;
    Text _tabLabel;
    Text _statusText;
    Text _speedText;
    Text _turnText;
    Text _playPauseLabel;
    Slider _timeline;
    bool _suppressSlider;
    bool _expanded;
    bool _pinnedByClick;
    float _hoverHideAt = -1f;
    readonly List<int> _turns = new List<int>();

    void Start()
    {
        EnsureEventSystem();
        BuildUi();
        SetExpanded(false);
    }

    void Update()
    {
        var driver = ReplayDriver.Instance;
        if (driver == null)
        {
            return;
        }

        if (_playPauseLabel != null)
        {
            _playPauseLabel.text = driver.IsPaused ? "Play" : "Pause";
        }

        if (_speedText != null)
        {
            _speedText.text = $"{driver.Speed:0.##}x";
        }

        if (_turnText != null)
        {
            _turnText.text = driver.IsSeeking
                ? $"Seeking T{driver.CurrentTurn}..."
                : $"Turn {Mathf.Max(0, driver.CurrentTurn)}";
        }

        if (_statusText != null)
        {
            _statusText.text = $"Replay  {driver.EventIndex}/{driver.EventCount}";
        }

        if (_tabLabel != null)
        {
            string pauseMark = driver.IsPaused ? "||" : ">";
            _tabLabel.text = _expanded
                ? "Hide"
                : $"Replay  T{Mathf.Max(0, driver.CurrentTurn)}  {pauseMark}";
        }

        if (_timeline != null && !_suppressSlider && !driver.IsSeeking)
        {
            RefreshTurns(driver);
            if (_turns.Count > 0)
            {
                int idx = 0;
                for (int i = 0; i < _turns.Count; i++)
                {
                    if (_turns[i] <= driver.CurrentTurn)
                    {
                        idx = i;
                    }
                }

                if (!Mathf.Approximately(_timeline.value, idx))
                {
                    _suppressSlider = true;
                    _timeline.SetValueWithoutNotify(idx);
                    _suppressSlider = false;
                }
            }
        }
    }

    void LateUpdate()
    {
        if (_hoverHideAt > 0f && Time.unscaledTime >= _hoverHideAt && !_pinnedByClick)
        {
            _hoverHideAt = -1f;
            SetExpanded(false);
        }
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        if (_overlayCanvas != null)
        {
            Destroy(_overlayCanvas.gameObject);
        }
    }

    static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(go);
    }

    void RefreshTurns(ReplayDriver driver)
    {
        var list = driver.GetTurnList();
        if (list.Count == _turns.Count)
        {
            return;
        }

        _turns.Clear();
        _turns.AddRange(list);
        if (_timeline != null)
        {
            _timeline.minValue = 0;
            _timeline.maxValue = Mathf.Max(0, _turns.Count - 1);
            _timeline.wholeNumbers = true;
        }
    }

    void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        if (_panel != null)
        {
            _panel.SetActive(expanded);
        }

        if (!expanded)
        {
            _pinnedByClick = false;
            _hoverHideAt = -1f;
        }
    }

    void ToggleExpanded()
    {
        if (_expanded)
        {
            SetExpanded(false);
            return;
        }

        _pinnedByClick = true;
        _hoverHideAt = -1f;
        SetExpanded(true);
    }

    public void NotifyHoverEnter()
    {
        _hoverHideAt = -1f;
        if (!_expanded)
        {
            SetExpanded(true);
        }
    }

    public void NotifyHoverExit()
    {
        if (_pinnedByClick)
        {
            return;
        }

        _hoverHideAt = Time.unscaledTime + 0.35f;
    }

    void BuildUi()
    {
        Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        var canvasGo = new GameObject("ReplayControlsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _overlayCanvas = canvasGo.GetComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 5000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildTab(canvasGo.transform, font);
        BuildPanel(canvasGo.transform, font);
    }

    void BuildTab(Transform canvas, Font font)
    {
        _tab = new GameObject("ReplayTab", typeof(RectTransform), typeof(Image), typeof(Button));
        _tab.transform.SetParent(canvas, false);
        var rt = _tab.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-16f, 16f);
        rt.sizeDelta = new Vector2(220f, 44f);

        var image = _tab.GetComponent<Image>();
        image.color = new Color(0.12f, 0.18f, 0.32f, 0.92f);
        image.raycastTarget = true;

        var button = _tab.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = new Color(0.12f, 0.18f, 0.32f, 0.92f);
        colors.highlightedColor = new Color(0.22f, 0.35f, 0.55f, 1f);
        colors.pressedColor = new Color(0.1f, 0.14f, 0.28f, 1f);
        button.colors = colors;
        button.onClick.AddListener(ToggleExpanded);

        var hover = _tab.AddComponent<ReplayControlsHoverRelay>();
        hover.Bind(this);

        _tabLabel = CreateText(_tab.transform, "TabLabel", font, 18, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.zero, stretch: true);
        _tabLabel.text = "Replay";
        _tabLabel.fontStyle = FontStyle.Bold;
    }

    void BuildPanel(Transform canvas, Font font)
    {
        _panel = new GameObject("ReplayControlsPanel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(canvas, false);
        var rt = _panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        // Sit above the hand; tab remains in the corner when collapsed.
        rt.anchoredPosition = new Vector2(0f, 72f);
        rt.sizeDelta = new Vector2(960f, 120f);
        var bgImage = _panel.GetComponent<Image>();
        bgImage.color = new Color(0.05f, 0.07f, 0.12f, 0.94f);
        bgImage.raycastTarget = true;

        var panelHover = _panel.AddComponent<ReplayControlsHoverRelay>();
        panelHover.Bind(this);

        _statusText = CreateText(_panel.transform, "Status", font, 18, TextAnchor.MiddleLeft,
            new Vector2(20f, 70f), new Vector2(280f, 32f));
        _turnText = CreateText(_panel.transform, "Turn", font, 18, TextAnchor.MiddleCenter,
            new Vector2(340f, 70f), new Vector2(220f, 32f));
        _speedText = CreateText(_panel.transform, "SpeedLabel", font, 18, TextAnchor.MiddleLeft,
            new Vector2(280f, 18f), new Vector2(70f, 40f));

        var playBtn = CreateButton(_panel.transform, "PlayPause", font, "Pause",
            new Vector2(20f, 16f), new Vector2(130f, 44f), () =>
            {
                ReplayDriver.Instance?.TogglePause();
            });
        _playPauseLabel = playBtn.GetComponentInChildren<Text>();

        CreateButton(_panel.transform, "Speed", font, "Speed",
            new Vector2(160f, 16f), new Vector2(110f, 44f), () =>
            {
                ReplayDriver.Instance?.CycleSpeed();
            });

        CreateButton(_panel.transform, "Hide", font, "Hide",
            new Vector2(690f, 70f), new Vector2(110f, 40f), () => SetExpanded(false));

        CreateButton(_panel.transform, "Exit", font, "Exit",
            new Vector2(820f, 70f), new Vector2(120f, 40f), OnExit);

        BuildTimeline(_panel.transform, font);
    }

    void BuildTimeline(Transform parent, Font font)
    {
        var sliderGo = new GameObject("Timeline", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(parent, false);
        var srt = sliderGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(0f, 0f);
        srt.pivot = new Vector2(0f, 0f);
        srt.anchoredPosition = new Vector2(360f, 20f);
        srt.sizeDelta = new Vector2(440f, 32f);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(sliderGo.transform, false);
        Stretch(bg.GetComponent<RectTransform>());
        bg.GetComponent<Image>().color = new Color(0.2f, 0.22f, 0.28f, 1f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var faRt = fillArea.GetComponent<RectTransform>();
        Stretch(faRt);
        faRt.offsetMin = new Vector2(6f, 4f);
        faRt.offsetMax = new Vector2(-6f, -4f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Stretch(fill.GetComponent<RectTransform>());
        fill.GetComponent<Image>().color = new Color(0.3f, 0.55f, 0.95f, 1f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        Stretch(handleArea.GetComponent<RectTransform>());
        handleArea.GetComponent<RectTransform>().offsetMin = new Vector2(10f, 0f);
        handleArea.GetComponent<RectTransform>().offsetMax = new Vector2(-10f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(22f, 22f);
        handle.GetComponent<Image>().color = Color.white;

        _timeline = sliderGo.GetComponent<Slider>();
        _timeline.fillRect = fill.GetComponent<RectTransform>();
        _timeline.handleRect = hRt;
        _timeline.targetGraphic = handle.GetComponent<Image>();
        _timeline.direction = Slider.Direction.LeftToRight;
        _timeline.minValue = 0;
        _timeline.maxValue = 0;
        _timeline.wholeNumbers = true;
        _timeline.onValueChanged.AddListener(OnTimelineChanged);

        CreateText(parent, "TimelineHint", font, 14, TextAnchor.MiddleLeft,
            new Vector2(360f, 52f), new Vector2(440f, 24f)).text = "Timeline: forward = fast-forward, back = restart";
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void OnTimelineChanged(float value)
    {
        if (_suppressSlider || ReplayDriver.Instance == null || _turns.Count == 0)
        {
            return;
        }

        int idx = Mathf.Clamp(Mathf.RoundToInt(value), 0, _turns.Count - 1);
        int turn = _turns[idx];
        if (turn == ReplayDriver.Instance.CurrentTurn && !ReplayDriver.Instance.IsSeeking)
        {
            return;
        }

        ReplayDriver.Instance.SeekToTurn(turn);
    }

    void OnExit()
    {
        Time.timeScale = 1f;
        var cc = ContinuousController.instance;
        if (cc != null)
        {
            cc.CancelReplayReload();
            cc.ReturnToMatchHistoryAfterBattle = true;
        }

        if (ReplayDriver.Instance != null)
        {
            ReplayDriver.Instance.CancelSeekForExit();
        }

        if (GManager.instance != null)
        {
            GManager.instance.ReturnToTitle();
        }
    }

    static Text CreateText(Transform parent, string name, Font font, int size, TextAnchor anchor, Vector2 pos, Vector2 sizeDelta, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        if (stretch)
        {
            Stretch(rt);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
        }

        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    static Button CreateButton(Transform parent, string name, Font font, string label, Vector2 pos, Vector2 sizeDelta, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        go.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.7f, 1f);
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = new Color(0.2f, 0.35f, 0.7f, 1f);
        colors.highlightedColor = new Color(0.3f, 0.5f, 0.9f, 1f);
        colors.pressedColor = new Color(0.15f, 0.25f, 0.55f, 1f);
        colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var text = labelGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 22;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        text.raycastTarget = false;
        var lrt = text.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        return button;
    }
}

/// <summary>Forwards pointer enter/exit to ReplayControls for hover show/hide.</summary>
public class ReplayControlsHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    ReplayControls _owner;

    public void Bind(ReplayControls owner)
    {
        _owner = owner;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _owner?.NotifyHoverEnter();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _owner?.NotifyHoverExit();
    }
}
