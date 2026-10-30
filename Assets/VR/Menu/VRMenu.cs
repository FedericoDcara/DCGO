#if DCGO_VR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using S = VRMenuStyle;

// === DCGO-CUSTOM:vr ===
// Native VR menu (outside duels). It follows the hidden flat menu: several
// times a second it works out which flat screens are open, keeps the one that
// opened most recently, and shows it as VR panels, buttons and real card
// pictures. Every action goes through the flat menu's own handlers (FlatUI
// clicks or the same public methods its buttons call), so matchmaking, deck
// rules and saving are the game's own code.
//
// Look ("floating castle" system menu, see VRMenuStyle): the home screen is a
// column of round icon buttons with name tags beside a player card; every
// other page is a white panel with a header, an icon rail on its left (page
// icon, back, classic screen) and amber highlights. Pages unfold and their
// entries cascade in when they open.
//
// Pages: title, home, any dialog, battle deck choice, random lobby, rooms,
// join room, deck browser, deck card list, test draw, deck editor (cards,
// filters, deck list) and a generic mirror for every other window (options,
// sound, language, graphics, patch notes...).
public partial class VRMenu : MonoBehaviour
{
    const float Refresh = 0.2f;
    const float PanelH = 0.86f;
    const float HeaderH = 0.072f;
    const float RailGap = 0.085f;

    // Deck editing uses the flat screen (the VR editor drops too many frames).
    static readonly bool EditorInVR = false;

    enum Page { None, Loading, Dialog, EnterRoom, SelectDeck, RandomLobby, Room, Title, Home, Decks, DeckList, TrialDraw, Editor, Window }

    enum Zone { Main, Side, Bar }

    sealed class Btn
    {
        public string Label;
        public Action Act;
        public bool Enabled = true;
        public Zone Zone = Zone.Main;
    }

    sealed class CardItem
    {
        public CEntity_Base Card;
        public string Note;
        public Action Act;
        public bool Dim;
    }

    sealed class Spec
    {
        public Page Page;
        public string Key = "";
        public string Title = "";
        public string Body = "";
        public string Side = "";
        public bool Wide;
        public readonly List<Btn> Buttons = new List<Btn>();
        public List<DeckData> Decks;
        public DeckData Selected;
        public Action<DeckData> OnDeck;
        public List<CardItem> Cards;
        public int Cols = 6;
        public int Rows = 3;
        public List<Btn> Lines;
    }

    sealed class Tween
    {
        public Transform T;
        public Vector3 Pos;
        public Vector3 Scale;
        public Vector3 From;
        public float Start;
    }

    VRRig _rig;
    Transform _root;
    Transform _frame;
    VRLabel _title;
    VRLabel _body;
    VRLabel _side;
    VRLabel _footer;
    VRButton3D _toggle;
    VRLabel _toggleTag;
    VRButton3D _preview;
    float _previewUntil;
    readonly List<GameObject> _dynamic = new List<GameObject>();
    readonly Dictionary<VRButton3D, CEntity_Base> _cardButtons = new Dictionary<VRButton3D, CEntity_Base>();
    readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();
    readonly Dictionary<string, float> _openedAt = new Dictionary<string, float>();
    readonly HashSet<string> _openNow = new HashSet<string>();
    readonly List<Tween> _tweens = new List<Tween>();
    string _signature;
    float _next;
    int _deckPage;
    int _cardPage;
    int _linePage;
    bool _classic;
    Spec _spec;
    string _loggedKey;
    float _pageSince;
    bool _captured;
    float _panelW = 1.42f;
    float _panelH = PanelH;
    string _frameKey;
    Page _builtPage = Page.None;
    bool _shown;
    float _unfoldStart = -10f;
    float _rootScale = 1f;
    int _cascade;
    Vector3 _toggleLocal;
    Transform _pageIcon;

    /// <summary>True while a VR page replaces the flat screen.</summary>
    public bool ShowsPage => !_classic && _spec != null && _spec.Page != Page.None && !FlatPage;

    /// <summary>Pages the VR menu leaves to the flat screen.</summary>
    bool FlatPage => !EditorInVR && _spec != null && _spec.Page == Page.Editor;

    bool Hub => _spec != null && (_spec.Page == Page.Home || _spec.Page == Page.Title);

    public static VRMenu Create(Transform parent, VRRig rig)
    {
        GameObject go = new GameObject("VRMenu") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRMenu menu = go.AddComponent<VRMenu>();
        menu._rig = rig;
        menu.Build();
        return menu;
    }

