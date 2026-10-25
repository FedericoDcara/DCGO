#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Read-only viewer for a public pile (trash or lost zone, yours or the
// opponent's): the real cards fly out of the pile into a grid in front of you,
// newest first, and fly back when it closes. Point at / touch a card for the
// big preview. Opened by pointing the laser at the pile + Trigger, or with the
// TRASH button on the wrist disk; closed with CLOSE or the left stick click.
public class PileViewer
{
    const float CardScale = 0.72f;
    const float GapX = 0.01f;
    const float GapY = 0.014f;

    readonly Board3D _board;
    readonly Transform _frame;
    readonly Transform _accent;
    readonly VRLabel _title;
    readonly VRButton3D _close;

    Vector3 _position;     // board space
    Quaternion _rotation;  // board space
    float _openTime;
    float _scale = 1f;     // undoes the board's scale (hologram) so the viewer keeps its real size

    public bool IsOpen { get; private set; }
    public Player Owner { get; private set; }
    public BoardZone Zone { get; private set; }

    /// <summary>Set when the viewer shows a field stack (all cards of one Digimon / Tamer).</summary>
    public Permanent Stack { get; private set; }

    public bool IsShowingStack(Permanent permanent) => IsOpen && Stack != null && Stack == permanent;

    public PileViewer(Board3D board, Func<Color, Material> material)
    {
        _board = board;

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "PileViewer";
        frame.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(board.transform, false);
        frame.GetComponent<Renderer>().sharedMaterial = material(new Color(0.05f, 0.09f, 0.14f));
        _frame = frame.transform;

        GameObject accent = GameObject.CreatePrimitive(PrimitiveType.Cube);
        accent.name = "PileViewerAccent";
        accent.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(accent.GetComponent<Collider>());
        accent.transform.SetParent(board.transform, false);
        accent.GetComponent<Renderer>().sharedMaterial = material(new Color(0.25f, 0.85f, 1f));
        _accent = accent.transform;

        _title = VRLabel.Create(board.transform, Vector3.zero, Quaternion.identity, new Vector2(0.4f, 0.035f), "", new Color(0.85f, 0.95f, 1f), new Color(0.05f, 0.09f, 0.14f));
        _title.alignment = TMPro.TextAlignmentOptions.Left;
        _close = VRButton3D.CreatePlate(board.transform, Vector3.zero, new Vector2(0.1f, 0.032f), "CLOSE", Close);

        SetVisible(false);
    }

    void SetVisible(bool visible)
    {
        if (!visible)
        {
            HideStackDetail();
        }

        _frame.gameObject.SetActive(visible);
        _accent.gameObject.SetActive(visible);
        _title.gameObject.SetActive(visible);
        _close.gameObject.SetActive(visible);
    }

    public bool IsShowing(Player owner, BoardZone zone) => IsOpen && Stack == null && Owner == owner && Zone == zone;

    public void OpenStack(Permanent permanent, Transform head)
    {
        Open(permanent.TopCard != null ? permanent.TopCard.Owner : null, BoardZone.Field, head);
        Stack = permanent;
    }

    public void Open(Player owner, BoardZone zone, Transform head)
    {
        if (head == null)
        {
            return;
        }

        Vector3 forward = head.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 world = head.position + forward * 0.55f + Vector3.down * 0.06f;
        Quaternion worldRotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(10f, 0f, 0f);

        Transform board = _board.transform;
        _position = board.InverseTransformPoint(world);
        _rotation = Quaternion.Inverse(board.rotation) * worldRotation;
        _scale = 1f / Mathf.Max(0.01f, board.lossyScale.x);
        _openTime = Time.unscaledTime;
        Owner = owner;
        Zone = zone;
        Stack = null;
        IsOpen = true;
        SetVisible(true);
    }

    public void Close()
    {
        IsOpen = false;
        Owner = null;
        Stack = null;
        SetVisible(false);
    }

