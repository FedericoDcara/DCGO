#if DCGO_VR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// === DCGO-CUSTOM:vr ===
// Virtual keyboard for the flat game's text fields (player name, room name /
// password, deck codes, deck search...). It opens by itself when a text field on
// the game screen is selected with the laser, floats in front of you below eye
// level, and types straight into that field. Keys work with the laser + trigger
// or by touching them. PASTE inserts the PC clipboard (handy for deck codes).
public class VRKeyboard : MonoBehaviour
{
    const float Key = 0.046f;
    const float Gap = 0.006f;
    const float Step = Key + Gap;

    static readonly string[] Rows =
    {
        "1234567890",
        "qwertyuiop",
        "asdfghjkl-",
        "zxcvbnm,.@",
    };

    VRRig _rig;
    VRLabel _preview;
    readonly List<KeyValuePair<VRButton3D, char>> _letters = new List<KeyValuePair<VRButton3D, char>>();
    VRButton3D _shiftButton;
    bool _shift;
    bool _caps;
    GameObject _field;        // the TMP_InputField / InputField being edited
    GameObject _dismissedFor; // HIDE pressed: stay closed until another field is chosen
    float _nextPreview;
    float _lastKeyTime = -1f;
    int _lastKeyFrame = -1;
    string _lastKeyName;
    const float KeyDebounce = 0.18f;

    public bool IsOpen => gameObject.activeSelf;

    public static VRKeyboard Create(Transform parent, VRRig rig)
    {
        GameObject root = new GameObject("VRKeyboard") { layer = VRRig.Layer };
        root.transform.SetParent(parent, false);
        VRKeyboard keyboard = root.AddComponent<VRKeyboard>();
        keyboard._rig = rig;
        keyboard.Build();
        root.SetActive(false);

        GameObject watcher = new GameObject("VRKeyboardWatcher");
        watcher.transform.SetParent(parent, false);
        watcher.AddComponent<Watcher>().Keyboard = keyboard;
        return keyboard;
    }

