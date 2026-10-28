#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Duel surroundings in the style of a Digital World arena: an endless dark floor with a
// glowing cyan grid that fades into the distance, a dark-green horizon wall with drifting
// data specks and a bright glow where it meets the floor, and tall streaming "data
// waterfalls" of light around the arena. Built entirely in code (procedural textures, no
// assets); unlit and cheap. Follows the arena (it is a child of the arena anchor).
public class VRDuelEnvironment : MonoBehaviour
{
    const float FloorSize = 180f;
    const float GridCell = 1.5f;          // metres per grid square
    const float WallRadius = 60f;
    const float WallHeight = 45f;
    const int Waterfalls = 10;

    static readonly Color Cyan = new Color(0.15f, 1f, 0.85f);
    static readonly Color FloorDark = new Color(0.015f, 0.05f, 0.05f);

    Transform _arena;
    Transform _root;
    Material _fallMaterial;
    Material _wallMaterial;

    public static VRDuelEnvironment Create(Transform parent, Transform arena)
    {
        GameObject go = new GameObject("DuelEnvironment") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRDuelEnvironment environment = go.AddComponent<VRDuelEnvironment>();
        environment._arena = arena;
        return environment;
    }

    void LateUpdate()
    {
        bool show = VRSettings.DuelEnvironment && _arena != null
            && ((BoardObserver.Context != null && BoardObserver.Context.You != null) || VRModelStudio.Active);

        if (show && _root == null)
        {
            Build();
        }

        if (_root != null && _root.gameObject.activeSelf != show)
        {
            _root.gameObject.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        // Streaming light: scroll the waterfalls down, drift the wall specks slowly.
        float t = Time.unscaledTime;
        _fallMaterial.SetTextureOffset("_BaseMap", new Vector2(0f, t * 0.35f));
        _wallMaterial.SetTextureOffset("_BaseMap", new Vector2(t * 0.002f, 0f));
    }

    // ---------------------------------------------------------------- build

    void Build()
    {
        _root = new GameObject("DigitalWorld") { layer = VRRig.Layer }.transform;
        _root.SetParent(_arena, false);
        // Centred on your seat, just under the stage mats.
        Vector3 seat = VRFieldModels.PreviewSpot(true);
        _root.localPosition = new Vector3(0f, -0.04f, seat.z - 3f);

        // Floor: grid + a radial darkening on top so the lines fade out with distance.
        Material grid = VRMaterials.Unlit(Color.white);
        Texture2D gridTexture = GridTexture();
        grid.SetTexture("_BaseMap", gridTexture);
        grid.SetTextureScale("_BaseMap", Vector2.one * (FloorSize / GridCell));
        Quad("GridFloor", new Vector3(0f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(FloorSize, FloorSize, 1f), grid);

        Material fade = VRMaterials.Transparent(Color.white);
        Texture2D fadeTexture = RadialFade();
        fade.SetTexture("_BaseMap", fadeTexture);
        Quad("DistanceFade", new Vector3(0f, 0.01f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(FloorSize, FloorSize, 1f), fade);

        // Horizon wall: dark green with specks, glowing band at the bottom.
        _wallMaterial = VRMaterials.Unlit(Color.white);
        Texture2D wallTexture = WallTexture();
        _wallMaterial.SetTexture("_BaseMap", wallTexture);
        _wallMaterial.SetTextureScale("_BaseMap", new Vector2(6f, 1f));
        _wallMaterial.SetFloat("_Cull", 0f);
        GameObject wall = new GameObject("HorizonWall") { layer = VRRig.Layer };
        wall.transform.SetParent(_root, false);
        wall.transform.localPosition = new Vector3(0f, -2f, 0f);
        wall.transform.localScale = new Vector3(WallRadius * 2f, WallHeight, WallRadius * 2f);
        wall.AddComponent<MeshFilter>().sharedMesh = Tube(64);
        wall.AddComponent<MeshRenderer>().sharedMaterial = _wallMaterial;

        // Data waterfalls around the arena (mostly ahead and to the sides, like the reference).
        _fallMaterial = VRMaterials.Transparent(Color.white);
        Texture2D fallTexture = FallTexture();
        _fallMaterial.SetTexture("_BaseMap", fallTexture);
        _fallMaterial.SetFloat("_Cull", 0f);
        Material splash = VRMaterials.Transparent(Color.white);
        splash.SetTexture("_BaseMap", RadialGlow());
        Random.State state = Random.state;
        Random.InitState(1234);

        for (int i = 0; i < Waterfalls; i++)
        {
            float angle = (-150f + i * (300f / (Waterfalls - 1)) + Random.Range(-8f, 8f)) * Mathf.Deg2Rad;
            float radius = WallRadius * Random.Range(0.72f, 0.9f);
            Vector3 position = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            float width = Random.Range(4f, 6.5f); // a bundle of circuit traces
            float height = WallHeight * Random.Range(0.75f, 1f);
            Quaternion face = Quaternion.LookRotation(position.normalized, Vector3.up); // front (-Z) faces the seat
            Quad("DataFall", position + new Vector3(0f, height * 0.5f, 0f), face, new Vector3(width, height, 1f), _fallMaterial);
            Quad("DataFallSide", position + new Vector3(0f, height * 0.5f, 0f), face * Quaternion.Euler(0f, 90f, 0f), new Vector3(width * 0.7f, height, 1f), _fallMaterial);
            Quad("Splash", position + new Vector3(0f, 0.03f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(width * 3.2f, width * 2.2f, 1f), splash);
        }

        Random.state = state;
    }

    void Quad(string name, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.layer = VRRig.Layer;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(_root, false);
        quad.transform.localPosition = position;
        quad.transform.localRotation = rotation;
        quad.transform.localScale = scale;
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ---------------------------------------------------------------- procedural textures

    // One grid cell: dark teal with a glowing cyan line along two edges (tiles seamlessly).
    static Texture2D GridTexture()
    {
        const int n = 128;
        Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, filterMode = FilterMode.Trilinear };
        Color[] pixels = new Color[n * n];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Min(x, n - 1 - x);
                float dy = Mathf.Min(y, n - 1 - y);
                float d = Mathf.Min(dx, dy);
                float line = Mathf.Clamp01(1.6f - d * 0.9f);               // crisp core
                float glow = Mathf.Exp(-d * 0.35f) * 0.35f;                  // soft halo
                pixels[y * n + x] = Color.Lerp(FloorDark, Cyan, Mathf.Clamp01(line + glow));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // Transparent in the middle, dark towards the edges: the grid fades out in the distance.
    static Texture2D RadialFade()
    {
        const int n = 256;
        Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[n * n];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.62f, r));
                Color c = new Color(0.01f, 0.035f, 0.035f, a);
                pixels[y * n + x] = c;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // Horizon wall (u around, v up): bright teal glow at the floor line, fading to a deep
    // green-black sky with scattered faint data specks.
    static Texture2D WallTexture()
    {
        const int w = 256;
        const int h = 256;
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        Color[] pixels = new Color[w * h];
        Random.State state = Random.state;
        Random.InitState(42);

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float glow = Mathf.Exp(-Mathf.Max(0f, v - 0.045f) * 28f);       // bright band at the bottom
            Color sky = Color.Lerp(new Color(0.02f, 0.11f, 0.08f), new Color(0.005f, 0.02f, 0.02f), Mathf.Pow(v, 0.7f));

            for (int x = 0; x < w; x++)
            {
                float speck = Random.value > 0.992f ? Random.Range(0.25f, 0.7f) * (1f - v) : 0f;
                float cloud = Mathf.PerlinNoise(x * 0.04f, y * 0.06f) * 0.05f * (1f - v);
                Color c = sky + Cyan * (glow * 0.85f + speck + cloud);
                c.a = 1f;
                pixels[y * w + x] = c;
            }
        }

        Random.state = state;
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // A data waterfall drawn as circuit traces: a bundle of thin glowing lines running down
    // with right-angle jogs, node dots at the corners and a soft halo, densest in the middle.
    // Tiles vertically (every trace ends where it started), so it can scroll forever.
    static Texture2D FallTexture()
    {
        const int w = 128;
        const int h = 512;
        const int traces = 14;
        float[] light = new float[w * h];
        Random.State state = Random.state;
        Random.InitState(7);

        for (int k = 0; k < traces; k++)
        {
            // Traces bunch towards the centre of the stream.
            float spread = Random.Range(-1f, 1f);
            int start = Mathf.RoundToInt(w * 0.5f + spread * Mathf.Abs(spread) * w * 0.36f);
            int x = start;
            int y = 0;
            float strength = Random.Range(0.55f, 1f);

            while (y < h)
            {
                int run = Random.Range(14, 46);
                int end = Mathf.Min(h, y + run);

                // Last stretch: walk back to the start column so the texture tiles.
                bool last = end >= h - 20;

                for (; y < end; y++)
                {
                    Plot(light, w, h, x, y, strength);
                }

                if (y >= h)
                {
                    break;
                }

                int target = last ? start : Mathf.Clamp(x + Random.Range(-10, 11), 6, w - 7);
                int step = target > x ? 1 : -1;
                Node(light, w, h, x, y, strength);

                while (x != target)
                {
                    x += step;
                    Plot(light, w, h, x, y, strength);
                }

                Node(light, w, h, x, y, strength);

                if (last)
                {
                    for (; y < h; y++)
                    {
                        Plot(light, w, h, x, y, strength);
                    }
                }
            }
        }

        Random.state = state;

        // Halo: a cheap horizontal blur added on top of the crisp lines.
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        Color[] pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float halo = 0f;

                for (int o = -4; o <= 4; o++)
                {
                    int xx = x + o;

                    if (xx >= 0 && xx < w)
                    {
                        halo += light[y * w + xx] * Mathf.Exp(-o * o * 0.18f);
                    }
                }

                float core = light[y * w + x];
                float edge = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);   // fades out at the sides
                float a = Mathf.Clamp01(core + halo * 0.22f) * Mathf.Clamp01(edge * 2.5f);
                Color c = Color.Lerp(Cyan, Color.white, Mathf.Clamp01(core - 0.4f));
                c.a = a;
                pixels[y * w + x] = c;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // Traces are 2 px thick so they stay visible from far away (mipmaps would erase 1 px lines).
    static void Plot(float[] light, int w, int h, int x, int y, float value)
    {
        for (int dy = 0; dy <= 1; dy++)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                int px = x + dx;
                int py = (y + dy) % h;

                if (px >= 0 && px < w && py >= 0)
                {
                    light[py * w + px] = Mathf.Max(light[py * w + px], value);
                }
            }
        }
    }

    // Small square pad at a corner, like a via on a circuit board.
    static void Node(float[] light, int w, int h, int x, int y, float value)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Plot(light, w, h, x + dx, y + dy, value);
            }
        }
    }

    static Texture2D RadialGlow()
    {
        const int n = 64;
        Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[n * n];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                Color c = Cyan;
                c.a = a * a * 0.8f;
                pixels[y * n + x] = c;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // Open tube, radius 0.5, height 1, faces inward (seen from inside), UV u around / v up.
    static Mesh Tube(int segments)
    {
        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Sin(angle) * 0.5f;
            float z = Mathf.Cos(angle) * 0.5f;
            vertices[i * 2] = new Vector3(x, 0f, z);
            vertices[i * 2 + 1] = new Vector3(x, 1f, z);
            uvs[i * 2] = new Vector2(i / (float)segments, 0f);
            uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            int t = i * 6;
            triangles[t] = a;
            triangles[t + 1] = a + 2;
            triangles[t + 2] = a + 1;
            triangles[t + 3] = a + 2;
            triangles[t + 4] = a + 3;
            triangles[t + 5] = a + 1;
        }

        Mesh mesh = new Mesh { name = "HorizonWall", vertices = vertices, uv = uvs, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }
}
#endif