    /// <summary>Places the pile's cards in the grid (newest first). Call every frame while open.</summary>
    public void Layout(List<CardSource> cards, Func<CardSource, Card3D> get, string zoneName)
    {
        int n = cards.Count;
        int cols = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, n) * 1.8f)), 1, 10);
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)cols));
        float cellW = (Card3D.Width * CardScale + GapX) * _scale;
        float cellH = (Card3D.Height * CardScale + GapY) * _scale;
        float unfold = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - _openTime) / 0.18f);

        float frameW = Mathf.Max(cols * cellW + 0.03f * _scale, 0.5f * _scale);
        float frameH = rows * cellH + 0.07f * _scale;
        _frame.localPosition = _position + _rotation * new Vector3(0f, 0.018f * _scale, 0.006f * _scale);
        _frame.localRotation = _rotation;
        _frame.localScale = new Vector3(frameW, frameH * unfold, 0.004f * _scale);

        float topEdge = 0.018f * _scale + frameH * 0.5f * unfold;
        _accent.localPosition = _position + _rotation * new Vector3(0f, topEdge, 0.004f * _scale);
        _accent.localRotation = _rotation;
        _accent.localScale = new Vector3(frameW, 0.005f * _scale, 0.006f * _scale);

        _title.transform.localPosition = _position + _rotation * new Vector3(-frameW * 0.5f + 0.22f * _scale, topEdge - 0.022f * _scale, -0.002f * _scale);
        _title.transform.localRotation = _rotation;
        _title.transform.localScale = new Vector3(0.4f * _scale, 0.035f * _scale, 1f);
        _title.text = $"{(Owner != null && Owner.isYou ? "YOUR" : "OPPONENT")} {zoneName}  {n}";

        _close.transform.localPosition = _position + _rotation * new Vector3(frameW * 0.5f - 0.065f * _scale, topEdge - 0.022f * _scale, -0.004f * _scale);
        _close.transform.localRotation = _rotation;
        _close.transform.localScale = Vector3.one * _scale;

        Quaternion faceUser = _rotation * Quaternion.Euler(-90f, 0f, 0f);
        Vector3 hover = _rotation * new Vector3(0f, 0f, -0.03f * _scale);

        for (int i = 0; i < n; i++)
        {
            // Newest (last added) first.
            CardSource card = cards[n - 1 - i];

            if (card == null)
            {
                continue;
            }

            int row = i / cols;
            int inRow = row == rows - 1 ? n - row * cols : cols;
            float x = (i % cols - (inRow - 1) * 0.5f) * cellW;
            float y = ((rows - 1) * 0.5f - row) * cellH * unfold - 0.012f * _scale;
            Vector3 position = _position + _rotation * new Vector3(x, y, 0f);
            get(card).SetTarget(position, faceUser, true, card.IsDigiEgg, fastFollow: false, scale: CardScale * _scale, hoverOffset: hover);
        }
    }

    // ---- Stack detail (like the flat PermanentDetail panel) ----
    // Left: every card under the top card with its inherited / link effect.
    // Right: what currently applies to the Digimon (security attack, DP and
    // other effects, debuffs), its name and the big top card.

    const float StackW = 0.80f;
    const float ListW = 0.40f;
    const float RowH = 0.072f;
    const float RowGap = 0.008f;
    const float ThumbScale = 0.48f;
    const float BigScale = 1.35f;
    const float StatusW = 0.34f;
    const float StatusH = 0.15f;
    const float TextRefresh = 0.5f;
    const int MaxRows = 5;

    float _scroll;          // first visible row (fractional while scrolling)
    float _scrollTarget;
    VRButton3D _up;
    VRButton3D _down;

    /// <summary>Scrolls the stack list by this many rows (positive = further down the stack).</summary>
    public void ScrollStack(float rows)
    {
        _scrollTarget += rows;
    }

    public bool StackScrollable => IsOpen && Stack != null && _under.Count > MaxRows;

    readonly List<VRLabel> _rows = new List<VRLabel>();
    readonly List<CardSource> _under = new List<CardSource>();
    VRLabel _statusText;
    VRLabel _nameText;
    float _nextText;
    Permanent _textFor;

    void HideStackDetail()
    {
        foreach (VRLabel row in _rows)
        {
            row.gameObject.SetActive(false);
        }

        if (_statusText != null)
        {
            _statusText.gameObject.SetActive(false);
            _nameText.gameObject.SetActive(false);
        }

        if (_up != null)
        {
            _up.gameObject.SetActive(false);
            _down.gameObject.SetActive(false);
        }

        _textFor = null;
        _scroll = _scrollTarget = 0f;
    }

    /// <summary>Lays out the opened field stack as a detail panel. Call every frame while open.</summary>
    public void LayoutStack(Permanent permanent, Func<CardSource, Card3D> get)
    {
        _under.Clear();
        _under.AddRange(permanent.DigivolutionCards);
        _under.AddRange(permanent.LinkedCards);
        int n = _under.Count;
        float s = _scale;
        float unfold = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - _openTime) / 0.18f);

        int visible = Mathf.Min(n, MaxRows);
        int maxFirst = Mathf.Max(0, n - MaxRows);
        _scrollTarget = Mathf.Clamp(_scrollTarget, 0f, maxFirst);
        _scroll = Mathf.MoveTowards(_scroll, _scrollTarget, Time.unscaledDeltaTime * 12f);
        bool scrollable = maxFirst > 0;
        float listH = Mathf.Max(1, visible) * (RowH + RowGap) + (scrollable ? 0.045f : 0f);
        float rightH = StatusH + 0.045f + Card3D.Height * BigScale + 0.02f;
        float bodyH = Mathf.Max(listH, rightH);
        float frameW = StackW * s;
        float frameH = (bodyH + 0.07f) * s;

        _frame.localPosition = _position + _rotation * new Vector3(0f, 0.018f * s, 0.006f * s);
        _frame.localRotation = _rotation;
        _frame.localScale = new Vector3(frameW, frameH * unfold, 0.004f * s);

        float topEdge = 0.018f * s + frameH * 0.5f * unfold;
        _accent.localPosition = _position + _rotation * new Vector3(0f, topEdge, 0.004f * s);
        _accent.localRotation = _rotation;
        _accent.localScale = new Vector3(frameW, 0.005f * s, 0.006f * s);

        _title.transform.localPosition = _position + _rotation * new Vector3(-frameW * 0.5f + 0.22f * s, topEdge - 0.022f * s, -0.002f * s);
        _title.transform.localRotation = _rotation;
        _title.transform.localScale = new Vector3(0.4f * s, 0.035f * s, 1f);
        _title.text = scrollable
            ? $"{(Owner != null && Owner.isYou ? "YOUR" : "OPPONENT")} STACK  {n + 1} CARDS  ({Mathf.RoundToInt(_scroll) + 1}-{Mathf.RoundToInt(_scroll) + visible} of {n})"
            : $"{(Owner != null && Owner.isYou ? "YOUR" : "OPPONENT")} STACK  {n + 1} CARDS";

        _close.transform.localPosition = _position + _rotation * new Vector3(frameW * 0.5f - 0.065f * s, topEdge - 0.022f * s, -0.004f * s);
        _close.transform.localRotation = _rotation;
        _close.transform.localScale = Vector3.one * s;

        Quaternion faceUser = _rotation * Quaternion.Euler(-90f, 0f, 0f);
        Vector3 hover = _rotation * new Vector3(0f, 0f, -0.03f * s);
        float bodyTop = topEdge - 0.05f * s;
        bool texts = _textFor != permanent || Time.unscaledTime >= _nextText;

        if (texts)
        {
            _textFor = permanent;
            _nextText = Time.unscaledTime + TextRefresh;
        }

        // Left column: cards under the top one, top-down, each with its effect.
        float listLeft = -frameW * 0.5f + 0.015f * s;
        float thumbW = Card3D.Width * ThumbScale;

        while (_rows.Count < n)
        {
            VRLabel row = VRLabel.CreateParagraph(_board.transform, new Vector2(ListW - thumbW - 0.01f, RowH), 192, 30f, Color.white, VRLabel.DefaultPlate);
            _rows.Add(row);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            VRLabel row = _rows[i];

            if (i >= n)
            {
                row.gameObject.SetActive(false);
                continue;
            }

            CardSource card = _under[i];
            float slot = i - _scroll;
            float rowW = ListW - thumbW - 0.01f;

            if (slot < -0.5f || slot > visible - 0.5f)
            {
                // Scrolled out: hide the row, park its card behind the panel.
                row.gameObject.SetActive(false);

                if (card != null)
                {
                    float parkY = bodyTop - (Mathf.Clamp(slot, 0f, visible - 1) * (RowH + RowGap) + RowH * 0.5f) * s * unfold;
                    Vector3 park = _position + _rotation * new Vector3(listLeft + (rowW + 0.005f + thumbW * 0.5f) * s, parkY, 0.03f * s);
                    get(card).SetTarget(park, faceUser, !card.IsFlipped, card.IsDigiEgg, fastFollow: false, scale: ThumbScale * 0.5f * s, hoverOffset: hover);
                }

                if (texts)
                {
                    StyleRow(row, card, permanent);
                }

                continue;
            }

            float y = bodyTop - (slot * (RowH + RowGap) + RowH * 0.5f) * s * unfold;

            row.gameObject.SetActive(true);
            row.transform.localPosition = _position + _rotation * new Vector3(listLeft + rowW * 0.5f * s, y, -0.002f * s);
            row.transform.localRotation = _rotation;
            row.transform.localScale = new Vector3(rowW * s, RowH * s, 1f);

            if (texts)
            {
                StyleRow(row, card, permanent);
            }

            if (card != null)
            {
                Vector3 thumb = _position + _rotation * new Vector3(listLeft + (rowW + 0.005f + thumbW * 0.5f) * s, y, 0f);
                get(card).SetTarget(thumb, faceUser, !card.IsFlipped, card.IsDigiEgg, fastFollow: false, scale: ThumbScale * s, hoverOffset: hover);
            }
        }

        // Scroll buttons under the list (the thumbstick also scrolls).
        if (scrollable)
        {
            if (_up == null)
            {
                _up = VRButton3D.CreatePlate(_board.transform, Vector3.zero, new Vector2(0.12f, 0.032f), "UP", () => ScrollStack(-MaxRows + 1));
                _down = VRButton3D.CreatePlate(_board.transform, Vector3.zero, new Vector2(0.12f, 0.032f), "DOWN", () => ScrollStack(MaxRows - 1));
            }

            float buttonsY = bodyTop - (visible * (RowH + RowGap) + 0.022f) * s * unfold;
            PlaceButton(_up, new Vector3(listLeft + 0.07f * s, buttonsY, -0.004f * s), _scrollTarget > 0.01f);
            PlaceButton(_down, new Vector3(listLeft + 0.21f * s, buttonsY, -0.004f * s), _scrollTarget < maxFirst - 0.01f);
        }
        else if (_up != null)
        {
            _up.gameObject.SetActive(false);
            _down.gameObject.SetActive(false);
        }

        // Right column: applied effects, name, big top card.
        if (_statusText == null)
        {
            _statusText = VRLabel.CreateParagraph(_board.transform, new Vector2(StatusW, StatusH), 384, 34f, new Color(0.92f, 0.96f, 1f), new Color(0.03f, 0.13f, 0.18f));
            _nameText = VRLabel.Create(_board.transform, Vector3.zero, Quaternion.identity, new Vector2(StatusW, 0.032f), "", new Color(1f, 0.82f, 0.15f), new Color(0.05f, 0.09f, 0.14f));
        }

        float rightX = frameW * 0.5f - (StatusW * 0.5f + 0.02f) * s;
        float statusY = bodyTop - StatusH * 0.5f * s * unfold;
        _statusText.gameObject.SetActive(true);
        _statusText.transform.localPosition = _position + _rotation * new Vector3(rightX, statusY, -0.002f * s);
        _statusText.transform.localRotation = _rotation;
        _statusText.transform.localScale = new Vector3(StatusW * s, StatusH * s * unfold, 1f);

        float nameY = bodyTop - (StatusH + 0.022f) * s * unfold;
        _nameText.gameObject.SetActive(true);
        _nameText.transform.localPosition = _position + _rotation * new Vector3(rightX, nameY, -0.002f * s);
        _nameText.transform.localRotation = _rotation;
        _nameText.transform.localScale = new Vector3(StatusW * s, 0.032f * s, 1f);

        if (texts)
        {
            _statusText.text = StatusText(permanent);
            _nameText.text = permanent.TopCard != null ? CardName(permanent.TopCard) : "";
        }

        if (permanent.TopCard != null)
        {
            float bigY = bodyTop - (StatusH + 0.045f + Card3D.Height * BigScale * 0.5f) * s * unfold;
            Vector3 big = _position + _rotation * new Vector3(rightX, bigY, 0f);
            get(permanent.TopCard).SetTarget(big, faceUser, !permanent.TopCard.IsFlipped, permanent.TopCard.IsDigiEgg, fastFollow: false, scale: BigScale * s, hoverOffset: hover);
        }
    }

    void PlaceButton(VRButton3D button, Vector3 local, bool enabled)
    {
        button.gameObject.SetActive(enabled);
        button.transform.localPosition = _position + _rotation * local;
        button.transform.localRotation = _rotation;
        button.transform.localScale = Vector3.one * _scale;
    }

    static bool Japanese => ContinuousController.instance != null && ContinuousController.instance.language != Language.ENG;

    static string CardName(CardSource card) => Japanese ? card.BaseJPNCardNameFromEntity : card.BaseENGCardNameFromEntity;

    static void StyleRow(VRLabel row, CardSource card, Permanent permanent)
    {
        if (card == null || card.IsFlipped)
        {
            row.text = "???";
            row.plateColor = VRLabel.DefaultPlate;
            row.color = Color.white;
            return;
        }

        string effect;

        try
        {
            effect = card.IsLinked || permanent.LinkedCards.Contains(card)
                ? card.LinkEffectDiscription
                : Japanese ? card.InheritedEffectDiscription_JPN : card.InheritedEffectDiscription_ENG;
        }
        catch (Exception)
        {
            effect = "";
        }

        row.text = string.IsNullOrWhiteSpace(effect) ? "<i>" + CardName(card) + " - no inherited effect</i>" : effect.Trim();

        Color plate = VRLabel.DefaultPlate;
        Color text = Color.white;
        List<CardColor> colors = card.BaseCardColorsFromEntity;

        if (colors != null && colors.Count > 0 && DataBase.CardColor_ColorDarkDictionary.TryGetValue(colors[0], out Color dark))
        {
            plate = dark;

            if (!colors.Contains(CardColor.Black) && (colors[0] == CardColor.Yellow || colors[0] == CardColor.White))
            {
                text = Color.black;
            }
        }

        row.plateColor = plate;
        row.color = text;
    }

    static string StatusText(Permanent permanent)
    {
        string text;

        try
        {
            text = VRCompat.AppliedEffectText(permanent);
        }
        catch (Exception exception)
        {
            text = "";
            Debug.LogWarning("[DCGO VR] Stack detail text failed: " + exception.Message);
        }

        return string.IsNullOrWhiteSpace(text) ? "<i>No effects applied.</i>" : text.Replace("、", ",").Replace("，", ",");
    }
}
#endif
