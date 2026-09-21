using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime Options row + gallery for playmat / sleeve selection.
/// Clones the Graphics options button so BattleScene / Opening need no YAML edits.
/// </summary>
public class PlayAreaOptionPanel : MonoBehaviour
{
    OptionPanel _optionPanel;
    GameObject _menuRow;
    GameObject _panelRoot;
    Transform _galleryContent;
    Text _titleLabel;
    Text _tabPlaymatLabel;
    Text _tabPlaymatOpponentLabel;
    Text _tabSleeveMainLabel;
    Text _tabSleeveEggLabel;
    Text _tabProfileIconLabel;
    Text _sectionLabel;
    readonly List<GameObject> _thumbRows = new List<GameObject>();
    readonly List<string> _thumbTextureNames = new List<string>();
    bool _built;
    bool _open;
    int _galleryRefreshVersion;
    CosmeticSection _activeSection = CosmeticSection.Playmat;
    CosmeticSection _displayedSection = (CosmeticSection)(-1);

    public bool IsOpen => _open && _panelRoot != null && _panelRoot.activeSelf;

    enum CosmeticSection
    {
        Playmat,
        PlaymatOpponent,
        SleeveMain,
        SleeveEgg,
        ProfileIcon
    }

    public static PlayAreaOptionPanel EnsureExists(OptionPanel optionPanel)
    {
        if (optionPanel == null)
            return null;

        PlayAreaOptionPanel existing = optionPanel.GetComponentInChildren<PlayAreaOptionPanel>(true);
        // Host is a sibling of OptionPanel, so children-only search misses it.
        if (existing == null && optionPanel.transform.parent != null)
            existing = optionPanel.transform.parent.GetComponentInChildren<PlayAreaOptionPanel>(true);
        if (existing == null)
            existing = FindObjectOfType<PlayAreaOptionPanel>(true);

        if (existing != null)
        {
            existing._optionPanel = optionPanel;
            existing.EnsureBuilt();
            existing.RemoveDuplicateMenuRows();
            return existing;
        }

        // Prefer a sibling host under the same canvas parent as OptionPanel (like GraphicsPanel).
        Transform hostParent = optionPanel.transform.parent != null
            ? optionPanel.transform.parent
            : optionPanel.transform;

        var host = new GameObject("PlayAreaOptionPanelHost");
        host.transform.SetParent(hostParent, false);
        var panel = host.AddComponent<PlayAreaOptionPanel>();
        panel._optionPanel = optionPanel;
        panel.EnsureBuilt();
        return panel;
    }

    public void Init()
    {
        EnsureBuilt();
        Off();
    }

    public void Off()
    {
        _open = false;
        _displayedSection = (CosmeticSection)(-1);
        _galleryRefreshVersion++;
        if (_panelRoot != null)
            _panelRoot.SetActive(false);
    }

    public void Close_(bool playSE)
    {
        if (playSE && _open)
            PlayCancelSE();

        Off();
    }

    public void Open()
    {
        EnsureBuilt();
        if (_panelRoot == null)
            return;

        // Closing sibling option panels so Graphics / Window Size do not stay open underneath.
        CloseSiblingOptionPanels();

        if (ContinuousController.instance != null)
            ContinuousController.instance.LoadPlayAreaCosmetics();

        ApplyLocalizedLabels();

        _open = true;
        _panelRoot.SetActive(true);
        _panelRoot.transform.SetAsLastSibling();
        transform.SetAsLastSibling();
        _activeSection = CosmeticSection.Playmat;
        _ = RefreshGalleryAsync();
    }

    public void OpenProfileIcons()
    {
        EnsureBuilt();
        if (_panelRoot == null)
            return;

        CloseSiblingOptionPanels();

        if (ContinuousController.instance != null)
            ContinuousController.instance.LoadPlayAreaCosmetics();

        ApplyLocalizedLabels();

        _open = true;
        _panelRoot.SetActive(true);
        _panelRoot.transform.SetAsLastSibling();
        transform.SetAsLastSibling();
        _activeSection = CosmeticSection.ProfileIcon;
        _ = RefreshGalleryAsync();
    }