    void Build()
    {
        _root = new GameObject("MenuPanel") { layer = VRRig.Layer }.transform;
        _root.SetParent(_rig.MenuAnchor, false);

        _title = S.Text(_root, Vector3.zero, new Vector2(1.2f, 0.042f), "", S.Ink, S.Header, TMPro.TextAlignmentOptions.Left);
        _body = VRLabel.CreateParagraph(_root, new Vector2(1.3f, 0.13f), 256, 44f, S.Ink, S.Panel);
        _side = VRLabel.CreateParagraph(_root, new Vector2(0.44f, 0.30f), 384, 40f, S.Ink, S.Well);
        _footer = S.Text(_root, Vector3.zero, new Vector2(0.95f, 0.024f), "", S.InkSoft, S.Panel, TMPro.TextAlignmentOptions.Left);

        // Classic screen / VR menu switch: a round button with a name tag.
        _toggle = S.Button(transform, Vector3.zero, new Vector2(0.062f, 0.062f), S.Shape3.Disc, "CLASSIC SCREEN", S.Icon.Screen, ToggleClassic);
        S.Card(_toggle.transform, "Tag", new Vector3(0f, -0.054f, 0.004f), new Vector2(0.13f, 0.026f), 0.013f, S.Header, S.Hairline, 0.0015f);
        _toggleTag = S.Text(_toggle.transform, new Vector3(0f, -0.054f, -0.001f), new Vector2(0.11f, 0.017f), "CLASSIC", S.InkSoft, S.Header);

        _preview = VRButton3D.CreateCard(_root, Vector3.zero, new Vector2(0.25f, 0.35f), null, null);
        _preview.Interactable = false;
        Destroy(_preview.GetComponent<Collider>());
        S.Card(_preview.transform, "PreviewFrame", new Vector3(0f, 0f, 0.003f), new Vector2(0.262f, 0.362f), 0.012f, S.Header, S.Edge);
        _preview.gameObject.SetActive(false);
        _root.gameObject.SetActive(false);
        ApplyPanelSize(1.42f, PanelH);
    }

    // The panel frame: soft halo, edged body, white header with an amber tick and underline.
    void ApplyPanelSize(float width, float height)
    {
        _panelW = width;
        _panelH = height;
        string key = width.ToString("F3") + "x" + height.ToString("F3");

        if (_frameKey != key)
        {
            _frameKey = key;

            if (_frame != null)
            {
                Destroy(_frame.gameObject);
            }

            _frame = new GameObject("Frame") { layer = VRRig.Layer }.transform;
            _frame.SetParent(_root, false);
            float top = height * 0.5f;
            float left = -width * 0.5f + 0.05f;
            S.Shape(_frame, "Halo", S.RoundedRect(width + 0.05f, height + 0.05f, 0.05f), S.Glass(S.Halo), new Vector3(0f, 0f, 0.02f));
            S.Card(_frame, "Body", new Vector3(0f, 0f, 0.012f), new Vector2(width, height), 0.03f, S.Panel, S.Edge, 0.003f);
            S.Card(_frame, "Header", new Vector3(0f, top - 0.012f - HeaderH * 0.5f, 0.0105f), new Vector2(width - 0.024f, HeaderH), 0.022f, S.Header);
            S.Shape(_frame, "Tick", S.RoundedRect(0.007f, 0.036f, 0.0035f), S.Solid(S.Accent), new Vector3(left - 0.012f, top - 0.012f - HeaderH * 0.5f, 0.009f));
            S.Shape(_frame, "Rule", S.RoundedRect(width - 0.07f, 0.0022f, 0f), S.Solid(S.Hairline), new Vector3(0f, top - 0.094f, 0.0105f));
            S.Shape(_frame, "Underline", S.RoundedRect(0.18f, 0.004f, 0.002f), S.Solid(S.Accent), new Vector3(left + 0.09f, top - 0.094f, 0.0098f));
            S.Shape(_frame, "FootRule", S.RoundedRect(width - 0.07f, 0.0018f, 0f), S.Solid(S.Hairline), new Vector3(0f, -height * 0.5f + 0.058f, 0.0105f));
        }

        _title.Size = new Vector2(width - 0.14f, 0.040f);
        _title.transform.localPosition = new Vector3(0.005f, height * 0.5f - 0.012f - HeaderH * 0.5f, 0f);
        _footer.Size = new Vector2(0.95f, 0.022f);
        _footer.transform.localPosition = new Vector3(-width * 0.5f + 0.05f + 0.475f, -height * 0.5f + 0.032f, 0f);
    }

    void ToggleClassic()
    {
        _classic = !_classic;
        _signature = null;
        Debug.Log("[DCGO VR] Menu: " + (_classic ? "classic flat screen" : "VR menu"));
    }

