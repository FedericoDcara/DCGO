using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class KeywordTooltip
{
    const float MaxWidth = 360f;
    const float PointerOffsetX = 18f;
    const float PointerOffsetY = 18f;
    const float Padding = 12f;

    static GameObject _root;
    static RectTransform _rootRect;
    static TextMeshProUGUI _label;
    static Canvas _canvas;
    static RectTransform _canvasRect;

    public static void Show(string reminder, Vector2 screenPosition, Canvas canvas, TMP_FontAsset font)
    {
        if (string.IsNullOrEmpty(reminder) || canvas == null)
        {
            Hide();
            return;
        }

        Ensure(canvas, font);
        _label.text = reminder;
        _label.font = font != null ? font : _label.font;
        _root.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_rootRect);
        Position(screenPosition);
    }

    public static void Hide()
    {
        if (_root != null)
        {
            _root.SetActive(false);
        }
    }

    static void Ensure(Canvas canvas, TMP_FontAsset font)
    {
        if (_root != null && _canvas == canvas)
        {
            return;
        }

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }

        _canvas = canvas;
        _canvasRect = canvas.transform as RectTransform;

        _root = new GameObject("KeywordTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
        _rootRect = _root.GetComponent<RectTransform>();
        _rootRect.SetParent(canvas.transform, false);
        _rootRect.pivot = new Vector2(0f, 0f);
        _rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        _rootRect.anchorMax = new Vector2(0.5f, 0.5f);

        Image background = _root.GetComponent<Image>();
        background.color = new Color(0.07f, 0.12f, 0.18f, 0.96f);
        background.raycastTarget = false;

        ContentSizeFitter fitter = _root.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup layout = _root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        GameObject textObject = new GameObject("Reminder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(_rootRect, false);

        LayoutElement layoutElement = textObject.GetComponent<LayoutElement>();
        layoutElement.preferredWidth = MaxWidth;

        _label = textObject.GetComponent<TextMeshProUGUI>();
        _label.fontSize = 16f;
        _label.color = Color.white;
        _label.alignment = TextAlignmentOptions.TopLeft;
        _label.enableWordWrapping = true;
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.raycastTarget = false;
        if (font != null)
        {
            _label.font = font;
        }

        _root.transform.SetAsLastSibling();
        _root.SetActive(false);
    }

    static void Position(Vector2 screenPosition)
    {
        if (_canvasRect == null)
        {
            return;
        }

        Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPosition, camera, out Vector2 localPoint);

        Vector2 size = _rootRect.sizeDelta;
        Vector2 canvasSize = _canvasRect.rect.size;
        float minX = -canvasSize.x * 0.5f + 8f;
        float maxX = canvasSize.x * 0.5f - size.x - 8f;
        float minY = -canvasSize.y * 0.5f + 8f;
        float maxY = canvasSize.y * 0.5f - size.y - 8f;

        float x = Mathf.Clamp(localPoint.x + PointerOffsetX, minX, maxX);
        float y = Mathf.Clamp(localPoint.y + PointerOffsetY, minY, maxY);
        _rootRect.anchoredPosition = new Vector2(x, y);
        _root.transform.SetAsLastSibling();
    }
}
