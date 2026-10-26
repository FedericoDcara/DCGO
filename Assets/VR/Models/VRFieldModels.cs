#if DCGO_VR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// The duel arena (anime style): every Digimon and Tamer on the field also
// stands life-size on the floor in front of you, on a glowing copy of its card.
// Yours stand near you facing the opponent, theirs far away facing you, in the
// same left-to-right order as on the board.
//   - 3D model from the player's own _DSTSModels folder (VRModelLibrary);
//     cards without a model get a standing card hologram instead
//   - appear: light pillar, model grows out of its card, victory pose, idle
//   - suspended: turns sideways
//   - attack: runs at the target (or the opponent's side for a security
//     attack) with its attack animation; the defender guards or takes the hit
//   - digivolve: old form shrinks away, new one rises in a pillar
//   - deleted: knock-down animation, then it fades away
// Purely visual: reads the board only, never touches the rules or the network.
public class VRFieldModels
{
    const float GrowTime = 0.45f;
    const float DeathTime = 1.6f;
    const float ReleaseAfter = 90f;
    const float LungeTime = 1.1f;
    // Arena space: origin = stage anchor, +Z = away from you.
    const float Diorama = 1.0f;            // figure scale on the stage (Lv.5 ~ 1.8 m)

    // Two straight, parallel fields (like two duel mats facing each other): yours
    // a long near row that also uses the space left and right of the desk, the
    // opponent's a raised far row. Cards always point the same way (bottom to you).
    const float SeatToStage = 4.3f;        // seat -> arena anchor (see VRRig StageBehindDesk)
    const float YourRowZ = 3.4f;           // distance of your row in front of the seat
    const float YourHalfWidth = 3.4f;      // your row spans 6.8 m (beside the desk too)
    const float TheirRowZ = 7.0f;          // opponent row (was 5.8: big models overlapped yours)
    const float TheirHalfWidth = 3.2f;
    const float Lane = 0.8f;               // open lane straight ahead (half width)
    const float TheirElevation = 0.3f;     // opponent tier height above yours (low: keeps their figures under the avatar's hand / shields)
    /// <summary>Reference floor for the opponent's podium + avatar (kept at the old tier height when the tier was lowered).</summary>
    public const float PodiumFloor = 1.0f;
    const float RowStep = 0.6f;            // second row when an arc is crowded (outwards)
    public const float TheirBoost = 1.45f;        // opponent figures drawn bigger: they are farther away

    static readonly Color YourColor = new Color(0.25f, 0.8f, 1f);
    static readonly Color TheirColor = new Color(1f, 0.35f, 0.3f);

    sealed class Loaded
    {
        public GltfImport Import;
        public Task<bool> Loading;
        public bool Ok;
        public int Users;
        public float LastUsed;
    }

    sealed class Entry
    {
        public Permanent Permanent;
        public CardSource Top;
        public string Path;
        public bool Yours;
        public Transform Root;      // on the arena floor
        public Transform Body;      // model or standee (lunges, turns)
        public Transform Model;     // instantiated glb scene
        public Renderer Pad;        // card lying under it
        public Renderer Glow;       // coloured frame around the card
        public BoxCollider Hit;     // laser target (VRArenaTarget)
        public float FigureHeight = 1f; // figure height at body scale 1
        public Renderer Pillar;
        public Animation Anim;
        public string Idle;
        public float Spawned;
        public float DyingSince = -1f;
        public bool Seen;
        public Vector3 LungeTarget;
        public float LungeStart = -1f;
        public float BoardX;
        public bool Suspended;
        public bool Tamer;
        public float ActionUntil;
        public Vector3 Target;     // arena position from the layout pass
        public float Width;        // figure width at full size (for fitting)
        public VRLabel DpLabel;    // "Lv.5  DP 7000" + status, on the stage in front of the card
        public float NextText;
        public bool Placed;
        public float Spacing = 1f;     // room left/right: wide models are scaled to fit
    }

    static readonly Dictionary<string, Loaded> _files = new Dictionary<string, Loaded>();
    static Material _padMaterial;
    static Material _glowMaterial;

    readonly Transform _arena;
    readonly Dictionary<Permanent, Entry> _entries = new Dictionary<Permanent, Entry>();
    readonly List<Entry> _dying = new List<Entry>();
    readonly List<Permanent> _gone = new List<Permanent>();
    readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
    Permanent _lastAttacker;
    Light _light;

    readonly StatusBadges _badges;

    public VRFieldModels(Transform arena, StatusBadges badges)
    {
        _arena = arena;
        _badges = badges;
    }

    public static bool Enabled => VRSettings.Models;

