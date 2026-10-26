#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Model studio: go through every 3D model one by one and set its size by hand,
// shown in the real duel arena (same place, distance and view as a duel): one copy
// in your row next to a 1.7 m person, one in the opponent's row (drawn 1.45x like
// in duels). Sizes are those of the field for the chosen level. Every confirmed size is saved to _DSTSModels/tuning.csv
// (VRModelTuning) and used in all duels. Opened from VR settings.
public class VRModelStudio : MonoBehaviour
{
    const float PanelWidth = 0.5f;
    const float Row = 0.046f;

    public static bool Active { get; private set; }

    VRRig _rig;
    Transform _arenaRoot;     // under the rig's arena anchor (not under this object)
    readonly Transform[] _turn = new Transform[2];       // 0 = your row, 1 = opponent row
    readonly Transform[] _model = new Transform[2];
    readonly Animation[] _anims = new Animation[2];
    readonly string[] _idles = new string[2];
    Transform _panel;
    Light _light;
    VRLabel _title;
    VRLabel _name;
    VRLabel _measure;
    readonly List<Action> _refreshers = new List<Action>();

    List<VRModelLibrary.ModelInfo> _list = new List<VRModelLibrary.ModelInfo>();
    bool _includeNpcs;
    int _index;
    int _level = 5;
    float _yaw;
    int _loadToken;

    string _loadedPath;
    float _height;
    string _status = "";

    public static VRModelStudio Create(Transform parent)
    {
        GameObject go = new GameObject("ModelStudio") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRModelStudio studio = go.AddComponent<VRModelStudio>();
        studio.Build();
        go.SetActive(false);
        return studio;
    }

    // ------------------------------------------------------------------ open / close

    public void Open(VRRig rig, Transform head)
    {
        if (head == null || rig == null)
        {
            return;
        }

        _rig = rig;
        rig.SetArenaPreview(true);
        BuildArena();
        _arenaRoot.gameObject.SetActive(true);

        Vector3 forward = head.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        // Panel at arm's length on the left, a bit below the eyes: the arena stays in full view.
        _panel.position = head.position + forward * 0.5f - right * 0.42f + Vector3.down * 0.12f;
        _panel.rotation = Quaternion.LookRotation(_panel.position - head.position, Vector3.up);

        Active = true;
        gameObject.SetActive(true);
        RebuildList();

        if (_list.Count > 0 && (_index < 0 || _index >= _list.Count))
        {
            _index = 0;
        }

        ShowCurrent();
    }

    public void Close()
    {
        UnloadModel();
        Active = false;

        if (_arenaRoot != null)
        {
            _arenaRoot.gameObject.SetActive(false);
        }

        _rig?.SetArenaPreview(false);
        gameObject.SetActive(false);
    }


    void OnDestroy()
    {
        Active = false;
        UnloadModel();

        if (_arenaRoot != null)
        {
            Destroy(_arenaRoot.gameObject);
        }
    }

    // ------------------------------------------------------------------ building

