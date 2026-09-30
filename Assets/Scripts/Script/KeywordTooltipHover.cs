using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TMP_Text))]
public class KeywordTooltipHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public CardInfo CardInfoClickTarget;

    TMP_Text _text;
    Canvas _canvas;
    Camera _eventCamera;
    bool _pointerInside;
    string _shownKey;

    void Awake()
    {
        Bind();
    }

    public void Bind()
    {
        _text = GetComponent<TMP_Text>();
        _text.raycastTarget = true;
        _canvas = GetComponentInParent<Canvas>();
        _eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _canvas.worldCamera
            : null;
    }

    void Update()
    {
        if (!_pointerInside || _text == null)
        {
            return;
        }

        UpdateHover(Input.mousePosition);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerInside = true;
        UpdateHover(eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
        _shownKey = null;
        KeywordTooltip.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (CardInfoClickTarget == null)
        {
            return;
        }

        if (FindLinkIndex(eventData.position) == -1)
        {
            CardInfoClickTarget.OnClick();
        }
    }

    void OnDisable()
    {
        _pointerInside = false;
        _shownKey = null;
        KeywordTooltip.Hide();
    }

    void UpdateHover(Vector2 screenPosition)
    {
        if (_text == null)
        {
            return;
        }

        int linkIndex = FindLinkIndex(screenPosition);
        if (linkIndex == -1)
        {
            if (_shownKey != null)
            {
                _shownKey = null;
                KeywordTooltip.Hide();
            }

            return;
        }

        string key = _text.textInfo.linkInfo[linkIndex].GetLinkID();
        if (!KeywordReminder.TryGetReminder(key, out string reminder))
        {
            _shownKey = null;
            KeywordTooltip.Hide();
            return;
        }

        if (_canvas == null)
        {
            Bind();
        }

        if (key != _shownKey)
        {
            _shownKey = key;
        }

        KeywordTooltip.Show(reminder, screenPosition, _canvas, _text.font);
    }

    int FindLinkIndex(Vector2 screenPosition)
    {
        return TMP_TextUtilities.FindIntersectingLink(_text, screenPosition, _eventCamera);
    }
}
