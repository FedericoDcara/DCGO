#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Main menu surroundings: you stand on a floating garden island high above a sea
// of clouds. A giant landmark tree with a glowing ring stands ahead (behind the
// menu panels); ring paths, a water ring and a ring of coloured flowers circle
// it; glassy city blocks, a research dome and tall station towers fill the
// districts; a raised walkway and an outer support ring with glowing pylons frame
// the island; waterfalls pour off its edge and small islands float around it.
// Digimon and Tamers from your _DSTSModels folder walk and run along the paths
// (Tamers with a partner beside them) and a few Digimon hang around near you.
//
// Original layout inspired by a floating "city in a garden" concept; everything is
// built in code. Cheap to draw: all solid scenery is ONE mesh with ONE unlit
// material (a small colour palette texture, flat shading baked into the palette),
// plus a sky dome, a cloud layer, the small islands and one waterfall mesh.
// Shown only on the menu (not in duels); its characters are unloaded during duels.
public class VRMenuWorld : MonoBehaviour
{
    // Island space: origin = centre of the island (the tree), +Z = away from you.
    const float PlayerRadius = 14f;   // you stand on the third ring, facing the tree
    const float IslandRadius = 24f;
    const int Shades = 8;
    static readonly Vector3 ShadeLight = new Vector3(-0.35f, 0.85f, -0.45f).normalized;

    static readonly Color Grass = new Color(0.42f, 0.74f, 0.30f);
    static readonly Color GrassLight = new Color(0.52f, 0.81f, 0.36f);
    static readonly Color GrassDark = new Color(0.30f, 0.58f, 0.22f);
    static readonly Color Path = new Color(0.95f, 0.89f, 0.70f);
    static readonly Color Stone = new Color(0.90f, 0.90f, 0.88f);
    static readonly Color Water = new Color(0.36f, 0.82f, 0.95f);
    static readonly Color Earth1 = new Color(0.52f, 0.34f, 0.19f);
    static readonly Color Earth2 = new Color(0.43f, 0.27f, 0.15f);
    static readonly Color Earth3 = new Color(0.34f, 0.22f, 0.14f);
    static readonly Color Bark = new Color(0.46f, 0.30f, 0.18f);
    static readonly Color Leaf1 = new Color(0.25f, 0.62f, 0.22f);
    static readonly Color Leaf2 = new Color(0.34f, 0.71f, 0.26f);
    static readonly Color Leaf3 = new Color(0.20f, 0.52f, 0.20f);
    static readonly Color Glow = new Color(1f, 0.58f, 0.16f);
    static readonly Color Crystal = new Color(0.45f, 0.93f, 1f);
    static readonly Color DeepCrystal = new Color(0.55f, 0.45f, 1f);
    static readonly Color Metal = new Color(0.80f, 0.85f, 0.90f);
    static readonly Color Glass1 = new Color(0.62f, 0.74f, 0.86f);
    static readonly Color Glass2 = new Color(0.72f, 0.80f, 0.90f);
    static readonly Color Glass3 = new Color(0.50f, 0.62f, 0.76f);
    static readonly Color Roof = new Color(0.92f, 0.95f, 0.98f);
    static readonly Color Cloud = new Color(0.98f, 0.99f, 1f);
    static readonly Color CloudShade = new Color(0.86f, 0.90f, 0.96f);
    static readonly Color[] Flowers =
    {
        new Color(0.95f, 0.25f, 0.30f), new Color(1f, 0.55f, 0.15f), new Color(1f, 0.86f, 0.22f),
        new Color(0.92f, 0.32f, 0.75f), new Color(0.62f, 0.38f, 0.96f), new Color(0.30f, 0.66f, 1f),
    };

    VRRig _rig;
    Transform _root;      // whole world (rig space)
    Transform _island;    // island space
    Transform _clouds;
    readonly List<Transform> _floaters = new List<Transform>();
    readonly List<Vector3> _floaterBase = new List<Vector3>();
    Material _fallMaterial;
    readonly List<(Material material, float speed)> _code = new List<(Material, float)>();
    Light _sun;
    Light _previousSun;
    bool _shown;

    public static VRMenuWorld Create(Transform parent, VRRig rig)
    {
        GameObject go = new GameObject("MenuWorld") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRMenuWorld world = go.AddComponent<VRMenuWorld>();
        world._rig = rig;
        return world;
    }