    void Build()
    {
        GameObject lightGo = new GameObject("StudioLight") { layer = VRRig.Layer };
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.rotation = Quaternion.Euler(45f, 160f, 0f);
        _light = lightGo.AddComponent<Light>();
        _light.type = LightType.Directional;
        _light.intensity = 1.15f;
        _light.color = new Color(1f, 0.97f, 0.92f);
        _light.cullingMask = 1 << VRRig.Layer;
        _light.shadows = LightShadows.None;

        // Control panel.
        _panel = new GameObject("Panel") { layer = VRRig.Layer }.transform;
        _panel.SetParent(transform, false);
        int rows = 0;
        Color ink = new Color(0.05f, 0.09f, 0.14f);

        _title = VRLabel.Create(_panel, RowPos(rows++), Quaternion.identity, new Vector2(PanelWidth, 0.036f), "MODEL STUDIO", new Color(0.55f, 0.95f, 1f), ink);
        _name = VRLabel.Create(_panel, RowPos(rows++), Quaternion.identity, new Vector2(PanelWidth, 0.04f), "", Color.white, ink);
        _measure = VRLabel.Create(_panel, RowPos(rows++), Quaternion.identity, new Vector2(PanelWidth, 0.036f), "", new Color(1f, 0.9f, 0.5f), ink);

        Plates(rows++, B("<< 10", () => Move(-10)), B("< Prev", () => Move(-1)), B("Next >", () => Move(1)), B("10 >>", () => Move(10)));
        Plates(rows++, B("Next not sized >", NextUnsized), L(() => _includeNpcs ? "Show: all models" : "Show: Digimon only", ToggleNpcs));
        Plates(rows++, B("-15%", () => Scale(0.85f)), B("-3%", () => Scale(0.97f)), B("+3%", () => Scale(1f / 0.97f)), B("+15%", () => Scale(1f / 0.85f)));
        Plates(rows++, B("< Lv", () => Level(-1)), L(() => "Preview as Lv." + _level, null), B("Lv >", () => Level(1)));
        Plates(rows++, B("Idle", () => Anim("_bn01")), B("Attack", () => Anim("_ba01")), B("Hit", () => Anim("_bd01")), B("Win", () => Anim("_bv01")));
        Plates(rows++, B("Turn 45", () => { _yaw += 45f; ApplyTransform(); }), B("Auto size", ResetSize));
        Plates(rows++, B("SAVE + NEXT", SaveAndNext), B("Close", Close));

        float height = rows * Row + 0.03f;
        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "Frame";
        frame.layer = VRRig.Layer;
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(_panel, false);
        frame.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(ink);
        frame.transform.localScale = new Vector3(PanelWidth + 0.03f, height, 0.004f);
        frame.transform.localPosition = new Vector3(0f, -(rows - 1) * Row * 0.5f, 0.006f);
    }