    /// <summary>Shown only outside duels (VRSession decides).</summary>
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
            _signature = null;
        }

        // The panel lives under the rig's menu anchor, not under this object:
        // hide it explicitly (otherwise the last page, e.g. "Please wait", stays up in the duel).
        if (!visible && _root != null && _root.gameObject.activeSelf)
        {
            _root.gameObject.SetActive(false);
            _preview.gameObject.SetActive(false);
            _spec = null;
            _shown = false;
        }
    }

    void Update()
    {
        if (Time.unscaledTime >= _next)
        {
            _next = Time.unscaledTime + Refresh;
            _spec = Detect();

            if (_spec.Key != _loggedKey)
            {
                if (_loggedKey == null || _spec.Page.ToString() != _loggedKey.Split(':')[0])
                {
                    _cardPage = 0;
                    _linePage = 0;
                }

                _loggedKey = _spec.Key;
                _pageSince = Time.unscaledTime;
                _captured = false;
                Debug.Log("[DCGO VR] Menu page: " + _spec.Key);
            }

            Render();

            // Dev aid: one picture of each page once it has settled (images loaded).
            if (!_captured && ShowsPage && Time.unscaledTime - _pageSince > 2.5f)
            {
                _captured = true;
                Transform anchor = _rig.MenuAnchor;
                Vector3 eye = anchor.position - anchor.forward * (_spec.Wide ? 1.75f : 1.45f) + Vector3.up * 0.05f;
                VRCapture.Capture("menu-" + _spec.Page.ToString().ToLowerInvariant(), eye, Quaternion.LookRotation(anchor.position - eye, Vector3.up));
            }
        }

        Animate();
        UpdatePreview();
        PlaceToggle();
    }

    // ---------------------------------------------------------------- choosing the page

    Spec Detect()
    {
        Opening op = Opening.instance;

        // Battle scene loading/running: the menu steps aside.
        if (op == null || GManager.instance != null)
        {
            return new Spec();
        }

        Cache(op);

        // Loading always wins: nothing can be clicked meanwhile.
        Spec loading = new Spec();

        if (DetectLoading(op, loading))
        {
            loading.Key = "Loading";
            return loading;
        }

        // Every open screen is a candidate; the one opened most recently is on top.
        List<Spec> open = new List<Spec>(4);
        Try(open, s => DetectDialogs(op, s, open));
        Try(open, s => DetectEnterRoom(s));
        Try(open, s => DetectSelectDeck(op, s));
        Try(open, s => DetectRandomLobby(op, s));
        Try(open, s => DetectRoom(op, s));
        Try(open, s => DetectDeckList(op, s));
        Try(open, s => DetectTrialDraw(op, s));
        Try(open, s => DetectEditor(op, s));
        Try(open, s => DetectDecks(op, s));
        Try(open, s => DetectWindows(op, s, open));

        _openNow.Clear();

        foreach (Spec spec in open)
        {
            _openNow.Add(spec.Key);

            if (!_openedAt.ContainsKey(spec.Key))
            {
                _openedAt[spec.Key] = Time.unscaledTime + open.IndexOf(spec) * 1e-4f;
            }
        }

        foreach (string key in _openedAt.Keys.ToList())
        {
            if (!_openNow.Contains(key))
            {
                _openedAt.Remove(key);
            }
        }

        if (open.Count > 0)
        {
            return open.OrderByDescending(s => _openedAt[s.Key]).First();
        }

        Spec base_ = new Spec();

        if (DetectTitle(op, base_) || DetectHome(op, base_))
        {
            base_.Key = base_.Page.ToString();
            return base_;
        }

        return base_;
    }

    void Try(List<Spec> open, Func<Spec, bool> detector)
    {
        Spec spec = new Spec();

        try
        {
            if (detector(spec))
            {
                if (string.IsNullOrEmpty(spec.Key))
                {
                    spec.Key = spec.Page.ToString();
                }

                open.Add(spec);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Menu page check failed: " + exception.Message);
        }
    }

    // ---------------------------------------------------------------- rendering

    void Render()
    {
        bool show = ShowsPage;

        if (_root.gameObject.activeSelf != show)
        {
            _root.gameObject.SetActive(show);
        }

        if (!show)
        {
            _shown = false;
            return;
        }

        string signature = Signature(_spec);

        if (signature != _signature)
        {
            _signature = signature;
            Rebuild();
        }

        _title.text = _spec.Title;
        _body.text = _spec.Body;
        _side.text = Hub ? "" : _spec.Side;
        string player = ContinuousController.instance != null ? ContinuousController.instance.PlayerName : "";
        string version = Opening.instance != null && Opening.instance.VerText != null ? Opening.instance.VerText.text : "";
        _footer.text = Hub ? version : (string.IsNullOrEmpty(player) ? "" : "Player: " + player + "     ") + version;
    }

    string Signature(Spec spec)
    {
        StringBuilder s = new StringBuilder();
        s.Append(spec.Key).Append('|').Append(spec.Wide).Append('|').Append(_deckPage).Append(',').Append(_cardPage).Append(',').Append(_linePage).Append('|');

        foreach (Btn button in spec.Buttons)
        {
            s.Append(button.Label).Append(button.Enabled ? '+' : '-').Append((int)button.Zone).Append('|');
        }

        if (spec.Decks != null)
        {
            foreach (DeckData deck in spec.Decks)
            {
                s.Append(deck.DeckID).Append(deck == spec.Selected ? '*' : ' ').Append(deck.DeckName).Append(deck.KeyCardId).Append('|');
            }
        }

        if (spec.Cards != null)
        {
            foreach (CardItem card in spec.Cards)
            {
                s.Append(card.Card != null ? card.Card.CardIndex : -1).Append(card.Note).Append(card.Dim ? 'd' : ' ').Append('|');
            }
        }

        if (spec.Lines != null)
        {
            foreach (Btn line in spec.Lines)
            {
                s.Append(line.Label).Append('|');
            }
        }

        return s.ToString();
    }

    // Layout regions (panel space, metres; the panel faces -Z).
    float Left => -_panelW * 0.5f + 0.05f;
    float Right => _panelW * 0.5f - 0.05f;
    bool HasGrid => _spec.Decks != null || _spec.Cards != null;
    float SideWidth => _spec.Wide ? 0.66f : 0.44f;
    float SideCenter => Right - SideWidth * 0.5f;
    float GridRight => SideCenter - SideWidth * 0.5f - 0.04f;

    static bool IsBack(string label)
    {
        string l = (label ?? "").Trim().ToUpperInvariant();
        return l == "BACK" || l == "CLOSE";
    }

    static S.Icon PageIcon(Page page)
    {
        switch (page)
        {
            case Page.Loading: return S.Icon.Hourglass;
            case Page.Dialog: return S.Icon.Info;
            case Page.EnterRoom:
            case Page.Room: return S.Icon.Room;
            case Page.RandomLobby: return S.Icon.Bolt;
            case Page.SelectDeck:
            case Page.Decks: return S.Icon.Decks;
            case Page.DeckList:
            case Page.TrialDraw:
            case Page.Editor: return S.Icon.Cards;
            case Page.Window: return S.Icon.Options;
            case Page.Home: return S.Icon.Home;
            default: return S.Icon.Play;
        }
    }

    void Rebuild()
    {
        foreach (GameObject go in _dynamic)
        {
            if (go != null)
            {
                Destroy(go);
            }
        }

        _dynamic.Clear();
        _cardButtons.Clear();
        _tweens.Clear();
        _pageIcon = null;
        _cascade = 0;

        // A new page (or the menu just came back): unfold the panel and cascade its entries in.
        bool animate = !_shown || _spec.Page != _builtPage;
        _shown = true;
        _builtPage = _spec.Page;

        if (animate)
        {
            _unfoldStart = Time.unscaledTime;
        }

        if (Hub)
        {
            RebuildHub(animate);
            return;
        }

        float height = _spec.Wide ? 0.96f : PanelH;
        ApplyPanelSize(_spec.Wide ? 1.9f : 1.42f, height);
        _frame.gameObject.SetActive(true);
        _title.alignment = TMPro.TextAlignmentOptions.Left;
        _rootScale = _spec.Wide ? 0.92f : 1f;
        _root.localScale = Vector3.one * _rootScale;
        float top = height * 0.5f;

        // BACK / CLOSE move to the rail (round back button) on ordinary pages.
        Btn back = _spec.Page == Page.Dialog || _spec.Page == Page.RandomLobby ? null : _spec.Buttons.FirstOrDefault(b => IsBack(b.Label));
        List<Btn> main = _spec.Buttons.FindAll(b => b.Zone == Zone.Main && b != back);
        List<Btn> side = _spec.Buttons.FindAll(b => b.Zone == Zone.Side && b != back);
        List<Btn> bar = _spec.Buttons.FindAll(b => b.Zone == Zone.Bar && b != back);
        bool hasSide = HasGrid || side.Count > 0 || !string.IsNullOrEmpty(_spec.Side) || _spec.Lines != null;

        BuildRail(top, back);

        // Body: full width on simple pages, one hint line over the grid otherwise.
        float bodyRight = hasSide ? GridRight : Right;
        float bodyH = HasGrid ? 0.05f : 0.13f;
        _body.Size = new Vector2(bodyRight - Left, bodyH);
        _body.transform.localPosition = new Vector3((Left + bodyRight) * 0.5f, top - 0.105f - bodyH * 0.5f, 0f);
        _body.gameObject.SetActive(true);
        _footer.gameObject.SetActive(true);

        // Side column: an inset well holding the details text and the side buttons.
        _side.gameObject.SetActive(hasSide);
        float sideTextH = _spec.Lines != null ? 0.11f : HasGrid ? 0.26f : 0.24f;
        _side.Size = new Vector2(SideWidth - 0.01f, sideTextH);
        _side.transform.localPosition = new Vector3(SideCenter, top - 0.115f - sideTextH * 0.5f, 0f);

        if (hasSide)
        {
            float wellTop = top - 0.105f;
            float wellBottom = -height * 0.5f + 0.07f;
            Track(S.Card(_root, "SideWell", new Vector3(SideCenter, (wellTop + wellBottom) * 0.5f, 0.008f), new Vector2(SideWidth + 0.024f, wellTop - wellBottom), 0.02f, S.Well).gameObject);
        }

        float gridTop = top - 0.105f - bodyH - 0.02f;
        float gridBottom = -height * 0.5f + (bar.Count > 0 ? 0.135f : 0.09f);

        if (_spec.Decks != null)
        {
            BuildDeckGrid(gridTop, gridBottom);
        }
        else if (_spec.Cards != null)
        {
            BuildCardGrid(gridTop, gridBottom);
        }

        // Main buttons: a centred list (left of the side column when there is one).
        if (main.Count > 0)
        {
            float areaLeft = Left;
            float areaRight = hasSide ? GridRight : Right;
            float center = (areaLeft + areaRight) * 0.5f;
            bool twoColumns = main.Count > 5;
            int perColumn = twoColumns ? Mathf.CeilToInt(main.Count / 2f) : main.Count;
            float width = twoColumns ? Mathf.Min(0.44f, (areaRight - areaLeft) * 0.5f - 0.03f) : 0.58f;
            float startY = HasGrid ? gridTop - 0.04f : top - 0.105f - bodyH - 0.07f;

            for (int i = 0; i < main.Count; i++)
            {
                int column = twoColumns ? i / perColumn : 0;
                int row = twoColumns ? i % perColumn : i;
                float x = twoColumns ? center + (column == 0 ? -1f : 1f) * (width * 0.5f + 0.015f) : center;
                AddButton(main[i], new Vector3(x, startY - row * 0.086f, 0f), new Vector2(width, 0.070f), S.Shape3.Bar);
            }
        }

        // Side: the deck list lines (editor), then the side buttons.
        float sideY = top - 0.115f - sideTextH - 0.02f;

        if (_spec.Lines != null)
        {
            sideY = BuildLines(sideY);
        }

        if (side.Count > 0)
        {
            int columns = side.Count > 4 ? 2 : 1;
            float gap = 0.012f;
            float width = (SideWidth - gap * (columns - 1)) / columns;
            float rowH = 0.058f;

            for (int i = 0; i < side.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                float x = SideCenter - SideWidth * 0.5f + width * 0.5f + column * (width + gap);
                AddButton(side[i], new Vector3(x, sideY - rowH * 0.5f - row * (rowH + 0.01f), 0f), new Vector2(width, rowH), S.Shape3.Bar);
            }
        }

        // Bar: compact entries along the bottom of the grid area.
        float barX = Left;

        foreach (Btn button in bar)
        {
            float width = Mathf.Clamp(0.07f + button.Label.Length * 0.016f, 0.15f, 0.40f);
            AddButton(button, new Vector3(barX + width * 0.5f, -height * 0.5f + 0.098f, 0f), new Vector2(width, 0.05f), S.Shape3.Bar);
            barX += width + 0.012f;
        }
    }

    // Left of the panel: page icon, back, classic-screen switch, on a thin rail.
    void BuildRail(float top, Btn back)
    {
        float x = -_panelW * 0.5f - 0.075f;
        float y = top - 0.05f;
        int slots = back != null ? 3 : 2;
        float bottom = y - (slots - 1) * RailGap;
        Track(S.Shape(_root, "Rail", S.RoundedRect(0.004f, y - bottom + 0.02f, 0.002f), S.Solid(S.Edge), new Vector3(x, (y + bottom) * 0.5f, 0.01f)).gameObject);

        Transform icon = new GameObject("PageIcon") { layer = VRRig.Layer }.transform;
        icon.SetParent(_root, false);
        icon.localPosition = new Vector3(x, y, 0f);
        S.Shape(icon, "Edge", S.Disc(0.081f), S.Solid(S.AccentDeep), new Vector3(0f, 0f, 0.0015f));
        S.Shape(icon, "Disc", S.Disc(0.075f), S.Solid(S.Accent), Vector3.zero);
        S.IconQuad(icon, PageIcon(_spec.Page), 0.044f, new Vector3(0f, 0f, -0.003f)).transform.localRotation = Quaternion.identity;
        _pageIcon = _spec.Page == Page.Loading ? icon : null;
        Track(icon.gameObject);
        y -= RailGap;

        if (back != null)
        {
            AddButton(back, new Vector3(x, y, 0f), new Vector2(0.062f, 0.062f), S.Shape3.Disc, S.Icon.Back);
            y -= RailGap;
        }

        _toggleLocal = new Vector3(x, y, 0f);
    }

    // Home / title: a column of round icon buttons with name tags, beside a player card.
    void RebuildHub(bool animate)
    {
        _frame.gameObject.SetActive(false);
        _rootScale = 1f;
        _root.localScale = Vector3.one;
        _side.gameObject.SetActive(false);
        List<Btn> buttons = _spec.Buttons;
        const float step = 0.096f;
        const float discD = 0.08f;
        const float itemW = 0.40f;
        float columnX = 0.12f;
        float discX = columnX - itemW * 0.5f + discD * 0.5f;
        float firstY = (buttons.Count - 1) * step * 0.5f - 0.02f;
        float lastY = firstY - (buttons.Count - 1) * step;

        // Title plate above everything.
        float titleY = Mathf.Max(firstY, 0.17f) + 0.12f;
        Transform plate = S.Card(_root, "TitlePlate", new Vector3(0f, titleY, 0.006f), new Vector2(0.50f, 0.074f), 0.022f, S.Header, S.Edge);
        Track(plate.gameObject);
        Track(S.Shape(_root, "TickL", S.RoundedRect(0.007f, 0.034f, 0.0035f), S.Solid(S.Accent), new Vector3(-0.22f, titleY, 0.003f)).gameObject);
        Track(S.Shape(_root, "TickR", S.RoundedRect(0.007f, 0.034f, 0.0035f), S.Solid(S.Accent), new Vector3(0.22f, titleY, 0.003f)).gameObject);
        _title.Size = new Vector2(0.40f, 0.042f);
        _title.transform.localPosition = new Vector3(0f, titleY, 0f);
        _title.alignment = TMPro.TextAlignmentOptions.Center;

        // Player card on the left.
        const float cardW = 0.36f;
        const float cardH = 0.40f;
        float cardX = discX - 0.08f - cardW * 0.5f;
        float cardY = 0f;
        float cardTop = cardY + cardH * 0.5f;
        Transform card = S.Card(_root, "PlayerCard", new Vector3(cardX, cardY, 0.012f), new Vector2(cardW, cardH), 0.026f, S.Panel, S.Edge);
        S.Card(card, "Head", new Vector3(0f, cardH * 0.5f - 0.045f, -0.0015f), new Vector2(cardW - 0.02f, 0.07f), 0.02f, S.Header);
        S.Shape(card, "Avatar", S.Disc(0.054f), S.Solid(S.Accent), new Vector3(-cardW * 0.5f + 0.05f, cardH * 0.5f - 0.045f, -0.003f));
        S.IconQuad(card, S.Icon.Person, 0.034f, new Vector3(-cardW * 0.5f + 0.05f, cardH * 0.5f - 0.045f, -0.0045f));
        S.Shape(card, "Rule", S.RoundedRect(cardW - 0.05f, 0.0022f, 0f), S.Solid(S.Hairline), new Vector3(0f, cardH * 0.5f - 0.092f, -0.0015f));
        Track(card.gameObject);
        string player = ContinuousController.instance != null ? ContinuousController.instance.PlayerName : "";
        VRLabel name = S.Text(card, new Vector3(0.03f, cardH * 0.5f - 0.045f, -0.006f), new Vector2(cardW - 0.13f, 0.03f), string.IsNullOrEmpty(player) ? "PLAYER" : player, S.Ink, S.Header, TMPro.TextAlignmentOptions.Left);
        _dynamic.Add(name.gameObject);

        _body.Size = new Vector2(cardW - 0.04f, 0.20f);
        _body.transform.localPosition = new Vector3(cardX, cardTop - 0.105f - 0.10f, -0.004f);
        _body.gameObject.SetActive(true);
        _footer.Size = new Vector2(cardW - 0.05f, 0.02f);
        _footer.transform.localPosition = new Vector3(cardX, cardY - cardH * 0.5f + 0.028f, -0.004f);
        _footer.gameObject.SetActive(true);

        // The column: a thin rail behind the discs, then one tagged button per entry.
        if (buttons.Count > 1)
        {
            Track(S.Shape(_root, "Rail", S.RoundedRect(0.004f, firstY - lastY, 0.002f), S.Solid(S.Edge), new Vector3(discX, (firstY + lastY) * 0.5f, 0.012f)).gameObject);
        }

        for (int i = 0; i < buttons.Count; i++)
        {
            AddButton(buttons[i], new Vector3(columnX, firstY - i * step, 0f), new Vector2(itemW, discD), S.Shape3.Tagged);
        }

        _toggleLocal = new Vector3(cardX + cardW * 0.5f - 0.05f, cardY - cardH * 0.5f - 0.06f, 0f);
    }

    void Track(GameObject go)
    {
        _dynamic.Add(go);
    }

    // Entries appear one after another: grow from nothing while sliding into place.
    void Pop(Transform t, int order, bool animate, Vector3? from = null)
    {
        if (!animate || t == null)
        {
            return;
        }

        Tween tween = new Tween
        {
            T = t,
            Pos = t.localPosition,
            Scale = t.localScale,
            From = from ?? new Vector3(-0.03f, 0f, 0f),
            Start = Time.unscaledTime + 0.09f + order * 0.028f,
        };
        _tweens.Add(tween);
        t.localPosition = tween.Pos + tween.From;
        t.localScale = tween.Scale * 0.001f;
    }

    void Animate()
    {
        float now = Time.unscaledTime;

        // Panel unfold: the page opens from a thin line (vertical scale).
        float unfold = Mathf.Clamp01((now - _unfoldStart) / 0.2f);

        if (_root != null && _root.gameObject.activeSelf && unfold < 1.0001f)
        {
            float e = 1f - Mathf.Pow(1f - unfold, 3f);
            _root.localScale = new Vector3(_rootScale, _rootScale * Mathf.Lerp(0.04f, 1f, e), _rootScale);
        }

        for (int i = _tweens.Count - 1; i >= 0; i--)
        {
            Tween tween = _tweens[i];

            if (tween.T == null)
            {
                _tweens.RemoveAt(i);
                continue;
            }

            float p = Mathf.Clamp01((now - tween.Start) / 0.2f);
            float e = 1f - Mathf.Pow(1f - p, 3f);
            tween.T.localPosition = tween.Pos + tween.From * (1f - e);
            tween.T.localScale = tween.Scale * Mathf.Max(0.001f, e);

            if (p >= 1f)
            {
                _tweens.RemoveAt(i);
            }
        }

        if (_pageIcon != null)
        {
            _pageIcon.localRotation = Quaternion.Euler(0f, 0f, -now * 120f);
        }
    }

    void AddButton(Btn spec, Vector3 position, Vector2 size, S.Shape3 shape, S.Icon? icon = null)
    {
        Action act = spec.Act;
        string label = spec.Label;
        VRButton3D button = S.Button(_root, position, size, shape, label, icon ?? S.IconFor(label), () =>
        {
            Debug.Log("[DCGO VR] Menu button: " + label);

            try
            {
                act?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DCGO VR] Menu action failed: " + exception.Message);
            }

            _signature = null;
            _next = 0f;
        }, spec.Enabled);
        _dynamic.Add(button.gameObject);
        Pop(button.transform, ++_cascade, _shown && Time.unscaledTime - _unfoldStart < 0.05f, shape == S.Shape3.Disc ? Vector3.zero : (Vector3?)null);
    }

    VRLabel AddLabel(Vector3 position, Vector2 size, string text, Color color, Color? surface = null)
    {
        VRLabel label = S.Text(_root, position, size, text, color, surface ?? S.Panel);
        _dynamic.Add(label.gameObject);
        return label;
    }

    void AddGlow(Vector3 position, Vector2 size)
    {
        Track(S.Card(_root, "SelectedGlow", position + new Vector3(0f, 0f, 0.006f), new Vector2(size.x + 0.018f, size.y + 0.018f), 0.012f, S.Accent).gameObject);
    }

    // Decks as cards (key card picture) with their names under them.
    void BuildDeckGrid(float gridTop, float gridBottom)
    {
        List<DeckData> decks = _spec.Decks;
        const int cols = 5;
        const int rows = 2;
        int perPage = cols * rows;
        int pages = Mathf.Max(1, Mathf.CeilToInt(decks.Count / (float)perPage));
        _deckPage = Mathf.Clamp(_deckPage, 0, pages - 1);
        float areaW = GridRight - Left;
        float cellW = areaW / cols;
        float cellH = (gridTop - gridBottom) / rows;
        float cardH = Mathf.Min(cellH - 0.05f, (cellW - 0.02f) * 1.4f);
        float cardW = cardH / 1.4f;

        if (decks.Count == 0)
        {
            AddLabel(new Vector3((Left + GridRight) * 0.5f, (gridTop + gridBottom) * 0.5f, 0f), new Vector2(areaW - 0.1f, 0.04f), "No decks yet.", S.InkSoft);
        }

        for (int i = 0; i < perPage; i++)
        {
            int index = _deckPage * perPage + i;

            if (index >= decks.Count)
            {
                break;
            }

            DeckData deck = decks[index];
            Vector3 position = new Vector3(Left + cellW * (i % cols + 0.5f), gridTop - cellH * (i / cols) - cardH * 0.5f - 0.005f, 0f);
            Action<DeckData> onDeck = _spec.OnDeck;
            VRButton3D card = VRButton3D.CreateCard(_root, position, new Vector2(cardW, cardH), null, () =>
            {
                onDeck?.Invoke(deck);
                _signature = null;
                _next = 0f;
            });
            _dynamic.Add(card.gameObject);
            LoadSprite(deck.KeyCard, card);
            Pop(card.transform, ++_cascade, _shown && Time.unscaledTime - _unfoldStart < 0.05f, new Vector3(0f, -0.02f, 0f));

            bool selected = deck == _spec.Selected;

            if (selected)
            {
                AddGlow(position, new Vector2(cardW, cardH));
            }

            Color ink = selected ? S.AccentDeep : deck.IsValidDeckData() ? S.Ink : S.Danger;
            AddLabel(position + new Vector3(0f, -cardH * 0.5f - 0.02f, 0f), new Vector2(cellW - 0.01f, 0.026f), deck.DeckName, ink);
        }

        if (pages > 1)
        {
            AddPager(pages, gridBottom - 0.035f);
        }
    }

    // Generic card grid (deck list, test draw, editor pool), paged.
    void BuildCardGrid(float gridTop, float gridBottom)
    {
        List<CardItem> cards = _spec.Cards;
        int cols = Mathf.Max(1, _spec.Cols);
        int rows = Mathf.Max(1, _spec.Rows);
        int perPage = cols * rows;
        int pages = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)perPage));
        _cardPage = Mathf.Clamp(_cardPage, 0, pages - 1);
        float areaW = GridRight - Left;
        float cellW = areaW / cols;
        float cellH = (gridTop - gridBottom - (pages > 1 ? 0.05f : 0f)) / rows;
        bool notes = cards.Any(c => !string.IsNullOrEmpty(c.Note));
        float cardH = Mathf.Min(cellH - (notes ? 0.035f : 0.01f), (cellW - 0.015f) * 1.4f);
        float cardW = cardH / 1.4f;

        if (cards.Count == 0)
        {
            AddLabel(new Vector3((Left + GridRight) * 0.5f, (gridTop + gridBottom) * 0.5f, 0f), new Vector2(areaW - 0.1f, 0.04f), "No cards to show.", S.InkSoft);
        }

        for (int i = 0; i < perPage; i++)
        {
            int index = _cardPage * perPage + i;

            if (index >= cards.Count)
            {
                break;
            }

            CardItem item = cards[index];
            Vector3 position = new Vector3(Left + cellW * (i % cols + 0.5f), gridTop - cellH * (i / cols) - cardH * 0.5f - 0.005f, 0f);
            Action act = item.Act;
            VRButton3D card = VRButton3D.CreateCard(_root, position, new Vector2(cardW, cardH), null, () =>
            {
                act?.Invoke();
                _signature = null;
                _next = 0f;
            });
            card.Interactable = act != null && !item.Dim;
            _dynamic.Add(card.gameObject);
            _cardButtons[card] = item.Card;
            LoadSprite(item.Card, card);
            Pop(card.transform, ++_cascade, _shown && Time.unscaledTime - _unfoldStart < 0.05f, new Vector3(0f, -0.02f, 0f));

            if (!string.IsNullOrEmpty(item.Note))
            {
                AddLabel(position + new Vector3(0f, -cardH * 0.5f - 0.016f, 0f), new Vector2(cellW - 0.01f, 0.022f), item.Note, item.Dim ? S.Danger : S.InkSoft);
            }
        }

        if (pages > 1)
        {
            AddPager(pages, gridBottom - 0.01f);
        }
    }

    // Round previous / next buttons with "page / pages" between them.
    void AddPager(int pages, float y)
    {
        bool deckPager = _spec.Decks != null;
        int current = deckPager ? _deckPage : _cardPage;
        float x = Left + 0.04f;
        AddButton(new Btn
        {
            Label = "< PREV",
            Enabled = current > 0,
            Act = () => { if (deckPager) { _deckPage--; } else { _cardPage--; } },
        }, new Vector3(x, y, 0f), new Vector2(0.05f, 0.05f), S.Shape3.Disc, S.Icon.Left);
        AddLabel(new Vector3(x + 0.1f, y, 0f), new Vector2(0.11f, 0.03f), $"{current + 1} / {pages}", S.InkSoft);
        AddButton(new Btn
        {
            Label = "NEXT >",
            Enabled = current < pages - 1,
            Act = () => { if (deckPager) { _deckPage++; } else { _cardPage++; } },
        }, new Vector3(x + 0.2f, y, 0f), new Vector2(0.05f, 0.05f), S.Shape3.Disc, S.Icon.Right);
    }

    // Compact two-column list in the side area (the deck being edited).
    float BuildLines(float y)
    {
        List<Btn> lines = _spec.Lines;
        const int perColumn = 11;
        const float rowH = 0.031f;
        int perPage = perColumn * 2;
        int pages = Mathf.Max(1, Mathf.CeilToInt(lines.Count / (float)perPage));
        _linePage = Mathf.Clamp(_linePage, 0, pages - 1);
        float columnW = (SideWidth - 0.01f) * 0.5f;

        for (int i = 0; i < perPage; i++)
        {
            int index = _linePage * perPage + i;

            if (index >= lines.Count)
            {
                break;
            }

            int column = i / perColumn;
            int row = i % perColumn;
            float x = SideCenter - SideWidth * 0.5f + columnW * 0.5f + column * (columnW + 0.01f);
            AddButton(lines[index], new Vector3(x, y - rowH * 0.5f - row * (rowH + 0.003f), 0f), new Vector2(columnW, rowH), S.Shape3.Chip);
        }

        float bottom = y - perColumn * (rowH + 0.003f);

        if (pages > 1)
        {
            int current = _linePage;
            AddButton(new Btn { Label = "< MORE", Enabled = current > 0, Act = () => _linePage-- }, new Vector3(SideCenter - 0.1f, bottom - 0.024f, 0f), new Vector2(0.04f, 0.04f), S.Shape3.Disc, S.Icon.Left);
            AddLabel(new Vector3(SideCenter, bottom - 0.024f, 0f), new Vector2(0.07f, 0.024f), $"{current + 1}/{pages}", S.InkSoft, S.Well);
            AddButton(new Btn { Label = "MORE >", Enabled = current < pages - 1, Act = () => _linePage++ }, new Vector3(SideCenter + 0.1f, bottom - 0.024f, 0f), new Vector2(0.04f, 0.04f), S.Shape3.Disc, S.Icon.Right);
            bottom -= 0.05f;
        }

        return bottom - 0.015f;
    }

    async void LoadSprite(CEntity_Base card, VRButton3D button)
    {
        if (card == null || button == null)
        {
            return;
        }

        try
        {
            if (!_sprites.TryGetValue(card.CardIndex, out Sprite sprite) || sprite == null)
            {
                sprite = await card.GetCardSprite();
                _sprites[card.CardIndex] = sprite;

                if (_sprites.Count > 400)
                {
                    _sprites.Clear();
                }
            }

            if (button != null && sprite != null)
            {
                button.SetSprite(sprite);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Menu: card image failed: " + exception.Message);
        }
    }

    // Big picture of the card under the laser, beside the panel.
    void UpdatePreview()
    {
        if (!ShowsPage)
        {
            _preview.gameObject.SetActive(false);
            return;
        }

        CEntity_Base hovered = null;

        foreach (KeyValuePair<VRButton3D, CEntity_Base> pair in _cardButtons)
        {
            if (pair.Key != null && Time.unscaledTime - pair.Key.LastHovered < 0.15f)
            {
                hovered = pair.Value;
                break;
            }
        }

        if (hovered != null)
        {
            _previewUntil = Time.unscaledTime + 0.6f;

            if (_previewCard != hovered)
            {
                _previewCard = hovered;
                LoadSprite(hovered, _preview);
            }
        }

        bool show = _previewCard != null && Time.unscaledTime < _previewUntil;

        if (_preview.gameObject.activeSelf != show)
        {
            _preview.gameObject.SetActive(show);
        }

        if (show)
        {
            _preview.transform.localPosition = new Vector3(_panelW * 0.5f + 0.17f, 0.05f, -0.03f);
            _preview.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
        }
    }

    CEntity_Base _previewCard;

    // CLASSIC SCREEN / VR MENU switch: in the rail (or under the player card on
    // the home screen) when a VR page is shown, under the big flat screen otherwise.
    void PlaceToggle()
    {
        bool page = _spec != null && _spec.Page != Page.None && !FlatPage;
        bool visible = page || _classic;

        if (_toggle.gameObject.activeSelf != visible)
        {
            _toggle.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        _toggleTag.text = _classic ? "VR MENU" : "CLASSIC";

        if (!_classic)
        {
            _toggle.transform.SetPositionAndRotation(_root.TransformPoint(_toggleLocal), _root.rotation);
            _toggle.transform.localScale = Vector3.one * _rootScale;
            return;
        }

        Transform screen = _rig.Screen;

        if (screen != null)
        {
            Vector3 bottom = screen.position - screen.up * (screen.lossyScale.y * 0.5f + 0.08f);
            _toggle.transform.SetPositionAndRotation(bottom, screen.rotation);
            _toggle.transform.localScale = Vector3.one * 1.4f;
        }
    }
}
#endif
