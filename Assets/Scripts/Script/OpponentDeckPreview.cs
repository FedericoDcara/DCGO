using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Result-screen entry to preview the opponent's original deck, then save it locally.
/// </summary>
public class OpponentDeckPreview : MonoBehaviour
{
    const int Columns = 7;
    const float CellW = 118f;
    const float CellH = 166f;
    const float Spacing = 10f;
    const float Pad = 8f;

    ReplayData _finishedReplay;
    DeckData _deck;
    string _opponentName;
    Font _font;
    Button _entryButton;
    GameObject _overlay;
    ScrollRect _scroll;
    Image _zoomImage;
    Text _zoomName;
    Text _status;
    Button _importButton;
    Text _importLabel;
    int _zoomVersion;
    bool _saved;

    public static void Attach(ResultObject host, ReplayData finishedReplay, Font font)
    {
        if (host == null)
        {
            return;
        }

        var preview = host.GetComponent<OpponentDeckPreview>();
        if (preview == null)
        {
            preview = host.gameObject.AddComponent<OpponentDeckPreview>();
        }

        preview.Setup(finishedReplay, font);
    }

    void OnDestroy()
    {
        ResultObject.SetHoldAutoLeave(false);
        if (_overlay != null)
        {
            Destroy(_overlay);
            _overlay = null;
        }
    }

    void Setup(ReplayData finishedReplay, Font font)
    {
        _finishedReplay = finishedReplay;
        _font = ResolveFont(font);

        bool available = TryResolve(out _deck, out _opponentName);
        if (!available)
        {
            if (_entryButton != null)
            {
                _entryButton.gameObject.SetActive(false);
            }

            return;
        }

        if (_entryButton == null)
        {
            _entryButton = CreateEntryButton();
        }

        if (_entryButton != null)
        {
            _entryButton.gameObject.SetActive(true);
        }
    }

    bool TryResolve(out DeckData deck, out string opponentName)
    {
        deck = null;
        opponentName = null;

        var manager = GManager.instance;
        if (manager == null || manager.You == null)
        {
            return false;
        }

        int opponentId = manager.You.PlayerID == 0 ? 1 : 0;
        ReplayData replay = _finishedReplay;
        var cc = ContinuousController.instance;
        if (replay == null && cc != null)
        {
            if (cc.isReplay && cc.ActiveReplay != null)
            {
                replay = cc.ActiveReplay;
            }
            else if (cc.ActiveCatchUpReplay != null)
            {
                replay = cc.ActiveCatchUpReplay;
            }
            else
            {
                replay = cc.ActiveReplay;
            }
        }

        if (replay != null)
        {
            opponentName = opponentId == 0 ? replay.player0Name : replay.player1Name;
            string code = opponentId == 0 ? replay.player0DeckCode : replay.player1DeckCode;
            if (TryParse(code, out deck))
            {
                opponentName = FallbackName(opponentName);
                return true;
            }
        }

        if (TryPhotonDeck(opponentId, out string photonCode, out string photonName) &&
            TryParse(photonCode, out deck))
        {
            if (string.IsNullOrEmpty(opponentName))
            {
                opponentName = photonName;
            }

            opponentName = FallbackName(opponentName);
            return true;
        }

        return false;
    }

