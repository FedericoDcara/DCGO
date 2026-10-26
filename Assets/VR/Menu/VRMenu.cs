#if DCGO_VR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// === DCGO-CUSTOM:vr ===
// Native VR menu (outside duels). It follows the hidden flat menu: several
// times a second it works out which flat screens are open, keeps the one that
// opened most recently, and shows it as VR panels, buttons and real card
// pictures. Every action goes through the flat menu's own handlers (FlatUI
// clicks or the same public methods its buttons call), so matchmaking, deck
// rules and saving are the game's own code.
//
// Pages: title, home, any dialog, battle deck choice, random lobby, rooms,
// join room, deck browser, deck card list, test draw, deck editor (cards,
// filters, deck list) and a generic mirror for every other window (options,
// sound, language, graphics, patch notes...).
public partial class VRMenu : MonoBehaviour
{
    const float Refresh = 0.2f;
    const float PanelH = 0.86f;

    static readonly Color FrameColor = new Color(0.035f, 0.06f, 0.10f);
    static readonly Color AccentColor = new Color(0.25f, 0.85f, 1f);
    static readonly Color TitleColor = new Color(1f, 0.82f, 0.25f);
    static readonly Color SelectedPlate = new Color(0.10f, 0.55f, 0.80f);
    static readonly Color WarningPlate = new Color(0.55f, 0.10f, 0.10f);
    static readonly Color LinePlate = new Color(0.07f, 0.13f, 0.20f);

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

    VRRig _rig;
    Transform _root;
    Transform _frame;
    Transform _accent;
    VRLabel _title;
    VRLabel _body;
    VRLabel _side;
    VRLabel _footer;
    VRButton3D _toggle;
    VRButton3D _preview;
    float _previewUntil;
    readonly List<GameObject> _dynamic = new List<GameObject>();
    readonly Dictionary<VRButton3D, CEntity_Base> _cardButtons = new Dictionary<VRButton3D, CEntity_Base>();
    readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();
    readonly Dictionary<string, float> _openedAt = new Dictionary<string, float>();
    readonly HashSet<string> _openNow = new HashSet<string>();
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

    /// <summary>True while a VR page replaces the flat screen.</summary>
    public bool ShowsPage => !_classic && _spec != null && _spec.Page != Page.None;

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

        _frame = Box("Frame", FrameColor);
        _accent = Box("Accent", AccentColor);

        _title = VRLabel.Create(_root, Vector3.zero, Quaternion.identity, new Vector2(1.3f, 0.065f), "", TitleColor, FrameColor);
        _body = VRLabel.CreateParagraph(_root, new Vector2(1.3f, 0.13f), 256, 44f, new Color(0.88f, 0.94f, 1f), FrameColor);
        _side = VRLabel.CreateParagraph(_root, new Vector2(0.44f, 0.30f), 384, 40f, new Color(0.88f, 0.94f, 1f), new Color(0.05f, 0.10f, 0.15f));
        _footer = VRLabel.Create(_root, Vector3.zero, Quaternion.identity, new Vector2(0.95f, 0.03f), "", new Color(0.6f, 0.75f, 0.85f), FrameColor);
        _footer.alignment = TMPro.TextAlignmentOptions.Left;

