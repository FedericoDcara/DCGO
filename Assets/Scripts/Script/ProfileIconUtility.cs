using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Built-in + drop-in profile icons for the home name plate and match HUD.
/// Compiled sprites: Assets/Resources/ProfileIcons/ (ids: res:name).
/// Procedural fallbacks: builtin:0–7.
/// Custom PNG/JPG files: StreamingAssets/Textures/ProfileIcons/.
/// </summary>
public static class ProfileIconUtility
{
    public const string BuiltinPrefix = "builtin:";
    public const string ResourcesPrefix = "res:";
    public const string ResourcesFolder = "ProfileIcons";
    public const int BuiltinCount = 8;
    public const string DefaultId = BuiltinPrefix + "0";

    static readonly Color[] BuiltinColors =
    {
        new Color(0.22f, 0.55f, 0.95f, 1f),
        new Color(0.95f, 0.35f, 0.28f, 1f),
        new Color(0.28f, 0.78f, 0.45f, 1f),
        new Color(0.95f, 0.75f, 0.22f, 1f),
        new Color(0.66f, 0.38f, 0.92f, 1f),
        new Color(0.20f, 0.80f, 0.82f, 1f),
        new Color(0.95f, 0.50f, 0.20f, 1f),
        new Color(0.55f, 0.62f, 0.72f, 1f),
    };

    static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Sprite> _resourceSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> _resourceIds = new List<string>();
    static bool _resourcesLoaded;

    public static event Action IconChanged;

    public static bool IsBuiltin(string id)
    {
        return !string.IsNullOrEmpty(id) && id.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsResource(string id)
    {
        return !string.IsNullOrEmpty(id) && id.StartsWith(ResourcesPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static int ParseBuiltinIndex(string id)
    {
        if (!IsBuiltin(id))
            return 0;

        string tail = id.Substring(BuiltinPrefix.Length);
        if (!int.TryParse(tail, out int index))
            return 0;

        return Mathf.Clamp(index, 0, BuiltinCount - 1);
    }

    public static string BuiltinId(int index)
    {
        return BuiltinPrefix + Mathf.Clamp(index, 0, BuiltinCount - 1);
    }

    public static string ResourceId(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName))
            return DefaultId;

        spriteName = spriteName.Replace("\\", "/").Trim();
        int slash = spriteName.LastIndexOf('/');
        if (slash >= 0 && slash < spriteName.Length - 1)
            spriteName = spriteName.Substring(slash + 1);

        return ResourcesPrefix + spriteName;
    }

    public static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return DefaultId;

        id = id.Replace("\\", "/").Trim();
        if (id.Contains(".."))
            return DefaultId;

        if (id.Length > 80)
            return DefaultId;

        if (IsBuiltin(id))
            return BuiltinId(ParseBuiltinIndex(id));

        if (IsResource(id))
        {
            EnsureResourcesLoaded();
            string key = id.Substring(ResourcesPrefix.Length).Trim();
            if (string.IsNullOrEmpty(key) || key.Contains("/") || key.Contains("\\"))
                return DefaultId;

            string normalized = ResourceId(key);
            // Unknown compiled ids fall back so a stale Photon prop cannot path-walk.
            if (!_resourceSprites.ContainsKey(normalized))
                return DefaultId;

            return normalized;
        }

        return id;
    }

    public static string GetLocalId()
    {
        if (ContinuousController.instance == null)
            return DefaultId;

        return SanitizeId(ContinuousController.instance.profileIcon);
    }

    public static string ReadPhotonId(Photon.Realtime.Player photonPlayer)
    {
        if (photonPlayer?.CustomProperties != null &&
            photonPlayer.CustomProperties.TryGetValue(ContinuousController.ProfileIconKey, out object value) &&
            value is string id &&
            !string.IsNullOrEmpty(id))
        {
            return SanitizeId(id);
        }

        return DefaultId;
    }

    public static void PublishLocalToPhoton()
    {
        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.LocalPlayer == null)
            return;

