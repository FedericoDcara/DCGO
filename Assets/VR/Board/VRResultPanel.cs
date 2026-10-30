#if DCGO_VR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using S = VRMenuStyle;

// === DCGO-CUSTOM:vr ===
// End-of-match screen in VR. Follows the game's own result screen (ResultObject): when it
// opens, a panel appears in front of you with VICTORY / DEFEAT / DRAW / DISCONNECTED and the
// game's result line ("The opponent has surrendered." ...), plus RETURN TO MENU and VIEW
// BOARD. Both buttons click the flat result screen's own buttons, so the game ends the
// battle exactly as on the flat screen. While viewing the board a small BACK TO RESULTS
// button floats below your view (the flat "Back to Results" button).
public class VRResultPanel : MonoBehaviour
{
    static readonly Color VictoryColor = new Color(0.976f, 0.678f, 0.192f);
    static readonly Color DefeatColor = new Color(0.36f, 0.42f, 0.52f);
    static readonly Color DrawColor = new Color(0.55f, 0.62f, 0.70f);

    static FieldInfo _winImage;
    static FieldInfo _resultText;

    VRRig _rig;
    Transform _root;
    string _shown = "";
    float _next;
    float _openedAt;

    public static VRResultPanel Create(Transform parent, VRRig rig)
    {
        GameObject go = new GameObject("ResultPanel") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRResultPanel panel = go.AddComponent<VRResultPanel>();
        panel._rig = rig;
        return panel;
    }