    static bool TryParse(string code, out DeckData deck)
    {
        deck = null;
        if (string.IsNullOrEmpty(code) || ContinuousController.instance == null)
        {
            return false;
        }

        try
        {
            var parsed = new DeckData(code);
            if (parsed.AllDeckCards().Count == 0)
            {
                return false;
            }

            deck = parsed;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Deck] Could not read opponent deck: {ex.Message}");
            return false;
        }
    }

    static string FallbackName(string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        var manager = GManager.instance;
        if (manager != null && manager.Opponent != null && !string.IsNullOrEmpty(manager.Opponent.PlayerName))
        {
            return manager.Opponent.PlayerName;
        }

        return "Opponent";
    }

    static bool TryPhotonDeck(int seat, out string code, out string playerName)
    {
        code = null;
        playerName = null;
        if (!PhotonNetwork.InRoom || PhotonNetwork.PlayerList == null)
        {
            return false;
        }

        Photon.Realtime.Player seatPlayer = FindSeatPlayer(seat);
        if (seatPlayer == null)
        {
            return false;
        }

        code = TournamentState.ReadDeckCode(seatPlayer);
        playerName = ReadPhotonName(seatPlayer);
        return !string.IsNullOrEmpty(code);
    }

    static Photon.Realtime.Player FindSeatPlayer(int seat)
    {
        bool tournamentExtras = ContinuousController.instance != null &&
                                ContinuousController.instance.isTournament &&
                                PhotonNetwork.PlayerList != null &&
                                PhotonNetwork.PlayerList.Length > 2;

        Photon.Realtime.Player seat0;
        Photon.Realtime.Player seat1 = null;
        if (tournamentExtras)
        {
            seat0 = TournamentKeys.FindCompetitorMaster();
            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (!TournamentKeys.IsCompetitor(player))
                {
                    continue;
                }

                if (seat0 != null && player.ActorNumber == seat0.ActorNumber)
                {
                    continue;
                }

                seat1 = player;
                break;
            }
        }
        else
        {
            seat0 = PhotonNetwork.MasterClient;
            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (player != null && seat0 != null && player.ActorNumber != seat0.ActorNumber)
                {
                    seat1 = player;
                    break;
                }
            }
        }

        return seat == 0 ? seat0 : seat1;
    }

    static string ReadPhotonName(Photon.Realtime.Player player)
    {
        if (player == null)
        {
            return null;
        }

        if (player.CustomProperties != null &&
            player.CustomProperties.TryGetValue(ContinuousController.PlayerNameKey, out object value) &&
            value is string name &&
            !string.IsNullOrEmpty(name))
        {
            return name;
        }

        return string.IsNullOrEmpty(player.NickName) ? null : player.NickName;
    }

    Button CreateEntryButton()
    {
        var source = transform.Find("ViewBoardButton");
        if (source != null)
        {
            return CloneEntryButton(source);
        }

        return CreateFallbackEntryButton();
    }

    Button CloneEntryButton(Transform source)
    {
        var clone = Instantiate(source.gameObject, transform);
        clone.name = "OpponentDeckButton";

        var sourceRt = source.GetComponent<RectTransform>();
        var rt = clone.GetComponent<RectTransform>();
        rt.localScale = sourceRt.localScale;
        rt.anchorMin = sourceRt.anchorMin;
        rt.anchorMax = sourceRt.anchorMax;
        rt.pivot = sourceRt.pivot;
        rt.sizeDelta = sourceRt.sizeDelta;
        float width = sourceRt.sizeDelta.x * Mathf.Abs(sourceRt.localScale.x);
        rt.anchoredPosition = sourceRt.anchoredPosition + new Vector2(width + 24f, 0f);

        var label = clone.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = Loc("Opponent deck", "相手のデッキ");
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = 48;
        }

        var button = clone.GetComponent<Button>();
        int persistent = button.onClick.GetPersistentEventCount();
        for (int i = persistent - 1; i >= 0; i--)
        {
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OpenPreview);
        return button;
    }

    Button CreateFallbackEntryButton()
    {
        var go = new GameObject("OpponentDeckButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(350f, 132f);
        rt.anchoredPosition = new Vector2(360f, -368f);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.15f, 0.28f, 0.45f, 1f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            if (GManager.instance != null)
            {
                GManager.instance.PlayDecisionSE();
            }

            OpenPreview();
        });

        var text = CreateText(go.transform, "Label", Loc("Opponent deck", "相手のデッキ"), 36, TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform, 8f, 8f, 8f, 8f);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 16;
        text.resizeTextMaxSize = 40;
        return button;
    }

    void OpenPreview()
    {
        if (_deck == null)
        {
            return;
        }

        if (_overlay == null)
        {
            try
            {
                BuildOverlay();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Deck] Opponent deck preview failed: {ex.Message}");
                if (_overlay != null)
                {
                    Destroy(_overlay);
                    _overlay = null;
                }

                return;
            }
        }

        if (_overlay == null)
        {
            return;
        }

        _overlay.SetActive(true);
        _overlay.transform.SetAsLastSibling();
        ResultObject.SetHoldAutoLeave(true);
        StartCoroutine(ResetScroll());
    }

    void ClosePreview()
    {
        if (GManager.instance != null)
        {
            GManager.instance.PlayDecisionSE();
        }

        if (_overlay != null)
        {
            _overlay.SetActive(false);
        }

        ResultObject.SetHoldAutoLeave(false);
    }

    void BuildOverlay()
    {
        if (_font == null)
        {
            _font = ResolveFont(null);
        }

        if (_font == null || _deck == null)
        {
            Debug.LogWarning("[Deck] No UI font — cannot show opponent deck.");
            return;
        }

        _overlay = new GameObject("OpponentDeckPreviewOverlay");
        if (gameObject.scene.IsValid())
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_overlay, gameObject.scene);
        }

        var root = _overlay.AddComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var canvas = _overlay.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        _overlay.AddComponent<GraphicRaycaster>();

        var scaler = _overlay.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var dim = _overlay.AddComponent<Image>();
        dim.color = new Color(0.04f, 0.05f, 0.08f, 0.94f);
        dim.raycastTarget = true;

        string who = string.IsNullOrEmpty(_opponentName) ? "Opponent" : _opponentName;
        string title = string.IsNullOrEmpty(_deck.DeckName) ? who : who + " - " + _deck.DeckName;
        var titleText = CreateText(_overlay.transform, "Title", title, 34, TextAnchor.MiddleCenter, Color.white);
        Anchor(titleText.rectTransform, 0.04f, 0.91f, 0.96f, 0.985f);
        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 18;
        titleText.resizeTextMaxSize = 34;

        int main = _deck.DeckCards().Count;
        int eggs = _deck.DigitamaDeckCards().Count;
        var countText = CreateText(_overlay.transform, "Count", $"{main}+{eggs}/50+5", 28, TextAnchor.MiddleCenter, CountColor(_deck));
        Anchor(countText.rectTransform, 0.04f, 0.855f, 0.96f, 0.91f);

        BuildScroll();
        BuildZoom();

        _status = CreateText(_overlay.transform, "Status", "", 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 0.85f, 1f));
        Anchor(_status.rectTransform, 0.04f, 0.105f, 0.96f, 0.145f);

        CreateActionButton(
            "CloseButton",
            Loc("Close", "閉じる"),
            new Color(0.22f, 0.24f, 0.3f, 1f),
            0.04f, 0.025f, 0.48f, 0.10f,
            ClosePreview);

        _importButton = CreateActionButton(
            "ImportButton",
            Loc("Import", "インポート"),
            new Color(0.15f, 0.42f, 0.28f, 1f),
            0.52f, 0.025f, 0.96f, 0.10f,
            OnImport);
        _importLabel = _importButton.GetComponentInChildren<Text>(true);
        if (_saved)
        {
            ApplySavedVisual(null);
        }

        var stacks = Group(_deck.DeckCards());
        if (stacks.Count > 0)
        {
            ShowZoom(stacks[0].Card, stacks[0].Count);
        }
        else
        {
            var eggStacks = Group(_deck.DigitamaDeckCards());
            if (eggStacks.Count > 0)
            {
                ShowZoom(eggStacks[0].Card, eggStacks[0].Count);
            }
        }
    }

    void BuildScroll()
    {
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(_overlay.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        Anchor(scrollRt, 0.03f, 0.15f, 0.72f, 0.85f);
        scrollGo.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);

        var viewport = CreateRect("Viewport", scrollGo.transform);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);
        var contentImage = content.gameObject.AddComponent<Image>();
        contentImage.color = new Color(1f, 1f, 1f, 0f);
        contentImage.raycastTarget = true;

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        AddSection(content, Loc("Deck", "デッキ"), Group(_deck.DeckCards()));
        AddSection(content, Loc("Digi-Egg", "デジタマ"), Group(_deck.DigitamaDeckCards()));

        _scroll = scrollGo.GetComponent<ScrollRect>();
        _scroll.viewport = viewport;
        _scroll.content = content;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;
    }

    void AddSection(RectTransform content, string header, List<CardStack> stacks)
    {
        if (stacks == null || stacks.Count == 0)
        {
            return;
        }

        var headerText = CreateText(content, header + "Header", header, 24, TextAnchor.MiddleLeft, new Color(0.8f, 0.86f, 0.95f, 1f));
        var headerElement = headerText.gameObject.AddComponent<LayoutElement>();
        headerElement.preferredHeight = 36f;
        headerElement.minHeight = 36f;

        var gridGo = new GameObject(header + "Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
        gridGo.transform.SetParent(content, false);
        var grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CellW, CellH);
        grid.spacing = new Vector2(Spacing, Spacing);
        grid.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;

        for (int i = 0; i < stacks.Count; i++)
        {
            CreateCardCell(gridGo.transform, stacks[i]);
        }

        float height = GridHeight(stacks.Count);
        var element = gridGo.GetComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;
        element.flexibleWidth = 1f;
    }

    void CreateCardCell(Transform parent, CardStack stack)
    {
        var go = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = new Color(0.18f, 0.18f, 0.2f, 1f);
        image.preserveAspect = true;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        CEntity_Base card = stack.Card;
        int count = stack.Count;
        button.onClick.AddListener(() => ShowZoom(card, count));
        RequestSprite(image, card, -1);

        var badge = CreateText(go.transform, "Count", "x" + count, 40, TextAnchor.MiddleCenter, Color.white);
        var badgeRt = badge.rectTransform;
        badgeRt.anchorMin = new Vector2(1f, 0f);
        badgeRt.anchorMax = new Vector2(1f, 0f);
        badgeRt.pivot = new Vector2(1f, 0f);
        badgeRt.anchoredPosition = new Vector2(-3f, 3f);
        badgeRt.sizeDelta = new Vector2(84f, 52f);
        badge.fontStyle = FontStyle.Bold;
        badge.horizontalOverflow = HorizontalWrapMode.Overflow;
        badge.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = badge.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    void BuildZoom()
    {
        var column = CreateRect("Zoom", _overlay.transform);
        Anchor(column, 0.735f, 0.15f, 0.97f, 0.85f);
        var background = column.gameObject.AddComponent<Image>();
        background.color = new Color(0.1f, 0.11f, 0.15f, 1f);

        var imageGo = new GameObject("Art", typeof(RectTransform), typeof(Image));
        imageGo.transform.SetParent(column, false);
        _zoomImage = imageGo.GetComponent<Image>();
        _zoomImage.color = new Color(0.18f, 0.18f, 0.2f, 1f);
        _zoomImage.preserveAspect = true;
        Anchor(_zoomImage.rectTransform, 0.08f, 0.16f, 0.92f, 0.98f);

        _zoomName = CreateText(column, "ZoomName", Loc("Select a card", "カードを選択"), 22, TextAnchor.MiddleCenter, Color.white);
        Anchor(_zoomName.rectTransform, 0.04f, 0.02f, 0.96f, 0.15f);
        _zoomName.horizontalOverflow = HorizontalWrapMode.Wrap;
        _zoomName.verticalOverflow = VerticalWrapMode.Truncate;
        _zoomName.resizeTextForBestFit = true;
        _zoomName.resizeTextMinSize = 14;
        _zoomName.resizeTextMaxSize = 22;
    }

    void ShowZoom(CEntity_Base card, int count)
    {
        if (card == null || _zoomImage == null)
        {
            return;
        }

        _zoomVersion++;
        int version = _zoomVersion;
        if (_zoomName != null)
        {
            _zoomName.text = CardTitle(card) + "  x" + count;
        }

        if (card.CardSprite != null)
        {
            _zoomImage.sprite = card.CardSprite;
            _zoomImage.color = Color.white;
            return;
        }

        _zoomImage.sprite = null;
        _zoomImage.color = new Color(0.18f, 0.18f, 0.2f, 1f);
        RequestSprite(_zoomImage, card, version);
    }

    void RequestSprite(Image image, CEntity_Base card, int zoomVersion)
    {
        if (image == null || card == null)
        {
            return;
        }

        if (card.CardSprite != null)
        {
            image.sprite = card.CardSprite;
            image.color = Color.white;
            return;
        }

        StartCoroutine(LoadSpriteRoutine(image, card, zoomVersion));
    }

    IEnumerator LoadSpriteRoutine(Image image, CEntity_Base card, int zoomVersion)
    {
        if (card.CardSprite == null && !card.HasLoadStarted)
        {
            Task<Sprite> task = null;
            try
            {
                task = card.GetCardSprite();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Deck] Card image failed: {ex.Message}");
                yield break;
            }

            while (task != null && !task.IsCompleted)
            {
                yield return null;
            }
        }

        float waited = 0f;
        while (card != null && card.CardSprite == null && waited < 8f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (image == null || card == null || card.CardSprite == null)
        {
            yield break;
        }

        if (zoomVersion >= 0 && zoomVersion != _zoomVersion)
        {
            yield break;
        }

        image.sprite = card.CardSprite;
        image.color = Color.white;
    }

    IEnumerator ResetScroll()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (_scroll != null)
        {
            _scroll.verticalNormalizedPosition = 1f;
        }
    }

    void OnImport()
    {
        if (_saved || _deck == null || ContinuousController.instance == null)
        {
            return;
        }

        if (GManager.instance != null)
        {
            GManager.instance.PlayDecisionSE();
        }

        var cc = ContinuousController.instance;
        DeckData imported = null;
        bool stored = false;
        try
        {
            imported = _deck.ModifiedDeckData();
            imported.DeckName = UniqueName(_deck, _opponentName);
            if (cc.DeckDatas == null)
            {
                cc.DeckDatas = new List<DeckData>();
            }

            cc.DeckDatas.Insert(0, imported);
            stored = true;
            cc.SaveDeckData(imported);
            _saved = true;
            ApplySavedVisual(imported.DeckName);
            Debug.Log($"[Deck] Imported opponent deck as {imported.DeckName}");
        }
        catch (Exception ex)
        {
            if (stored && !_saved && imported != null && cc.DeckDatas != null)
            {
                cc.DeckDatas.Remove(imported);
            }

            Debug.LogWarning($"[Deck] Opponent deck import failed: {ex.Message}");
            if (_status != null)
            {
                _status.text = Loc("Import failed", "インポートに失敗しました");
            }
        }
    }

    void ApplySavedVisual(string deckName)
    {
        if (_importLabel != null)
        {
            _importLabel.text = Loc("Saved", "保存済み");
        }

        if (_importButton != null)
        {
            _importButton.interactable = false;
        }

        if (_status != null && !string.IsNullOrEmpty(deckName))
        {
            _status.text = Loc("Saved as ", "保存名: ") + deckName;
        }
    }

    static string UniqueName(DeckData deck, string opponentName)
    {
        string who = string.IsNullOrEmpty(opponentName) ? "Opponent" : opponentName;
        string preferred = IsGenericName(deck != null ? deck.DeckName : null)
            ? who + " deck"
            : who + " " + deck.DeckName;
        preferred = DeckData.ValidateDeckName(preferred);
        if (string.IsNullOrEmpty(preferred))
        {
            preferred = "Opponent deck";
        }

        preferred = preferred.Trim();
        if (preferred.Length > 40)
        {
            preferred = preferred.Substring(0, 40).TrimEnd();
        }

        if (string.IsNullOrEmpty(preferred))
        {
            preferred = "Opponent deck";
        }

        string name = preferred;
        int suffix = 2;
        while (NameTaken(name) && suffix < 100)
        {
            name = preferred + " " + suffix;
            suffix++;
        }

        return name;
    }

    static bool IsGenericName(string name)
    {
        return string.IsNullOrEmpty(name) ||
               name == "NewDeck" ||
               name == "新しいデッキ" ||
               name == "サンプルデッキ";
    }

    static bool NameTaken(string name)
    {
        var datas = ContinuousController.instance != null ? ContinuousController.instance.DeckDatas : null;
        if (datas == null)
        {
            return false;
        }

        for (int i = 0; i < datas.Count; i++)
        {
            if (datas[i] != null && datas[i].DeckName == name)
            {
                return true;
            }
        }

        return false;
    }

    static List<CardStack> Group(List<CEntity_Base> cards)
    {
        var stacks = new List<CardStack>();
        if (cards == null)
        {
            return stacks;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            CEntity_Base card = cards[i];
            if (card == null)
            {
                continue;
            }

            int found = -1;
            for (int j = 0; j < stacks.Count; j++)
            {
                if (SameCard(stacks[j].Card, card))
                {
                    found = j;
                    break;
                }
            }

            if (found >= 0)
            {
                stacks[found].Count++;
            }
            else
            {
                stacks.Add(new CardStack { Card = card, Count = 1 });
            }
        }

        return stacks;
    }

    static bool SameCard(CEntity_Base a, CEntity_Base b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(a.CardID) || !string.IsNullOrEmpty(b.CardID))
        {
            return a.CardID == b.CardID;
        }

        return a.CardIndex == b.CardIndex;
    }

    static string CardTitle(CEntity_Base card)
    {
        if (card == null)
        {
            return "";
        }

        bool jpn = ContinuousController.instance != null && ContinuousController.instance.language == Language.JPN;
        string name = jpn ? card.CardName_JPN : card.CardName_ENG;
        if (string.IsNullOrEmpty(name))
        {
            name = jpn ? card.CardName_ENG : card.CardName_JPN;
        }

        if (string.IsNullOrEmpty(name))
        {
            name = card.CardID;
        }

        return name ?? "";
    }

    static float GridHeight(int count)
    {
        if (count <= 0)
        {
            return 0f;
        }

        int rows = (count + Columns - 1) / Columns;
        return Pad * 2f + rows * CellH + (rows - 1) * Spacing;
    }

    static Color CountColor(DeckData deck)
    {
        if (deck != null && deck.IsValidDeckData())
        {
            return new Color32(69, 255, 69, 255);
        }

        return new Color32(255, 64, 64, 255);
    }

    Button CreateActionButton(string name, string label, Color color, float xMin, float yMin, float xMax, float yMax, UnityAction action)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_overlay.transform, false);
        Anchor(go.GetComponent<RectTransform>(), xMin, yMin, xMax, yMax);
        var image = go.GetComponent<Image>();
        image.color = color;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.85f);
        button.colors = colors;
        button.onClick.AddListener(action);

        var text = CreateText(go.transform, "Label", label, 28, TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform, 8f, 4f, 8f, 4f);
        return button;
    }

    Text CreateText(Transform parent, string name, string value, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = _font;
        text.text = value;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static void Anchor(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static Font ResolveFont(Font preferred)
    {
        if (preferred != null)
        {
            return preferred;
        }

        if (Opening.instance != null && Opening.instance.VerText != null && Opening.instance.VerText.font != null)
        {
            return Opening.instance.VerText.font;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            return font;
        }

        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null)
        {
            return font;
        }

        return Font.CreateDynamicFontFromOSFont("Arial", 24);
    }

    static string Loc(string english, string japanese)
    {
        return LocalizeUtility.GetLocalizedString(english, japanese);
    }

    class CardStack
    {
        public CEntity_Base Card;
        public int Count;
    }
}
