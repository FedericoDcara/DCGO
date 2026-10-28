#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Settings panel, opened with the MENU button on the wrist disk. Opens in front of
// you; every entry is a plate you touch or laser. Choices are saved (VRSettings).
public class VRSettingsPanel : MonoBehaviour
{
    VRRig _rig;
    Board3D _board;
    Transform _frame;
    VRLabel _title;
    VRModelTuner _tuner;
    VRModelStudio _studio;
    readonly List<VRButton3D> _plates = new List<VRButton3D>();
    readonly List<Func<string>> _labels = new List<Func<string>>();

    public bool IsOpen => gameObject.activeSelf;

    public static VRSettingsPanel Create(Transform parent, VRRig rig, Board3D board)
    {
        GameObject go = new GameObject("SettingsPanel") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRSettingsPanel panel = go.AddComponent<VRSettingsPanel>();
        panel._rig = rig;
        panel._board = board;
        panel.Build();
        go.SetActive(false);
        return panel;
    }

    void Build()
    {
        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "Frame";
        frame.layer = VRRig.Layer;
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(transform, false);
        frame.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.05f, 0.09f, 0.14f));
        _frame = frame.transform;

        _title = VRLabel.Create(transform, Vector3.zero, Quaternion.identity, new Vector2(0.3f, 0.04f), "VR SETTINGS", new Color(0.55f, 0.95f, 1f), new Color(0.05f, 0.09f, 0.14f));

        Add(() => "Field hologram: " + (VRSettings.HoloBoard ? "On" : "Off"), () => VRSettings.HoloBoard = !VRSettings.HoloBoard);
        Add(() => "Debug logs: " + (VRSettings.VerboseLogs ? "On (slower)" : "Off"), () => VRPerformance.SetVerboseLogs(!VRSettings.VerboseLogs));
        Add(() => "Side screen in duels: " + (VRSettings.HideScreenInDuel ? "Hidden" : "Shown"), () => VRSettings.HideScreenInDuel = !VRSettings.HideScreenInDuel);
        Add(() => "Hand cards: " + (_board.HandHeld ? "Held in hand" : "Floating"), () =>
        {
            _board.HandHeld = !_board.HandHeld;
            VRSettings.HandFloating = !_board.HandHeld;
        });
        Add(() => "Hand fan: " + (_board.HandSpread > 0.5f ? "Wide" : "Tight"), () => _board.HandSpread = _board.HandSpread > 0.5f ? 0.15f : 1f);
        Add(() => "Memory gauge on top of view: " + (VRSettings.MemoryHud ? "On" : "Off"), () => VRSettings.MemoryHud = !VRSettings.MemoryHud);
        Add(() => "Big card preview: " + (VRSettings.CardPreview ? "On" : "Off"), () => VRSettings.CardPreview = !VRSettings.CardPreview);
        Add(() => "Vibration: " + (VRSettings.Haptics ? "On" : "Off"), () => VRSettings.Haptics = !VRSettings.Haptics);
        Add(() => "Duel arena (3D): " + (VRSettings.Models ? "On" : "Off") + (VRModelLibrary.Available ? "" : " (cards only)"), () => VRSettings.Models = !VRSettings.Models);
        Add(() => "3D Digimon size: " + (VRSettings.ModelScale < 0.9f ? "Small" : VRSettings.ModelScale > 1.1f ? "Large" : "Medium"), () =>
        {
            VRSettings.ModelScale = VRSettings.ModelScale < 0.9f ? 1f : VRSettings.ModelScale > 1.1f ? 0.75f : 1.35f;
            VRSession.Instance?.Board?.RefreshModels();
        });
        Add(() => "Tune 3D model sizes (debug)...", () =>
        {
            if (_tuner == null)
            {
                _tuner = VRModelTuner.Create(transform.parent, _board);
            }

            _tuner.Open(transform.position, transform.rotation);
            Close();
        });
        Add(() => "3D model studio (size every model)...", () =>
        {
            if (_studio == null)
            {
                _studio = VRModelStudio.Create(transform.parent);
            }

            _studio.Open(_rig, _board.Head);
            Close();
        });
        Add(() => "Opponent in person (3D): " + (VRSettings.OpponentAvatar ? "On" : "Off"), () => VRSettings.OpponentAvatar = !VRSettings.OpponentAvatar);
        Add(() => "Security check scene: " + (VRSettings.SecurityBattle ? "On" : "Off"), () => VRSettings.SecurityBattle = !VRSettings.SecurityBattle);
        Add(() => "Player webcams: " + (VRSettings.AvatarCams ? "On" : "Off"), () => VRSettings.AvatarCams = !VRSettings.AvatarCams);
        Add(() => "My avatar: " + AvatarName(), () =>
        {
            List<VRModelLibrary.ModelInfo> choices = VRAvatarCams.Choices();

            if (choices.Count > 0)
            {
                int index = choices.FindIndex(m => m.Key == VRSettings.MyAvatar);
                VRSettings.MyAvatar = choices[(index + 1) % choices.Count].Key;
                VRSession.Instance?.AvatarCams?.Reload();
            }
        });
        Add(() => "Duel background: " + (VRSettings.DuelEnvironment ? "Digital World" : "Off"), () => VRSettings.DuelEnvironment = !VRSettings.DuelEnvironment);
        Add(() => "Arena view: " + (VRSettings.ArenaOverview ? "Overview (from above)" : "Ground level"), () =>
        {
            VRSettings.ArenaOverview = !VRSettings.ArenaOverview;
            _rig.ApplyArenaDistance();
        });
        Add(() => "Arena distance: " + (VRSettings.ArenaDistance < 2.1f ? "Near" : VRSettings.ArenaDistance > 2.7f ? "Far" : "Medium"), () =>
        {
            VRSettings.ArenaDistance = VRSettings.ArenaDistance < 2.1f ? 2.4f : VRSettings.ArenaDistance > 2.7f ? 1.9f : 3.1f;
            _rig.ApplyArenaDistance();
        });
        Add(() => "Render quality: " + VRSettings.RenderScale + "%", () =>
        {
            int[] steps = { 80, 100, 120, 140 };
            int index = Array.IndexOf(steps, VRSettings.RenderScale);
            VRSettings.RenderScale = steps[(index + 1) % steps.Length];
            VRPerf.ApplyRenderScale();
        });
        Add(() => "Performance stats: " + (VRSettings.PerfStats ? "On" : "Off"), () => VRSettings.PerfStats = !VRSettings.PerfStats);
        Add(() => "Reset hologram position", () => _rig.ResetHoloPlacement());
        Add(() => "Close", Close);

        // Two columns once the list gets long, so the panel stays within view.
        const float step = 0.048f;
        const float columnWidth = 0.34f;
        int columns = _plates.Count > 12 ? 2 : 1;
        int perColumn = Mathf.CeilToInt(_plates.Count / (float)columns);
        float height = (perColumn + 1) * step + 0.03f;
        float top = height * 0.5f;
        float width = columns * columnWidth + 0.02f;
        _frame.localScale = new Vector3(width, height, 0.004f);
        _frame.localPosition = new Vector3(0f, 0f, 0.006f);
        _title.transform.localPosition = new Vector3(0f, top - 0.035f, -0.001f);

        for (int i = 0; i < _plates.Count; i++)
        {
            int column = i / perColumn;
            int row = i % perColumn;
            float x = (column - (columns - 1) * 0.5f) * columnWidth;
            _plates[i].transform.localPosition = new Vector3(x, top - 0.035f - (row + 1) * step, -0.002f);
        }
    }

    static string AvatarName()
    {
        VRModelLibrary.ModelInfo info = VRModelLibrary.All.Find(m => m.Key == VRSettings.MyAvatar);
        return info != null ? info.Name : VRSettings.MyAvatar;
    }

    void Add(Func<string> label, Action action)
    {
        VRButton3D plate = null;
        plate = VRButton3D.CreatePlate(transform, Vector3.zero, new Vector2(0.32f, 0.04f), label(), () =>
        {
            action();
            Refresh();
        });
        _plates.Add(plate);
        _labels.Add(label);
    }

    void Refresh()
    {
        for (int i = 0; i < _plates.Count; i++)
        {
            if (_plates[i] != null)
            {
                _plates[i].SetLabel(_labels[i]());
            }
        }
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        Transform head = _board.Head;

        if (head == null)
        {
            return;
        }

        Vector3 forward = head.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        transform.position = head.position + forward * 0.5f + Vector3.down * 0.05f;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(8f, 0f, 0f);
        gameObject.SetActive(true);
        Refresh();
    }

    public void Close() => gameObject.SetActive(false);
}
#endif
