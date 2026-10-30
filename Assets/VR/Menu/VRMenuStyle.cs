#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Look of the native VR menu: a "floating castle" style system UI. White
// rounded panels with a thin grey edge, dark grey text, amber highlights, and
// round icon buttons in a vertical column.
//
// Everything is built for cheap rendering: shared materials (one per colour),
// cached procedural meshes, icons drawn once into small textures, and text on
// see-through label plates so hover effects only change colours on the mesh
// and never redraw the label atlas.
public static class VRMenuStyle
{
    // ---------------------------------------------------------------- palette

    public static readonly Color Panel = new Color(0.925f, 0.935f, 0.950f);
    public static readonly Color Header = new Color(0.985f, 0.988f, 0.992f);
    public static readonly Color Edge = new Color(0.62f, 0.67f, 0.74f);
    public static readonly Color Well = new Color(0.855f, 0.875f, 0.900f);
    public static readonly Color Hairline = new Color(0.78f, 0.81f, 0.85f);
    public static readonly Color Ink = new Color(0.20f, 0.22f, 0.26f);
    public static readonly Color InkSoft = new Color(0.46f, 0.49f, 0.54f);
    public static readonly Color Accent = new Color(0.976f, 0.678f, 0.192f);
    public static readonly Color AccentDeep = new Color(0.902f, 0.498f, 0.086f);
    public static readonly Color AccentSoft = new Color(1.000f, 0.882f, 0.690f);
    public static readonly Color Item = Color.white;
    public static readonly Color ItemOff = new Color(0.835f, 0.850f, 0.870f);
    public static readonly Color Danger = new Color(0.80f, 0.27f, 0.25f);
    public static readonly Color Good = new Color(0.16f, 0.58f, 0.32f);
    public static readonly Color Halo = new Color(0.80f, 0.92f, 1f, 0.10f);
    public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

    // ---------------------------------------------------------------- materials (shared)

    static readonly Dictionary<Color, Material> _solid = new Dictionary<Color, Material>();
    static readonly Dictionary<Color, Material> _glass = new Dictionary<Color, Material>();
    static readonly Dictionary<Icon, Material> _iconMaterials = new Dictionary<Icon, Material>();

    public static Material Solid(Color color)
    {
        if (!_solid.TryGetValue(color, out Material material) || material == null)
        {
            material = VRMaterials.Unlit(color);
            material.name = "DCGOVR_Menu " + ColorUtility.ToHtmlStringRGB(color);
            _solid[color] = material;
        }

        return material;
    }

    public static Material Glass(Color color)
    {
        if (!_glass.TryGetValue(color, out Material material) || material == null)
        {
            material = VRMaterials.Transparent(color);
            material.renderQueue = 3000;
            _glass[color] = material;
        }

        return material;
    }

    public static Material IconMaterial(Icon icon)
    {
        if (!_iconMaterials.TryGetValue(icon, out Material material) || material == null)
        {
            material = VRMaterials.Transparent(Color.white);
            Texture2D texture = IconTexture(icon);
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.renderQueue = 3001;
            _iconMaterials[icon] = material;
        }

        return material;
    }

    // ---------------------------------------------------------------- meshes (cached)

    static readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();