        string id = GetLocalId();
        var hash = new Hashtable { { ContinuousController.ProfileIconKey, id } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    public static Sprite GetBuiltinSprite(int index)
    {
        index = Mathf.Clamp(index, 0, BuiltinCount - 1);
        string key = BuiltinId(index);
        if (_spriteCache.TryGetValue(key, out Sprite cached) && cached != null)
            return cached;

        Sprite sprite = CreatePortraitSprite(BuiltinColors[index], DrawPersonSilhouette);
        _spriteCache[key] = sprite;
        return sprite;
    }

    public static Sprite GetLetterSprite(string letter, Color color)
    {
        if (string.IsNullOrEmpty(letter))
            letter = "?";

        string key = "letter:" + letter[0] + ":" + ColorUtility.ToHtmlStringRGB(color);
        if (_spriteCache.TryGetValue(key, out Sprite cached) && cached != null)
            return cached;

        char ch = char.ToUpperInvariant(letter[0]);
        Sprite sprite = CreatePortraitSprite(color, tex => DrawLetter(tex, ch));
        _spriteCache[key] = sprite;
        return sprite;
    }

    static void EnsureResourcesLoaded()
    {
        if (_resourcesLoaded)
            return;

        _resourcesLoaded = true;
        _resourceSprites.Clear();
        _resourceIds.Clear();

        Sprite[] sprites = Resources.LoadAll<Sprite>(ResourcesFolder);
        if (sprites == null || sprites.Length == 0)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Array.Sort(sprites, (a, b) => string.Compare(a != null ? a.name : "", b != null ? b.name : "", StringComparison.OrdinalIgnoreCase));

        foreach (Sprite sprite in sprites)
        {
            if (sprite == null || string.IsNullOrEmpty(sprite.name))
                continue;

            string id = ResourceId(sprite.name);
            if (!seen.Add(id))
                continue;

            _resourceSprites[id] = sprite;
            _resourceIds.Add(id);
            _spriteCache[id] = sprite;
        }
    }

    public static List<string> ListResourceIconIds()
    {
        EnsureResourcesLoaded();
        return new List<string>(_resourceIds);
    }

    public static Sprite GetResourceSprite(string id)
    {
        EnsureResourcesLoaded();
        id = SanitizeId(id);
        if (!IsResource(id))
            return null;

        if (_resourceSprites.TryGetValue(id, out Sprite sprite) && sprite != null)
            return sprite;

        return null;
    }

    public static async Task<Sprite> GetSprite(string id)
    {
        id = SanitizeId(id);

        if (IsResource(id))
        {
            Sprite resourceSprite = GetResourceSprite(id);
            if (resourceSprite != null)
                return resourceSprite;

            return GetBuiltinSprite(0);
        }

        if (IsBuiltin(id))
            return GetBuiltinSprite(ParseBuiltinIndex(id));

        if (_spriteCache.TryGetValue(id, out Sprite cached) && cached != null)
            return cached;

        Sprite fileSprite = await StreamingAssetsUtility.GetSprite(id);
        if (fileSprite != null)
        {
            _spriteCache[id] = fileSprite;
            return fileSprite;
        }

        return GetBuiltinSprite(0);
    }

    public static void NotifyChanged()
    {
        IconChanged?.Invoke();
    }

    public static void ApplyToImage(Image image, Sprite sprite, bool preserveAspect = true)
    {
        if (image == null)
            return;

        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.enabled = true;
            image.preserveAspect = preserveAspect;
        }
    }

    public static Image EnsureHomeIconImage(PlayerInfo playerInfo)
    {
        if (playerInfo == null || playerInfo.PlayerNameInputField == null)
            return null;

        Transform nameRoot = playerInfo.PlayerNameInputField.transform.parent;
        if (nameRoot == null)
            return null;

        Transform yourIcon = FindDeepChild(nameRoot, "Your icon");
        Image image = yourIcon != null ? yourIcon.GetComponent<Image>() : null;
        if (image == null)
            return null;

        LayoutHomeIcon(image.rectTransform);
        image.gameObject.SetActive(true);
        // Clicks go through the ProfileIconButton child, not this image.
        image.raycastTarget = false;
        image.preserveAspect = true;

        // Keep icon above the name InputField so the child button wins raycasts.
        image.transform.SetAsLastSibling();

        Transform silhouette = FindDeepChild(nameRoot, "Icon");
        if (silhouette != null && silhouette != yourIcon)
            silhouette.gameObject.SetActive(false);

        return image;
    }

