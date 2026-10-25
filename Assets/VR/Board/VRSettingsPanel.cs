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

        Add(() => "View: " + (_rig.DuelMode ? "Duel Disk" : _rig.BoardMode ? "Table" : "Flat screen"), () => _rig.CycleView());
        Add(() => "Side screen in duels: " + (VRSettings.HideScreenInDuel ? "Hidden" : "Shown"), () => VRSettings.HideScreenInDuel = !VRSettings.HideScreenInDuel);
        Add(() => "Hand cards: " + (_board.HandHeld ? "Held in hand" : "Floating"), () =>
        {
            _board.HandHeld = !_board.HandHeld;
            VRSettings.HandFloating = !_board.HandHeld;
        });
        Add(() => "Hand fan: " + (_board.HandSpread > 0.5f ? "Wide" : "Tight"), () => _board.HandSpread = _board.HandSpread > 0.5f ? 0.15f : 1f);
        Add(() => "Big card preview: " + (VRSettings.CardPreview ? "On" : "Off"), () => VRSettings.CardPreview = !VRSettings.CardPreview);
        Add(() => "Vibration: " + (VRSettings.Haptics ? "On" : "Off"), () => VRSettings.Haptics = !VRSettings.Haptics);
        Add(() => "Reset hologram position", () => _rig.ResetHoloPlacement());
        Add(() => "Close", Close);

        const float step = 0.048f;
        float height = (_plates.Count + 1) * step + 0.03f;
        float top = height * 0.5f;
        _frame.localScale = new Vector3(0.36f, height, 0.004f);
        _frame.localPosition = new Vector3(0f, 0f, 0.006f);
        _title.transform.localPosition = new Vector3(0f, top - 0.035f, -0.001f);

        for (int i = 0; i < _plates.Count; i++)
        {
            _plates[i].transform.localPosition = new Vector3(0f, top - 0.035f - (i + 1) * step, -0.002f);
        }
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
