#if DCGO_VR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// === DCGO-CUSTOM:vr ===
// Text for the VR room, printed on small solid plates.
//
// 3D TextMeshPro text did not render reliably in the headset (one eye only),
// so labels are drawn with the normal UI system (TextMeshProUGUI on a canvas)
// by a hidden camera into one shared texture (the "label atlas"), the same way
// the game screen itself is drawn. Each label in the room is then just a flat
// quad showing its piece of that texture, with the same unlit material as the
// cards, so it shows in both eyes like everything else.
public class VRLabel : MonoBehaviour
{
    TextMeshProUGUI _text;
    Image _plate;
    Renderer _quad;
    RectInt _cells;
    Vector2 _size;
    int _pixelHeight;
    MaterialPropertyBlock _block;

    public static readonly Color DefaultPlate = new Color(0.03f, 0.07f, 0.11f);

    /// <summary>A label plate of the given size (metres). Its front faces -Z, like a Quad.</summary>
    public static VRLabel Create(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector2 size, string text, Color textColor, Color? plateColor = null)
        => Create(parent, localPosition, localRotation, size, text, textColor, plateColor, 0);

    /// <summary>
    /// A multi-line paragraph label: word-wrapped, top-left aligned, drawn with
    /// <paramref name="pixelHeight"/> atlas pixels of height so small text stays sharp.
    /// </summary>
    public static VRLabel CreateParagraph(Transform parent, Vector2 size, int pixelHeight, float maxFontPx, Color textColor, Color plateColor)
    {
        VRLabel label = Create(parent, Vector3.zero, Quaternion.identity, size, "", textColor, plateColor, pixelHeight);
        label._text.enableWordWrapping = true;
        label._text.alignment = TextAlignmentOptions.TopLeft;
        label._text.fontSizeMax = maxFontPx;
        label._text.richText = true;
        RectTransform textRect = label._text.rectTransform;
        textRect.offsetMin = new Vector2(10f, 6f);
        textRect.offsetMax = new Vector2(-10f, -6f);
        return label;
    }

    static VRLabel Create(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector2 size, string text, Color textColor, Color? plateColor, int pixelHeight)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Label " + text;
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localRotation = localRotation;

        VRLabel label = quad.AddComponent<VRLabel>();
        label._pixelHeight = pixelHeight;
        label._quad = quad.GetComponent<Renderer>();
        label._quad.sharedMaterial = VRLabelAtlas.Instance.Material;
        label._quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        label._block = new MaterialPropertyBlock();
        VRLabelAtlas.Instance.CreateEntry(out label._plate, out label._text);
        label._text.color = textColor;
        label._plate.color = plateColor ?? DefaultPlate;
        label.Size = size;
        label.text = text;
        return label;
    }

    public string text
    {
        get => _text.text;
        set
        {
            if (_text.text != value)
            {
                _text.text = value;
                VRLabelAtlas.MarkDirty();
            }
        }
    }

    public Color color
    {
        get => _text.color;
        set
        {
            if (_text.color != value)
            {
                _text.color = value;
                VRLabelAtlas.MarkDirty();
            }
        }
    }

    public Color plateColor
    {
        get => _plate.color;
        set
        {
            if (_plate.color != value)
            {
                _plate.color = value;
                VRLabelAtlas.MarkDirty();
            }
        }
    }

    public TextAlignmentOptions alignment
    {
        set
        {
            _text.alignment = value;
            VRLabelAtlas.MarkDirty();
        }
    }

    public bool enableWordWrapping
    {
        set
        {
            _text.enableWordWrapping = value;
            VRLabelAtlas.MarkDirty();
        }
    }

    /// <summary>Plate size in metres. The text is auto-sized to fill it.</summary>
    public Vector2 Size
    {
        get => _size;
        set
        {
            if ((value - _size).sqrMagnitude < 1e-6f)
            {
                return;
            }

            _size = value;
            transform.localScale = new Vector3(value.x, value.y, 1f);
            VRLabelAtlas atlas = VRLabelAtlas.Instance;

            if (_cells.width > 0)
            {
                atlas.Free(_cells);
            }

            int heightPx = _pixelHeight > 0 ? _pixelHeight : value.y >= 0.08f ? 128 : 64;
            RectInt pixels = atlas.Allocate(value.x / Mathf.Max(0.001f, value.y), heightPx, out _cells);
            atlas.Place(_plate, _text, pixels);
            VRLabelAtlas.MarkDirty();

            Vector4 st = new Vector4(
                pixels.width / (float)VRLabelAtlas.TextureSize,
                pixels.height / (float)VRLabelAtlas.TextureSize,
                pixels.x / (float)VRLabelAtlas.TextureSize,
                pixels.y / (float)VRLabelAtlas.TextureSize);

            _quad.GetPropertyBlock(_block);
            _block.SetVector("_BaseMap_ST", st);
            _block.SetVector("_MainTex_ST", st);
            _quad.SetPropertyBlock(_block);
        }
    }

    void OnEnable()
    {
        if (_plate != null)
        {
            _plate.gameObject.SetActive(true);
            VRLabelAtlas.MarkDirty();
        }
    }

    void OnDisable()
    {
        if (_plate != null)
        {
            _plate.gameObject.SetActive(false);
        }
    }

    void OnDestroy()
    {
        if (VRLabelAtlas.HasInstance && _cells.width > 0)
        {
            VRLabelAtlas.Instance.Free(_cells);
        }

        if (_plate != null)
        {
            Destroy(_plate.gameObject);
        }
    }
}

