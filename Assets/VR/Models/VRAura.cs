#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// "Effect activated" visual for arena figures: a glowing magic circle on the floor
// (slowly spinning ring with notches) and a soft aura column rising around the figure,
// fading towards the top. Built from code: two procedural textures and an open tube mesh.
public static class VRAura
{
    static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    static Material _ring;
    static Material _column;
    static Mesh _tube;
    static int _pulseFrame = -1;

    /// <summary>Creates the (hidden) aura under <paramref name="parent"/>.</summary>
    public static Transform Create(Transform parent)
    {
        Ensure();
        Transform aura = new GameObject("EffectAura") { layer = VRRig.Layer }.transform;
        aura.SetParent(parent, false);

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ring.name = "Ring";
        ring.layer = VRRig.Layer;
        Object.Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(aura, false);
        ring.GetComponent<Renderer>().sharedMaterial = _ring;

        GameObject column = new GameObject("Column") { layer = VRRig.Layer };
        column.transform.SetParent(aura, false);
        column.AddComponent<MeshFilter>().sharedMesh = _tube;
        column.AddComponent<MeshRenderer>().sharedMaterial = _column;

        aura.gameObject.SetActive(false);
        return aura;
    }

    /// <summary>Shows the aura at <paramref name="level"/> (0 hidden .. 1 full) sized to the figure.</summary>
    public static void Apply(Transform aura, float level, float radius, float height)
    {
        Pulse();
        bool show = level > 0.001f;

        if (aura.gameObject.activeSelf != show)
        {
            aura.gameObject.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        float ease = Mathf.SmoothStep(0f, 1f, level);
        Transform ring = aura.GetChild(0);
        Transform column = aura.GetChild(1);
        float size = radius * 2.6f * (0.6f + 0.4f * ease);
        ring.localPosition = new Vector3(0f, 0.015f, 0f);
        ring.localRotation = Quaternion.Euler(0f, Time.unscaledTime * 40f, 0f) * Quaternion.Euler(90f, 0f, 0f);
        ring.localScale = new Vector3(size, size, 1f);
        column.localPosition = Vector3.zero;
        column.localScale = new Vector3(radius * 2f, Mathf.Max(0.01f, height * ease), radius * 2f);
    }

    // Shared materials breathe a little (once per frame for all auras).
    static void Pulse()
    {
        if (_pulseFrame == Time.frameCount || _column == null)
        {
            return;
        }

        _pulseFrame = Time.frameCount;
        float t = Time.unscaledTime;
        Color column = Gold;
        column.a = 0.55f + 0.2f * Mathf.Sin(t * 6f);
        _column.SetColor("_BaseColor", column);
        Color ring = Gold;
        ring.a = 0.8f + 0.2f * Mathf.Sin(t * 4f);
        _ring.SetColor("_BaseColor", ring);
    }

    static void Ensure()
    {
        if (_ring != null)
        {
            return;
        }

        _ring = VRMaterials.Transparent(Gold);
        Texture2D ringTexture = RingTexture();
        _ring.SetTexture("_BaseMap", ringTexture);
        _ring.SetTexture("_MainTex", ringTexture);

        _column = VRMaterials.Transparent(Gold);
        Texture2D columnTexture = ColumnTexture();
        _column.SetTexture("_BaseMap", columnTexture);
        _column.SetTexture("_MainTex", columnTexture);
        _column.SetFloat("_Cull", 0f); // both sides of the tube
        _column.renderQueue += 1;

        _tube = TubeMesh(28);
    }

    // Magic circle: bright outer ring, thin inner ring, 12 notches between them, soft glow inside.
    static Texture2D RingTexture()
    {
        const int n = 256;
        Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[n * n];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float angle = Mathf.Atan2(v, u);
                float a = 0f;
                a += Mathf.Exp(-Mathf.Pow((r - 0.88f) / 0.035f, 2f));          // outer ring
                a += 0.7f * Mathf.Exp(-Mathf.Pow((r - 0.66f) / 0.018f, 2f));   // inner ring
                float notch = Mathf.Abs(Mathf.Sin(angle * 6f));                  // 12 notches
                if (r > 0.68f && r < 0.86f && notch > 0.97f)
                {
                    a += 0.8f;
                }

                a += 0.18f * Mathf.Clamp01(1f - r / 0.9f);                       // soft inner glow
                a = r > 1f ? 0f : Mathf.Clamp01(a);
                byte alpha = (byte)(a * 255f);
                pixels[y * n + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // Aura column: strongest at the floor, fading out towards the top.
    static Texture2D ColumnTexture()
    {
        const int h = 64;
        Texture2D texture = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float a = Mathf.Pow(1f - v, 1.6f) * 0.75f;

            for (int x = 0; x < 4; x++)
            {
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    // Open tube: radius 0.5, height 1 (y 0..1), UV v = height.
    static Mesh TubeMesh(int segments)
    {
        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * 0.5f;
            float z = Mathf.Sin(angle) * 0.5f;
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
            triangles[t + 1] = a + 1;
            triangles[t + 2] = a + 2;
            triangles[t + 3] = a + 2;
            triangles[t + 4] = a + 1;
            triangles[t + 5] = a + 3;
        }

        Mesh mesh = new Mesh { name = "AuraTube", vertices = vertices, uv = uvs, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }
}
#endif