    // The arena as in a duel: both mats, a turntable in each row, a 1.7 m person in yours.
    void BuildArena()
    {
        if (_arenaRoot != null)
        {
            return;
        }

        _arenaRoot = new GameObject("ModelStudioArena") { layer = VRRig.Layer }.transform;
        _arenaRoot.SetParent(_rig.ArenaAnchor, false);
        VRFieldModels.CreateStage(_arenaRoot);

        for (int side = 0; side < 2; side++)
        {
            bool yours = side == 0;
            Transform slot = new GameObject(yours ? "YourSlot" : "TheirSlot") { layer = VRRig.Layer }.transform;
            slot.SetParent(_arenaRoot, false);
            slot.localPosition = VRFieldModels.PreviewSpot(yours);
            // Yours face the opponent, theirs face you; theirs are drawn bigger, as in duels.
            slot.localRotation = Quaternion.Euler(0f, yours ? 0f : 180f, 0f);
            slot.localScale = Vector3.one * (yours ? 1f : VRFieldModels.TheirBoost);
            _turn[side] = new GameObject("Turntable") { layer = VRRig.Layer }.transform;
            _turn[side].SetParent(slot, false);
        }

        Transform person = new GameObject("Person1.7m") { layer = VRRig.Layer }.transform;
        person.SetParent(_arenaRoot, false);
        person.localPosition = VRFieldModels.PreviewSpot(true) + new Vector3(1.3f, 0f, 0f);
        Color grey = new Color(0.45f, 0.5f, 0.58f);
        Shape(person, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.72f, 0f), new Vector3(0.36f, 0.72f, 0.22f), grey);
        Shape(person, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.57f, 0f), new Vector3(0.24f, 0.26f, 0.24f), grey);
        VRLabel.Create(person, new Vector3(0f, 1.95f, 0f), Quaternion.identity, new Vector2(0.6f, 0.11f), "PERSON 1.70 m", Color.white, new Color(0.05f, 0.09f, 0.14f));
    }

    static Vector3 RowPos(int row) => new Vector3(0f, -row * Row, -0.002f);

    struct Plate
    {
        public string Label;
        public Func<string> Live;
        public Action Action;
    }

    static Plate B(string label, Action action) => new Plate { Label = label, Action = action };

    static Plate L(Func<string> live, Action action) => new Plate { Live = live, Action = action };

    // A row of equal plates across the panel; "live" plates update their text on every refresh.
    void Plates(int row, params Plate[] items)
    {
        int n = items.Length;
        const float gap = 0.006f;
        float width = (PanelWidth - gap * (n - 1)) / n;

        for (int i = 0; i < n; i++)
        {
            float x = -PanelWidth * 0.5f + width * 0.5f + i * (width + gap);
            Plate item = items[i];
            VRButton3D plate = VRButton3D.CreatePlate(_panel, RowPos(row) + new Vector3(x, 0f, 0f), new Vector2(width, 0.038f), item.Live != null ? item.Live() : item.Label, () =>
            {
                item.Action?.Invoke();
                Refresh();
            });

            if (item.Live != null)
            {
                Func<string> live = item.Live;
                _refreshers.Add(() => plate.SetLabel(live()));
            }
        }
    }

    void ToggleNpcs()
    {
        _includeNpcs = !_includeNpcs;
        string path = Current?.Path;
        RebuildList();
        _index = Mathf.Max(0, _list.FindIndex(m => m.Path == path));
        ShowCurrent();
    }

    static void Box(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        => Shape(parent, PrimitiveType.Cube, name, position, size, color);

    static void Shape(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 size, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.layer = VRRig.Layer;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
    }

    // ------------------------------------------------------------------ list / navigation

    VRModelLibrary.ModelInfo Current => _index >= 0 && _index < _list.Count ? _list[_index] : null;

    void RebuildList()
    {
        _list = VRModelLibrary.All.FindAll(m => _includeNpcs || m.Category == "digimon");
    }

    void Move(int delta)
    {
        if (_list.Count == 0)
        {
            return;
        }

        _index = ((_index + delta) % _list.Count + _list.Count) % _list.Count;
        ShowCurrent();
    }

    void NextUnsized()
    {
        for (int i = 1; i <= _list.Count; i++)
        {
            int index = (_index + i) % _list.Count;

            if (!VRModelTuning.Has(_list[index].Key))
            {
                _index = index;
                ShowCurrent();
                return;
            }
        }

        _status = "All models are sized!";
    }

    void SaveAndNext()
    {
        VRModelLibrary.ModelInfo model = Current;

        if (model == null)
        {
            return;
        }

        VRModelTuning.Set(model.Key, VRModelTuning.Get(model.Key));
        NextUnsized();
    }

    void Scale(float factor)
    {
        VRModelLibrary.ModelInfo model = Current;

        if (model != null)
        {
            VRModelTuning.Set(model.Key, VRModelTuning.Get(model.Key) * factor);
            ApplyTransform();
        }
    }

    void ResetSize()
    {
        VRModelLibrary.ModelInfo model = Current;

        if (model != null)
        {
            VRModelTuning.Clear(model.Key);
            ApplyTransform();
        }
    }

    void Level(int delta)
    {
        _level = Mathf.Clamp(_level + delta, 2, 7);
        ApplyTransform();
    }

    // ------------------------------------------------------------------ model

    async void ShowCurrent()
    {
        UnloadModel();
        VRModelLibrary.ModelInfo model = Current;
        _yaw = 0f;
        _status = "";
        Refresh();

        if (model == null)
        {
            return;
        }

        int token = ++_loadToken;
        _status = "Loading...";
        Refresh();

        Transform yours = await VRFieldModels.LoadPreview(model.Path, _turn[0]);
        Transform theirs = yours != null ? await VRFieldModels.LoadPreview(model.Path, _turn[1]) : null;

        if (token != _loadToken || !Active || this == null)
        {
            // Moved on (or closed) while loading.
            VRFieldModels.ReleasePreview(model.Path, yours);
            VRFieldModels.ReleasePreview(model.Path, theirs);
            return;
        }

        if (yours == null)
        {
            _status = "Could not load this model";
            Refresh();
            return;
        }

        _loadedPath = model.Path;
        _model[0] = yours;
        _model[1] = theirs;

        for (int side = 0; side < 2; side++)
        {
            if (_model[side] == null)
            {
                continue;
            }

            Animation anim = _model[side].GetComponentInChildren<Animation>(true);
            _anims[side] = anim;
            _idles[side] = null;

            if (anim != null)
            {
                AnimationClip idle = VRFieldModels.Clip(anim, "_bn01") ?? VRFieldModels.Clip(anim, "_bn02");
                anim.playAutomatically = false;
                anim.cullingType = AnimationCullingType.AlwaysAnimate;

                if (idle != null)
                {
                    _idles[side] = idle.name;
                    anim[idle.name].wrapMode = WrapMode.Loop;
                    anim.Play(idle.name);
                }
            }
        }

        _status = "";
        ApplyTransform();
    }

    void UnloadModel()
    {
        _loadToken++;

        for (int side = 0; side < 2; side++)
        {
            if (_model[side] != null)
            {
                VRFieldModels.ReleasePreview(_loadedPath, _model[side]);
            }

            _model[side] = null;
            _anims[side] = null;
        }

        _loadedPath = null;
    }

    // Same formula as the field: Normalize(target = level height), then the hand tuning.
    void ApplyTransform()
    {
        float tune = VRModelTuning.Get(Current?.Key);
        float target = VRFieldModels.TargetHeight(_level);

        for (int side = 0; side < 2; side++)
        {
            if (_model[side] == null)
            {
                continue;
            }

            _turn[side].localScale = Vector3.one;
            _model[side].localScale = Vector3.one;
            VRFieldModels.Normalize(_model[side], target, out _);
            _turn[side].localScale = Vector3.one * tune;
            _turn[side].localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        _height = _model[0] != null ? MeasureHeight(_model[0]) : 0f;
        Refresh();
    }

    float MeasureHeight(Transform model)
    {
        float top = float.MinValue;
        float bottom = float.MaxValue;

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled)
            {
                top = Mathf.Max(top, renderer.bounds.max.y);
                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
            }
        }

        return top > bottom ? (top - bottom) / Mathf.Max(1e-4f, _arenaRoot.lossyScale.y) : 0f;
    }

    void Anim(string suffix)
    {
        bool found = false;

        for (int side = 0; side < 2; side++)
        {
            Animation anim = _anims[side];
            AnimationClip clip = VRFieldModels.Clip(anim, suffix);

            if (clip == null)
            {
                continue;
            }

            found = true;
            string idle = _idles[side];
            anim[clip.name].wrapMode = clip.name == idle ? WrapMode.Loop : WrapMode.Once;
            anim.Stop();
            anim.Play(clip.name);

            if (idle != null && clip.name != idle)
            {
                anim.PlayQueued(idle, QueueMode.CompleteOthers);
            }
        }

        _status = found ? "" : "No " + suffix.TrimStart('_') + " animation";
    }

    // ------------------------------------------------------------------ text

    void Refresh()
    {
        VRModelLibrary.ModelInfo model = Current;
        _title.text = "MODEL STUDIO   " + (_list.Count > 0 ? (_index + 1) + " / " + _list.Count : "no models") + "   sized " + VRModelTuning.Count;

        if (model == null)
        {
            _name.text = "<b>No _DSTSModels folder found</b>";
            _measure.text = "";
        }
        else
        {
            bool sized = VRModelTuning.Has(model.Key);
            _name.text = "<b>" + model.Name + "</b>  <size=70%>" + model.Key + (model.Category != "digimon" ? " · " + model.Category : "") + (sized ? "  <color=#7CFC9A>SAVED</color>" : "  <color=#FFB060>auto</color>") + "</size>";
            _measure.text = _status.Length > 0 ? _status : "Size x" + VRModelTuning.Get(model.Key).ToString("0.###") + "   height " + _height.ToString("0.00") + " m   (Lv." + _level + " target " + VRFieldModels.TargetHeight(_level).ToString("0.0") + " m)";
        }

        foreach (Action refresh in _refreshers)
        {
            refresh();
        }
    }
}
#endif