    /// <summary>Filled rounded rectangle centred on the origin, front facing -Z (like a Quad).</summary>
    public static Mesh RoundedRect(float width, float height, float radius)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);
        string key = $"rr{width:F4}x{height:F4}r{radius:F4}";

        if (_meshes.TryGetValue(key, out Mesh cached) && cached != null)
        {
            return cached;
        }

        List<Vector3> outline = Perimeter(width, height, radius);
        Vector3[] vertices = new Vector3[outline.Count + 1];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[outline.Count * 3];
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < outline.Count; i++)
        {
            vertices[i + 1] = outline[i];
            uvs[i + 1] = new Vector2(outline[i].x / width + 0.5f, outline[i].y / height + 0.5f);
            int next = (i + 1) % outline.Count;
            // Perimeter runs counter-clockwise; Unity front faces are clockwise.
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = next + 1;
            triangles[i * 3 + 2] = i + 1;
        }

        Mesh mesh = new Mesh { name = key, vertices = vertices, uv = uvs, triangles = triangles };
        mesh.RecalculateBounds();
        _meshes[key] = mesh;
        return mesh;
    }

    public static Mesh Disc(float diameter) => RoundedRect(diameter, diameter, diameter * 0.5f);

    /// <summary>Unit quad with UVs (for icons), front facing -Z.</summary>
    public static Mesh Quad
    {
        get
        {
            const string key = "quad";

            if (_meshes.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            Mesh mesh = new Mesh
            {
                name = key,
                vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                triangles = new[] { 0, 3, 1, 3, 0, 2 },
            };
            mesh.RecalculateBounds();
            _meshes[key] = mesh;
            return mesh;
        }
    }

    static List<Vector3> Perimeter(float width, float height, float radius)
    {
        List<Vector3> points = new List<Vector3>();
        float hx = width * 0.5f - radius;
        float hy = height * 0.5f - radius;
        int segments = radius <= 0.0005f ? 1 : Mathf.Clamp(Mathf.CeilToInt(radius / 0.004f), 3, 10);
        Vector2[] centres = { new Vector2(hx, hy), new Vector2(-hx, hy), new Vector2(-hx, -hy), new Vector2(hx, -hy) };

        for (int corner = 0; corner < 4; corner++)
        {
            for (int s = 0; s <= segments; s++)
            {
                if (radius <= 0.0005f && s > 0)
                {
                    break;
                }

                float angle = (corner * 90f + s * 90f / segments) * Mathf.Deg2Rad;
                Vector2 p = centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                points.Add(new Vector3(p.x, p.y, 0f));
            }
        }

        return points;
    }

    // ---------------------------------------------------------------- scene helpers

    public static MeshRenderer Shape(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition)
    {
        GameObject go = new GameObject(name) { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    /// <summary>A rounded panel with a thin edge. Returns the body renderer (its parent holds both).</summary>
    public static Transform Card(Transform parent, string name, Vector3 centre, Vector2 size, float radius, Color body, Color? edge = null, float edgeWidth = 0.003f)
    {
        GameObject root = new GameObject(name) { layer = VRRig.Layer };
        root.transform.SetParent(parent, false);
        root.transform.localPosition = centre;

        if (edge.HasValue)
        {
            Shape(root.transform, "Edge", RoundedRect(size.x + edgeWidth * 2f, size.y + edgeWidth * 2f, radius + edgeWidth), Solid(edge.Value), new Vector3(0f, 0f, 0.0015f));
        }

        Shape(root.transform, "Body", RoundedRect(size.x, size.y, radius), Solid(body), Vector3.zero);
        return root.transform;
    }

    public static MeshRenderer IconQuad(Transform parent, Icon icon, float size, Vector3 localPosition)
    {
        MeshRenderer renderer = Shape(parent, "Icon " + icon, Quad, IconMaterial(icon), localPosition);
        renderer.transform.localScale = new Vector3(size, size, 1f);
        return renderer;
    }

    /// <summary>
    /// Menu text. Its plate must be the colour of the surface it sits on (labels
    /// are opaque quads cut from the label atlas), so it blends in invisibly.
    /// </summary>
    public static VRLabel Text(Transform parent, Vector3 localPosition, Vector2 size, string text, Color color, Color surface, TMPro.TextAlignmentOptions alignment = TMPro.TextAlignmentOptions.Center)
    {
        VRLabel label = VRLabel.Create(parent, localPosition, Quaternion.identity, size, text, color, surface);
        label.alignment = alignment;
        return label;
    }

    static void Tint(Renderer renderer, MaterialPropertyBlock block, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    // ---------------------------------------------------------------- buttons

    public enum Shape3 { Bar, Disc, Tagged, Chip }

    /// <summary>
    /// A menu button: Bar = list entry (icon + text), Disc = round icon button,
    /// Tagged = big round icon with a name tag beside it (home column),
    /// Chip = small text-only entry. Size is the whole button (metres).
    /// </summary>
    public static VRButton3D Button(Transform parent, Vector3 localPosition, Vector2 size, Shape3 shape, string label, Icon icon, Action onPress, bool enabled = true)
    {
        VRButton3D button = VRButton3D.CreateCustom(parent, localPosition, size, label, onPress);
        button.Interactable = enabled;
        Transform face = new GameObject("Face") { layer = VRRig.Layer }.transform;
        face.SetParent(button.transform, false);

        Renderer body = null;
        Renderer edge = null;
        Renderer disc = null;
        Color plate = enabled ? Item : ItemOff; // fixed: the text plate matches it
        Renderer iconRenderer = null;
        Color text = enabled ? Ink : InkSoft;
        float w = size.x;
        float h = size.y;

        switch (shape)
        {
            case Shape3.Bar:
            {
                float r = Mathf.Min(h * 0.32f, 0.016f);
                edge = Shape(face, "Edge", RoundedRect(w + 0.006f, h + 0.006f, r + 0.003f), Solid(Hairline), new Vector3(0f, 0f, 0.0015f));
                body = Shape(face, "Body", RoundedRect(w, h, r), Solid(plate), Vector3.zero);
                float d = h * 0.70f;
                float cx = -w * 0.5f + h * 0.5f;
                disc = Shape(face, "IconDisc", Disc(d), Solid(Well), new Vector3(cx, 0f, -0.0015f));
                iconRenderer = IconQuad(face, icon, d * 0.66f, new Vector3(cx, 0f, -0.003f));
                float left = -w * 0.5f + h + 0.008f;
                float right = w * 0.5f - 0.012f;
                Text(face, new Vector3((left + right) * 0.5f, 0f, -0.003f), new Vector2(Mathf.Max(0.02f, right - left), h * 0.50f), label, text, plate, TMPro.TextAlignmentOptions.Left);
                break;
            }

            case Shape3.Chip:
            {
                float r = h * 0.5f;
                edge = Shape(face, "Edge", RoundedRect(w + 0.004f, h + 0.004f, r + 0.002f), Solid(Hairline), new Vector3(0f, 0f, 0.0015f));
                body = Shape(face, "Body", RoundedRect(w, h, r), Solid(plate), Vector3.zero);
                Text(face, new Vector3(0f, 0f, -0.003f), new Vector2(w - h * 0.6f, h * 0.62f), label, text, plate, TMPro.TextAlignmentOptions.Left);
                break;
            }

            case Shape3.Disc:
            {
                float d = Mathf.Min(w, h);
                edge = Shape(face, "Edge", Disc(d + 0.005f), Solid(Edge), new Vector3(0f, 0f, 0.0015f));
                disc = Shape(face, "Body", Disc(d), Solid(Item), Vector3.zero);
                iconRenderer = IconQuad(face, icon, d * 0.58f, new Vector3(0f, 0f, -0.003f));
                break;
            }

            case Shape3.Tagged:
            {
                float d = h;
                float cx = -w * 0.5f + d * 0.5f;
                float tagH = d * 0.56f;
                float tagLeft = cx;
                float tagW = w * 0.5f - tagLeft;
                edge = Shape(face, "TagEdge", RoundedRect(tagW + 0.006f, tagH + 0.006f, tagH * 0.2f), Solid(Hairline), new Vector3(tagLeft + tagW * 0.5f, 0f, 0.003f));
                Shape(face, "Tag", RoundedRect(tagW, tagH, tagH * 0.18f), Solid(plate), new Vector3(tagLeft + tagW * 0.5f, 0f, 0.0015f));
                Shape(face, "Edge", Disc(d + 0.006f), Solid(Edge), new Vector3(cx, 0f, 0.0005f));
                disc = Shape(face, "Disc", Disc(d), Solid(Item), new Vector3(cx, 0f, -0.001f));
                iconRenderer = IconQuad(face, icon, d * 0.56f, new Vector3(cx, 0f, -0.003f));
                float left = cx + d * 0.5f + 0.014f;
                float right = w * 0.5f - 0.014f;
                Text(face, new Vector3((left + right) * 0.5f, 0f, -0.001f), new Vector2(right - left, tagH * 0.58f), label, text, plate, TMPro.TextAlignmentOptions.Left);
                break;
            }
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        float lastT = -1f;
        bool lastPressed = false;
        bool lastEnabled = !enabled;
        float pop = shape == Shape3.Bar || shape == Shape3.Chip ? 0.015f : 0.06f;

        button.Visual = (t, pressed, interactable) =>
        {
            if (Mathf.Abs(t - lastT) < 0.002f && pressed == lastPressed && interactable == lastEnabled)
            {
                return;
            }

            lastT = t;
            lastPressed = pressed;
            lastEnabled = interactable;

            // The body (and its text plate) never changes colour, so the text never redraws:
            // the highlight is the edge turning amber, plus the icon disc and icon.
            Tint(edge, block, interactable ? Color.Lerp(shape == Shape3.Disc ? Edge : Hairline, AccentDeep, t) : Hairline);

            if (disc != null)
            {
                Color discIdle = shape == Shape3.Bar ? Well : Item;
                Tint(disc, block, !interactable ? ItemOff : Color.Lerp(discIdle, Accent, t));
            }

            if (iconRenderer != null)
            {
                Color iconColor = !interactable ? new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.45f) : Color.Lerp(InkSoft, Color.white, t);
                Tint(iconRenderer, block, iconColor);
            }

            float scale = 1f + pop * t;
            face.localScale = new Vector3(scale, scale, 1f);
            face.localPosition = new Vector3(0f, 0f, pressed ? 0.004f : -0.006f * t);
        };

        return button;
    }

    // ---------------------------------------------------------------- icons

    public enum Icon
    {
        Dot, Battle, Decks, Options, Person, Notes, Power, Back, Screen, Play, Check, Close, Plus, Edit,
        Copy, Search, Cards, Room, Bolt, Star, Home, Reset, Trash, Left, Right, Up, Down, Hourglass, Info,
    }

    static bool Has(string text, params string[] words)
    {
        foreach (string word in words)
        {
            if (text.Contains(word))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Picks an icon from a button's text (works for the game's own, possibly translated, labels as a best effort).</summary>
    public static Icon IconFor(string label)
    {
        string l = (label ?? "").Trim().ToUpperInvariant();

        if (l.StartsWith("<") || Has(l, "PREV")) return Icon.Left;
        if (l.EndsWith(">") || Has(l, "NEXT")) return Icon.Right;
        if (Has(l, "BACK", "RETURN")) return Icon.Back;
        if (Has(l, "EXIT", "LEAVE", "QUIT", "LOG OUT")) return Icon.Power;
        if (Has(l, "CLOSE", "CANCEL") || l == "NO") return Icon.Close;
        if (Has(l, "DELETE", "REMOVE")) return Icon.Trash;
        if (Has(l, "CLEAR", "RESET", "REDRAW", "RECONNECT", "RETRY")) return Icon.Reset;
        if (Has(l, "EDIT", "RENAME")) return Icon.Edit;
        if (Has(l, "NAME", "PROFILE", "PLAYER")) return Icon.Person;
        if (Has(l, "COPY", "PASTE", "IMPORT", "EXPORT", "CODE")) return Icon.Copy;
        if (Has(l, "RANDOM")) return Icon.Bolt;
        if (Has(l, "ROOM", "FRIEND", "JOIN")) return Icon.Room;
        if (Has(l, "BOT", "CPU", "START", "PLAY")) return Icon.Play;
        if (Has(l, "BATTLE", "DUEL", "FIGHT", "MATCH")) return Icon.Battle;
        if (Has(l, "NEW", "CREATE", "ADD")) return Icon.Plus;
        if (Has(l, "SEARCH", "FILTER", "TYPE")) return Icon.Search;
        if (Has(l, "KEY", "FIRST", "FAVOURITE", "FAVORITE")) return Icon.Star;
        if (Has(l, "DECK")) return Icon.Decks;
        if (Has(l, "DRAW", "CARD", "VIEW", "LIST")) return Icon.Cards;
        if (Has(l, "OPTION", "SETTING", "CONFIG", "SOUND", "GRAPHIC", "LANGUAGE", "GAMEPLAY", "REGION", "WINDOW", "IMAGE")) return Icon.Options;
        if (Has(l, "PATCH", "NOTE", "RULE", "NEWS", "HELP")) return Icon.Notes;
        if (Has(l, "HOME")) return Icon.Home;
        if (Has(l, "YES", "OK", "CONFIRM", "SELECT", "APPLY", "READY", "SAVE", "DONE", "ACCEPT")) return Icon.Check;
        if (Has(l, "SCREEN", "CLASSIC", "MENU")) return Icon.Screen;
        return Icon.Dot;
    }

    const int IconPx = 128;
    static readonly Dictionary<Icon, Texture2D> _iconTextures = new Dictionary<Icon, Texture2D>();

    static Texture2D IconTexture(Icon icon)
    {
        if (_iconTextures.TryGetValue(icon, out Texture2D cached) && cached != null)
        {
            return cached;
        }

        Func<Vector2, float> shape = IconShape(icon);
        Color32[] pixels = new Color32[IconPx * IconPx];
        float pixel = 2f / IconPx;

        for (int y = 0; y < IconPx; y++)
        {
            for (int x = 0; x < IconPx; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / IconPx * 2f - 1f, (y + 0.5f) / IconPx * 2f - 1f);
                float d = shape(p);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d / pixel) * 255f);
                pixels[y * IconPx + x] = new Color32(255, 255, 255, a);
            }
        }

        Texture2D texture = new Texture2D(IconPx, IconPx, TextureFormat.RGBA32, true)
        {
            name = "DCGOVR_Icon " + icon,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 2,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        _iconTextures[icon] = texture;
        return texture;
    }

    // Signed distance shapes in [-1, 1] icon space (negative = inside).
    const float W = 0.085f; // stroke half width

    static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
    static float RingD(Vector2 p, Vector2 c, float r, float w = W) => Mathf.Abs((p - c).magnitude - r) - w;

    static float Seg(Vector2 p, Vector2 a, Vector2 b, float w = W)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - w;
    }

    static float Box(Vector2 p, Vector2 c, Vector2 half, float r = 0.06f)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }

    static float Outline(float d, float w = W) => Mathf.Abs(d) - w;

    static float Polygon(Vector2 p, params Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;

        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i];
            Vector2 w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y;
            bool c2 = p.y < v[j].y;
            bool c3 = e.x * w.y > e.y * w.x;

            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
            {
                s = -s;
            }
        }

        return s * Mathf.Sqrt(d);
    }

    static float Arc(Vector2 p, Vector2 c, float r, float fromDeg, float toDeg, float w = W)
    {
        Vector2 q = p - c;
        float angle = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
        float span = toDeg - fromDeg;
        float rel = Mathf.Repeat(angle - fromDeg, 360f);

        if (rel <= span)
        {
            return Mathf.Abs(q.magnitude - r) - w;
        }

        Vector2 a = c + new Vector2(Mathf.Cos(fromDeg * Mathf.Deg2Rad), Mathf.Sin(fromDeg * Mathf.Deg2Rad)) * r;
        Vector2 b = c + new Vector2(Mathf.Cos(toDeg * Mathf.Deg2Rad), Mathf.Sin(toDeg * Mathf.Deg2Rad)) * r;
        return Mathf.Min((p - a).magnitude, (p - b).magnitude) - w;
    }

    static Vector2 Rot(Vector2 p, float deg)
    {
        float a = -deg * Mathf.Deg2Rad;
        float c = Mathf.Cos(a);
        float s = Mathf.Sin(a);
        return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
    }

    static float Min(params float[] values)
    {
        float m = float.MaxValue;

        foreach (float v in values)
        {
            m = Mathf.Min(m, v);
        }

        return m;
    }

    static Vector2 V(float x, float y) => new Vector2(x, y);

    static Func<Vector2, float> IconShape(Icon icon)
    {
        switch (icon)
        {
            case Icon.Battle:
                return p => Min(
                    Seg(p, V(-0.55f, -0.55f), V(0.62f, 0.62f), 0.07f),
                    Seg(p, V(0.55f, -0.55f), V(-0.62f, 0.62f), 0.07f),
                    Seg(p, V(-0.58f, -0.18f), V(-0.18f, -0.58f), 0.075f),
                    Seg(p, V(0.18f, -0.58f), V(0.58f, -0.18f), 0.075f),
                    Circle(p, V(-0.70f, -0.70f), 0.11f),
                    Circle(p, V(0.70f, -0.70f), 0.11f));

            case Icon.Decks:
                return p => Mathf.Min(
                    Mathf.Max(Outline(Box(p, V(-0.16f, 0.14f), V(0.38f, 0.52f))), -Box(p, V(0.16f, -0.12f), V(0.48f, 0.62f))),
                    Box(p, V(0.16f, -0.12f), V(0.38f, 0.52f)));

            case Icon.Options:
                return p =>
                {
                    float d = RingD(p, Vector2.zero, 0.40f, 0.13f);

                    for (int k = 0; k < 8; k++)
                    {
                        Vector2 q = Rot(p, k * 45f);
                        d = Mathf.Min(d, Box(q, V(0.62f, 0f), V(0.14f, 0.12f), 0.03f));
                    }

                    return d;
                };

            case Icon.Person:
                return p => Mathf.Min(Circle(p, V(0f, 0.36f), 0.26f), Mathf.Max(Circle(p, V(0f, -0.78f), 0.64f), -(p.y + 0.70f)));

            case Icon.Room:
                return p => Min(
                    Circle(p, V(-0.32f, 0.30f), 0.19f),
                    Circle(p, V(0.32f, 0.30f), 0.19f),
                    Mathf.Max(Circle(p, V(-0.32f, -0.58f), 0.42f), -(p.y + 0.52f)),
                    Mathf.Max(Circle(p, V(0.32f, -0.58f), 0.42f), -(p.y + 0.52f)));

            case Icon.Notes:
                return p => Min(
                    Outline(Box(p, Vector2.zero, V(0.46f, 0.62f))),
                    Seg(p, V(-0.22f, 0.26f), V(0.22f, 0.26f), 0.06f),
                    Seg(p, V(-0.22f, 0f), V(0.22f, 0f), 0.06f),
                    Seg(p, V(-0.22f, -0.26f), V(0.08f, -0.26f), 0.06f));

            case Icon.Power:
                return p => Mathf.Min(Arc(p, V(0f, -0.06f), 0.52f, 125f, 415f), Seg(p, V(0f, 0.68f), V(0f, 0.08f)));

            case Icon.Back:
                return p => Min(Seg(p, V(0.55f, 0f), V(-0.5f, 0f)), Seg(p, V(-0.5f, 0f), V(-0.12f, 0.38f)), Seg(p, V(-0.5f, 0f), V(-0.12f, -0.38f)));

            case Icon.Left:
                return p => Mathf.Min(Seg(p, V(0.18f, 0.45f), V(-0.24f, 0f), 0.1f), Seg(p, V(-0.24f, 0f), V(0.18f, -0.45f), 0.1f));

            case Icon.Right:
                return p => Mathf.Min(Seg(p, V(-0.18f, 0.45f), V(0.24f, 0f), 0.1f), Seg(p, V(0.24f, 0f), V(-0.18f, -0.45f), 0.1f));

            case Icon.Up:
                return p => Mathf.Min(Seg(p, V(-0.45f, -0.18f), V(0f, 0.24f), 0.1f), Seg(p, V(0f, 0.24f), V(0.45f, -0.18f), 0.1f));

            case Icon.Down:
                return p => Mathf.Min(Seg(p, V(-0.45f, 0.18f), V(0f, -0.24f), 0.1f), Seg(p, V(0f, -0.24f), V(0.45f, 0.18f), 0.1f));

            case Icon.Screen:
                return p => Min(Outline(Box(p, V(0f, 0.14f), V(0.64f, 0.42f))), Seg(p, V(0f, -0.28f), V(0f, -0.52f)), Seg(p, V(-0.32f, -0.56f), V(0.32f, -0.56f)));

            case Icon.Play:
                return p => Polygon(p, V(-0.32f, 0.55f), V(0.58f, 0f), V(-0.32f, -0.55f)) - 0.04f;

            case Icon.Check:
                return p => Mathf.Min(Seg(p, V(-0.52f, 0.02f), V(-0.16f, -0.36f), 0.1f), Seg(p, V(-0.16f, -0.36f), V(0.54f, 0.40f), 0.1f));

            case Icon.Close:
                return p => Mathf.Min(Seg(p, V(-0.42f, -0.42f), V(0.42f, 0.42f), 0.1f), Seg(p, V(-0.42f, 0.42f), V(0.42f, -0.42f), 0.1f));

            case Icon.Plus:
                return p => Mathf.Min(Seg(p, V(0f, -0.55f), V(0f, 0.55f), 0.1f), Seg(p, V(-0.55f, 0f), V(0.55f, 0f), 0.1f));

            case Icon.Edit:
                return p => Min(
                    Seg(p, V(-0.22f, -0.22f), V(0.42f, 0.42f), 0.15f),
                    Polygon(p, V(-0.62f, -0.62f), V(-0.20f, -0.44f), V(-0.44f, -0.20f)),
                    Seg(p, V(-0.1f, -0.66f), V(0.6f, -0.66f), 0.05f));

            case Icon.Copy:
                return p => Mathf.Min(
                    Mathf.Max(Outline(Box(p, V(-0.16f, 0.16f), V(0.38f, 0.44f))), -Box(p, V(0.18f, -0.16f), V(0.48f, 0.54f))),
                    Outline(Box(p, V(0.18f, -0.16f), V(0.38f, 0.44f))));

            case Icon.Search:
                return p => Mathf.Min(RingD(p, V(-0.14f, 0.14f), 0.36f), Seg(p, V(0.14f, -0.14f), V(0.58f, -0.58f), 0.12f));

            case Icon.Cards:
                return p =>
                {
                    Vector2 pivot = V(0f, -0.75f);
                    float left = Outline(Box(Rot(p - pivot, 20f) + pivot, V(0f, 0.02f), V(0.27f, 0.42f)), 0.06f);
                    float right = Outline(Box(Rot(p - pivot, -20f) + pivot, V(0f, 0.02f), V(0.27f, 0.42f)), 0.06f);
                    float middle = Box(p, V(0f, 0.06f), V(0.27f, 0.42f));
                    float sides = Mathf.Max(Mathf.Min(left, right), -(middle - 0.08f));
                    return Mathf.Min(sides, middle);
                };

            case Icon.Bolt:
                return p => Polygon(p, V(0.18f, 0.78f), V(-0.46f, -0.06f), V(-0.04f, -0.06f), V(-0.18f, -0.78f), V(0.46f, 0.08f), V(0.04f, 0.08f)) - 0.02f;

            case Icon.Star:
            {
                Vector2[] star = new Vector2[10];

                for (int i = 0; i < 10; i++)
                {
                    float a = (90f + i * 36f) * Mathf.Deg2Rad;
                    float r = i % 2 == 0 ? 0.72f : 0.30f;
                    star[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }

                return p => Polygon(p, star) - 0.02f;
            }

            case Icon.Home:
                return p => Mathf.Min(
                    Polygon(p, V(-0.70f, 0.02f), V(0f, 0.66f), V(0.70f, 0.02f)),
                    Mathf.Max(Box(p, V(0f, -0.32f), V(0.46f, 0.36f), 0.04f), -Box(p, V(0f, -0.48f), V(0.13f, 0.24f), 0.03f)));

            case Icon.Reset:
                return p => Mathf.Min(Arc(p, Vector2.zero, 0.48f, 70f, 340f), Polygon(p, V(0.05f, 0.70f), V(0.42f, 0.36f), V(-0.02f, 0.22f)));

            case Icon.Trash:
                return p => Min(
                    Seg(p, V(-0.52f, 0.42f), V(0.52f, 0.42f), 0.07f),
                    Seg(p, V(-0.16f, 0.58f), V(0.16f, 0.58f), 0.07f),
                    Outline(Polygon(p, V(-0.38f, 0.28f), V(0.38f, 0.28f), V(0.30f, -0.62f), V(-0.30f, -0.62f)), 0.07f),
                    Seg(p, V(0f, 0.1f), V(0f, -0.42f), 0.05f));

            case Icon.Hourglass:
                return p => Min(
                    Seg(p, V(-0.42f, 0.62f), V(0.42f, 0.62f), 0.07f),
                    Seg(p, V(-0.42f, -0.62f), V(0.42f, -0.62f), 0.07f),
                    Outline(Polygon(p, V(-0.34f, 0.56f), V(0.34f, 0.56f), V(0.05f, 0f), V(0.34f, -0.56f), V(-0.34f, -0.56f), V(-0.05f, 0f)), 0.06f),
                    Polygon(p, V(-0.2f, -0.5f), V(0.2f, -0.5f), V(0f, -0.2f)));

            case Icon.Info:
                return p => Mathf.Min(Circle(p, V(0f, 0.48f), 0.12f), Seg(p, V(0f, 0.18f), V(0f, -0.58f), 0.1f));

            default:
                return p => Polygon(p, V(0f, 0.34f), V(0.34f, 0f), V(0f, -0.34f), V(-0.34f, 0f));
        }
    }
}
#endif
