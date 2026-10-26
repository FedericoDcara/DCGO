#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

// === DCGO-CUSTOM:vr ===
// "React now" cue. When the game starts waiting for YOUR answer outside your normal main-phase
// play (an effect asks yes/no, block / redirect an attack, choose a target, pick cards...),
// often during the opponent's turn:
//   - both controllers give a double buzz (once per new request)
// The instruction itself is shown on the duel disk's info screen (VRDuelDisk); no floating banner.
public class VRAttention : MonoBehaviour
{
    const float ScanInterval = 0.2f;
    const int MaxChars = 110;

    Board3D _board;
    Transform _head;
    float _nextScan;
    bool _pending;
    string _signature = "";
    readonly List<Button> _buttons = new List<Button>();
    readonly List<string> _labels = new List<string>();


    public static VRAttention Create(Transform parent, Board3D board, Transform head)
    {
        GameObject go = new GameObject("AttentionCue") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRAttention attention = go.AddComponent<VRAttention>();
        attention._board = board;
        attention._head = head;
        return attention;
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + ScanInterval;
            Scan();
        }
    }

    void Scan()
    {
        GameContext context = BoardObserver.Context;

        if (_board == null || context == null || context.You == null || context.Opponent == null)
        {
            SetPending(false, "", "");
            return;
        }

        // Your normal main-phase play (play / digivolve / attack) is not a "response".
        if (LegalActions.IsAwaitingLocalInput(out _))
        {
            SetPending(false, "", "");
            return;
        }

        string message = Clean(VRPromptBridge.Message);
        VRPromptBridge.CollectButtons(_buttons, _labels);
        int panelCards = VRPromptBridge.PanelCards.Count;
        int arenaTargets = CountArenaTargets(context);
        bool opponentTarget = _board.OpponentIsTarget;

        // Only something you can actually act on counts (instruction text alone can be the opponent's).
        bool pending = _buttons.Count > 0 || panelCards > 0 || arenaTargets > 0 || opponentTarget;

        if (!pending)
        {
            SetPending(false, "", "");
            return;
        }

        string where;

        if (_buttons.Count > 0 || panelCards > 0)
        {
            where = "Answer on the panel";
        }
        else if (arenaTargets > 0 && opponentTarget)
        {
            where = "Point at a glowing Digimon or the opponent";
        }
        else if (arenaTargets > 0)
        {
            where = "Point at a glowing Digimon";
        }
        else if (opponentTarget)
        {
            where = "Point at the opponent / her shields";
        }
        else
        {
            where = "Check the panel / your hand";
        }

        string text = "<b>YOUR RESPONSE</b>" + (message.Length > 0 ? "\n" + message : "") + "\n<size=75%>" + where + "</size>";
        // A new request = different instruction or different kind of answer.
        string signature = message + "|" + where;
        SetPending(true, signature, text);
    }

    void SetPending(bool pending, string signature, string text)
    {
        if (pending && (!_pending || signature != _signature))
        {
            Buzz();
        }

        _pending = pending;
        _signature = signature;
    }

    static void Buzz()
    {
        VRLaserPointer.Haptic(XRNode.LeftHand, 0.7f, 0.12f);
        VRLaserPointer.Haptic(XRNode.RightHand, 0.7f, 0.12f);
        DoubleBuzz();
    }

    static async void DoubleBuzz()
    {
        await System.Threading.Tasks.Task.Delay(220);
        VRLaserPointer.Haptic(XRNode.LeftHand, 0.7f, 0.12f);
        VRLaserPointer.Haptic(XRNode.RightHand, 0.7f, 0.12f);
    }

    int CountArenaTargets(GameContext context)
    {
        int n = 0;

        try
        {
            foreach (Player player in new[] { context.You, context.Opponent })
            {
                foreach (Permanent permanent in player.GetFieldPermanents())
                {
                    if (_board.ArenaGlow(permanent).HasValue)
                    {
                        n++;
                    }
                }
            }
        }
        catch (System.Exception)
        {
            // board mid-update
        }

        return n;
    }

    static string Clean(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        message = message.Replace("\r", " ").Replace("\n", " ").Trim();

        while (message.Contains("  "))
        {
            message = message.Replace("  ", " ");
        }

        return message.Length > MaxChars ? message.Substring(0, MaxChars - 3) + "..." : message;
    }
}
#endif