    public static void EnsureHomeIconButton(PlayerInfo playerInfo, UnityEngine.Events.UnityAction onClick)
    {
        if (playerInfo == null || playerInfo.PlayerNameInputField == null || onClick == null)
            return;

        Image iconImage = EnsureHomeIconImage(playerInfo);
        if (iconImage == null)
            return;

        Transform iconRoot = iconImage.transform;
        Transform existing = iconRoot.Find("ProfileIconButton");
        // Migrate an older sibling button (pre-fix) onto the icon.
        if (existing == null && iconRoot.parent != null)
        {
            Transform legacy = iconRoot.parent.Find("ProfileIconButton");
            if (legacy != null)
            {
                legacy.SetParent(iconRoot, false);
                existing = legacy;
            }
        }

        Button button;
        RectTransform buttonRt;
        if (existing == null)
        {
            var go = new GameObject("ProfileIconButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = iconRoot.gameObject.layer;
            go.transform.SetParent(iconRoot, false);
            buttonRt = go.GetComponent<RectTransform>();
            var bg = go.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0f);
            bg.raycastTarget = true;
            button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
        }
        else
        {
            button = existing.GetComponent<Button>();
            buttonRt = existing as RectTransform;
            if (existing.parent != iconRoot)
                existing.SetParent(iconRoot, false);
        }

        // Stretch to fill the visible icon — hitbox matches the portrait exactly.
        if (buttonRt != null)
        {
            buttonRt.anchorMin = Vector2.zero;
            buttonRt.anchorMax = Vector2.one;
            buttonRt.pivot = new Vector2(0.5f, 0.5f);
            buttonRt.offsetMin = Vector2.zero;
            buttonRt.offsetMax = Vector2.zero;
            buttonRt.localScale = Vector3.one;
            buttonRt.SetAsLastSibling();
        }

        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(onClick);
    }

    static void LayoutHomeIcon(RectTransform rt)
    {
        if (rt == null)
            return;

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        // Sit inside the SciFi circle frame (prefab circles are ~scale 0.55 at x≈300).
        rt.anchoredPosition = new Vector2(300f, 0f);
        rt.sizeDelta = new Vector2(96f, 96f);
    }

    public static Image EnsureMatchIconImage(Player player)
    {
        if (player == null || player.PlayerNameText == null)
            return null;

        Transform namePlate = player.PlayerNameText.transform.parent;
        if (namePlate == null)
            return null;

        Transform existing = namePlate.Find("ProfileIcon");
        Image image;
        if (existing == null)
        {
            var frame = new GameObject("ProfileIcon", typeof(RectTransform), typeof(Image), typeof(Outline));
            frame.layer = namePlate.gameObject.layer;
            frame.transform.SetParent(namePlate, false);

            var rt = frame.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            LayoutMatchIcon(rt, player.isYou);

            var frameImage = frame.GetComponent<Image>();
            frameImage.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
            frameImage.raycastTarget = false;

            var outline = frame.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portrait.layer = frame.layer;
            portrait.transform.SetParent(frame.transform, false);
            var portraitRt = portrait.GetComponent<RectTransform>();
            portraitRt.anchorMin = new Vector2(0.08f, 0.08f);
            portraitRt.anchorMax = new Vector2(0.92f, 0.92f);
            portraitRt.offsetMin = Vector2.zero;
            portraitRt.offsetMax = Vector2.zero;
            image = portrait.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = true;
        }
        else
        {
            LayoutMatchIcon(existing.GetComponent<RectTransform>(), player.isYou);
            image = existing.Find("Portrait") != null
                ? existing.Find("Portrait").GetComponent<Image>()
                : existing.GetComponent<Image>();
        }

        existing = namePlate.Find("ProfileIcon");
        if (existing != null)
            existing.gameObject.SetActive(namePlate.gameObject.activeSelf);

        NudgeOpponentSecurity(player);
        return image;
    }

    static void LayoutMatchIcon(RectTransform rt, bool isYou)
    {
        if (rt == null)
            return;

        // Local player: icon on the left of the name bar.
        // Opponent: same offset on the right so it sits beside their plate.
        // Opponent name plate is scaled ~0.7, so enlarge the rect to match on-screen size.
        rt.anchoredPosition = new Vector2(isYou ? -196f : 196f, 0f);
        rt.sizeDelta = isYou ? new Vector2(82f, 82f) : new Vector2(118f, 118f);
    }

    static void NudgeOpponentSecurity(Player player)
    {
        if (player == null || player.isYou || player.securityObject == null)
            return;

        var rt = player.securityObject.transform as RectTransform;
        if (rt == null || rt.Find("ProfileIconSecurityNudge") != null)
            return;

        var marker = new GameObject("ProfileIconSecurityNudge");
        marker.transform.SetParent(rt, false);

        Vector2 pos = rt.anchoredPosition;
        pos.x += 70f;
        rt.anchoredPosition = pos;
    }

