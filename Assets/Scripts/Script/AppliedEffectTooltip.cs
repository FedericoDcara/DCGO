using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class AppliedEffectTooltip
{
    const float MaxWidth = 300f;
    const float Gap = 12f;
    const float Padding = 10f;

    static readonly Panel _applied = new Panel("AppliedEffectTooltip");
    static readonly Panel _digivolution = new Panel("DigivolutionEffectTooltip");

    public static void Show(string text, Vector2 leftScreen, Vector2 rightScreen, Canvas canvas, TMP_FontAsset font, object owner)
    {
        _applied.Show(text, leftScreen, rightScreen, canvas, font, owner, preferLeft: false);
    }

    public static void ShowDigivolution(string text, Vector2 leftScreen, Vector2 rightScreen, Canvas canvas, TMP_FontAsset font, object owner)
    {
        _digivolution.Show(text, leftScreen, rightScreen, canvas, font, owner, preferLeft: true);
    }

    public static void HideApplied(object owner)
    {
        _applied.Hide(owner);
    }

    public static void HideDigivolution(object owner)
    {
        _digivolution.Hide(owner);
    }

    public static void Hide(object owner)
    {
        _applied.Hide(owner);
        _digivolution.Hide(owner);
    }

    public static void KeepApart()
    {
        if (!_applied.IsShown || !_digivolution.IsShown)
        {
            return;
        }

        Rect applied = _applied.Placed;
        Rect sources = _digivolution.Placed;
        if (!applied.Overlaps(sources))
        {
            return;
        }

        float minY = _digivolution.MinY;
        float maxY = _digivolution.MaxY(sources.height);
        float below = applied.y - Gap - sources.height;
        float above = applied.y + applied.height + Gap;
        float y = below >= minY ? below : above;
        y = Mathf.Clamp(y, minY, Mathf.Max(minY, maxY));
        _digivolution.MoveTo(sources.x, y);
    }

    sealed class Panel
    {
        readonly string _name;

        GameObject _root;
        RectTransform _rootRect;
        TextMeshProUGUI _label;
        Canvas _canvas;
        RectTransform _canvasRect;
        object _owner;
        Rect _placed;

        public Panel(string name)
        {
            _name = name;
        }

        public bool IsShown => _root != null && _root.activeSelf;

        public Rect Placed => _placed;

        public float MinY
        {
            get
            {
                if (_canvasRect == null)
                {
                    return 0f;
                }

                return -_canvasRect.rect.size.y * 0.5f + 8f;
            }
        }

        public float MaxY(float height)
        {
            if (_canvasRect == null)
            {
                return 0f;
            }

            return _canvasRect.rect.size.y * 0.5f - height - 8f;
        }

        public void Show(string text, Vector2 leftScreen, Vector2 rightScreen, Canvas canvas, TMP_FontAsset font, object owner, bool preferLeft)
        {
            if (string.IsNullOrWhiteSpace(text) || canvas == null)
            {
                Hide(owner);
                return;
            }

            _owner = owner;
            Ensure(canvas, font);
            _label.text = text.Trim();
            _label.font = font != null ? font : _label.font;
            _root.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rootRect);
            Position(leftScreen, rightScreen, preferLeft);
        }

        public void Hide(object owner)
        {
            if (_owner != null && !ReferenceEquals(_owner, owner))
            {
                return;
            }

            _owner = null;
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        public void MoveTo(float x, float y)
        {
            if (_rootRect == null)
            {
                return;
            }

            _rootRect.anchoredPosition = new Vector2(x, y);
            _placed = new Rect(x, y, _rootRect.sizeDelta.x, _rootRect.sizeDelta.y);
            _root.transform.SetAsLastSibling();
        }

        void Ensure(Canvas canvas, TMP_FontAsset font)
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

            _root = new GameObject(_name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
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

            GameObject textObject = new GameObject("Effects", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.GetComponent<RectTransform>().SetParent(_rootRect, false);

            LayoutElement layoutElement = textObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = MaxWidth;

            _label = textObject.GetComponent<TextMeshProUGUI>();
            _label.fontSize = 15f;
            _label.color = Color.white;
            _label.alignment = TextAlignmentOptions.TopLeft;
            _label.enableWordWrapping = true;
            _label.richText = false;
            _label.overflowMode = TextOverflowModes.Overflow;
            _label.raycastTarget = false;
            if (font != null)
            {
                _label.font = font;
            }

            _root.transform.SetAsLastSibling();
            _root.SetActive(false);
        }

        void Position(Vector2 leftScreen, Vector2 rightScreen, bool preferLeft)
        {
            if (_canvasRect == null)
            {
                return;
            }

            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, rightScreen, camera, out Vector2 rightLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, leftScreen, camera, out Vector2 leftLocal);

            Vector2 size = _rootRect.sizeDelta;
            Vector2 canvasSize = _canvasRect.rect.size;
            float minX = -canvasSize.x * 0.5f + 8f;
            float maxX = canvasSize.x * 0.5f - size.x - 8f;
            float minY = -canvasSize.y * 0.5f + 8f;
            float maxY = canvasSize.y * 0.5f - size.y - 8f;

            float preferred = preferLeft ? leftLocal.x - size.x - Gap : rightLocal.x + Gap;
            float alternate = preferLeft ? rightLocal.x + Gap : leftLocal.x - size.x - Gap;
            bool preferredFits = preferLeft ? preferred >= minX : preferred <= maxX;
            float x = preferredFits ? preferred : alternate;
            float y = rightLocal.y - size.y * 0.5f;
            x = Mathf.Clamp(x, minX, Mathf.Max(minX, maxX));
            y = Mathf.Clamp(y, minY, Mathf.Max(minY, maxY));
            MoveTo(x, y);
        }
    }
}