    void Build()
    {
        // Backing plate.
        float width = 10 * Step + 0.03f;
        float height = 7 * Step + 0.05f;
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "KeyboardPlate";
        plate.layer = VRRig.Layer;
        Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(transform, false);
        plate.transform.localPosition = new Vector3(0f, -0.02f, 0.01f);
        plate.transform.localScale = new Vector3(width, height, 0.006f);
        plate.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.04f, 0.07f, 0.11f));

        // What you are typing (the real field may be far away on the screen).
        _preview = VRLabel.Create(transform, new Vector3(0f, 2.5f * Step + 0.02f, -0.004f), Quaternion.identity, new Vector2(width - 0.03f, 0.04f), "", Color.white, new Color(0.02f, 0.03f, 0.05f));
        _preview.alignment = TextAlignmentOptions.Left;

        for (int r = 0; r < Rows.Length; r++)
        {
            string row = Rows[r];
            float y = (1.5f - r) * Step;

            for (int c = 0; c < row.Length; c++)
            {
                char ch = row[c];
                float x = (c - (row.Length - 1) * 0.5f) * Step;
                VRButton3D key = VRButton3D.CreatePlate(transform, new Vector3(x, y, 0f), new Vector2(Key, Key), ch.ToString(), () => TypeLetter(ch));
                _letters.Add(new KeyValuePair<VRButton3D, char>(key, ch));
            }
        }

        // Bottom row: SHIFT  SPACE  BACK  PASTE  CLEAR  ENTER  HIDE
        float by = -2.5f * Step;
        _shiftButton = Wide(-4.0f, 1.8f, by, "SHIFT", ToggleShift);
        Wide(-1.6f, 2.8f, by, "SPACE", () => Insert(" "));
        Wide(0.85f, 1.9f, by, "BACK", Backspace);
        Wide(2.85f, 1.9f, by, "PASTE", () => Insert(GUIUtility.systemCopyBuffer ?? ""));
        Wide(4.4f, 1.0f, by, "CLR", Clear);

        // Last row: ENTER (confirm the field) and HIDE.
        float sy = -3.5f * Step;
        Wide(-2.0f, 3.5f, sy, "ENTER", Submit);
        Wide(2.0f, 3.5f, sy, "HIDE", Hide);
    }

    // One physical press = one character: ignore repeats that arrive in the same
    // frame or right after (two hands / laser + touch / a bouncing trigger).
    bool AcceptKey(string name)
    {
        float now = Time.unscaledTime;

        float window = name == _lastKeyName ? KeyDebounce : 0.06f;

        if (Time.frameCount == _lastKeyFrame || now - _lastKeyTime < window)
        {
            Debug.Log($"[DCGO VR] Keyboard: ignored repeat of '{name}' ({(now - _lastKeyTime) * 1000f:0} ms after the last key)");
            return false;
        }

        _lastKeyTime = now;
        _lastKeyFrame = Time.frameCount;
        _lastKeyName = name;
        Debug.Log($"[DCGO VR] Keyboard: key '{name}' (frame {Time.frameCount})");
        return true;
    }

    VRButton3D Wide(float centerKeys, float widthKeys, float y, string label, Action action)
    {
        Action guarded = () =>
        {
            if (AcceptKey(label))
            {
                action();
            }
        };

        return VRButton3D.CreatePlate(transform, new Vector3(centerKeys * Step, y, 0f), new Vector2(widthKeys * Step - Gap, Key), label, guarded);
    }

    // ---------------------------------------------------------------- open / close

    void Open(GameObject field)
    {
        _field = field;

        if (!gameObject.activeSelf)
        {
            Transform head = _rig.Camera.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            transform.position = head.position + forward * 0.48f + Vector3.down * 0.30f;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(35f, 0f, 0f);
            gameObject.SetActive(true);
            Debug.Log("[DCGO VR] Keyboard opened for " + field.name);
        }

        UpdatePreview();
    }

    void Close()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }

        _field = null;
    }

    void Hide()
    {
        _dismissedFor = _field;
        Close();
    }

    // Called every frame by the watcher (which stays active while the keyboard is hidden).
    void Watch()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        GameObject field = IsTextField(selected) ? selected : null;

        if (field == null)
        {
            _dismissedFor = null;

            // Keep the keyboard while it is open for a field that is still alive
            // (pressing 3D keys must not close it); close when the field goes away.
            if (_field == null || !_field.activeInHierarchy)
            {
                Close();
            }

            return;
        }

        if (field == _dismissedFor)
        {
            return;
        }

        if (field != _field || !gameObject.activeSelf)
        {
            Open(field);
        }
    }

    static bool IsTextField(GameObject go)
    {
        if (go == null || !go.activeInHierarchy)
        {
            return false;
        }

        TMP_InputField tmp = go.GetComponent<TMP_InputField>();

        if (tmp != null)
        {
            return tmp.interactable && !tmp.readOnly;
        }

        InputField legacy = go.GetComponent<InputField>();
        return legacy != null && legacy.interactable && !legacy.readOnly;
    }

    // ---------------------------------------------------------------- typing

    void TypeLetter(char ch)
    {
        if (!AcceptKey(ch.ToString()))
        {
            return;
        }

        bool upper = _shift ^ _caps;
        Insert((upper ? char.ToUpperInvariant(ch) : ch).ToString());

        if (_shift)
        {
            _shift = false;
            RefreshShift();
        }
    }

    void ToggleShift()
    {
        // Tap = one capital letter, tap again = caps lock, third tap = off.
        if (!_shift && !_caps)
        {
            _shift = true;
        }
        else if (_shift)
        {
            _shift = false;
            _caps = true;
        }
        else
        {
            _caps = false;
        }

        RefreshShift();
    }

    void RefreshShift()
    {
        bool upper = _shift ^ _caps;

        foreach (KeyValuePair<VRButton3D, char> pair in _letters)
        {
            pair.Key.SetLabel((upper ? char.ToUpperInvariant(pair.Value) : pair.Value).ToString());
        }

        _shiftButton.SetLabel(_caps ? "CAPS" : _shift ? "Shift" : "SHIFT");
    }

    void Insert(string value)
    {
        if (string.IsNullOrEmpty(value) || _field == null)
        {
            return;
        }

        value = value.Replace("\r", "");

        if (_field.TryGetComponent(out TMP_InputField tmp))
        {
            if (tmp.lineType == TMP_InputField.LineType.SingleLine)
            {
                value = value.Replace("\n", "");
            }

            string text = tmp.text ?? "";
            int a = Mathf.Clamp(Mathf.Min(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, text.Length);
            int b = Mathf.Clamp(Mathf.Max(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, text.Length);

            if (!tmp.isFocused)
            {
                a = b = text.Length;
            }

            string result = text.Substring(0, a) + value + text.Substring(b);

            if (tmp.characterLimit > 0 && result.Length > tmp.characterLimit)
            {
                result = result.Substring(0, tmp.characterLimit);
            }

            tmp.text = result;
            int caret = Mathf.Min(a + value.Length, result.Length);
            tmp.stringPosition = caret;
            tmp.selectionStringAnchorPosition = caret;
            tmp.selectionStringFocusPosition = caret;
        }
        else if (_field.TryGetComponent(out InputField legacy))
        {
            if (legacy.lineType == InputField.LineType.SingleLine)
            {
                value = value.Replace("\n", "");
            }

            string text = legacy.text ?? "";
            int a = Mathf.Clamp(Mathf.Min(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, text.Length);
            int b = Mathf.Clamp(Mathf.Max(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, text.Length);

            if (!legacy.isFocused)
            {
                a = b = text.Length;
            }

            string result = text.Substring(0, a) + value + text.Substring(b);

            if (legacy.characterLimit > 0 && result.Length > legacy.characterLimit)
            {
                result = result.Substring(0, legacy.characterLimit);
            }

            legacy.text = result;
            legacy.caretPosition = Mathf.Min(a + value.Length, result.Length);
        }

        UpdatePreview();
    }

    void Backspace()
    {
        if (_field == null)
        {
            return;
        }

        if (_field.TryGetComponent(out TMP_InputField tmp))
        {
            string text = tmp.text ?? "";
            int a = Mathf.Clamp(Mathf.Min(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, text.Length);
            int b = Mathf.Clamp(Mathf.Max(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, text.Length);

            if (!tmp.isFocused)
            {
                a = b = text.Length;
            }

            if (a == b && a > 0)
            {
                a--;
            }

            tmp.text = text.Substring(0, a) + text.Substring(b);
            tmp.stringPosition = a;
            tmp.selectionStringAnchorPosition = a;
            tmp.selectionStringFocusPosition = a;
        }
        else if (_field.TryGetComponent(out InputField legacy))
        {
            string text = legacy.text ?? "";
            int a = Mathf.Clamp(Mathf.Min(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, text.Length);
            int b = Mathf.Clamp(Mathf.Max(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, text.Length);

            if (!legacy.isFocused)
            {
                a = b = text.Length;
            }

            if (a == b && a > 0)
            {
                a--;
            }

            legacy.text = text.Substring(0, a) + text.Substring(b);
            legacy.caretPosition = a;
        }

        UpdatePreview();
    }

    void Clear()
    {
        if (_field == null)
        {
            return;
        }

        if (_field.TryGetComponent(out TMP_InputField tmp))
        {
            tmp.text = "";
        }
        else if (_field.TryGetComponent(out InputField legacy))
        {
            legacy.text = "";
        }

        UpdatePreview();
    }

    void Submit()
    {
        if (_field == null)
        {
            return;
        }

        if (_field.TryGetComponent(out TMP_InputField tmp))
        {
            if (tmp.lineType == TMP_InputField.LineType.MultiLineNewline)
            {
                Insert("\n");
                return;
            }

            tmp.onSubmit?.Invoke(tmp.text);
            tmp.onEndEdit?.Invoke(tmp.text);
            tmp.DeactivateInputField();
        }
        else if (_field.TryGetComponent(out InputField legacy))
        {
            if (legacy.lineType == InputField.LineType.MultiLineNewline)
            {
                Insert("\n");
                return;
            }

            legacy.onEndEdit?.Invoke(legacy.text);
            legacy.DeactivateInputField();
        }

        Debug.Log("[DCGO VR] Keyboard: entered text");

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _field)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        Close();
    }

    void UpdatePreview()
    {
        if (_field == null)
        {
            _preview.text = "";
            return;
        }

        string text = "";
        bool password = false;

        if (_field.TryGetComponent(out TMP_InputField tmp))
        {
            text = tmp.text;
            password = tmp.contentType == TMP_InputField.ContentType.Password || tmp.inputType == TMP_InputField.InputType.Password;
        }
        else if (_field.TryGetComponent(out InputField legacy))
        {
            text = legacy.text;
            password = legacy.contentType == InputField.ContentType.Password || legacy.inputType == InputField.InputType.Password;
        }

        text = text ?? "";

        if (password)
        {
            text = new string('*', text.Length);
        }

        // Show the end of long text (deck codes) so the newest characters are visible.
        if (text.Length > 46)
        {
            text = "..." + text.Substring(text.Length - 43);
        }

        _preview.text = text.Replace("<", "<​") + "|";
    }

    void Update()
    {
        // The field can also change from the game itself (e.g. cleared after search).
        if (Time.unscaledTime >= _nextPreview)
        {
            _nextPreview = Time.unscaledTime + 0.25f;
            UpdatePreview();
        }
    }

    sealed class Watcher : MonoBehaviour
    {
        public VRKeyboard Keyboard;

        void LateUpdate()
        {
            if (Keyboard != null)
            {
                Keyboard.Watch();
            }
        }
    }
}
#endif