    void LateUpdate()
    {
        bool show = VRSettings.MenuWorld && _rig != null && _rig.MenuLayout && !VRModelStudio.Active;

        if (show && _root == null)
        {
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DCGO VR] Menu world build failed: " + exception);
                VRSettings.MenuWorld = false;
                return;
            }
        }

        if (show != _shown)
        {
            _shown = show;

            if (_root != null)
            {
                _root.gameObject.SetActive(show);
            }

            _rig.SetRoomFloor(!show);
            UseSun(show);

            if (show)
            {
                StartCharacters();
            }
            else
            {
                StopCharacters();
            }

            Debug.Log("[DCGO VR] Menu world " + (show ? "shown" : "hidden"));
        }

        if (!show)
        {
            return;
        }

        float t = Time.unscaledTime;
        _fallMaterial.SetTextureOffset("_BaseMap", new Vector2(0f, t * 0.55f));

        // Digital code rains down the sky (each layer at its own speed).
        foreach ((Material material, float speed) in _code)
        {
            Vector2 offset = material.GetTextureOffset("_BaseMap");
            material.SetTextureOffset("_BaseMap", new Vector2(offset.x, t * speed));
        }
        _clouds.localRotation = Quaternion.Euler(0f, t * 0.4f, 0f);

        for (int i = 0; i < _floaters.Count; i++)
        {
            _floaters[i].localPosition = _floaterBase[i] + Vector3.up * (Mathf.Sin(t * 0.35f + i * 1.7f) * 0.6f);
        }

        UpdateCharacters(Time.unscaledDeltaTime);
    }

    void OnDestroy()
    {
        StopCharacters();
        UseSun(false);
    }

    // The characters' toon shader is lit by the main light. Like the duel arena, the island
    // brings its own soft sun (from behind you, above) and makes it the main light while the
    // menu shows; otherwise the menu scene's light washed the models out to white.
    void UseSun(bool on)
    {
        if (_sun == null)
        {
            return;
        }

        if (on)
        {
            if (RenderSettings.sun != _sun)
            {
                _previousSun = RenderSettings.sun;
            }

            RenderSettings.sun = _sun;
        }
        else if (RenderSettings.sun == _sun)
        {
            RenderSettings.sun = _previousSun;
        }
    }

    // ================================================================ building

    sealed class Palette
    {
        public readonly List<Color> Colors = new List<Color>();
        readonly Dictionary<Color, int> _index = new Dictionary<Color, int>();
        const int Cell = 4;

        public int Index(Color color)
        {
            if (!_index.TryGetValue(color, out int index))
            {
                index = Colors.Count;
                Colors.Add(color);
                _index[color] = index;
            }

            return index;
        }

        public Texture2D Build()
        {
            int rows = Mathf.Max(1, Colors.Count);
            Texture2D texture = new Texture2D(Shades * Cell, rows * Cell, TextureFormat.RGBA32, false)
            {
                name = "DCGOVR_MenuWorldPalette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Color[] pixels = new Color[texture.width * texture.height];

            for (int row = 0; row < rows; row++)
            {
                for (int shade = 0; shade < Shades; shade++)
                {
                    Color c = Colors[row] * ShadeValue(shade);
                    c.a = 1f;

                    for (int y = 0; y < Cell; y++)
                    {
                        for (int x = 0; x < Cell; x++)
                        {
                            pixels[(row * Cell + y) * texture.width + shade * Cell + x] = c;
                        }
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public Vector2 UV(int code)
        {
            int row = code / Shades;
            int shade = code % Shades;
            return new Vector2((shade + 0.5f) / Shades, (row + 0.5f) / Mathf.Max(1, Colors.Count));
        }
    }

    static float ShadeValue(int shade) => 0.52f + shade / (float)(Shades - 1) * 0.55f;

    static int ShadeOf(Vector3 normal)
    {
        float s = 0.55f + 0.45f * Mathf.Max(0f, Vector3.Dot(normal, ShadeLight)) + 0.08f * Mathf.Max(0f, normal.y);
        return Mathf.Clamp(Mathf.RoundToInt((s - 0.52f) / 0.55f * (Shades - 1)), 0, Shades - 1);
    }

    // Flat-shaded triangle soup; each triangle gets a palette cell (colour x shade).
    sealed class Kit
    {
        readonly Palette _palette;
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<int> _codes = new List<int>();

        public Kit(Palette palette)
        {
            _palette = palette;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 outward, bool twoSided = false)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);

            if (n.sqrMagnitude < 1e-12f)
            {
                return;
            }

            if (Vector3.Dot(n, outward) < 0f)
            {
                (b, c) = (c, b);
                n = -n;
            }

            int code = _palette.Index(color) * Shades + ShadeOf(n.normalized);
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _codes.Add(code);
            _codes.Add(code);
            _codes.Add(code);

            if (twoSided)
            {
                _vertices.Add(a);
                _vertices.Add(c);
                _vertices.Add(b);
                _codes.Add(code);
                _codes.Add(code);
                _codes.Add(code);
            }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 outward, bool twoSided = false)
        {
            Tri(a, b, c, color, outward, twoSided);
            Tri(a, c, d, color, outward, twoSided);
        }

        public Mesh Build(string name)
        {
            Mesh mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            Vector2[] uvs = new Vector2[_vertices.Count];
            int[] triangles = new int[_vertices.Count];

            for (int i = 0; i < uvs.Length; i++)
            {
                uvs[i] = _palette.UV(_codes[i]);
                triangles[i] = i;
            }

            mesh.SetVertices(_vertices);
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }

    // ---------------------------------------------------------------- shapes

    static Vector3 Polar(float radius, float degrees, float y) =>
        new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad) * radius, y, Mathf.Sin(degrees * Mathf.Deg2Rad) * radius);

    static void Ring(Kit kit, Vector3 centre, float inner, float outer, float y, Color color, float from = 0f, float to = 360f, int segments = 72)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt(segments * (to - from) / 360f));

        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.Lerp(from, to, i / (float)n);
            float a1 = Mathf.Lerp(from, to, (i + 1) / (float)n);

            if (inner <= 0.001f)
            {
                kit.Tri(centre + Vector3.up * y, centre + Polar(outer, a0, y), centre + Polar(outer, a1, y), color, Vector3.up);
            }
            else
            {
                kit.Quad(centre + Polar(inner, a0, y), centre + Polar(outer, a0, y), centre + Polar(outer, a1, y), centre + Polar(inner, a1, y), color, Vector3.up);
            }
        }
    }

    // Low wall ring (flower beds): top + both sides.
    static void RaisedRing(Kit kit, float inner, float outer, float height, Color color, float from, float to, int segments = 72)
    {
        Ring(kit, Vector3.zero, inner, outer, height, color, from, to, segments);
        int n = Mathf.Max(2, Mathf.CeilToInt(segments * (to - from) / 360f));

        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.Lerp(from, to, i / (float)n);
            float a1 = Mathf.Lerp(from, to, (i + 1) / (float)n);
            Vector3 mid = Polar(1f, (a0 + a1) * 0.5f, 0f);
            kit.Quad(Polar(outer, a0, 0f), Polar(outer, a1, 0f), Polar(outer, a1, height), Polar(outer, a0, height), color, mid);
            kit.Quad(Polar(inner, a0, 0f), Polar(inner, a1, 0f), Polar(inner, a1, height), Polar(inner, a0, height), color, -mid);
        }
    }

    // Surface of revolution through (y, radius) rings; side bands take their ring's colour.
    // Noise roughens the radius per angle (rocks). Optional top / bottom caps.
    static void Lathe(Kit kit, Vector3 centre, (float y, float r, Color c)[] rings, int segments, float noise, int seed, bool capTop = false, bool capBottom = false)
    {
        System.Random random = new System.Random(seed);
        float[,] jitter = new float[rings.Length, segments];

        for (int k = 0; k < rings.Length; k++)
        {
            for (int s = 0; s < segments; s++)
            {
                jitter[k, s] = 1f + ((float)random.NextDouble() * 2f - 1f) * noise;
            }
        }

        Vector3 P(int k, int s) => centre + Polar(rings[k].r * jitter[k, s % segments], s * 360f / segments, rings[k].y);

        for (int k = 0; k < rings.Length - 1; k++)
        {
            for (int s = 0; s < segments; s++)
            {
                Vector3 axis = centre + Vector3.up * (rings[k].y + rings[k + 1].y) * 0.5f;
                Vector3 a = P(k, s);
                Vector3 b = P(k, s + 1);
                Vector3 c = P(k + 1, s + 1);
                Vector3 d = P(k + 1, s);
                Vector3 mid = (a + b + c + d) * 0.25f;
                kit.Quad(a, b, c, d, rings[k].c, mid - axis);
            }
        }

        if (capTop)
        {
            int k = rings.Length - 1;

            for (int s = 0; s < segments; s++)
            {
                kit.Tri(centre + Vector3.up * rings[k].y, P(k, s), P(k, s + 1), rings[k].c, Vector3.up);
            }
        }

        if (capBottom)
        {
            for (int s = 0; s < segments; s++)
            {
                kit.Tri(centre + Vector3.up * rings[0].y, P(0, s), P(0, s + 1), rings[0].c, Vector3.down);
            }
        }
    }

    static void Box(Kit kit, Vector3 centre, Vector3 size, float yaw, Color color, Color? top = null)
    {
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        Vector3 h = size * 0.5f;
        Vector3 C(float x, float y, float z) => centre + r * new Vector3(x * h.x, y * h.y, z * h.z);
        Vector3[] p = { C(-1, -1, -1), C(1, -1, -1), C(1, 1, -1), C(-1, 1, -1), C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1) };
        int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 } };

        for (int f = 0; f < faces.Length; f++)
        {
            int[] q = faces[f];
            Vector3 mid = (p[q[0]] + p[q[1]] + p[q[2]] + p[q[3]]) * 0.25f;
            kit.Quad(p[q[0]], p[q[1]], p[q[2]], p[q[3]], f == 4 && top.HasValue ? top.Value : color, mid - centre);
        }
    }

    // A ground strip from point a to point b (paths, spokes).
    static void Strip(Kit kit, Vector3 a, Vector3 b, float width, Color color)
    {
        Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized) * (width * 0.5f);
        kit.Quad(a - side, b - side, b + side, a + side, color, Vector3.up);
    }

    static readonly List<Vector3> _icoVertices = new List<Vector3>();
    static readonly List<int> _icoTriangles = new List<int>();

    static void Icosphere()
    {
        if (_icoVertices.Count > 0)
        {
            return;
        }

        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        List<Vector3> v = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        for (int i = 0; i < v.Count; i++)
        {
            v[i] = v[i].normalized;
        }

        // One subdivision: 80 faces (low-poly look).
        Dictionary<long, int> mids = new Dictionary<long, int>();

        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            if (!mids.TryGetValue(key, out int index))
            {
                index = v.Count;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                mids[key] = index;
            }

            return index;
        }

        for (int i = 0; i < f.Length; i += 3)
        {
            int a = f[i], b = f[i + 1], c = f[i + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            _icoTriangles.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }

        _icoVertices.AddRange(v);
    }

    static void Blob(Kit kit, Vector3 centre, Vector3 radii, Color color, float noise, int seed, Color? second = null)
    {
        Icosphere();
        System.Random random = new System.Random(seed);
        Vector3[] p = new Vector3[_icoVertices.Count];

        for (int i = 0; i < p.Length; i++)
        {
            float j = 1f + ((float)random.NextDouble() * 2f - 1f) * noise;
            Vector3 u = _icoVertices[i];
            p[i] = centre + new Vector3(u.x * radii.x, u.y * radii.y, u.z * radii.z) * j;
        }

        for (int i = 0; i < _icoTriangles.Count; i += 3)
        {
            Vector3 a = p[_icoTriangles[i]], b = p[_icoTriangles[i + 1]], c = p[_icoTriangles[i + 2]];
            Color col = second.HasValue && random.NextDouble() < 0.35 ? second.Value : color;
            kit.Tri(a, b, c, col, (a + b + c) / 3f - centre);
        }
    }

    static void Torus(Kit kit, Vector3 centre, float radius, float tube, Color color, int segments = 64, int sides = 6)
    {
        Vector3 P(int u, int v)
        {
            float a = u * 360f / segments * Mathf.Deg2Rad;
            float b = v * 360f / sides * Mathf.Deg2Rad;
            float r = radius + tube * Mathf.Cos(b);
            return centre + new Vector3(Mathf.Cos(a) * r, tube * Mathf.Sin(b), Mathf.Sin(a) * r);
        }

        for (int u = 0; u < segments; u++)
        {
            for (int v = 0; v < sides; v++)
            {
                Vector3 a = P(u, v), b = P(u + 1, v), c = P(u + 1, v + 1), d = P(u, v + 1);
                float mid = (u + 0.5f) * 360f / segments;
                kit.Quad(a, b, c, d, color, (a + b + c + d) * 0.25f - (centre + Polar(radius, mid, 0f)));
            }
        }
    }

    static void Gem(Kit kit, Vector3 centre, float width, float height, Color color, float yaw = 0f)
    {
        Vector3 top = centre + Vector3.up * height * 0.5f;
        Vector3 bottom = centre - Vector3.up * height * 0.5f;

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = centre + Polar(width * 0.5f, yaw + i * 90f, 0f);
            Vector3 b = centre + Polar(width * 0.5f, yaw + (i + 1) * 90f, 0f);
            kit.Tri(top, a, b, color, (top + a + b) / 3f - centre);
            kit.Tri(bottom, a, b, color, (bottom + a + b) / 3f - centre);
        }
    }

    static void Tree(Kit kit, Vector3 foot, float height, int seed)
    {
        Lathe(kit, foot, new[] { (0f, height * 0.09f, Bark), (height * 0.5f, height * 0.06f, Bark) }, 6, 0.1f, seed);
        Color leaf = seed % 3 == 0 ? Leaf1 : seed % 3 == 1 ? Leaf2 : Leaf3;
        Blob(kit, foot + Vector3.up * height * 0.68f, new Vector3(height * 0.36f, height * 0.4f, height * 0.36f), leaf, 0.14f, seed, Leaf2);
    }

    // ---------------------------------------------------------------- the world

    void Build()
    {
        Palette palette = new Palette();
        Material solid = null;

        _root = new GameObject("FloatingIsland") { layer = VRRig.Layer }.transform;
        _root.SetParent(transform, false);
        Vector3 seat = new Vector3(0f, 0f, -1.05f); // VRRig seat (rig space)

        _island = new GameObject("Island") { layer = VRRig.Layer }.transform;
        _island.SetParent(_root, false);
        _island.localPosition = seat + new Vector3(0f, -0.01f, PlayerRadius);

        Kit main = new Kit(palette);
        BuildIsland(main);

        Kit glass = new Kit(palette);
        BuildHolograms(glass);

        // Small islands drifting around (each its own mesh so it can bob).
        (Vector3 pos, float r)[] satellites =
        {
            (new Vector3(-31f, -5f, 12f), 6f), (new Vector3(33f, -9f, 4f), 5f), (new Vector3(-20f, -13f, 42f), 7f), (new Vector3(27f, 1.5f, 38f), 4.5f),
        };
        List<(Transform, Kit)> pending = new List<(Transform, Kit)>();

        for (int i = 0; i < satellites.Length; i++)
        {
            Kit kit = new Kit(palette);
            BuildSatellite(kit, satellites[i].r, 100 + i);
            Transform t = new GameObject("Islet " + i) { layer = VRRig.Layer }.transform;
            t.SetParent(_island, false);
            t.localPosition = satellites[i].pos;
            _floaters.Add(t);
            _floaterBase.Add(satellites[i].pos);
            pending.Add((t, kit));
        }

        // Sea of clouds below, slowly turning around you.
        _clouds = new GameObject("Clouds") { layer = VRRig.Layer }.transform;
        _clouds.SetParent(_root, false);
        _clouds.localPosition = seat;
        Kit clouds = new Kit(palette);
        BuildClouds(clouds);

        // Every palette colour is known now: one texture, one material for all solid parts.
        Texture2D texture = palette.Build();
        solid = VRMaterials.Unlit(Color.white);
        solid.SetTexture("_BaseMap", texture);
        solid.SetTexture("_MainTex", texture);
        solid.name = "DCGOVR_MenuWorld";

        AddMesh(_island, "Scenery", main.Build("MenuWorldIsland"), solid);
        AddMesh(_clouds, "CloudSea", clouds.Build("MenuWorldClouds"), solid);

        foreach ((Transform t, Kit kit) in pending)
        {
            AddMesh(t, "Islet", kit.Build("MenuWorldIslet"), solid);
        }

        _fallMaterial = VRMaterials.Transparent(new Color(0.85f, 0.97f, 1f, 0.75f));
        Texture2D stripes = FallTexture();
        _fallMaterial.SetTexture("_BaseMap", stripes);
        _fallMaterial.SetTexture("_MainTex", stripes);
        AddMesh(_island, "Waterfalls", FallsMesh(), _fallMaterial);

        Material holo = VRMaterials.Transparent(new Color(1f, 1f, 1f, 0.35f));
        holo.SetTexture("_BaseMap", texture);
        holo.SetTexture("_MainTex", texture);
        AddMesh(_island, "Holograms", glass.Build("MenuWorldHolo"), holo);

        BuildSky(seat);
        BuildCode(seat);

        GameObject sun = new GameObject("MenuWorldSun") { layer = VRRig.Layer };
        sun.transform.SetParent(_root, false);
        sun.transform.localRotation = Quaternion.Euler(48f, 26f, 0f);
        _sun = sun.AddComponent<Light>();
        _sun.type = LightType.Directional;
        _sun.intensity = 1.15f; // same as the duel arena light
        _sun.color = new Color(1f, 0.97f, 0.92f);
        _sun.cullingMask = 1 << VRRig.Layer;
        _sun.shadows = LightShadows.None;
        Debug.Log($"[DCGO VR] Menu world built ({palette.Colors.Count} colours)");
    }

    static MeshRenderer AddMesh(Transform parent, string name, Mesh mesh, Material material)
    {
        GameObject go = new GameObject(name) { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    static bool NearPlayer(float degrees, float margin) => Mathf.Abs(Mathf.DeltaAngle(degrees, 270f)) < margin;

    void BuildIsland(Kit kit)
    {
        System.Random random = new System.Random(7);
        float R = IslandRadius;

        // Top: grass with lighter patches.
        Ring(kit, Vector3.zero, 0f, R, 0f, Grass, 0f, 360f, 96);

        for (int i = 0; i < 26; i++)
        {
            float a = (float)random.NextDouble() * 360f;
            float r = 6f + (float)random.NextDouble() * (R - 8f);
            Ring(kit, Polar(r, a, 0f), 0f, 0.8f + (float)random.NextDouble() * 1.6f, 0.008f, GrassLight, 0f, 360f, 10);
        }

        // Underside: grassy lip, earth bands, rocky cone, glowing crystal under the tip.
        Lathe(kit, Vector3.zero, new[]
        {
            (-27f, 2.5f, Earth3), (-15f, 13f, Earth3), (-6f, 20.5f, Earth2), (-1.2f, R * 0.985f, Earth1), (-0.4f, R, GrassDark), (0f, R, GrassDark),
        }, 48, 0.06f, 11);
        Gem(kit, new Vector3(0f, -31f, 0f), 4f, 10f, DeepCrystal, 20f);

        for (int i = 0; i < 9; i++)
        {
            float a = i * 40f + 10f;
            Gem(kit, Polar(9f + (i % 3) * 3f, a, -17f - (i % 4) * 3f), 1.4f, 3.6f, i % 2 == 0 ? DeepCrystal : Crystal, a);
        }

        // Paths: plaza edge, three rings, eight spokes (one runs from you to the tree).
        Ring(kit, Vector3.zero, 0f, 4.6f, 0.02f, Stone, 0f, 360f, 48);
        Ring(kit, Vector3.zero, 4.6f, 5.6f, 0.025f, Path);
        Ring(kit, Vector3.zero, 6.6f, 7.8f, 0.02f, Water);
        Ring(kit, Vector3.zero, 9.6f, 10.6f, 0.025f, Path);
        Ring(kit, Vector3.zero, 13.4f, 14.6f, 0.025f, Path, 0f, 360f, 96);
        Ring(kit, Vector3.zero, 18.4f, 19.4f, 0.025f, Path, 0f, 360f, 96);

        for (int k = 0; k < 8; k++)
        {
            float a = 270f + k * 45f;
            Strip(kit, Polar(5.6f, a, 0.03f), Polar(22.8f, a, 0.03f), 1.1f, Path);
            // little bridges over the water ring
            Strip(kit, Polar(6.4f, a, 0.05f), Polar(8f, a, 0.05f), 1.3f, Stone);
        }

        // Your spot: a round stone viewpoint with a crystal rim.
        Ring(kit, Polar(PlayerRadius, 270f, 0f), 0f, 1.7f, 0.04f, Stone, 0f, 360f, 32);
        Ring(kit, Polar(PlayerRadius, 270f, 0f), 1.7f, 1.85f, 0.045f, Crystal, 0f, 360f, 32);

        // The colourful circle of flowers.
        for (int i = 0; i < 6; i++)
        {
            RaisedRing(kit, 11.3f, 12.7f, 0.22f, Flowers[i], i * 60f + 2f, i * 60f + 58f, 72);
        }

        // Lake with a channel to the edge.
        Ring(kit, Polar(17f, 25f, 0f), 0f, 3.2f, 0.03f, Water, 0f, 360f, 24);
        Strip(kit, Polar(19.5f, 28f, 0.03f), Polar(R - 0.1f, 32f, 0.03f), 1.2f, Water);

        BuildLandmark(kit);
        BuildDistricts(kit, random);
        BuildFrame(kit);
    }

    void BuildLandmark(Kit kit)
    {
        // White stone base, roots, trunk, a cloud of leaves and the glowing ring.
        Lathe(kit, Vector3.zero, new[] { (0f, 4f, Stone), (0.35f, 3.6f, Stone) }, 32, 0f, 1, capTop: true);

        for (int i = 0; i < 7; i++)
        {
            float a = i * 51.4f + 12f;
            Vector3 centre = Polar(2.9f, a, 0.55f);
            Box(kit, centre, new Vector3(0.9f, 0.55f, 3.6f), 90f - a, Bark);
        }

        Lathe(kit, Vector3.zero, new[] { (0.3f, 2.3f, Bark), (1.6f, 1.6f, Bark), (5f, 1.25f, Bark), (7.6f, 1.6f, Bark), (8.2f, 0.9f, Bark) }, 14, 0.09f, 3, capTop: true);

        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f + 30f;
            Box(kit, Polar(2.2f, a, 7.6f), new Vector3(0.6f, 0.6f, 3.6f), 90f - a, Bark);
        }

        (Vector3 c, float r)[] leaves =
        {
            (new Vector3(0f, 10.6f, 0f), 5.2f), (new Vector3(3.6f, 9f, 1f), 3.8f), (new Vector3(-3.7f, 9.2f, -0.8f), 3.9f),
            (new Vector3(1f, 9f, -3.7f), 3.6f), (new Vector3(-1f, 9.3f, 3.6f), 3.7f), (new Vector3(2.4f, 12.6f, -1.5f), 3.2f),
            (new Vector3(-2.2f, 12.7f, 1.4f), 3.3f), (new Vector3(0f, 14.2f, 0f), 2.8f), (new Vector3(4.9f, 8f, -2.6f), 2.6f),
            (new Vector3(-4.7f, 8.2f, 2.6f), 2.6f), (new Vector3(3.2f, 8.4f, 3.6f), 2.5f), (new Vector3(-3f, 8.3f, -3.8f), 2.5f),
        };

        for (int i = 0; i < leaves.Length; i++)
        {
            Color main = i % 3 == 0 ? Leaf1 : i % 3 == 1 ? Leaf2 : Leaf3;
            Blob(kit, leaves[i].c, new Vector3(leaves[i].r, leaves[i].r * 0.85f, leaves[i].r), main, 0.13f, 40 + i, i % 2 == 0 ? Leaf2 : Leaf1);
        }

        Torus(kit, new Vector3(0f, 8.3f, 0f), 6.4f, 0.2f, Glow, 72, 6);
        Torus(kit, new Vector3(0f, 8.0f, 0f), 6.9f, 0.08f, new Color(1f, 0.78f, 0.35f), 72, 4);
    }

    void BuildDistricts(Kit kit, System.Random random)
    {
        Color[] glass = { Glass1, Glass2, Glass3 };

        // Glassy blocks in every sector between the spokes, away from your spot.
        for (int sector = 0; sector < 8; sector++)
        {
            float start = 270f + sector * 45f;

            for (int ring = 0; ring < 2; ring++)
            {
                int count = ring == 0 ? 5 : 7;
                float inner = ring == 0 ? 15.6f : 20.2f;
                float outer = ring == 0 ? 17.6f : 22.8f;

                for (int i = 0; i < count; i++)
                {
                    float a = start + 7f + (float)random.NextDouble() * 31f;

                    if (NearPlayer(a, 32f))
                    {
                        continue;
                    }

                    float r = Mathf.Lerp(inner, outer, (float)random.NextDouble());
                    float w = 1.2f + (float)random.NextDouble() * 1.5f;
                    float d = 1.2f + (float)random.NextDouble() * 1.5f;
                    float h = (ring == 0 ? 2.5f : 4f) + (float)random.NextDouble() * (ring == 0 ? 6f : 9f);
                    Color c = glass[random.Next(glass.Length)];
                    Box(kit, Polar(r, a, h * 0.5f), new Vector3(w, h, d), 90f - a, c, Roof);

                    if (random.NextDouble() < 0.4)
                    {
                        Box(kit, Polar(r, a, h + 0.6f), new Vector3(w * 0.6f, 1.2f, d * 0.6f), 90f - a, glass[random.Next(glass.Length)], Roof);
                    }
                }
            }
        }

        // Station towers on the far side (they rise behind the tree).
        float[] towers = { 72f, 84f, 97f, 108f };

        for (int i = 0; i < towers.Length; i++)
        {
            float h = 15f + i % 2 * 6f;
            Vector3 foot = Polar(21.2f + (i % 2) * 1.2f, towers[i], 0f);
            Lathe(kit, foot, new[] { (0f, 1.5f, Glass3), (h * 0.7f, 1.3f, Glass1), (h, 0.9f, Glass2) }, 8, 0f, 60 + i, capTop: true);
            Torus(kit, foot + Vector3.up * h * 0.72f, 1.6f, 0.12f, Crystal, 16, 4);
        }

        // Research dome (front right) and a rest-area pavilion (left).
        Vector3 dome = Polar(17.2f, 330f, 0f);
        Blob(kit, dome, new Vector3(3f, 2.4f, 3f), Glass2, 0f, 70);
        Ring(kit, dome, 0f, 3.4f, 0.03f, Stone, 0f, 360f, 24);
        Vector3 rest = Polar(17f, 205f, 0f);

        for (int i = 0; i < 4; i++)
        {
            Box(kit, rest + Polar(1.6f, i * 90f + 45f, 1.1f), new Vector3(0.2f, 2.2f, 0.2f), 0f, Roof);
        }

        Lathe(kit, rest, new[] { (2.2f, 2.6f, Glow), (3.2f, 0.2f, Glow) }, 8, 0f, 71, capBottom: true);

        // Trees dotted around the grass.
        for (int i = 0; i < 26; i++)
        {
            float a = (float)random.NextDouble() * 360f;
            // inner trees between the water and the second ring, outer ones among the blocks
            float r = i >= 14 ? 15f + (float)random.NextDouble() * 8f : 8.4f + (float)random.NextDouble() * 0.6f;

            if (NearPlayer(a, i >= 14 ? 20f : 9f))
            {
                continue;
            }

            Tree(kit, Polar(r, a, 0f), 2.4f + (float)random.NextDouble() * 1.8f, 80 + i);
        }
    }

    void BuildFrame(Kit kit)
    {
        // Raised walkway ring with posts.
        Ring(kit, Vector3.up * 2.4f, 20.6f, 21.4f, 0f, Roof, 0f, 360f, 96);
        Torus(kit, Vector3.up * 3.1f, 21.4f, 0.07f, Metal, 96, 4);

        for (int i = 0; i < 24; i++)
        {
            Box(kit, Polar(21f, i * 15f + 7.5f, 1.2f), new Vector3(0.22f, 2.4f, 0.22f), 0f, Metal);
        }

        // Outer support ring with struts and glowing pylons.
        Torus(kit, Vector3.up * -2f, 27f, 0.45f, Metal, 96, 6);

        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f + 22.5f;
            Vector3 inner = Polar(IslandRadius - 1.5f, a, -2.5f);
            Vector3 outer = Polar(27f, a, -2f);
            Box(kit, (inner + outer) * 0.5f, new Vector3(0.5f, 0.5f, (outer - inner).magnitude), 90f - a, Metal);
            Gem(kit, outer, 1.3f, 5.5f, Crystal, a);
        }
    }

    void BuildSatellite(Kit kit, float r, int seed)
    {
        System.Random random = new System.Random(seed);
        Ring(kit, Vector3.zero, 0f, r, 0f, Grass, 0f, 360f, 24);
        Lathe(kit, Vector3.zero, new[] { (-r * 1.3f, r * 0.12f, Earth3), (-r * 0.45f, r * 0.75f, Earth2), (-0.3f, r, GrassDark), (0f, r, GrassDark) }, 16, 0.1f, seed);
        Ring(kit, new Vector3(r * 0.3f, 0f, -r * 0.2f), 0f, r * 0.32f, 0.02f, Water, 0f, 360f, 14);

        for (int i = 0; i < 3; i++)
        {
            float a = (float)random.NextDouble() * 360f;
            Tree(kit, Polar(r * 0.55f, a, 0f), 2.2f + (float)random.NextDouble() * 1.5f, seed * 7 + i);
        }

        Gem(kit, new Vector3(0f, -r * 1.45f, 0f), r * 0.25f, r * 0.6f, DeepCrystal, seed);
    }

    // From the lake channel and two more spots on the rim, straight down into the clouds.
    // Two-sided strips with UVs running down them (the stripes scroll).
    static Mesh FallsMesh()
    {
        float[] angles = { 32f, 140f, 205f };
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        foreach (float a in angles)
        {
            Vector3 out1 = Polar(1f, a, 0f);
            Vector3 side = Vector3.Cross(Vector3.up, out1) * 1.4f;
            Vector3 top = Polar(IslandRadius + 0.25f, a, 0.04f);
            Vector3 bottom = Polar(IslandRadius + 1.6f, a, -26f);
            int i = vertices.Count;
            vertices.AddRange(new[] { top - side, top + side, bottom + side, bottom - side });
            uvs.AddRange(new[] { new Vector2(0f, 3f), new Vector2(1f, 3f), new Vector2(1f, 0f), new Vector2(0f, 0f) });
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        Mesh mesh = new Mesh { name = "MenuWorldFalls" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    void BuildHolograms(Kit kit)
    {
        // Floating hexagon panels above the research dome.
        Vector3 dome = Polar(17.2f, 330f, 0f);

        for (int i = 0; i < 7; i++)
        {
            Vector3 c = dome + new Vector3((i % 3 - 1) * 1.9f, 6.5f + (i / 3) * 1.65f, (i % 2) * 0.4f);
            Vector3 face = (Vector3.zero - dome).normalized;
            Quaternion turn = Quaternion.LookRotation(face, Vector3.up);

            for (int s = 0; s < 6; s++)
            {
                Vector3 a = c + turn * new Vector3(Mathf.Cos(s * 60f * Mathf.Deg2Rad), Mathf.Sin(s * 60f * Mathf.Deg2Rad), 0f) * 0.85f;
                Vector3 b = c + turn * new Vector3(Mathf.Cos((s + 1) * 60f * Mathf.Deg2Rad), Mathf.Sin((s + 1) * 60f * Mathf.Deg2Rad), 0f) * 0.85f;
                kit.Tri(c, a, b, Crystal, face, twoSided: true);
            }
        }
    }

    void BuildClouds(Kit kit)
    {
        System.Random random = new System.Random(5);

        for (int i = 0; i < 46; i++)
        {
            float a = (float)random.NextDouble() * 360f;
            float r = 36f + (float)random.NextDouble() * 34f;
            float y = -24f - (float)random.NextDouble() * 12f;
            float size = 7f + (float)random.NextDouble() * 9f;
            Blob(kit, Polar(r, a, y), new Vector3(size, size * 0.35f, size * 0.8f), Cloud, 0.12f, 200 + i, CloudShade);
        }

        // A few tall clouds on the horizon.
        for (int i = 0; i < 9; i++)
        {
            float a = i * 40f + (float)random.NextDouble() * 20f;
            float size = 9f + (float)random.NextDouble() * 6f;
            Blob(kit, Polar(70f, a, -6f + (float)random.NextDouble() * 6f), new Vector3(size, size * 0.6f, size * 0.7f), Cloud, 0.15f, 300 + i, CloudShade);
        }
    }

    void BuildSky(Vector3 seat)
    {
        // Inside of a big sphere around you, coloured by height (gradient texture).
        const int lon = 24;
        const int lat = 12;
        const float radius = 92f;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int y = 0; y <= lat; y++)
        {
            float v = y / (float)lat;
            float elevation = Mathf.Lerp(-90f, 90f, v) * Mathf.Deg2Rad;

            for (int x = 0; x <= lon; x++)
            {
                float a = x / (float)lon * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(elevation) * Mathf.Cos(a), Mathf.Sin(elevation), Mathf.Cos(elevation) * Mathf.Sin(a)) * radius);
                uvs.Add(new Vector2(0.5f, v));
            }
        }

        for (int y = 0; y < lat; y++)
        {
            for (int x = 0; x < lon; x++)
            {
                int i = y * (lon + 1) + x;
                // Faces point inwards (seen from inside).
                triangles.AddRange(new[] { i, i + 1, i + lon + 1, i + 1, i + lon + 2, i + lon + 1 });
            }
        }

        Mesh mesh = new Mesh { name = "MenuWorldSky" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        // Make sure the winding faces inwards whatever the maths above says.
        Vector3[] p = mesh.vertices;
        int[] t = mesh.triangles;
        int probe = (lat / 2) * lon * 6;
        Vector3 n = Vector3.Cross(p[t[probe + 1]] - p[t[probe]], p[t[probe + 2]] - p[t[probe]]);

        if (Vector3.Dot(n, (p[t[probe]] + p[t[probe + 1]] + p[t[probe + 2]]) / 3f) > 0f)
        {
            for (int i = 0; i < t.Length; i += 3)
            {
                (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }

            mesh.triangles = t;
        }

        Texture2D gradient = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "DCGOVR_MenuSky" };
        Color below = new Color(0.66f, 0.80f, 0.95f);
        Color horizon = new Color(0.92f, 0.97f, 1f);
        Color top = new Color(0.30f, 0.56f, 0.92f);

        for (int y = 0; y < 128; y++)
        {
            float v = y / 127f; // 0 = straight down, 0.5 = horizon, 1 = straight up
            Color c = v < 0.5f ? Color.Lerp(below, horizon, Mathf.Pow(v / 0.5f, 3f)) : Color.Lerp(horizon, top, Mathf.Pow((v - 0.5f) / 0.5f, 0.6f));
            gradient.SetPixel(0, y, c);
        }

        gradient.Apply(false, true);
        Material sky = VRMaterials.Unlit(Color.white);
        sky.SetTexture("_BaseMap", gradient);
        sky.SetTexture("_MainTex", gradient);
        sky.renderQueue = 1990; // behind everything else
        Transform dome = AddMesh(_root, "Sky", mesh, sky).transform;
        dome.localPosition = seat + Vector3.up * 1.2f;
    }

    // ---------------------------------------------------------------- digital code sky

    // Two walls of falling glyph streams around you (far behind the clouds, so the menu
    // stays readable), each cut into height bands of rising then falling opacity so the
    // code fades out towards the sea of clouds and the top of the sky.
    void BuildCode(Vector3 seat)
    {
        Texture2D glyphs = CodeTexture();
        (float radius, float speed, float tileU, float offsetU, float strength)[] layers =
        {
            (84f, 0.045f, 16f, 0f, 1f),
            (74f, 0.07f, 13f, 0.37f, 0.6f),
        };
        // Height bands (rig space, from your seat): bottom, alpha.
        float[] bands = { -22f, -8f, 4f, 22f, 38f, 50f };
        float[] alpha = { 0.10f, 0.28f, 0.42f, 0.28f, 0.10f };
        Color tint = new Color(0.32f, 1f, 0.78f);

        foreach (var layer in layers)
        {
            for (int b = 0; b < alpha.Length; b++)
            {
                Material material = VRMaterials.Transparent(new Color(tint.r, tint.g, tint.b, alpha[b] * layer.strength));
                material.SetTexture("_BaseMap", glyphs);
                material.SetTexture("_MainTex", glyphs);
                // Glyph cells come out roughly square at this distance; the bands share one
                // continuous V so the streams run straight through the band edges.
                float vPerMetre = 1f / 80f;
                material.SetTextureScale("_BaseMap", new Vector2(layer.tileU, 1f));
                material.SetTextureOffset("_BaseMap", new Vector2(layer.offsetU, 0f));
                material.renderQueue = 2990; // after the opaque world, before the island's glass and menu
                Mesh band = CodeBand(layer.radius, bands[b], bands[b + 1], vPerMetre);
                Transform t = AddMesh(_root, "DigitalCode", band, material).transform;
                t.localPosition = seat;
                _code.Add((material, layer.speed));
            }
        }
    }

    // Open cylinder section facing inwards (both windings, so it shows whatever the maths).
    static Mesh CodeBand(float radius, float bottom, float top, float vPerMetre)
    {
        const int segments = 64;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float a = u * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            vertices.Add(dir + Vector3.up * bottom);
            vertices.Add(dir + Vector3.up * top);
            uvs.Add(new Vector2(u, bottom * vPerMetre));
            uvs.Add(new Vector2(u, top * vPerMetre));
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            triangles.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2, a, a + 3, a + 1, a, a + 2, a + 3 });
        }

        Mesh mesh = new Mesh { name = "MenuWorldCode" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // 32 columns x 64 rows of 8 px glyph cells. Each used column holds one or two falling
    // streams: a bright head glyph at the bottom and a fading trail above it. Wraps vertically.
    internal static Texture2D CodeTexture()
    {
        const int cols = 32;
        const int rows = 64;
        const int cell = 8;
        System.Random random = new System.Random(2026);
        Color[] pixels = new Color[cols * cell * rows * cell];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(1f, 1f, 1f, 0f); // white when blurred into mipmaps, never grey edges
        }

        // A small alphabet of blocky 5x7 glyphs (made-up "digital" characters, 0/1-like and hex-like).
        bool[][] alphabet = new bool[20][];

        for (int g = 0; g < alphabet.Length; g++)
        {
            alphabet[g] = new bool[35];

            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    bool on = random.NextDouble() < 0.5 || (x == 0 && (y == 0 || y == 6));
                    alphabet[g][y * 5 + x] = on;
                    alphabet[g][y * 5 + 4 - x] = g % 3 == 0 ? on : random.NextDouble() < 0.45; // some symmetric
                }
            }
        }

        void Glyph(int col, int row, float a)
        {
            bool[] shape = alphabet[random.Next(alphabet.Length)];
            row = ((row % rows) + rows) % rows;

            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    if (!shape[y * 5 + x])
                    {
                        continue;
                    }

                    int px = col * cell + 1 + x;
                    int py = row * cell + y;
                    pixels[py * cols * cell + px] = new Color(1f, 1f, 1f, a);
                }
            }
        }

        for (int col = 0; col < cols; col++)
        {
            if (random.NextDouble() < 0.28)
            {
                continue; // empty column
            }

            int streams = random.NextDouble() < 0.4 ? 2 : 1;

            for (int s = 0; s < streams; s++)
            {
                int head = random.Next(rows);
                int length = 6 + random.Next(20);

                for (int k = 0; k < length; k++)
                {
                    float a = k == 0 ? 1f : Mathf.Lerp(0.75f, 0.08f, k / (float)length);
                    Glyph(col, head + k, a); // trail goes up from the head (the head leads the fall)
                }
            }
        }

        Texture2D texture = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, true)
        {
            name = "DCGOVR_DigitalCode",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    static Texture2D FallTexture()
    {
        Texture2D texture = new Texture2D(32, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "DCGOVR_MenuFalls" };
        System.Random random = new System.Random(3);
        float[] columns = new float[32];

        for (int x = 0; x < 32; x++)
        {
            columns[x] = (float)random.NextDouble();
        }

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float edge = Mathf.Sin(x / 31f * Mathf.PI);
                float streak = 0.55f + 0.45f * Mathf.Sin((y / 64f + columns[x]) * Mathf.PI * 2f);
                float a = Mathf.Clamp01(edge * (0.35f + 0.65f * streak));
                texture.SetPixel(x, y, new Color(0.75f + 0.25f * streak, 0.95f, 1f, a));
            }
        }

        texture.Apply(true, true);
        return texture;
    }

    // ================================================================ characters

    // Who walks where. Tamers come with a partner walking beside them.
    sealed class Group
    {
        public string[] Keys;
        public float[] Heights;
        public float Radius;
        public float Angle;
        public float Direction = 1f;
        public float Speed;
        public bool Stays;          // hangs around one spot (near you), no walking
        public bool Flies;          // circles the island in the air, never stops
        public float Altitude;
        public float Phase;
        public Vector3 Spot;        // for Stays: island-space position
        public float Facing;        // for Stays: yaw in island space
        public float StateUntil;
        public bool Walking;
        public readonly List<Walker> Members = new List<Walker>();
    }

    sealed class Walker
    {
        public string Path;
        public Transform Root;
        public Transform Model;
        public Animation Anim;
        public string Move;
        public string Idle;
        public string Cheer;
        public float MoveSpeed = 1f;
        public float Grow;
        public float Side;          // offset across the path
        public float NextCheer;
        public float CheerUntil;
    }

    static readonly (string[] keys, float[] heights, float radius, float angle, float dir, float speed, bool stays)[] Cast =
    {
        (new[] { "abducted-boy", "agumon" }, new[] { 1.4f, 0.85f }, 10.1f, 200f, 1f, 1.1f, false),
        (new[] { "abducted-girl", "patamon" }, new[] { 1.35f, 0.6f }, 18.9f, 330f, -1f, 1.0f, false),
        (new[] { "takemiya", "gabumon" }, new[] { 1.75f, 0.9f }, 5.1f, 60f, 1f, 0.9f, false),
        (new[] { "guilmon" }, new[] { 1.0f }, 18.9f, 120f, 1f, 2.2f, false),
        (new[] { "terriermon" }, new[] { 0.6f }, 10.1f, 20f, -1f, 2.0f, false),
        // Hanging around near you, just beside the menu panels (x/z from your spot in "radius"/"angle").
        (new[] { "palmon" }, new[] { 0.85f }, 3.0f, 3.4f, 1f, 0f, true),
        (new[] { "biyomon" }, new[] { 0.8f }, -2.8f, 3.6f, 1f, 0f, true),
    };

    // Flying Digimon circling the island: (model, height, circle radius, altitude, start angle, direction, speed m/s).
    static readonly (string key, float height, float radius, float altitude, float angle, float dir, float speed)[] Flyers =
    {
        ("birdramon", 2.6f, 31f, 15f, 100f, 1f, 6.5f),
        ("airdramon", 3.0f, 25f, 11f, 300f, -1f, 5f),
        ("kabuterimon", 2.2f, 19f, 7.5f, 200f, 1f, 3.8f),
        ("aquilamon", 2.0f, 35f, 19f, 20f, -1f, 7f),
    };

    readonly List<Group> _groups = new List<Group>();
    int _loadToken;

    async void StartCharacters()
    {
        if (!VRModelLibrary.Available || !VRSettings.Models)
        {
            return;
        }

        int token = ++_loadToken;
        System.Random random = new System.Random();

        foreach (var cast in Cast)
        {
            Group group = new Group
            {
                Keys = cast.keys,
                Heights = cast.heights,
                Radius = cast.radius,
                Angle = cast.angle,
                Direction = cast.dir,
                Speed = cast.speed,
                Stays = cast.stays,
                Walking = !cast.stays,
                StateUntil = Time.unscaledTime + 6f + (float)random.NextDouble() * 8f,
            };
            // Stayers stand at an offset from you (x right, z ahead) and face you.
            Vector3 you = Polar(PlayerRadius, 270f, 0f);

            if (group.Stays)
            {
                group.Spot = you + new Vector3(cast.radius, 0.03f, cast.angle);
                group.Facing = Quaternion.LookRotation(you - group.Spot, Vector3.up).eulerAngles.y;
            }
            _groups.Add(group);

            for (int i = 0; i < group.Keys.Length; i++)
            {
                // One model at a time keeps the menu smooth while they load.
                Walker walker = await LoadWalker(group.Keys[i], group.Heights[i], token);

                if (token != _loadToken || _island == null)
                {
                    if (walker != null)
                    {
                        FreeWalker(walker);
                    }

                    return;
                }

                if (walker == null)
                {
                    continue;
                }

                walker.Side = group.Keys.Length > 1 ? (i == 0 ? -0.45f : 0.45f) : 0f;
                walker.NextCheer = Time.unscaledTime + 4f + (float)random.NextDouble() * 10f;
                group.Members.Add(walker);
                PlayLoop(walker, group.Walking ? walker.Move : walker.Idle, group.Walking ? walker.MoveSpeed : 1f);
            }
        }

        foreach (var flyer in Flyers)
        {
            Walker walker = await LoadWalker(flyer.key, flyer.height, token);

            if (token != _loadToken || _island == null)
            {
                if (walker != null)
                {
                    FreeWalker(walker);
                }

                return;
            }

            if (walker == null)
            {
                continue;
            }

            Group group = new Group
            {
                Keys = new[] { flyer.key },
                Heights = new[] { flyer.height },
                Radius = flyer.radius,
                Altitude = flyer.altitude,
                Angle = flyer.angle,
                Direction = flyer.dir,
                Speed = flyer.speed,
                Flies = true,
                Walking = true,
                Phase = (float)random.NextDouble() * 10f,
            };
            group.Members.Add(walker);
            _groups.Add(group);
            // Flying: their move cycle (wings flapping) at normal speed; idle (hovering) if they have none.
            PlayLoop(walker, walker.Move ?? walker.Idle, 1f);
        }
    }

    async System.Threading.Tasks.Task<Walker> LoadWalker(string key, float height, int token)
    {
        VRModelLibrary.ModelInfo info = VRModelLibrary.All.Find(m => m.Key == key);

        if (info == null)
        {
            return null;
        }

        Transform root = new GameObject("Walker " + key) { layer = VRRig.Layer }.transform;
        root.SetParent(_island, false);
        root.localScale = Vector3.one * 0.001f;
        Transform holder = null;

        try
        {
            holder = await VRFieldModels.LoadPreview(info.Path, root);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Menu world: " + key + " failed: " + exception.Message);
        }

        if (holder == null || root == null || token != _loadToken)
        {
            if (holder != null)
            {
                VRFieldModels.ReleasePreview(info.Path, holder);
            }

            if (root != null)
            {
                Destroy(root.gameObject);
            }

            return null;
        }

        VRFieldModels.Normalize(holder, height, out _);
        Walker walker = new Walker { Path = info.Path, Root = root, Model = holder, Anim = holder.GetComponentInChildren<Animation>(true) };

        if (walker.Anim != null)
        {
            walker.Anim.playAutomatically = false;
            walker.Anim.cullingType = AnimationCullingType.BasedOnRenderers;
            // Digimon: chrXXX_fw01 / _br01 / _bn01 / _bv01. People: npcXXXa_fw01_01 / _fn01_01
            // (field walk / stand); their _bn01 is a battle stance, so it is only the last resort.
            string walk = ClipName(walker.Anim, "_fw01") ?? ClipName(walker.Anim, "_fw01_01");
            string run = ClipName(walker.Anim, "_br01");
            walker.Move = walk ?? run;
            walker.MoveSpeed = walk != null ? 1f : 0.8f;
            walker.Idle = ClipName(walker.Anim, "_fn01") ?? ClipName(walker.Anim, "_fn01_01") ?? ClipName(walker.Anim, "_bn01");
            walker.Cheer = ClipName(walker.Anim, "_bv01") ?? ClipName(walker.Anim, "_fe01") ?? ClipName(walker.Anim, "_fe01_01");
            Debug.Log($"[DCGO VR] Menu world: {key} walks '{walker.Move}', idles '{walker.Idle}'");

            foreach (string loop in new[] { walker.Move, walker.Idle })
            {
                if (loop != null)
                {
                    walker.Anim[loop].wrapMode = WrapMode.Loop;
                }
            }
        }

        return walker;
    }

    static string ClipName(Animation anim, string suffix)
    {
        AnimationClip clip = VRFieldModels.Clip(anim, suffix);
        return clip != null ? clip.name : null;
    }

    static void PlayLoop(Walker walker, string clip, float speed)
    {
        if (walker.Anim == null || clip == null)
        {
            return;
        }

        walker.Anim[clip].speed = speed;
        walker.Anim.CrossFade(clip, 0.3f);
    }

    void UpdateCharacters(float dt)
    {
        float now = Time.unscaledTime;
        System.Random random = null;

        foreach (Group group in _groups)
        {
            if (group.Members.Count == 0)
            {
                continue;
            }

            if (group.Flies)
            {
                Fly(group, now, dt);
                continue;
            }

            // Walk a while, stop for a moment, sometimes turn back.
            if (!group.Stays && now >= group.StateUntil)
            {
                random ??= new System.Random();
                group.Walking = !group.Walking;
                group.StateUntil = now + (group.Walking ? 8f + (float)random.NextDouble() * 10f : 2.5f + (float)random.NextDouble() * 3f);

                if (group.Walking && random.NextDouble() < 0.25)
                {
                    group.Direction = -group.Direction;
                }

                foreach (Walker walker in group.Members)
                {
                    PlayLoop(walker, group.Walking ? walker.Move : walker.Idle, group.Walking ? walker.MoveSpeed : 1f);
                }
            }

            if (group.Walking)
            {
                group.Angle += group.Direction * group.Speed / group.Radius * dt * Mathf.Rad2Deg;
            }

            foreach (Walker walker in group.Members)
            {
                if (walker.Root == null)
                {
                    continue;
                }

                walker.Grow = Mathf.MoveTowards(walker.Grow, 1f, dt / 0.4f);
                walker.Root.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.SmoothStep(0f, 1f, walker.Grow));
                if (group.Stays)
                {
                    walker.Root.localPosition = group.Spot;
                    walker.Root.localRotation = Quaternion.Euler(0f, group.Facing, 0f);

                    // Now and then a happy little pose, then back to idle.
                    if (walker.Cheer != null && now >= walker.NextCheer && walker.Anim != null)
                    {
                        random ??= new System.Random();
                        walker.Anim.CrossFade(walker.Cheer, 0.2f);
                        walker.CheerUntil = now + Mathf.Max(1f, walker.Anim[walker.Cheer].length - 0.2f);
                        walker.NextCheer = now + 10f + (float)random.NextDouble() * 12f;
                    }

                    if (walker.CheerUntil > 0f && now >= walker.CheerUntil)
                    {
                        walker.CheerUntil = 0f;
                        PlayLoop(walker, walker.Idle, 1f);
                    }

                    continue;
                }

                walker.Root.localPosition = Polar(group.Radius + walker.Side, group.Angle, 0.03f);

                // Face along the path (models look down +Z).
                Vector3 tangent = new Vector3(-Mathf.Sin(group.Angle * Mathf.Deg2Rad), 0f, Mathf.Cos(group.Angle * Mathf.Deg2Rad)) * group.Direction;
                Quaternion look = Quaternion.LookRotation(tangent, Vector3.up);
                walker.Root.localRotation = Quaternion.Slerp(walker.Root.localRotation, look, 1f - Mathf.Exp(-6f * dt));
            }
        }
    }

    // A wide circle around the island that slowly breathes in and out and up and down,
    // nose along the path, banking into the turn.
    static void Fly(Group group, float now, float dt)
    {
        float wave = now * 0.12f + group.Phase;
        float radius = group.Radius + Mathf.Sin(wave) * 4f;
        group.Angle += group.Direction * group.Speed / radius * dt * Mathf.Rad2Deg;
        float climb = Mathf.Cos(now * 0.5f + group.Phase) * 0.5f; // vertical speed of the bob below (m/s)

        foreach (Walker walker in group.Members)
        {
            if (walker.Root == null)
            {
                continue;
            }

            walker.Grow = Mathf.MoveTowards(walker.Grow, 1f, dt / 0.6f);
            walker.Root.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.SmoothStep(0f, 1f, walker.Grow));
            float height = group.Altitude + Mathf.Sin(now * 0.5f + group.Phase) * 1.0f + Mathf.Sin(wave * 1.7f) * 1.5f;
            walker.Root.localPosition = Polar(radius, group.Angle, height);

            Vector3 tangent = new Vector3(-Mathf.Sin(group.Angle * Mathf.Deg2Rad), 0f, Mathf.Cos(group.Angle * Mathf.Deg2Rad)) * group.Direction;
            Vector3 heading = (tangent * group.Speed + Vector3.up * climb).normalized;
            // Turning towards the centre: roll the inner wing down.
            Quaternion look = Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(0f, 0f, -group.Direction * 14f);
            walker.Root.localRotation = Quaternion.Slerp(walker.Root.localRotation, look, 1f - Mathf.Exp(-4f * dt));
        }
    }

    void StopCharacters()
    {
        _loadToken++;

        foreach (Group group in _groups)
        {
            foreach (Walker walker in group.Members)
            {
                FreeWalker(walker);
            }
        }

        _groups.Clear();
    }

    static void FreeWalker(Walker walker)
    {
        if (walker.Model != null)
        {
            VRFieldModels.ReleasePreview(walker.Path, walker.Model);
        }

        if (walker.Root != null)
        {
            Destroy(walker.Root.gameObject);
        }
    }
}
#endif