    /// <summary>The 3D models on the field now: (label, tuning key), for the size tuner.</summary>
    public List<KeyValuePair<string, string>> Tunables()
    {
        List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
        HashSet<string> seen = new HashSet<string>();

        foreach (Entry entry in _entries.Values)
        {
            string key = entry.Model != null ? VRModelTuning.KeyOf(entry.Path) : null;

            if (key == null || !seen.Add(key))
            {
                continue;
            }

            string name = entry.Top != null ? entry.Top.BaseENGCardNameFromEntity : key;
            list.Add(new KeyValuePair<string, string>((entry.Yours ? "You: " : "Opp: ") + name, key));
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list;
    }

    public void BeginFrame()
    {
        foreach (Entry entry in _entries.Values)
        {
            entry.Seen = false;
        }
    }

    /// <summary>Keeps the arena figure of a permanent in sync. <paramref name="boardSlot"/> is its slot in board space.</summary>
    public void Place(Permanent permanent, Vector3 boardSlot, bool yours, bool suspended)
    {
        if (!Enabled || permanent == null || permanent.TopCard == null || permanent.TopCard.IsFlipped)
        {
            return;
        }

        if (!(permanent.IsDigimon || permanent.IsTamer) || (permanent.PermanentFrame != null && !permanent.PermanentFrame.IsBattleAreaFrame()))
        {
            return;
        }

        if (!_entries.TryGetValue(permanent, out Entry entry))
        {
            entry = new Entry { Permanent = permanent, Yours = yours };
            _entries[permanent] = entry;
        }

        entry.Seen = true;

        // New permanent or digivolved: (re)build its figure.
        if (entry.Top != permanent.TopCard)
        {
            bool digivolved = entry.Top != null && entry.Root != null;

            if (digivolved)
            {
                _dying.Add(new Entry { Root = entry.Root, Body = entry.Body, Model = entry.Model, Path = entry.Path, DyingSince = Time.unscaledTime - DeathTime * 0.6f });
            }
            else
            {
                Release(entry);
            }

            entry.Top = permanent.TopCard;
            entry.Path = VRModelLibrary.Resolve(entry.Top);
            entry.Root = null;
            entry.Body = null;
            entry.Model = null;
            entry.Anim = null;
            entry.Idle = null;
            Spawn(entry);
        }

        // Positioned in the layout pass (EndFrame), once every figure is known.
        entry.BoardX = boardSlot.x;
        entry.Suspended = suspended;
        entry.Tamer = permanent.IsTamer;
        entry.Yours = yours;
    }

    readonly List<Entry> _line = new List<Entry>();

    // Digimon of each player stand in one line across the arena (left-to-right as
    // on the board), spaced so even big ones don't touch; Tamers stand on the
    // outer wings so they never block your view of the field.
    void Layout()
    {
        EnsureStage();

        for (int pass = 0; pass < 4; pass++)
        {
            bool yours = pass < 2;
            bool tamers = pass % 2 == 1;
            _line.Clear();

            foreach (Entry entry in _entries.Values)
            {
                if (entry.Root != null && entry.Yours == yours && entry.Tamer == tamers)
                {
                    _line.Add(entry);
                }
            }

            _line.Sort((a, b) => a.BoardX.CompareTo(b.BoardX));
            int n = _line.Count;
            float rowZ = yours ? YourRowZ : TheirRowZ;
            float halfWidth = yours ? YourHalfWidth : TheirHalfWidth;
            float height = yours ? 0f : TheirElevation;

            if (tamers)
            {
                // Tamers stand just past the end of their own row (yours left, theirs right).
                float side = yours ? -1f : 1f;

                for (int i = 0; i < n; i++)
                {
                    _line[i].Target = OnRow(side * (halfWidth + 0.5f + i * 0.75f), rowZ, height);
                    _line[i].Spacing = 0.7f;
                }

                continue;
            }

            if (yours)
            {
                LayoutYourRow(n);
                continue;
            }

            // Opponent: centred on the row (their middle figures sit in your open
            // lane); when crowded a second row stands behind.
            const float theirMax = 1.3f;
            const float theirMin = 0.7f;
            int rows = n > 0 && halfWidth * 2f / n < theirMin ? 2 : 1;
            int perRow = Mathf.CeilToInt(n / (float)rows);
            float step = Mathf.Min(theirMax, halfWidth * 2f / Mathf.Max(1, perRow));

            for (int i = 0; i < n; i++)
            {
                int row = i % rows;
                int column = i / rows;
                int inRow = Mathf.CeilToInt((n - row) / (float)rows);
                float x = (column - (inRow - 1) * 0.5f) * step + (row == 1 ? step * 0.5f : 0f);
                _line[i].Target = OnRow(x, rowZ + row * RowStep, height);
                _line[i].Spacing = step;
            }
        }

        foreach (Entry entry in _entries.Values)
        {
            if (entry.Root == null)
            {
                continue;
            }

            // Glide to a new spot when the line changes (a Digimon joined or left).
            Vector3 position = entry.Root.localPosition;
            entry.Root.localPosition = !entry.Placed ? entry.Target : Vector3.Lerp(position, entry.Target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            entry.Placed = true;
            // Yours face the opponent's tier, theirs face you.
            entry.Root.localRotation = Quaternion.Euler(0f, entry.Yours ? 0f : 180f, 0f);

            float age = Time.unscaledTime - entry.Spawned;
            float grow = Mathf.SmoothStep(0f, 1f, age / GrowTime);
            float boost = entry.Yours ? 1f : TheirBoost;
            float fit = entry.Width > 0.01f ? Mathf.Min(1f, entry.Spacing * 0.92f / (entry.Width * boost)) : 1f;
            // Hand tuning (debug panel) wins over the automatic fit.
            float tune = entry.Model != null ? VRModelTuning.Get(VRModelTuning.KeyOf(entry.Path)) : 1f;
            entry.Body.localScale = Vector3.one * Mathf.Max(0.001f, grow * fit * boost * tune);

            // Suspended: the model keeps standing and facing forward; the card lying under it turns
            // sideways (like suspending the real card). Card holograms (no model) lean back.
            entry.Body.localRotation = entry.Suspended && entry.Model == null
                ? Quaternion.Euler(-55f, 0f, 0f)
                : Quaternion.identity;
            // Card under the figure: as wide as its spot allows (no overlap with neighbours), up to 0.85 m.
            float padSize = Mathf.Min(0.85f, entry.Spacing * 0.92f / boost) * VRModelTuning.CardSize / 0.36f;

            if (entry.Pad != null)
            {
                Transform group = entry.Pad.transform.parent;
                Quaternion toArena = Quaternion.Inverse(entry.Root.localRotation);

                group.localScale = Vector3.one * padSize * boost;
                // Every card lies flat under its figure, bottom edge towards you (readable), both sides.
                Quaternion flat = entry.Suspended ? toArena * Quaternion.Euler(0f, 90f, 0f) : toArena;
                group.localRotation = entry.Placed ? Quaternion.Slerp(group.localRotation, flat, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)) : flat;
                group.localPosition = Vector3.zero;
            }
            UpdateTarget(entry, padSize * boost);
            UpdateLunge(entry);
            UpdatePillar(entry, age);
            KeepIdle(entry);
            UpdatePlates(entry, padSize * boost);
        }
    }

    static Material _hoverGlow;
    static readonly Dictionary<Color, Material> _glowMaterials = new Dictionary<Color, Material>();

    // Laser target box around card + figure, and the frame colour: white when pointed at,
    // the game's colour when it can be chosen (attack target, effect target...).
    void UpdateTarget(Entry entry, float padScale)
    {
        if (entry.Hit != null)
        {
            float scale = entry.Body.localScale.x;
            float height = Mathf.Max(0.4f, entry.FigureHeight * scale);
            float width = Mathf.Max(0.4f * padScale, entry.Width * scale);
            entry.Hit.size = new Vector3(width, height, Mathf.Max(0.54f * padScale, width * 0.6f));
            entry.Hit.center = new Vector3(0f, height * 0.5f, 0f);
        }

        if (entry.Glow == null)
        {
            return;
        }

        bool hovered = VRArenaTarget.Hovered != null && VRArenaTarget.Hovered.Permanent == entry.Permanent;
        Board3D board = VRSession.Instance != null ? VRSession.Instance.Board : null;
        Color? glow = board != null ? board.ArenaGlow(entry.Permanent) : null;
        Material material;

        if (hovered)
        {
            material = _hoverGlow != null ? _hoverGlow : _hoverGlow = VRMaterials.Unlit(Color.white);
        }
        else if (glow.HasValue)
        {
            // Pulse so choosable figures catch the eye.
            Color c = glow.Value;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
            Color key = new Color(Mathf.Round(c.r * 10f) / 10f, Mathf.Round(c.g * 10f) / 10f, Mathf.Round(c.b * 10f) / 10f, Mathf.Round(pulse * 4f) / 4f);

            if (!_glowMaterials.TryGetValue(key, out material))
            {
                material = VRMaterials.Unlit(new Color(key.r, key.g, key.b) * (0.6f + 0.6f * key.a));
                _glowMaterials[key] = material;
            }
        }
        else
        {
            material = entry.Yours ? GlowMaterial(YourColor) : GlowMaterial(TheirColor);
        }

        if (entry.Glow.sharedMaterial != material)
        {
            entry.Glow.sharedMaterial = material;
        }

        // The frame grows a little when it matters, so it shows around the bigger card.
        float frame = hovered || glow.HasValue ? 1.12f : 1f;
        entry.Glow.transform.localScale = new Vector3(0.4f * frame, 0.54f * frame, 1f);
    }

    static readonly Dictionary<Color, Material> _sideGlow = new Dictionary<Color, Material>();

    static Material GlowMaterial(Color color)
    {
        if (!_sideGlow.TryGetValue(color, out Material material))
        {
            material = VRMaterials.Unlit(color * 0.8f);
            _sideGlow[color] = material;
        }

        return material;
    }

    // One info plate per figure, lying almost flat on the stage just in front of
    // its card (towards your seat): "Lv.5  DP 7000" and, below, its status in
    // colour. Flat and in front, so it never covers the card or the figure.
    void UpdatePlates(Entry entry, float padSize)
    {
        if (entry.DpLabel == null)
        {
            entry.DpLabel = VRLabel.CreateParagraph(_arena, new Vector2(0.75f, 0.2f), 192, 64f, Color.white, new Color(0.05f, 0.09f, 0.14f));
            entry.DpLabel.alignment = TMPro.TextAlignmentOptions.Center;
        }

        bool visible = entry.Body.localScale.x > 0.01f;
        Vector3 front = entry.Root.localPosition + new Vector3(0f, 0.01f, -(0.27f * padSize + 0.16f));
        entry.DpLabel.transform.localPosition = front;
        entry.DpLabel.transform.localRotation = Quaternion.Euler(68f, 0f, 0f); // nearly flat, tilted towards you
        float plateScale = entry.Yours ? 1f : TheirBoost;
        entry.DpLabel.transform.localScale = visible ? new Vector3(0.75f, 0.2f, 1f) * plateScale : Vector3.one * 0.0001f;

        if (Time.unscaledTime < entry.NextText)
        {
            return;
        }

        entry.NextText = Time.unscaledTime + 0.25f;
        string dp = null;
        Color plate = new Color(0.05f, 0.09f, 0.14f);
        List<KeyValuePair<string, Color>> status = null;

        if (_badges != null)
        {
            _badges.TryGetStatus(entry.Permanent, out dp, out plate, out status);
        }

        int level = 0;

        try
        {
            level = entry.Top != null && entry.Permanent.IsDigimon ? entry.Top.Level : 0;
        }
        catch (Exception)
        {
            level = 0;
        }

        string text = entry.Tamer ? "<b>TAMER</b>" : "<b>" + (level > 0 ? "Lv." + level + "   " : "") + (dp ?? "") + "</b>";

        if (status != null && status.Count > 0)
        {
            text += "\n<size=70%>";

            for (int i = 0; i < status.Count && i < 3; i++)
            {
                text += (i > 0 ? "  " : "") + "<color=#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(status[i].Value, Color.white, 0.45f)) + ">" + status[i].Key + "</color>";
            }

            text += "</size>";
        }

        entry.DpLabel.text = text;
        entry.DpLabel.plateColor = entry.Tamer ? new Color(0.22f, 0.18f, 0.32f) : plate;
    }

    // Your Digimon split into a left and a right group (board order kept), leaving
    // a clear lane straight ahead so you can always see across to the opponent.
    void LayoutYourRow(int n)
    {
        const float maxStep = 1.6f;
        const float minStep = 0.8f;
        int left = n / 2;
        int right = n - left;
        int most = Mathf.Max(1, Mathf.Max(left, right));
        float room = YourHalfWidth - Lane;
        int rows = room / most < minStep ? 2 : 1;
        int perRow = Mathf.CeilToInt(most / (float)rows);
        float step = Mathf.Min(maxStep, room / perRow);

        for (int i = 0; i < n; i++)
        {
            bool onLeft = i < left;
            int k = onLeft ? left - 1 - i : i - left;      // 0 = closest to the lane
            int row = k % rows;
            int column = k / rows;
            float x = Lane + step * (0.5f + column) + (row == 1 ? step * 0.5f : 0f);
            _line[i].Target = OnRow(onLeft ? -x : x, YourRowZ + row * RowStep, 0f);
            _line[i].Spacing = step;
        }
    }

    int _capturedCount = -1;
    float _captureAt = -1f;

    // Dev aid: a few seconds after the field changes, save what the seat sees and an
    // overview to Logs/VRCaptures (arena-seat.png, arena-overview.png) for review.
    void CaptureWhenSettled()
    {
        if (_entries.Count != _capturedCount)
        {
            _capturedCount = _entries.Count;
            _captureAt = _entries.Count > 0 ? Time.unscaledTime + 4f : -1f;
        }

        if (_captureAt < 0f || Time.unscaledTime < _captureAt)
        {
            return;
        }

        _captureAt = -1f;
        // What the player sees: from the headset position, looking straight ahead and down.
        Transform head = VRSession.Instance != null && VRSession.Instance.Rig != null ? VRSession.Instance.Rig.Camera.transform : null;
        Vector3 eye = head != null ? head.position : _arena.TransformPoint(Seat + new Vector3(0f, 0.95f, 0f));
        float pitch = VRSettings.ArenaOverview ? 40f : 12f;
        VRCapture.Capture("arena-seat", eye, _arena.rotation * Quaternion.Euler(pitch, 0f, 0f), 95f);
        Vector3 above = _arena.TransformPoint(Seat + new Vector3(0f, 6.4f, 0.8f));
        VRCapture.Capture("arena-overview", above, _arena.rotation * Quaternion.Euler(42f, 0f, 0f), 75f);
    }

    static Vector3 Seat => new Vector3(0f, 0f, -SeatToStage - (VRSettings.ArenaDistance - 2.4f));

    // Point on a straight row in front of the seat (x = left/right, z = ahead).
    static Vector3 OnRow(float x, float z, float height) => Seat + new Vector3(x, height, z);

    // Idle loops whenever no other animation (appear, attack, guard, hit) is running.
    static void KeepIdle(Entry entry)
    {
        if (entry.Anim == null || entry.Idle == null || Time.unscaledTime < entry.ActionUntil)
        {
            return;
        }

        if (!entry.Anim.IsPlaying(entry.Idle))
        {
            entry.Anim.CrossFade(entry.Idle, 0.25f);
        }
    }

    public void Hide(Permanent permanent)
    {
        // The stack viewer borrows the cards, the arena figure stays.
        if (permanent != null && _entries.TryGetValue(permanent, out Entry entry))
        {
            entry.Seen = true;
        }
    }

    public void EndFrame()
    {
        _gone.Clear();

        foreach (KeyValuePair<Permanent, Entry> pair in _entries)
        {
            if (!pair.Value.Seen)
            {
                _gone.Add(pair.Key);
            }
        }

        foreach (Permanent permanent in _gone)
        {
            Entry entry = _entries[permanent];
            _entries.Remove(permanent);

            if (entry.Root != null && Enabled)
            {
                entry.DyingSince = Time.unscaledTime;
                Play(entry, "_bf01", false);
                _dying.Add(entry);
            }
            else
            {
                Release(entry);
            }
        }

        WatchAttacks();
        Layout();
        UpdateDying();
        ReleaseUnusedFiles();
        CaptureWhenSettled();
    }

    public void Clear()
    {
        foreach (Entry entry in _entries.Values)
        {
            Release(entry);
        }

        foreach (Entry entry in _dying)
        {
            Release(entry);
        }

        _entries.Clear();
        _dying.Clear();
        _lastAttacker = null;

        if (_stage != null)
        {
            _stage.gameObject.SetActive(false);
        }
    }

    Transform _stage;

    // A thin dark stage with a blue (your) and red (opponent) edge under the figures.
    void EnsureStage()
    {
        bool show = _entries.Count > 0;

        if (_stage == null && show)
        {
            _stage = CreateStage(_arena);
        }

        if (_stage != null && _stage.gameObject.activeSelf != show)
        {
            _stage.gameObject.SetActive(show);
        }
    }

    /// <summary>The two straight mats (yours near, the opponent's raised and far). Shared with the model studio.</summary>
    public static Transform CreateStage(Transform arena)
    {
        Transform stage = new GameObject("ArenaStage") { layer = VRRig.Layer }.transform;
        stage.SetParent(arena, false);
        RowMat(stage, YourRowZ + 0.2f, 1.9f, YourHalfWidth + 1.6f, 0f, new Color(0.04f, 0.09f, 0.15f), YourColor);
        RowMat(stage, TheirRowZ + 0.3f, 2.1f, TheirHalfWidth + 1.6f, TheirElevation, new Color(0.13f, 0.05f, 0.07f), TheirColor);
        return stage;
    }

    /// <summary>Arena spot for a preview figure: in your row beside the lane, or the middle of the opponent's row.</summary>
    /// <summary>Where the opponent avatar stands: centred, behind the opponent's mat (y = 0, set by the avatar).</summary>
    public static Vector3 OpponentAvatarSpot => OnRow(0f, TheirRowZ + 3.2f, 0f);

    /// <summary>Height of the opponent's mat above yours (arena space).</summary>
    public static float OpponentFloor => TheirElevation;

    /// <summary>Your option cards: lying flat past the right end of your row (arena space).</summary>
    public static Vector3 YourOptionSpot(int index) => OnRow(YourHalfWidth + 0.7f + index * 0.7f, YourRowZ, 0.012f);

    public static Vector3 PreviewSpot(bool yours) => yours ? OnRow(Lane + 0.9f, YourRowZ, 0f) : OnRow(0f, TheirRowZ, TheirElevation);

    // A straight mat with a glowing edge on its near (your) side.
    static void RowMat(Transform stage, float z, float depth, float halfWidth, float height, Color fill, Color edge)
    {
        StageBox(stage, OnRow(0f, z, height - 0.03f), new Vector3(halfWidth * 2f, 0.05f, depth), fill, Quaternion.identity);
        StageBox(stage, OnRow(0f, z - depth * 0.5f, height + 0.001f), new Vector3(halfWidth * 2f, 0.006f, 0.035f), edge, Quaternion.identity);
    }

    static void StageBox(Transform stage, Vector3 position, Vector3 size, Color color, Quaternion rotation)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(stage, false);
        box.transform.localPosition = position;
        box.transform.localRotation = rotation;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
    }

