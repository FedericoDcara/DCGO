using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class YesNoObject : MonoBehaviour
{
    private static readonly int CloseHash = Animator.StringToHash("Close");
    private static readonly int OpenHash = Animator.StringToHash("Open");

    // Layout sized for 3 tall buttons; shrink when we show 4 (Ranked).
    private const float CompactCellHeight = 128f;
    private const float CompactSpacingY = 14f;
    private const float CompactButtonsAnchoredY = -70f;

    // Battle-mode list (5+): square keys in a calculator pad (3 across).
    private const int CalculatorColumns = 3;
    private const float CalculatorSpacing = 20f;
    private const float CalculatorSideMargin = 36f;
    private const float CalculatorBottomMargin = 28f;
    private const float CalculatorHeaderGap = 14f;
    private const float CalculatorMaxCell = 320f;

    public Text InfoText;

    public List<CommandButton> Buttons;

    public Animator anim;

    public GameObject CloseButton;

    //public Vector3 defaultPos = Vector3.zero;

    public bool CloseOnButtonClicked = true;

    Vector2? _defaultCellSize;
    Vector2? _defaultSpacing;
    float? _defaultButtonsAnchoredY;
    int? _defaultButtonFontSize;
    bool _barChromeCached;
    Vector2 _barIconPos;
    Vector2 _barLabelAnchorMin;
    Vector2 _barLabelAnchorMax;
    Vector2 _barLabelPos;
    Vector2 _barLabelSize;
    TextAnchor _barLabelAlign;
    HorizontalWrapMode _barLabelHOverflow;
    VerticalWrapMode _barLabelVOverflow;
    GridLayoutGroup.Constraint _barConstraint;
    int _barConstraintCount;

    public void SetUpYesNoObject(List<UnityAction> OnClickActions, List<string> CommandTexts, string _InfoText, bool CanClose)
    {
        //this.transform.localPosition = defaultPos;

        EnsureButtonCapacity(OnClickActions.Count);

        for (int i = 0; i < Buttons.Count; i++)
        {
            Buttons[i].OnClickAction = null;

            if (i < OnClickActions.Count)
            {
                Buttons[i].gameObject.SetActive(true);

                Buttons[i].transform.GetChild(0).GetComponent<Text>().text = CommandTexts[i];

                int k = i;

                Buttons[i].OnClickAction = () => 
                {
                    if (!Buttons[k].GetComponent<Button>().interactable)
                        return;

                    OnClickActions[k]?.Invoke();
                    
                    if(this.CloseOnButtonClicked)
                    {
                        this.Close_(false);
                    }
                };
            }

            else
            {
                Buttons[i].gameObject.SetActive(false);
            }
        }

        InfoText.text = _InfoText;

        this.gameObject.SetActive(true);

        FitButtonsLayout(OnClickActions.Count);

        CloseButton.SetActive(CanClose);

        Open();
    }

    /// <summary>
    /// Authored battle-mode UI has Random / Room / Bot. Ranked must be inserted after Random
    /// so Room and Bot keep their distinct sprites (appending a Bot clone made Room look like Bot).
    /// </summary>
    void EnsureButtonCapacity(int needed)
    {
        if (Buttons.Count == 0 || Buttons.Count >= needed)
            return;

        while (Buttons.Count < needed)
        {
            const int insertAt = 1;
            var template = Buttons[0];
            // Use Room (or next authored) chrome so the extra slot isn't another yellow Random button.
            var chromeSource = Buttons.Count > 1 ? Buttons[1] : template;

            var clone = Instantiate(template, template.transform.parent);
            clone.name = template.name + "_Extra" + Buttons.Count;
            clone.transform.SetSiblingIndex(insertAt);
            Buttons.Insert(insertAt, clone);

            CopyButtonChrome(clone, chromeSource);
        }
    }

    static void CopyButtonChrome(CommandButton target, CommandButton source)
    {
        if (target == null || source == null || target == source)
            return;

        var targetImage = target.GetComponent<Image>();
        var sourceImage = source.GetComponent<Image>();
        if (targetImage != null && sourceImage != null)
            targetImage.sprite = sourceImage.sprite;

        var targetButton = target.GetComponent<Button>();
        var sourceButton = source.GetComponent<Button>();
        if (targetButton == null || sourceButton == null)
            return;

        var spriteState = targetButton.spriteState;
        spriteState.highlightedSprite = sourceButton.spriteState.highlightedSprite;
        spriteState.pressedSprite = sourceButton.spriteState.pressedSprite;
        spriteState.selectedSprite = sourceButton.spriteState.selectedSprite;
        spriteState.disabledSprite = sourceButton.spriteState.disabledSprite;
        targetButton.spriteState = spriteState;
    }

    void FitButtonsLayout(int activeCount)
    {
        if (Buttons.Count == 0)
            return;

        var buttonsParent = Buttons[0].transform.parent as RectTransform;
        if (buttonsParent == null)
            return;

        var grid = buttonsParent.GetComponent<GridLayoutGroup>();
        if (grid == null)
            return;

        if (!_defaultCellSize.HasValue)
        {
            _defaultCellSize = grid.cellSize;
            _defaultSpacing = grid.spacing;
            _defaultButtonsAnchoredY = buttonsParent.anchoredPosition.y;
        }

        if (activeCount >= 5 && TryApplyCalculator(buttonsParent, grid, activeCount))
            return;

        RestoreButtonFontSize();
        RestoreBarChrome(grid);

        if (activeCount >= 4)
        {
            grid.cellSize = new Vector2(_defaultCellSize.Value.x, CompactCellHeight);
            grid.spacing = new Vector2(_defaultSpacing.Value.x, CompactSpacingY);
            buttonsParent.anchoredPosition = new Vector2(
                buttonsParent.anchoredPosition.x,
                CompactButtonsAnchoredY);
            SetButtonIconSize(72f);
        }
        else
        {
            grid.cellSize = _defaultCellSize.Value;
            grid.spacing = _defaultSpacing.Value;
            if (_defaultButtonsAnchoredY.HasValue)
            {
                buttonsParent.anchoredPosition = new Vector2(
                    buttonsParent.anchoredPosition.x,
                    _defaultButtonsAnchoredY.Value);
            }
            SetButtonIconSize(100f);
        }

        FitBarLabels(grid);
    }

    /// <summary>
    /// Square keys in rows of three under the caption. A short last row leaves empty cells.
    /// </summary>
    bool TryApplyCalculator(RectTransform buttonsParent, GridLayoutGroup grid, int activeCount)
    {
        float top = FindStackTop(buttonsParent);
        float bottom = FindStackBottom(buttonsParent);
        float availableH = top - bottom;
        if (availableH < 200f)
            return false;

        var window = buttonsParent.parent as RectTransform;
        float windowW = 900f;
        if (window != null)
        {
            windowW = window.rect.width;
            if (windowW < 1f)
                windowW = window.sizeDelta.x;
        }

        int rows = Mathf.CeilToInt(activeCount / (float)CalculatorColumns);
        float availableW = windowW - CalculatorSideMargin * 2f;
        float cellFromW = (availableW - (CalculatorColumns - 1) * CalculatorSpacing) / CalculatorColumns;
        float cellFromH = (availableH - (rows - 1) * CalculatorSpacing) / rows;
        float cell = Mathf.Min(cellFromW, cellFromH);
        cell = Mathf.Clamp(cell, 96f, CalculatorMaxCell);

        float stack = rows * cell + (rows - 1) * CalculatorSpacing;
        if (stack > availableH)
        {
            cell = (availableH - (rows - 1) * CalculatorSpacing) / rows;
            cell = Mathf.Max(96f, cell);
            stack = rows * cell + (rows - 1) * CalculatorSpacing;
        }

        CacheBarChrome(grid);

        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = CalculatorColumns;
        grid.cellSize = new Vector2(cell, cell);
        grid.spacing = new Vector2(CalculatorSpacing, CalculatorSpacing);

        // Grid is middle-aligned on the buttons parent, so shift the parent to keep the top fixed.
        float parentY = top - stack * 0.5f;
        buttonsParent.anchoredPosition = new Vector2(buttonsParent.anchoredPosition.x, parentY);

        ApplySquareContents(cell);
        return true;
    }

    static float FindStackTop(RectTransform buttonsParent)
    {
        var window = buttonsParent.parent as RectTransform;
        if (window == null)
            return buttonsParent.anchoredPosition.y + 40f;

        for (int i = 0; i < window.childCount; i++)
        {
            var child = window.GetChild(i) as RectTransform;
            if (child == null || child.name != "InfoText")
                continue;

            // The info box is taller than the two-line caption. Start under the glyphs.
            const float captionBlock = 118f;
            return child.anchoredPosition.y - captionBlock * 0.5f - CalculatorHeaderGap;
        }

        return buttonsParent.anchoredPosition.y + 40f;
    }

    static float FindStackBottom(RectTransform buttonsParent)
    {
        var window = buttonsParent.parent as RectTransform;
        if (window == null)
            return -360f;

        float height = window.rect.height;
        if (height < 1f)
            height = window.sizeDelta.y;
        return -height * 0.5f + CalculatorBottomMargin;
    }

    void CacheBarChrome(GridLayoutGroup grid)
    {
        if (_barChromeCached)
            return;

        _barConstraint = grid.constraint;
        _barConstraintCount = grid.constraintCount;

        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i] == null)
                continue;

            var label = Buttons[i].GetComponentInChildren<Text>(true);
            if (label != null)
            {
                var rt = label.rectTransform;
                _barLabelAnchorMin = rt.anchorMin;
                _barLabelAnchorMax = rt.anchorMax;
                _barLabelPos = rt.anchoredPosition;
                _barLabelSize = rt.sizeDelta;
                _barLabelAlign = label.alignment;
                _barLabelHOverflow = label.horizontalOverflow;
                _barLabelVOverflow = label.verticalOverflow;
            }

            var t = Buttons[i].transform;
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if (child.name.IndexOf("icon", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var icon = child as RectTransform;
                if (icon != null)
                    _barIconPos = icon.anchoredPosition;
                break;
            }

            _barChromeCached = true;
            return;
        }
    }

    void ApplySquareContents(float cell)
    {
        float iconSize = Mathf.Clamp(cell * 0.34f, 72f, 120f);
        float labelH = Mathf.Clamp(cell * 0.28f, 56f, 96f);
        float iconY = cell * 0.14f;
        int font = Mathf.RoundToInt(Mathf.Clamp(cell * 0.13f, 28f, 42f));

        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i] == null)
                continue;

            var t = Buttons[i].transform;
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if (child.name.IndexOf("icon", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var icon = child as RectTransform;
                if (icon == null)
                    continue;

                icon.anchorMin = new Vector2(0.5f, 0.5f);
                icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.pivot = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = new Vector2(0f, iconY);
                icon.sizeDelta = new Vector2(iconSize, iconSize);
            }

            var label = Buttons[i].GetComponentInChildren<Text>(true);
            if (label == null)
                continue;

            var labelRt = label.rectTransform;
            labelRt.anchorMin = new Vector2(0.06f, 0f);
            labelRt.anchorMax = new Vector2(0.94f, 0f);
            labelRt.pivot = new Vector2(0.5f, 0f);
            labelRt.anchoredPosition = new Vector2(0f, 12f);
            labelRt.sizeDelta = new Vector2(0f, labelH);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        SetButtonFontSize(font);
    }

    void RestoreBarChrome(GridLayoutGroup grid)
    {
        if (!_barChromeCached)
            return;

        grid.constraint = _barConstraint;
        grid.constraintCount = _barConstraintCount;

        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i] == null)
                continue;

            var label = Buttons[i].GetComponentInChildren<Text>(true);
            if (label != null)
            {
                var rt = label.rectTransform;
                rt.anchorMin = _barLabelAnchorMin;
                rt.anchorMax = _barLabelAnchorMax;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = _barLabelPos;
                rt.sizeDelta = _barLabelSize;
                label.alignment = _barLabelAlign;
                label.horizontalOverflow = _barLabelHOverflow;
                label.verticalOverflow = _barLabelVOverflow;
            }

            var t = Buttons[i].transform;
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if (child.name.IndexOf("icon", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var icon = child as RectTransform;
                if (icon == null)
                    continue;

                icon.anchorMin = new Vector2(0.5f, 0.5f);
                icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = _barIconPos;
            }
        }
    }

    void SetButtonIconSize(float size)
    {
        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i] == null)
                continue;

            var t = Buttons[i].transform;
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if (child.name.IndexOf("icon", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var rt = child as RectTransform;
                if (rt != null)
                    rt.sizeDelta = new Vector2(size, size);
            }
        }
    }

    void SetButtonFontSize(int size)
    {
        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i] == null)
                continue;

            var label = Buttons[i].GetComponentInChildren<Text>(true);
            if (label == null)
                continue;

            if (!_defaultButtonFontSize.HasValue)
                _defaultButtonFontSize = label.fontSize;

            var localize = label.GetComponent<LocalizeTMPro>();
            if (localize != null)
                localize.ApplyFontSize(size);
            else
                label.fontSize = size;
        }
    }

    void RestoreButtonFontSize()
    {
        if (_defaultButtonFontSize.HasValue)
            SetButtonFontSize(_defaultButtonFontSize.Value);
    }

    /// <summary>
    /// Side-by-side bars were authored for short labels. Shrink the shared font
    /// until the longest label fits on one line inside the cell.
    /// </summary>
    void FitBarLabels(GridLayoutGroup grid)
    {
        const float sideInset = 56f;
        float usable = grid.cellSize.x - sideInset;
        if (usable < 80f)
            return;

        int fitted = int.MaxValue;
        bool any = false;
        bool overflowed = false;

        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i] == null || !Buttons[i].gameObject.activeSelf)
                continue;

            var label = Buttons[i].GetComponentInChildren<Text>(true);
            if (label == null || string.IsNullOrEmpty(label.text))
                continue;

            // Square keys already wrap inside their own rect.
            if (label.rectTransform.anchorMax.y < 0.5f)
                return;

            if (!_defaultButtonFontSize.HasValue)
                _defaultButtonFontSize = label.fontSize;

            int authored = _defaultButtonFontSize.Value;
            float authoredWidth = MeasureLabelWidth(label, authored);
            if (authoredWidth <= usable && authoredWidth > 0.5f)
            {
                if (authored < fitted)
                    fitted = authored;
                any = true;
                continue;
            }

            overflowed = true;
            int size = authored;
            while (size > 26)
            {
                float width = MeasureLabelWidth(label, size);
                if (width > 0.5f && width <= usable)
                    break;
                size--;
            }

            if (size < fitted)
                fitted = size;
            any = true;
        }

        if (!any || fitted == int.MaxValue || !overflowed)
            return;

        SetButtonFontSize(fitted);

        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i] == null)
                continue;

            var label = Buttons[i].GetComponentInChildren<Text>(true);
            if (label == null)
                continue;

            var rt = label.rectTransform;
            if (rt.anchorMax.x - rt.anchorMin.x > 0.9f)
                rt.sizeDelta = new Vector2(-sideInset, rt.sizeDelta.y);

            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.alignment = TextAnchor.MiddleCenter;
        }
    }

    static float MeasureLabelWidth(Text label, int size)
    {
        int previousSize = label.fontSize;
        var previousOverflow = label.horizontalOverflow;
        label.fontSize = size;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        float width = label.preferredWidth;
        label.fontSize = previousSize;
        label.horizontalOverflow = previousOverflow;
        return width;
    }

    public void Off()
    {
        this.gameObject.SetActive(false);
        Close_(false);
    }

    public void Open()
    {
        this.gameObject.SetActive(true);
        anim.SafeSetInt(OpenHash, 1);
        anim.SafeSetInt(CloseHash, 0);
    }

    public void Close()
    {
        Close_(true);
    }

    public void Close_(bool playSE)
    {
        if (playSE)
        {
            Opening.instance.PlayCancelSE();
        }

        anim.SafeSetInt(OpenHash, 0);
        anim.SafeSetInt(CloseHash, 1);
    }
}