/// <summary>Shared texture + hidden camera/canvas that all VRLabels are drawn into.</summary>
public class VRLabelAtlas : MonoBehaviour
{
    public const int TextureSize = 4096;
    const int CellW = 256;
    const int CellH = 64;
    const int Cols = TextureSize / CellW;
    const int Rows = TextureSize / CellH;
    const int AtlasLayer = 30;

    static VRLabelAtlas _instance;

    // The atlas camera only renders while something changed (plus a slow safety
    // refresh): redrawing a 4096 texture with mipmaps every frame is expensive.
    static int _dirtyFrames = 3;
    Camera _camera;
    float _nextSafetyRender;

    public static void MarkDirty() => _dirtyFrames = 3;

    void LateUpdate()
    {
        bool render = _dirtyFrames > 0 || Time.unscaledTime >= _nextSafetyRender;

        if (render && _dirtyFrames <= 0)
        {
            _dirtyFrames = 1;
        }

        if (render)
        {
            _nextSafetyRender = Time.unscaledTime + 2f;
        }

        if (_camera != null && _camera.enabled != render)
        {
            _camera.enabled = render;
        }

        if (_dirtyFrames > 0)
        {
            _dirtyFrames--;
        }
    }
    public static bool HasInstance => _instance != null;

    public static VRLabelAtlas Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("DCGO_VR_LabelAtlas") { layer = AtlasLayer };
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<VRLabelAtlas>();
                _instance.Build();
            }

            return _instance;
        }
    }

    public Material Material { get; private set; }

    RenderTexture _texture;
    RectTransform _canvas;
    readonly bool[,] _used = new bool[Cols, Rows];

    void Build()
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.ARGB32, 0)
        {
            useMipMap = true,
            autoGenerateMips = true,
            sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
        };
        _texture = new RenderTexture(desc)
        {
            name = "DCGO_VR_Labels",
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
            wrapMode = TextureWrapMode.Clamp,
        };
        _texture.Create();

        GameObject cameraGo = new GameObject("LabelCamera") { layer = AtlasLayer };
        cameraGo.transform.SetParent(transform, false);
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = TextureSize * 0.5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = VRLabel.DefaultPlate;
        camera.cullingMask = 1 << AtlasLayer;
        camera.targetTexture = _texture;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        camera.depth = -100;
        _camera = camera;

        GameObject canvasGo = new GameObject("LabelCanvas") { layer = AtlasLayer };
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        _canvas = canvasGo.GetComponent<RectTransform>();

        Material = VRMaterials.Screen(_texture);
    }

    /// <summary>Reserves space for a label with this aspect (width / height). Returns its pixel rect.</summary>
    public RectInt Allocate(float aspect, int heightPx, out RectInt cells)
    {
        int widthPx = Mathf.Max(8, Mathf.RoundToInt(heightPx * aspect));

        if (widthPx > TextureSize)
        {
            widthPx = TextureSize;
            heightPx = Mathf.Max(16, Mathf.RoundToInt(TextureSize / aspect));
        }

        int cw = Mathf.Clamp(Mathf.CeilToInt(widthPx / (float)CellW), 1, Cols);
        int ch = Mathf.Clamp(Mathf.CeilToInt(heightPx / (float)CellH), 1, Rows);

        for (int y = 0; y <= Rows - ch; y++)
        {
            for (int x = 0; x <= Cols - cw; x++)
            {
                if (IsFree(x, y, cw, ch))
                {
                    Mark(x, y, cw, ch, true);
                    cells = new RectInt(x, y, cw, ch);
                    return new RectInt(x * CellW, y * CellH, widthPx, heightPx);
                }
            }
        }

        Debug.LogWarning("[DCGO VR] Label atlas is full.");
        cells = new RectInt(0, 0, 0, 0);
        return new RectInt(0, 0, Mathf.Min(widthPx, CellW), Mathf.Min(heightPx, CellH));
    }

    public void Free(RectInt cells)
    {
        Mark(cells.x, cells.y, cells.width, cells.height, false);
    }

    bool IsFree(int x, int y, int w, int h)
    {
        for (int i = x; i < x + w; i++)
        {
            for (int j = y; j < y + h; j++)
            {
                if (_used[i, j])
                {
                    return false;
                }
            }
        }

        return true;
    }

    void Mark(int x, int y, int w, int h, bool used)
    {
        for (int i = x; i < x + w && i < Cols; i++)
        {
            for (int j = y; j < y + h && j < Rows; j++)
            {
                _used[i, j] = used;
            }
        }
    }

    public void CreateEntry(out Image plate, out TextMeshProUGUI text)
    {
        GameObject plateGo = new GameObject("Plate", typeof(RectTransform)) { layer = AtlasLayer };
        plateGo.transform.SetParent(_canvas, false);
        plate = plateGo.AddComponent<Image>();
        plate.raycastTarget = false;

        GameObject textGo = new GameObject("Text", typeof(RectTransform)) { layer = AtlasLayer };
        textGo.transform.SetParent(plateGo.transform, false);
        text = textGo.AddComponent<TextMeshProUGUI>();

        if (VRText.Font != null)
        {
            text.font = VRText.Font;
        }

        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 6f;
        text.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(4f, 2f);
        textRect.offsetMax = new Vector2(-4f, -2f);
    }

    /// <summary>Positions a label's plate at a pixel rect of the atlas (origin bottom-left).</summary>
    public void Place(Image plate, TextMeshProUGUI text, RectInt pixels)
    {
        RectTransform rect = plate.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(pixels.x, pixels.y);
        rect.sizeDelta = new Vector2(pixels.width, pixels.height);
        text.fontSizeMax = Mathf.Min(text.fontSizeMax > 0f && text.enableWordWrapping ? text.fontSizeMax : float.MaxValue, pixels.height * 0.75f);
    }
}
#endif