    void ApplyLocalizedLabels()
    {
        if (_titleLabel != null)
        {
            _titleLabel.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Play Area",
                JpnMessage: "プレイエリア");
        }

        if (_tabPlaymatLabel != null)
        {
            _tabPlaymatLabel.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Your Mat",
                JpnMessage: "自分のマット");
        }

        if (_tabPlaymatOpponentLabel != null)
        {
            _tabPlaymatOpponentLabel.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Enemy Mat",
                JpnMessage: "相手のマット");
        }

        if (_tabSleeveMainLabel != null)
        {
            _tabSleeveMainLabel.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Main Sleeves",
                JpnMessage: "メインスリーブ");
        }

        if (_tabSleeveEggLabel != null)
        {
            _tabSleeveEggLabel.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Egg Sleeves",
                JpnMessage: "デジタマスリーブ");
        }

        if (_tabProfileIconLabel != null)
        {
            _tabProfileIconLabel.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "Icon",
                JpnMessage: "アイコン");
        }
    }

    void CloseSiblingOptionPanels()
    {
        if (_optionPanel == null)
            return;

        GraphicsOptionPanel graphics = _optionPanel.GetComponentInChildren<GraphicsOptionPanel>(true);
        if (graphics == null)
            graphics = FindObjectOfType<GraphicsOptionPanel>(true);
        if (graphics != null)
            graphics.Off();

        ResizeWindow resize = _optionPanel.GetComponentInChildren<ResizeWindow>(true);
        if (resize == null)
            resize = FindObjectOfType<ResizeWindow>(true);
        if (resize != null)
            resize.Off();

        GameplayOption gameplay = _optionPanel.GetComponentInChildren<GameplayOption>(true);
        if (gameplay != null)
            gameplay.Off();

        VolumePanel volume = _optionPanel.GetComponentInChildren<VolumePanel>(true);
        if (volume != null)
            volume.Off();

        LanguagePanel language = _optionPanel.GetComponentInChildren<LanguagePanel>(true);
        if (language != null)
            language.Off();

        ServerRegionPanel server = _optionPanel.GetComponentInChildren<ServerRegionPanel>(true);
        if (server != null)
            server.Off();
    }

    void EnsureBuilt()
    {
        if (_built)
            return;

        _built = true;
        InjectMenuRow();
        BuildPanel();
        Off();
    }

    void InjectMenuRow()
    {
        if (_optionPanel == null || _menuRow != null)
            return;

        Transform options = FindDeepChild(_optionPanel.transform, "Options");
        if (options == null)
            return;

        Transform existingRow = options.Find("PlayArea");
        if (existingRow != null)
        {
            _menuRow = existingRow.gameObject;
            BindMenuRowButtons();
            RemoveDuplicateMenuRows();
            return;
        }

        Transform graphics = options.Find("Graphics");
        if (graphics == null)
            return;

        _menuRow = Instantiate(graphics.gameObject, options);
        _menuRow.name = "PlayArea";
        _menuRow.SetActive(true);

        string eng = "Play Area";
        string jpn = "プレイエリア";

        foreach (LocalizeTMPro localize in _menuRow.GetComponentsInChildren<LocalizeTMPro>(true))
        {
            if (string.Equals(localize._text_ENG, "Graphics", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(localize._text_ENG))
            {
                localize._text_ENG = eng;
                localize._text_JPN = jpn;
            }
        }

        foreach (Text text in _menuRow.GetComponentsInChildren<Text>(true))
        {
            if (string.Equals(text.text, "Graphics", StringComparison.OrdinalIgnoreCase)
                || text.gameObject.name.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                text.text = LocalizeUtility.GetLocalizedString(EngMessage: eng, JpnMessage: jpn);
            }
        }

        foreach (TMPro.TMP_Text tmp in _menuRow.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            if (string.Equals(tmp.text, "Graphics", StringComparison.OrdinalIgnoreCase)
                || tmp.gameObject.name.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tmp.text = LocalizeUtility.GetLocalizedString(EngMessage: eng, JpnMessage: jpn);
            }
        }

        BindMenuRowButtons();
        RemoveDuplicateMenuRows();
    }

    void BindMenuRowButtons()
    {
        if (_menuRow == null)
            return;

        foreach (Button button in _menuRow.GetComponentsInChildren<Button>(true))
        {
            // Persistent (Inspector) listeners survive RemoveAllListeners — replace the event.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Open);
        }
    }

    void RemoveDuplicateMenuRows()
    {
        if (_optionPanel == null)
            return;

        Transform options = FindDeepChild(_optionPanel.transform, "Options");
        if (options == null)
            return;

        if (_menuRow == null)
        {
            Transform first = options.Find("PlayArea");
            if (first != null)
                _menuRow = first.gameObject;
        }

        for (int i = options.childCount - 1; i >= 0; i--)
        {
            Transform child = options.GetChild(i);
            if (child.name == "PlayArea" && child.gameObject != _menuRow)
                Destroy(child.gameObject);
        }

        Transform hostParent = _optionPanel.transform.parent != null
            ? _optionPanel.transform.parent
            : _optionPanel.transform;
        PlayAreaOptionPanel[] hosts = hostParent.GetComponentsInChildren<PlayAreaOptionPanel>(true);
        for (int i = 0; i < hosts.Length; i++)
        {
            if (hosts[i] != null && hosts[i] != this)
                Destroy(hosts[i].gameObject);
        }
    }

    void BuildPanel()
    {
        if (_panelRoot != null)
            return;

        Font font = ResolveFont();

        _panelRoot = new GameObject("PlayAreaPanel", typeof(RectTransform), typeof(Image));
        _panelRoot.transform.SetParent(transform, false);
        var rootRt = _panelRoot.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = new Vector2(1100f, 780f);
        rootRt.anchoredPosition = Vector2.zero;
        // Sit above Graphics / ResizeWindow panels.
        rootRt.SetAsLastSibling();
        _panelRoot.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.16f, 0.96f);

        _titleLabel = CreateText(_panelRoot.transform, "Title", font, 28, TextAnchor.MiddleLeft,
            new Vector2(28f, -28f), new Vector2(480f, 40f));

        CreateButton(_panelRoot.transform, "Close", font, "X",
            new Vector2(1020f, -22f), new Vector2(52f, 48f), () => Close_(true));

        _tabPlaymatLabel = CreateButton(_panelRoot.transform, "TabPlaymat", font, "Your Mat",
            new Vector2(28f, -90f), new Vector2(168f, 48f), () => SwitchSection(CosmeticSection.Playmat));

        _tabPlaymatOpponentLabel = CreateButton(_panelRoot.transform, "TabPlaymatOpponent", font, "Enemy Mat",
            new Vector2(208f, -90f), new Vector2(168f, 48f), () => SwitchSection(CosmeticSection.PlaymatOpponent));

        _tabSleeveMainLabel = CreateButton(_panelRoot.transform, "TabSleeveMain", font, "Main Sleeves",
            new Vector2(388f, -90f), new Vector2(188f, 48f), () => SwitchSection(CosmeticSection.SleeveMain));

        _tabSleeveEggLabel = CreateButton(_panelRoot.transform, "TabSleeveEgg", font, "Egg Sleeves",
            new Vector2(588f, -90f), new Vector2(188f, 48f), () => SwitchSection(CosmeticSection.SleeveEgg));

        _tabProfileIconLabel = CreateButton(_panelRoot.transform, "TabProfileIcon", font, "Icon",
            new Vector2(788f, -90f), new Vector2(140f, 48f), () => SwitchSection(CosmeticSection.ProfileIcon));

        _sectionLabel = CreateText(_panelRoot.transform, "Section", font, 18, TextAnchor.MiddleLeft,
            new Vector2(28f, -148f), new Vector2(900f, 28f));

        ApplyLocalizedLabels();

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(_panelRoot.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.offsetMin = new Vector2(24f, 24f);
        scrollRt.offsetMax = new Vector2(-24f, -190f);
        scrollGo.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.1f, 0.9f);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        var grid = contentGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(160f, 220f);
        grid.spacing = new Vector2(16f, 16f);
        grid.padding = new RectOffset(16, 16, 16, 16);
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;

        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = viewportRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        _galleryContent = contentGo.transform;
    }

    void SwitchSection(CosmeticSection section)
    {
        PlayDecisionSE();

        // Same tab already showing — do not rebuild.
        if (_activeSection == section
            && _displayedSection == section
            && _thumbRows.Count > 0
            && _open)
        {
            return;
        }

        _activeSection = section;
        _ = RefreshGalleryAsync();
    }

    void ClearGalleryThumbs()
    {
        foreach (GameObject go in _thumbRows)
        {
            if (go != null)
                Destroy(go);
        }
        _thumbRows.Clear();
        _thumbTextureNames.Clear();
    }

    async Task RefreshGalleryAsync()
    {
        if (_galleryContent == null || ContinuousController.instance == null)
            return;

        int version = ++_galleryRefreshVersion;
        CosmeticSection section = _activeSection;

        ClearGalleryThumbs();

        string subfolder;
        string defaultName;
        string selected;
        string sectionTitle;

        switch (section)
        {
            case CosmeticSection.ProfileIcon:
                subfolder = StreamingAssetsUtility.ProfileIconsFolder;
                defaultName = ContinuousController.DefaultProfileIcon;
                selected = ContinuousController.instance.profileIcon;
                sectionTitle = LocalizeUtility.GetLocalizedString(
                    EngMessage: "Select your profile icon",
                    JpnMessage: "プロフィールアイコンを選択");
                break;
            case CosmeticSection.SleeveMain:
                subfolder = StreamingAssetsUtility.SleevesMainFolder;
                defaultName = ContinuousController.DefaultSleeveMain;
                selected = ContinuousController.instance.sleeveMain;
                sectionTitle = LocalizeUtility.GetLocalizedString(
                    EngMessage: "Select main deck sleeves",
                    JpnMessage: "メインデッキのスリーブを選択");
                break;
            case CosmeticSection.SleeveEgg:
                subfolder = StreamingAssetsUtility.SleevesEggFolder;
                defaultName = ContinuousController.DefaultSleeveEgg;
                selected = ContinuousController.instance.sleeveEgg;
                sectionTitle = LocalizeUtility.GetLocalizedString(
                    EngMessage: "Select Digi-Egg sleeves",
                    JpnMessage: "デジタマスリーブを選択");
                break;
            case CosmeticSection.PlaymatOpponent:
                subfolder = StreamingAssetsUtility.PlayMatsFolder;
                defaultName = ContinuousController.DefaultPlayMatOpponent;
                selected = ContinuousController.instance.playMatOpponent;
                sectionTitle = LocalizeUtility.GetLocalizedString(
                    EngMessage: "Select enemy playmat",
                    JpnMessage: "相手のプレイマットを選択");
                break;
            default:
                subfolder = StreamingAssetsUtility.PlayMatsFolder;
                defaultName = ContinuousController.DefaultPlayMatYou;
                selected = ContinuousController.instance.playMatYou;
                sectionTitle = LocalizeUtility.GetLocalizedString(
                    EngMessage: "Select your playmat",
                    JpnMessage: "自分のプレイマットを選択");
                break;
        }

        if (_sectionLabel != null)
            _sectionLabel.text = sectionTitle;

        if (version != _galleryRefreshVersion)
            return;

        List<string> names;
        if (section == CosmeticSection.ProfileIcon)
        {
            names = new List<string>();

            // 1) Compiled Resources icons
            foreach (string resourceId in ProfileIconUtility.ListResourceIconIds())
                EnsureNameInList(names, resourceId);

            // 2) Procedural colored fallbacks
            for (int i = 0; i < ProfileIconUtility.BuiltinCount; i++)
                names.Add(ProfileIconUtility.BuiltinId(i));

            // 3) Custom StreamingAssets drop-ins
            List<string> files = await StreamingAssetsUtility.ListCosmeticTextureNames(subfolder, null);
            if (version != _galleryRefreshVersion)
                return;

            foreach (string file in files)
            {
                if (string.IsNullOrEmpty(file)
                    || ProfileIconUtility.IsBuiltin(file)
                    || ProfileIconUtility.IsResource(file))
                    continue;
                EnsureNameInList(names, file);
            }
        }
        else
        {
            names = await StreamingAssetsUtility.ListCosmeticTextureNames(subfolder, defaultName);
            if (version != _galleryRefreshVersion)
                return;
        }

        // Both default mats appear in either playmat gallery (basename-deduped).
        if (section == CosmeticSection.Playmat || section == CosmeticSection.PlaymatOpponent)
        {
            EnsureNameInList(names, ContinuousController.DefaultPlayMatYou);
            EnsureNameInList(names, ContinuousController.DefaultPlayMatOpponent);
        }

        Font font = ResolveFont();

        foreach (string name in names)
        {
            if (version != _galleryRefreshVersion)
                return;

            string textureName = name;
            bool isSelected = string.Equals(textureName, selected, StringComparison.OrdinalIgnoreCase);
            GameObject thumb = CreateThumbnail(textureName, font, isSelected);

            Sprite sprite = section == CosmeticSection.ProfileIcon
                ? await ProfileIconUtility.GetSprite(textureName)
                : await StreamingAssetsUtility.GetSprite(textureName);

            if (version != _galleryRefreshVersion)
            {
                if (thumb != null)
                    Destroy(thumb);
                return;
            }

            if (thumb == null)
                continue;

            _thumbRows.Add(thumb);
            _thumbTextureNames.Add(textureName);

            Image image = thumb.transform.Find("Preview")?.GetComponent<Image>();
            if (image != null && sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;
            }
        }

        if (version == _galleryRefreshVersion)
            _displayedSection = section;
    }

    void UpdateSelectionHighlight(string selectedTextureName)
    {
        Color selectedColor = new Color(0.2f, 0.55f, 0.95f, 1f);
        Color normalColor = new Color(0.14f, 0.16f, 0.22f, 1f);

        for (int i = 0; i < _thumbRows.Count; i++)
        {
            GameObject thumb = _thumbRows[i];
            if (thumb == null)
                continue;

            Image bg = thumb.GetComponent<Image>();
            if (bg == null)
                continue;

            bool isSelected = i < _thumbTextureNames.Count
                && string.Equals(_thumbTextureNames[i], selectedTextureName, StringComparison.OrdinalIgnoreCase);
            bg.color = isSelected ? selectedColor : normalColor;
        }
    }

    GameObject CreateThumbnail(string textureName, Font font, bool isSelected)
    {
        var go = new GameObject("Thumb_" + textureName.Replace('/', '_'),
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(_galleryContent, false);
        var bg = go.GetComponent<Image>();
        bg.color = isSelected
            ? new Color(0.2f, 0.55f, 0.95f, 1f)
            : new Color(0.14f, 0.16f, 0.22f, 1f);

        var button = go.GetComponent<Button>();
        string captured = textureName;
        button.onClick.AddListener(() => _ = OnSelectTextureAsync(captured));

        var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(Image));
        previewGo.transform.SetParent(go.transform, false);
        var previewRt = previewGo.GetComponent<RectTransform>();
        previewRt.anchorMin = new Vector2(0.08f, 0.28f);
        previewRt.anchorMax = new Vector2(0.92f, 0.92f);
        previewRt.offsetMin = Vector2.zero;
        previewRt.offsetMax = Vector2.zero;
        var previewImage = previewGo.GetComponent<Image>();
        previewImage.color = new Color(0.3f, 0.3f, 0.35f, 1f);
        previewImage.raycastTarget = false;

        string label = textureName;
        if (ProfileIconUtility.IsBuiltin(textureName))
        {
            label = LocalizeUtility.GetLocalizedString(
                EngMessage: "Icon " + (ProfileIconUtility.ParseBuiltinIndex(textureName) + 1),
                JpnMessage: "アイコン" + (ProfileIconUtility.ParseBuiltinIndex(textureName) + 1));
        }
        else if (ProfileIconUtility.IsResource(textureName))
        {
            label = textureName.Substring(ProfileIconUtility.ResourcesPrefix.Length);
        }
        else
        {
            int slash = label.LastIndexOf('/');
            if (slash >= 0 && slash < label.Length - 1)
                label = label.Substring(slash + 1);
        }

        var labelGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.05f, 0.02f);
        labelRt.anchorMax = new Vector2(0.95f, 0.26f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var labelText = labelGo.GetComponent<Text>();
        labelText.font = font;
        labelText.fontSize = 14;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = Color.white;
        labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
        labelText.verticalOverflow = VerticalWrapMode.Truncate;
        labelText.raycastTarget = false;
        labelText.text = label;

        return go;
    }

    async Task OnSelectTextureAsync(string textureName)
    {
        if (ContinuousController.instance == null)
            return;

        PlayDecisionSE();

        switch (_activeSection)
        {
            case CosmeticSection.ProfileIcon:
                await ContinuousController.instance.SetProfileIcon(textureName);
                break;
            case CosmeticSection.SleeveMain:
                await ContinuousController.instance.SetSleeveMain(textureName);
                break;
            case CosmeticSection.SleeveEgg:
                await ContinuousController.instance.SetSleeveEgg(textureName);
                break;
            case CosmeticSection.PlaymatOpponent:
                await ContinuousController.instance.SetPlayMatOpponent(textureName);
                break;
            default:
                await ContinuousController.instance.SetPlayMatYou(textureName);
                break;
        }

        UpdateSelectionHighlight(textureName);
    }

    static void EnsureNameInList(List<string> names, string textureName)
    {
        if (names == null || string.IsNullOrEmpty(textureName))
            return;

        string key = TextureBasename(textureName);
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(TextureBasename(names[i]), key, StringComparison.OrdinalIgnoreCase))
                return;
        }

        names.Add(textureName);
    }

    static string TextureBasename(string textureName)
    {
        if (string.IsNullOrEmpty(textureName))
            return textureName;

        textureName = textureName.Replace("\\", "/");
        int slash = textureName.LastIndexOf('/');
        return slash >= 0 && slash < textureName.Length - 1
            ? textureName.Substring(slash + 1)
            : textureName;
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null)
            return null;

        if (parent.name == name)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static Font ResolveFont()
    {
        if (Opening.instance != null && Opening.instance.VerText != null && Opening.instance.VerText.font != null)
            return Opening.instance.VerText.font;

        if (GManager.instance != null)
        {
            Text any = GManager.instance.GetComponentInChildren<Text>(true);
            if (any != null && any.font != null)
                return any.font;
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    static Text CreateText(Transform parent, string name, Font font, int size, TextAnchor anchor,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    static Text CreateButton(Transform parent, string name, Font font, string label,
        Vector2 anchoredPos, Vector2 sizeDelta, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        go.GetComponent<Image>().color = new Color(0.18f, 0.42f, 0.85f, 0.95f);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var text = textGo.GetComponent<Text>();
        text.font = font;
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = label;
        return text;
    }

    static void PlayDecisionSE()
    {
        if (Opening.instance != null)
            Opening.instance.PlayDecisionSE();
        else if (GManager.instance != null)
            GManager.instance.PlayDecisionSE();
    }

    static void PlayCancelSE()
    {
        if (Opening.instance != null)
            Opening.instance.PlayCancelSE();
        else if (GManager.instance != null)
            GManager.instance.PlayCancelSE();
    }
}