    public static async Task ApplyHomeIcon(PlayerInfo playerInfo)
    {
        Image image = EnsureHomeIconImage(playerInfo);
        if (image == null)
            return;

        Sprite sprite = await GetSprite(GetLocalId());
        ApplyToImage(image, sprite);
    }

    public static async Task ApplyMatchIcon(Player player, string id)
    {
        if (player == null)
            return;

        Image image = EnsureMatchIconImage(player);
        if (image == null)
            return;

        player.ProfileIconId = SanitizeId(id);
        Sprite sprite = await GetSprite(player.ProfileIconId);
        ApplyToImage(image, sprite);

        Transform plate = player.PlayerNameText != null ? player.PlayerNameText.transform.parent : null;
        Transform icon = plate != null ? plate.Find("ProfileIcon") : null;
        if (icon != null && plate != null)
            icon.gameObject.SetActive(plate.gameObject.activeSelf);
    }

    public static void OpenPicker()
    {
        OptionPanel option = null;
        if (Opening.instance != null)
            option = Opening.instance.optionPanel;
        if (option == null)
            option = UnityEngine.Object.FindObjectOfType<OptionPanel>(true);

        if (option == null)
            return;

        option.OpenProfileIconPicker();
    }

    static Sprite CreatePortraitSprite(Color color, Action<Texture2D> drawForeground)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "ProfileIcon"
        };

        Color fill = color;
        Color rim = Color.Lerp(color, Color.white, 0.35f);
        Color dark = Color.Lerp(color, Color.black, 0.35f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r > 1.0f)
                    tex.SetPixel(x, y, Color.clear);
                else if (r > 0.90f)
                    tex.SetPixel(x, y, rim);
                else if (r > 0.82f)
                    tex.SetPixel(x, y, dark);
                else
                    tex.SetPixel(x, y, fill);
            }
        }

        drawForeground?.Invoke(tex);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static void DrawPersonSilhouette(Texture2D tex)
    {
        int size = tex.width;
        Color ink = new Color(1f, 1f, 1f, 0.92f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size;
                float ny = (y + 0.5f) / size;

                bool head = (nx - 0.50f) * (nx - 0.50f) / (0.13f * 0.13f) + (ny - 0.66f) * (ny - 0.66f) / (0.15f * 0.15f) <= 1f;
                bool body = (nx - 0.50f) * (nx - 0.50f) / (0.22f * 0.22f) + (ny - 0.28f) * (ny - 0.28f) / (0.20f * 0.20f) <= 1f
                            && ny < 0.46f;

                if (head || body)
                {
                    Color existing = tex.GetPixel(x, y);
                    if (existing.a > 0.2f)
                        tex.SetPixel(x, y, ink);
                }
            }
        }
    }

    static void DrawLetter(Texture2D tex, char letter)
    {
        // Simple block letter fallback — used when a remote file is missing.
        int size = tex.width;
        Color ink = new Color(1f, 1f, 1f, 0.95f);
        // Draw a thick plus-like mark for unknown glyphs; most names use A-Z via builtin portraits.
        if (letter < 'A' || letter > 'Z')
        {
            DrawPersonSilhouette(tex);
            return;
        }

        for (int y = 28; y < size - 28; y++)
        {
            for (int x = 40; x < size - 40; x++)
            {
                bool stroke = false;
                int gx = x - 40;
                int gy = y - 28;
                int w = size - 80;
                int h = size - 56;

                switch (letter)
                {
                    case 'A':
                        stroke = Mathf.Abs(gx - w / 2) < 8 && gy > h / 2
                                 || Mathf.Abs(gy - h / 2) < 6 && gx > 10 && gx < w - 10
                                 || Mathf.Abs(gx - (int)(w * (0.25f + 0.5f * gy / (float)h))) < 8
                                 || Mathf.Abs(gx - (int)(w * (0.75f - 0.5f * gy / (float)h))) < 8;
                        break;
                    default:
                        stroke = (gx < 14 || gx > w - 14 || gy < 12 || gy > h - 12) && gx > 4 && gx < w - 4;
                        break;
                }

                if (stroke)
                {
                    Color existing = tex.GetPixel(x, y);
                    if (existing.a > 0.2f)
                        tex.SetPixel(x, y, ink);
                }
            }
        }

        if (letter != 'A')
            DrawPersonSilhouette(tex);
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
}