        _toggle = VRButton3D.CreatePlate(transform, Vector3.zero, new Vector2(0.26f, 0.05f), "CLASSIC SCREEN", ToggleClassic);
        _preview = VRButton3D.CreateCard(_root, Vector3.zero, new Vector2(0.25f, 0.35f), null, null);
        _preview.Interactable = false;
        Destroy(_preview.GetComponent<Collider>());
        _preview.gameObject.SetActive(false);
        _root.gameObject.SetActive(false);
        ApplyPanelSize(1.42f, PanelH);
    }

    Transform Box(string name, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = VRRig.Layer;
        Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(_root, false);
        box.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
        return box.transform;
    }

    void ApplyPanelSize(float width, float height)
    {
        _panelW = width;
        _frame.localPosition = new Vector3(0f, 0f, 0.012f);
        _frame.localScale = new Vector3(width, height, 0.01f);
        _accent.localPosition = new Vector3(0f, height * 0.5f, 0.008f);
        _accent.localScale = new Vector3(width, 0.008f, 0.012f);
        _title.Size = new Vector2(width - 0.1f, 0.065f);
        _title.transform.localPosition = new Vector3(0f, height * 0.5f - 0.055f, 0f);
        _footer.transform.localPosition = new Vector3(-width * 0.5f + 0.05f + 0.475f, -height * 0.5f + 0.035f, 0f);
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
        _side.text = _spec.Side;
        string player = ContinuousController.instance != null ? ContinuousController.instance.PlayerName : "";
        string version = Opening.instance != null && Opening.instance.VerText != null ? Opening.instance.VerText.text : "";
        _footer.text = (string.IsNullOrEmpty(player) ? "" : "Player: " + player + "     ") + version;
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

        float height = _spec.Wide ? 0.96f : PanelH;
        ApplyPanelSize(_spec.Wide ? 1.9f : 1.42f, height);
        _root.localScale = Vector3.one * (_spec.Wide ? 0.92f : 1f);
        float top = height * 0.5f;

        List<Btn> main = _spec.Buttons.FindAll(b => b.Zone == Zone.Main);
        List<Btn> side = _spec.Buttons.FindAll(b => b.Zone == Zone.Side);
        List<Btn> bar = _spec.Buttons.FindAll(b => b.Zone == Zone.Bar);
        bool hasSide = HasGrid || side.Count > 0 || !string.IsNullOrEmpty(_spec.Side) || _spec.Lines != null;

        // Body: full width on simple pages, one hint line over the grid otherwise.
        float bodyRight = hasSide ? GridRight : Right;
        float bodyH = HasGrid ? 0.06f : 0.13f;
        _body.Size = new Vector2(bodyRight - Left, bodyH);
        _body.transform.localPosition = new Vector3((Left + bodyRight) * 0.5f, top - 0.1f - bodyH * 0.5f, 0f);

        _side.gameObject.SetActive(hasSide);
        float sideTextH = _spec.Lines != null ? 0.11f : HasGrid ? 0.30f : 0.28f;
        _side.Size = new Vector2(SideWidth, sideTextH);
        _side.transform.localPosition = new Vector3(SideCenter, top - 0.1f - sideTextH * 0.5f, 0f);

        float gridTop = top - 0.1f - bodyH - 0.02f;
        float gridBottom = -height * 0.5f + (bar.Count > 0 ? 0.13f : 0.08f);

        if (_spec.Decks != null)
        {
            BuildDeckGrid(gridTop, gridBottom);
        }
        else if (_spec.Cards != null)
        {
            BuildCardGrid(gridTop, gridBottom);
        }

        // Main buttons: a centred column (left of the side panel when there is one).
        if (main.Count > 0)
        {
            float areaLeft = Left;
            float areaRight = hasSide ? GridRight : Right;
            float center = (areaLeft + areaRight) * 0.5f;
            bool twoColumns = main.Count > 5;
            int perColumn = twoColumns ? Mathf.CeilToInt(main.Count / 2f) : main.Count;
            float width = twoColumns ? Mathf.Min(0.42f, (areaRight - areaLeft) * 0.5f - 0.03f) : 0.56f;
            float startY = HasGrid ? gridTop - 0.04f : top - 0.1f - bodyH - 0.07f;

            for (int i = 0; i < main.Count; i++)
            {
                int column = twoColumns ? i / perColumn : 0;
                int row = twoColumns ? i % perColumn : i;
                float x = twoColumns ? center + (column == 0 ? -1f : 1f) * (width * 0.5f + 0.015f) : center;
                AddButton(main[i], new Vector3(x, startY - row * 0.088f, 0f), new Vector2(width, 0.072f));
            }
        }

        // Side: the deck list lines (editor), then the side buttons.
        float sideY = top - 0.1f - sideTextH - 0.03f;

        if (_spec.Lines != null)
        {
            sideY = BuildLines(sideY);
        }

        if (side.Count > 0)
        {
            int columns = side.Count > 4 ? (_spec.Wide ? 3 : 2) : 1;
            float gap = 0.012f;
            float width = (SideWidth - gap * (columns - 1)) / columns;
            float rowH = 0.066f;

            for (int i = 0; i < side.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                float x = SideCenter - SideWidth * 0.5f + width * 0.5f + column * (width + gap);
                AddButton(side[i], new Vector3(x, sideY - rowH * 0.5f - row * (rowH + 0.012f), 0f), new Vector2(width, rowH));
            }
        }

        // Bar: small buttons along the bottom of the grid area.
        float barX = Left;

        foreach (Btn button in bar)
        {
            float width = Mathf.Clamp(0.03f + button.Label.Length * 0.017f, 0.12f, 0.36f);
            AddButton(button, new Vector3(barX + width * 0.5f, -height * 0.5f + 0.095f, 0f), new Vector2(width, 0.052f));
            barX += width + 0.012f;
        }
    }

    void AddButton(Btn spec, Vector3 position, Vector2 size)
    {
        Action act = spec.Act;
        string label = spec.Label;
        VRButton3D button = VRButton3D.CreatePlate(_root, position, size, label, () =>
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
        });
        button.Interactable = spec.Enabled;
        _dynamic.Add(button.gameObject);
    }

    VRLabel AddLabel(Vector3 position, Vector2 size, string text, Color plate)
    {
        VRLabel label = VRLabel.Create(_root, position, Quaternion.identity, size, text, Color.white, plate);
        _dynamic.Add(label.gameObject);
        return label;
    }

    void AddGlow(Vector3 position, Vector2 size)
    {
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        glow.name = "SelectedGlow";
        glow.layer = VRRig.Layer;
        Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(_root, false);
        glow.transform.localPosition = position + new Vector3(0f, 0f, 0.006f);
        glow.transform.localScale = new Vector3(size.x + 0.014f, size.y + 0.014f, 0.004f);
        glow.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(AccentColor);
        _dynamic.Add(glow);
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
            AddLabel(new Vector3((Left + GridRight) * 0.5f, (gridTop + gridBottom) * 0.5f, 0f), new Vector2(areaW - 0.1f, 0.05f), "No decks yet.", FrameColor);
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

            bool selected = deck == _spec.Selected;

            if (selected)
            {
                AddGlow(position, new Vector2(cardW, cardH));
            }

            Color plate = selected ? SelectedPlate : deck.IsValidDeckData() ? LinePlate : WarningPlate;
            AddLabel(position + new Vector3(0f, -cardH * 0.5f - 0.02f, 0f), new Vector2(cellW - 0.01f, 0.03f), deck.DeckName, plate);
        }

        if (pages > 1)
        {
            AddPager(ref _deckPage, pages, gridBottom - 0.035f);
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
            AddLabel(new Vector3((Left + GridRight) * 0.5f, (gridTop + gridBottom) * 0.5f, 0f), new Vector2(areaW - 0.1f, 0.05f), "No cards to show.", FrameColor);
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

            if (!string.IsNullOrEmpty(item.Note))
            {
                AddLabel(position + new Vector3(0f, -cardH * 0.5f - 0.016f, 0f), new Vector2(cellW - 0.01f, 0.026f), item.Note, item.Dim ? WarningPlate : LinePlate);
            }
        }

        if (pages > 1)
        {
            AddPager(ref _cardPage, pages, gridBottom - 0.01f);
        }
    }

    void AddPager(ref int page, int pages, float y)
    {
        bool deckPager = _spec.Decks != null;
        int current = page;
        float x = Left + 0.1f;
        AddButton(new Btn
        {
            Label = "< PREV",
            Enabled = current > 0,
            Act = () => { if (deckPager) { _deckPage--; } else { _cardPage--; } },
        }, new Vector3(x, y, 0f), new Vector2(0.18f, 0.045f));
        AddLabel(new Vector3(x + 0.17f, y, 0f), new Vector2(0.13f, 0.04f), $"{current + 1} / {pages}", FrameColor);
        AddButton(new Btn
        {
            Label = "NEXT >",
            Enabled = current < pages - 1,
            Act = () => { if (deckPager) { _deckPage++; } else { _cardPage++; } },
        }, new Vector3(x + 0.34f, y, 0f), new Vector2(0.18f, 0.045f));
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
            AddButton(lines[index], new Vector3(x, y - rowH * 0.5f - row * (rowH + 0.003f), 0f), new Vector2(columnW, rowH));
        }

        float bottom = y - perColumn * (rowH + 0.003f);

        if (pages > 1)
        {
            int current = _linePage;
            AddButton(new Btn { Label = "< MORE", Enabled = current > 0, Act = () => _linePage-- }, new Vector3(SideCenter - 0.12f, bottom - 0.022f, 0f), new Vector2(0.16f, 0.036f));
            AddLabel(new Vector3(SideCenter, bottom - 0.022f, 0f), new Vector2(0.07f, 0.03f), $"{current + 1}/{pages}", FrameColor);
            AddButton(new Btn { Label = "MORE >", Enabled = current < pages - 1, Act = () => _linePage++ }, new Vector3(SideCenter + 0.12f, bottom - 0.022f, 0f), new Vector2(0.16f, 0.036f));
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

    // CLASSIC SCREEN / VR MENU toggle: on the panel when a VR page is shown,
    // under the big flat screen otherwise.
    void PlaceToggle()
    {
        bool page = _spec != null && _spec.Page != Page.None;
        bool visible = page || _classic;

        if (_toggle.gameObject.activeSelf != visible)
        {
            _toggle.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        _toggle.SetLabel(_classic ? "VR MENU" : "CLASSIC SCREEN");

        if (!_classic)
        {
            float height = _spec.Wide ? 0.96f : PanelH;
            _toggle.transform.SetPositionAndRotation(_root.TransformPoint(new Vector3(_panelW * 0.5f - 0.18f, -height * 0.5f + 0.035f, 0f)), _root.rotation);
            _toggle.transform.localScale = Vector3.one * _root.localScale.x;
            return;
        }

        Transform screen = _rig.Screen;

        if (screen != null)
        {
            Vector3 bottom = screen.position - screen.up * (screen.lossyScale.y * 0.5f + 0.06f);
            _toggle.transform.SetPositionAndRotation(bottom, screen.rotation);
            _toggle.transform.localScale = Vector3.one * 1.4f;
        }
    }
}
#endif