    void Update()
    {
        // Pop-in animation of the result card.
        if (_root != null)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - _openedAt) / 0.3f);
            float e = 1f - Mathf.Pow(1f - k, 3f);
            _root.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, e);
        }

        if (Time.unscaledTime < _next)
        {
            return;
        }

        _next = Time.unscaledTime + 0.2f;
        GManager gm = GManager.instance;
        ResultObject result = gm != null ? gm.resultObject : null;
        bool open = result != null && result.gameObject.activeInHierarchy;
        Button backButton = result != null ? result.ReturnToResultButton : null;
        bool viewing = !open && backButton != null && backButton.gameObject.activeInHierarchy;
        string text = open ? ResultLine(result) : "";
        string key = open ? "result|" + Outcome(result, text) + "|" + text : viewing ? "board" : "";

        if (key == _shown)
        {
            return;
        }

        _shown = key;
        Clear();

        if (open)
        {
            BuildResult(result, Outcome(result, text), text);
        }
        else if (viewing)
        {
            BuildBackToResults(backButton);
        }
    }

    void Clear()
    {
        if (_root != null)
        {
            Destroy(_root.gameObject);
            _root = null;
        }
    }

    // ---------------------------------------------------------------- reading the flat result

    static string ResultLine(ResultObject result)
    {
        _resultText ??= typeof(ResultObject).GetField("ResultText", BindingFlags.Instance | BindingFlags.NonPublic);
        Text text = _resultText?.GetValue(result) as Text;
        return text != null ? text.text ?? "" : "";
    }

    static string Outcome(ResultObject result, string line)
    {
        if (line.IndexOf("Disconnected", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "DISCONNECTED";
        }

        if (line.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "DRAW";
        }

        _winImage ??= typeof(ResultObject).GetField("WinImage", BindingFlags.Instance | BindingFlags.NonPublic);
        Image win = _winImage?.GetValue(result) as Image;
        return win != null && win.gameObject.activeSelf ? "VICTORY" : "DEFEAT";
    }

    // ---------------------------------------------------------------- building

    Transform Place(float distance, float down, float side)
    {
        Transform head = _rig.Camera.transform;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Transform root = new GameObject("Result") { layer = VRRig.Layer }.transform;
        root.SetParent(transform, false);
        root.position = head.position + forward * distance + Vector3.down * down + right * side;
        root.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(6f, 0f, 0f);
        _openedAt = Time.unscaledTime;
        return root;
    }

    void BuildResult(ResultObject result, string outcome, string line)
    {
        _root = Place(0.75f, 0.06f, 0f);
        Color banner = outcome == "VICTORY" ? VictoryColor : outcome == "DEFEAT" ? DefeatColor : DrawColor;
        const float w = 0.64f;
        const float h = 0.40f;

        S.Shape(_root, "Halo", S.RoundedRect(w + 0.05f, h + 0.05f, 0.05f), S.Glass(S.Halo), new Vector3(0f, 0f, 0.02f));
        S.Card(_root, "Body", new Vector3(0f, 0f, 0.012f), new Vector2(w, h), 0.03f, S.Panel, S.Edge, 0.003f);

        // Banner with the outcome in big letters.
        S.Card(_root, "Banner", new Vector3(0f, h * 0.5f - 0.085f, 0.008f), new Vector2(w - 0.04f, 0.13f), 0.024f, banner);
        S.Text(_root, new Vector3(0f, h * 0.5f - 0.085f, 0f), new Vector2(w - 0.12f, 0.08f), outcome, Color.white, banner);
        S.Shape(_root, "TickL", S.RoundedRect(0.008f, 0.06f, 0.004f), S.Solid(Color.white), new Vector3(-w * 0.5f + 0.05f, h * 0.5f - 0.085f, 0.004f));
        S.Shape(_root, "TickR", S.RoundedRect(0.008f, 0.06f, 0.004f), S.Solid(Color.white), new Vector3(w * 0.5f - 0.05f, h * 0.5f - 0.085f, 0.004f));

        // The game's result line (who surrendered, draw...), or a default.
        string sub = !string.IsNullOrEmpty(line) ? line
            : outcome == "VICTORY" ? "You won the duel." : outcome == "DEFEAT" ? "You lost the duel." : "";
        S.Text(_root, new Vector3(0f, 0.0f, 0f), new Vector2(w - 0.08f, 0.036f), sub, S.Ink, S.Panel);
        S.Shape(_root, "Rule", S.RoundedRect(w - 0.08f, 0.002f, 0f), S.Solid(S.Hairline), new Vector3(0f, -0.045f, 0.0105f));

        Button toMenu = FlatUI.ButtonCalling(result, "ReturnToTitle");
        Button viewBoard = FlatUI.ButtonCalling(result, "OnClickCheckFieldResultsButton");
        S.Button(_root, new Vector3(-0.155f, -0.11f, 0f), new Vector2(0.28f, 0.066f), S.Shape3.Bar, "RETURN TO MENU", S.Icon.Home, () =>
        {
            Debug.Log("[DCGO VR] Result: return to menu");

            if (toMenu != null && toMenu.gameObject.activeInHierarchy)
            {
                FlatUI.Click(toMenu.gameObject);
            }
            else if (GManager.instance != null)
            {
                GManager.instance.ReturnToTitle();
            }
        });
        S.Button(_root, new Vector3(0.155f, -0.11f, 0f), new Vector2(0.28f, 0.066f), S.Shape3.Bar, "VIEW BOARD", S.Icon.Search, () =>
        {
            Debug.Log("[DCGO VR] Result: view board");

            if (viewBoard != null && viewBoard.gameObject.activeInHierarchy)
            {
                FlatUI.Click(viewBoard.gameObject);
            }
            else
            {
                result.OnClickCheckFieldResultsButton();
            }

            _next = 0f;
        }, viewBoard != null);

        Debug.Log("[DCGO VR] Result shown: " + outcome + (string.IsNullOrEmpty(line) ? "" : " - " + line));
    }

    void BuildBackToResults(Button back)
    {
        _root = Place(0.6f, 0.32f, -0.18f);
        S.Button(_root, Vector3.zero, new Vector2(0.30f, 0.06f), S.Shape3.Bar, "BACK TO RESULTS", S.Icon.Back, () =>
        {
            FlatUI.Click(back.gameObject);
            _next = 0f;
        });
    }
}
#endif