    // ---------------------------------------------------------------- attacks

    void WatchAttacks()
    {
        AttackProcess attack = GManager.instance != null ? GManager.instance.attackProcess : null;
        Permanent attacker = attack != null && attack.IsAttacking ? attack.AttackingPermanent : null;

        if (attacker == _lastAttacker)
        {
            return;
        }

        _lastAttacker = attacker;

        if (attacker == null || !_entries.TryGetValue(attacker, out Entry entry) || entry.Root == null)
        {
            return;
        }

        // Run at the defender, or at the opponent's side of the arena (security attack).
        Vector3 from = entry.Target;
        Vector3 target;

        if (attack.DefendingPermanent != null && _entries.TryGetValue(attack.DefendingPermanent, out Entry defender) && defender.Root != null)
        {
            target = defender.Target;
            Play(defender, attack.IsBlocking ? "_bg01" : "_bd01", true);
        }
        else
        {
            target = entry.Yours ? OnRow(from.x * 0.5f, TheirRowZ + 0.8f, TheirElevation) : OnRow(from.x * 0.5f, YourRowZ - 0.6f, 0f);
        }

        entry.LungeTarget = target;
        entry.LungeStart = Time.unscaledTime;
        Play(entry, "_ba01", true);
    }

    void UpdateLunge(Entry entry)
    {
        if (entry.LungeStart < 0f || entry.Root == null)
        {
            entry.Body.localPosition = Vector3.zero;
            return;
        }

        float t = (Time.unscaledTime - entry.LungeStart) / LungeTime;

        if (t >= 1f)
        {
            entry.LungeStart = -1f;
            entry.Body.localPosition = Vector3.zero;
            return;
        }

        // Out (stop short of the target), hold, back.
        float out_ = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.3f, t));
        float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, t));
        float amount = out_ * (1f - back) * 0.65f;
        Vector3 worldDelta = (entry.LungeTarget - entry.Target) * amount;
        entry.Body.localPosition = Quaternion.Inverse(entry.Root.localRotation) * worldDelta;
    }

    // ---------------------------------------------------------------- animation

    static void Play(Entry entry, string suffix, bool thenIdle)
    {
        if (entry.Anim == null)
        {
            return;
        }

        AnimationClip clip = FindClip(entry.Anim, suffix);

        if (clip == null)
        {
            return;
        }

        AnimationState state = entry.Anim[clip.name];

        if (state != null)
        {
            state.wrapMode = WrapMode.ClampForever; // hold the last pose until idle takes over
            state.time = 0f;
        }

        entry.Anim.CrossFade(clip.name, 0.15f);
        // KeepIdle returns to the idle loop when this one is over (never for the faint).
        entry.ActionUntil = thenIdle ? Time.unscaledTime + clip.length - 0.2f : float.MaxValue;
    }

    static AnimationClip FindClip(Animation anim, string suffix)
    {
        foreach (AnimationState state in anim)
        {
            if (state.clip != null && state.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return state.clip;
            }
        }

        return null;
    }

    void UpdateDying()
    {
        for (int i = _dying.Count - 1; i >= 0; i--)
        {
            Entry entry = _dying[i];
            float t = (Time.unscaledTime - entry.DyingSince) / DeathTime;

            if (entry.Root == null || t >= 1f)
            {
                Release(entry);
                _dying.RemoveAt(i);
                continue;
            }

            float shrink = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.6f, 1f, t));
            entry.Root.localScale = Vector3.one * Mathf.Max(0.001f, shrink);
        }
    }

    void UpdatePillar(Entry entry, float age)
    {
        if (entry.Pillar == null)
        {
            return;
        }

        float t = age / 0.9f;
        bool show = t >= 0f && t < 1f;

        if (entry.Pillar.gameObject.activeSelf != show)
        {
            entry.Pillar.gameObject.SetActive(show);
        }

        if (show)
        {
            float width = Mathf.Lerp(0.9f, 0.05f, t);
            entry.Pillar.transform.localScale = new Vector3(width * 0.6f, 1.2f, width * 0.6f);
        }
    }

    // ---------------------------------------------------------------- building a figure

    void Spawn(Entry entry)
    {
        EnsureLight();
        Transform root = new GameObject("Arena " + entry.Top.BaseENGCardNameFromEntity) { layer = VRRig.Layer }.transform;
        root.SetParent(_arena, false);
        entry.Root = root;
        entry.Spawned = Time.unscaledTime;
        entry.Placed = false;
        entry.ActionUntil = 0f;

        Transform body = new GameObject("Body") { layer = VRRig.Layer }.transform;
        body.SetParent(root, false);
        body.localScale = Vector3.one * 0.001f;
        entry.Body = body;

        Color color = entry.Yours ? YourColor : TheirColor;
        entry.Pad = CreatePad(root, color);
        entry.Glow = entry.Pad.transform.parent.Find("PadGlow").GetComponent<Renderer>();
        entry.Hit = root.gameObject.AddComponent<BoxCollider>();
        entry.Hit.isTrigger = true;
        root.gameObject.AddComponent<VRArenaTarget>().Permanent = entry.Permanent;
        entry.FigureHeight = HeightForLevel(entry.Top) * Diorama * VRSettings.ModelScale;
        entry.Pillar = CreatePillar(root, color);
        LoadCardArt(entry.Top, entry.Pad);

        if (entry.Path != null)
        {
            SpawnModel(entry, root, body);
        }
        else
        {
            CreateStandee(entry, body);
        }
    }

    async void SpawnModel(Entry entry, Transform root, Transform body)
    {
        string path = entry.Path;
        entry.Spawned = float.MaxValue; // stay hidden while loading
        Loaded file = await LoadFile(path);

        if (entry.Root != root || root == null)
        {
            return; // left the field / digivolved meanwhile
        }

        bool ok = false;
        Transform holder = new GameObject("Model") { layer = VRRig.Layer }.transform;
        holder.SetParent(body, false);

        if (file != null && file.Ok)
        {
            try
            {
                ok = await file.Import.InstantiateMainSceneAsync(holder);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DCGO VR] Model instance failed: " + exception.Message);
            }
        }

        if (root == null || entry.Root != root)
        {
            return;
        }

        if (!ok)
        {
            UnityEngine.Object.Destroy(holder.gameObject);
            CreateStandee(entry, body);
            entry.Spawned = Time.unscaledTime;
            return;
        }

        file.Users++;
        file.LastUsed = Time.unscaledTime;
        entry.Model = holder;
        VRCelShading.Apply(file.Import, path, holder); // DSTS cel shading + outline (web viewer look)
        Prepare(entry, holder);
        entry.Spawned = Time.unscaledTime;
        Play(entry, "_bv01", true);

        if (entry.Anim != null && !entry.Anim.isPlaying && entry.Idle != null)
        {
            entry.Anim.Play(entry.Idle);
        }
    }

    // Life-size but capped: small Digimon ~0.5 m, Agumon ~1 m, Mega ~2 m.
    void Prepare(Entry entry, Transform holder)
    {
        float scale = Normalize(holder, HeightForLevel(entry.Top) * Diorama * VRSettings.ModelScale, out float width);
        entry.Width = width * scale;

        entry.Anim = holder.GetComponentInChildren<Animation>(true);

        if (entry.Anim != null)
        {
            AnimationClip idle = FindClip(entry.Anim, "_bn01") ?? FindClip(entry.Anim, "_bn02");

            if (idle != null)
            {
                idle.wrapMode = WrapMode.Loop;
                entry.Idle = idle.name;
                entry.Anim[idle.name].wrapMode = WrapMode.Loop;
            }

            entry.Anim.playAutomatically = false;
            entry.Anim.Stop();
            entry.Anim.cullingType = AnimationCullingType.AlwaysAnimate;
        }
    }

    /// <summary>Scales a freshly loaded model to the target height (capped for very wide
    /// models), hides outline shells and puts it on the VR layer. Returns the scale;
    /// <paramref name="width"/> is the unscaled width. Shared with the model studio.</summary>
    public static float Normalize(Transform holder, float target, out float width)
    {
        holder.localScale = Vector3.one;
        holder.localPosition = Vector3.zero;
        Bounds bounds = new Bounds();
        bool first = true;

        foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
            }

            bool outlineShell = false;

            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && material.name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    outlineShell = true;

                    if (!VRCelShading.IsCelOutline(material))
                    {
                        renderer.enabled = false; // inverted-hull outline needs the DSTS outline shader
                    }
                }
            }

            if (renderer.enabled && !outlineShell)
            {
                Bounds local = renderer.bounds;

                if (first)
                {
                    bounds = local;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        foreach (Transform t in holder.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = VRRig.Layer;
        }

        float worldScale = Mathf.Max(1e-4f, holder.lossyScale.y);
        float height = Mathf.Max(0.1f, bounds.size.y / worldScale);
        width = Mathf.Max(bounds.size.x, bounds.size.z) / worldScale;
        float scale = Mathf.Min(target / height, 2.2f * VRSettings.ModelScale / Mathf.Max(0.1f, width));
        holder.localScale = Vector3.one * scale;

        // Some files place the figure far from their origin (or lift it): move the model so
        // its feet stand centred on the spot (bottom-centre of its bounds at the parent origin).
        // Done in the parent's space, so it works whatever the parent's scale or rotation.
        if (!first && holder.parent != null)
        {
            Transform parent = holder.parent;
            Vector3 feetWorld = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            // bounds were measured before scaling (holder at scale 1, same position): rescale the offset.
            Vector3 feetLocal = parent.InverseTransformPoint(feetWorld);
            holder.localPosition = -feetLocal * scale;
        }

        return scale;
    }

    /// <summary>Automatic figure height for a card level (before hand tuning).</summary>
    public static float TargetHeight(int level) => LevelHeight(level) * Diorama * VRSettings.ModelScale;

    /// <summary>Loads a model for preview (model studio). Null if it failed.</summary>
    public static async Task<Transform> LoadPreview(string path, Transform parent)
    {
        Loaded file = await LoadFile(path);

        if (file == null || !file.Ok || parent == null)
        {
            return null;
        }

        Transform holder = new GameObject("Model") { layer = VRRig.Layer }.transform;
        holder.SetParent(parent, false);
        bool ok = false;

        try
        {
            ok = await file.Import.InstantiateMainSceneAsync(holder);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Model preview failed: " + exception.Message);
        }

        if (!ok || holder == null || parent == null)
        {
            if (holder != null)
            {
                UnityEngine.Object.Destroy(holder.gameObject);
            }

            return null;
        }

        VRCelShading.Apply(file.Import, path, holder); // DSTS cel shading + outline (web viewer look)
        file.Users++;
        file.LastUsed = Time.unscaledTime;
        return holder;
    }

    /// <summary>Drops a preview and frees its file right away when nothing else uses it.</summary>
    public static void ReleasePreview(string path, Transform holder)
    {
        if (holder == null)
        {
            return; // nothing was taken
        }

        UnityEngine.Object.Destroy(holder.gameObject);

        if (path != null && _files.TryGetValue(path, out Loaded file))
        {
            file.Users = Mathf.Max(0, file.Users - 1);

            if (file.Users == 0 && file.Loading.IsCompleted)
            {
                VRCelShading.Release(file.Import);
                file.Import.Dispose();
                _files.Remove(path);
            }
        }
    }

    public static AnimationClip Clip(Animation anim, string suffix) => anim != null ? FindClip(anim, suffix) : null;

    // Height by level, like the anime: In-Training knee-high ... Ultra towering.
    static float HeightForLevel(CardSource card)
    {
        int level = 0;

        try
        {
            level = card != null ? card.Level : 0;
        }
        catch (Exception)
        {
            level = 0;
        }

        return LevelHeight(level);
    }

    public static float LevelHeight(int level)
    {
        switch (level)
        {
            case 2: return 0.45f;
            case 3: return 0.85f;
            case 4: return 1.4f;
            case 5: return 1.8f;
            case 6: return 2.4f;
            case 7: return 3.2f;
            default: return level > 7 ? 3.6f : 1.2f;
        }
    }

    // A card hologram standing upright (no model for this card, e.g. most Tamers).
    void CreateStandee(Entry entry, Transform body)
    {
        GameObject card = GameObject.CreatePrimitive(PrimitiveType.Quad);
        card.name = "Standee";
        card.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(card.GetComponent<Collider>());
        card.transform.SetParent(body, false);
        float height = entry.Permanent.IsTamer ? 0.6f : Mathf.Clamp(HeightForLevel(entry.Top) * 0.8f * Diorama, 0.45f, 1.2f);
        entry.Width = height / 1.4f;
        entry.FigureHeight = height + 0.05f;
        card.transform.localScale = new Vector3(height / 1.4f, height, 1f);
        card.transform.localPosition = new Vector3(0f, height * 0.5f + 0.05f, 0f);
        // A quad's front faces -Z; the figure faces +Z (towards the other side).
        card.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        Renderer renderer = card.GetComponent<Renderer>();
        renderer.sharedMaterial = PadMaterial();
        LoadCardArt(entry.Top, renderer);

        GameObject back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.name = "StandeeBack";
        back.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(back.GetComponent<Collider>());
        back.transform.SetParent(card.transform, false);
        back.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        back.transform.localScale = Vector3.one;
        Renderer backRenderer = back.GetComponent<Renderer>();
        backRenderer.sharedMaterial = PadMaterial();
        LoadCardArt(entry.Top, backRenderer); // readable from both sides
    }

    Renderer CreatePad(Transform root, Color color)
    {
        Transform group = new GameObject("PadGroup") { layer = VRRig.Layer }.transform;
        group.SetParent(root, false);
        root = group;

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        frame.name = "PadGlow";
        frame.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(root, false);
        frame.transform.localPosition = new Vector3(0f, 0.004f, 0f);
        frame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        frame.transform.localScale = new Vector3(0.4f, 0.54f, 1f);
        frame.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color * 0.8f);

        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pad.name = "Pad";
        pad.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(pad.GetComponent<Collider>());
        pad.transform.SetParent(root, false);
        pad.transform.localPosition = new Vector3(0f, 0.008f, 0f);
        pad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        pad.transform.localScale = new Vector3(0.36f, 0.5f, 1f);
        Renderer renderer = pad.GetComponent<Renderer>();
        renderer.sharedMaterial = PadMaterial();
        return renderer;
    }

    Renderer CreatePillar(Transform root, Color color)
    {
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "SummonPillar";
        pillar.layer = VRRig.Layer;
        UnityEngine.Object.Destroy(pillar.GetComponent<Collider>());
        pillar.transform.SetParent(root, false);
        pillar.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        Renderer renderer = pillar.GetComponent<Renderer>();
        renderer.sharedMaterial = VRMaterials.Unlit(Color.Lerp(color, Color.white, 0.5f));
        pillar.SetActive(false);
        return renderer;
    }

    static Material PadMaterial() => _padMaterial != null ? _padMaterial : _padMaterial = VRMaterials.Unlit(Color.white);

    async void LoadCardArt(CardSource card, Renderer target)
    {
        try
        {
            Sprite sprite = await card.GetCardSprite();

            if (sprite == null || target == null)
            {
                return;
            }

            Texture2D texture = sprite.texture;
            Rect rect = sprite.textureRect;
            Vector4 st = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
            target.GetPropertyBlock(_block);
            _block.SetTexture("_BaseMap", texture);
            _block.SetTexture("_MainTex", texture);
            _block.SetVector("_BaseMap_ST", st);
            _block.SetVector("_MainTex_ST", st);
            target.SetPropertyBlock(_block);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Arena card art failed: " + exception.Message);
        }
    }

    // ---------------------------------------------------------------- loading / releasing

    static async Task<Loaded> LoadFile(string path)
    {
        if (!_files.TryGetValue(path, out Loaded file))
        {
            file = new Loaded { Import = new GltfImport() };
            ImportSettings settings = new ImportSettings
            {
                AnimationMethod = AnimationMethod.Legacy,
                GenerateMipMaps = true,
            };
            file.Loading = file.Import.LoadFile(path, null, settings);
            _files[path] = file;
        }

        try
        {
            file.Ok = await file.Loading;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DCGO VR] Could not load {System.IO.Path.GetFileName(path)}: {exception.Message}");
            file.Ok = false;
        }

        file.LastUsed = Time.unscaledTime;
        return file;
    }

    static void Release(Entry entry)
    {
        if (entry.Root != null)
        {
            UnityEngine.Object.Destroy(entry.Root.gameObject);
        }

        if (entry.Model != null && entry.Path != null && _files.TryGetValue(entry.Path, out Loaded file))
        {
            file.Users = Mathf.Max(0, file.Users - 1);
            file.LastUsed = Time.unscaledTime;
        }

        if (entry.DpLabel != null)
        {
            UnityEngine.Object.Destroy(entry.DpLabel.gameObject);
        }

        entry.Root = null;
        entry.Body = null;
        entry.Model = null;
        entry.Anim = null;
        entry.DpLabel = null;
    }

    static float _nextReleaseCheck;

    static void ReleaseUnusedFiles()
    {
        if (Time.unscaledTime < _nextReleaseCheck)
        {
            return;
        }

        _nextReleaseCheck = Time.unscaledTime + 10f;
        List<string> unused = null;

        foreach (KeyValuePair<string, Loaded> pair in _files)
        {
            if (pair.Value.Users == 0 && pair.Value.Loading.IsCompleted && Time.unscaledTime - pair.Value.LastUsed > ReleaseAfter)
            {
                (unused ??= new List<string>()).Add(pair.Key);
            }
        }

        if (unused == null)
        {
            return;
        }

        foreach (string path in unused)
        {
            VRCelShading.Release(_files[path].Import);
            _files[path].Import.Dispose();
            _files.Remove(path);
        }

        Debug.Log($"[DCGO VR] Released {unused.Count} unused 3D model file(s).");
    }

    // A soft key light for the models (the VR room itself is unlit).
    void EnsureLight()
    {
        if (_light != null)
        {
            return;
        }

        GameObject go = new GameObject("DCGO_VR_ArenaLight") { layer = VRRig.Layer };
        go.transform.SetParent(_arena, false);
        go.transform.localRotation = Quaternion.Euler(45f, 160f, 0f);
        _light = go.AddComponent<Light>();
        _light.type = LightType.Directional;
        _light.intensity = 1.15f;
        _light.color = new Color(1f, 0.97f, 0.92f);
        _light.cullingMask = 1 << VRRig.Layer;
        _light.shadows = LightShadows.None;
    }
}
#endif
