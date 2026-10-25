#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Shows the real turn structure in VR:
//  - a phase strip along the far edge of the field: TURN n | UNSUSPEND  DRAW
//    BREEDING  MAIN  END, the current phase lit (blue on your turn, red on the
//    opponent's);
//  - a banner in front of you whenever the turn or phase changes
//    ("YOUR TURN 5 - DRAW PHASE"), with a short vibration on your turn.
// Read-only: it only looks at GameContext.TurnPhase / TurnPlayer.
public class VRPhaseBar : MonoBehaviour
{
    static readonly string[] Names = { "UNSUSPEND", "DRAW", "BREEDING", "MAIN", "END" };
    static readonly GameContext.phase[] Phases =
    {
        GameContext.phase.Active, GameContext.phase.Draw, GameContext.phase.Breeding, GameContext.phase.Main, GameContext.phase.End,
    };

    static readonly Color Idle = new Color(0.06f, 0.10f, 0.15f);
    static readonly Color YourPhase = new Color(0.10f, 0.50f, 0.85f);
    static readonly Color TheirPhase = new Color(0.70f, 0.15f, 0.15f);

    Board3D _board;
    Transform _head;
    Transform _strip;
    VRLabel _turn;
    readonly VRLabel[] _chips = new VRLabel[5];
    VRLabel _banner;
    float _bannerUntil;
    float _bannerStart;
    GameContext.phase _phase = GameContext.phase.None;
    Player _turnPlayer;
    int _turnCount = -1;

    public static VRPhaseBar Create(Transform parent, Board3D board, Transform head)
    {
        GameObject go = new GameObject("VRPhaseBar") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRPhaseBar bar = go.AddComponent<VRPhaseBar>();
        bar._board = board;
        bar._head = head;
        bar.Build();
        return bar;
    }

    void Build()
    {
        // The strip lives in board space, just beyond the HUD line.
        _strip = new GameObject("PhaseStrip") { layer = VRRig.Layer }.transform;
        _strip.SetParent(_board.transform, false);
        _strip.localPosition = new Vector3(0f, 0.004f, 0.975f);
        _strip.localRotation = Quaternion.Euler(90f, 0f, 0f);

        const float chipW = 0.29f;
        const float gap = 0.012f;
        float x = -((chipW + gap) * 6 - gap) * 0.5f + chipW * 0.5f;
        _turn = VRLabel.Create(_strip, new Vector3(x, 0f, 0f), Quaternion.identity, new Vector2(chipW, 0.065f), "", Color.white, Idle);

        for (int i = 0; i < Names.Length; i++)
        {
            _chips[i] = VRLabel.Create(_strip, new Vector3(x + (i + 1) * (chipW + gap), 0f, 0f), Quaternion.identity, new Vector2(chipW, 0.065f), Names[i], new Color(0.55f, 0.65f, 0.75f), Idle);
        }

        _banner = VRLabel.Create(transform, Vector3.zero, Quaternion.identity, new Vector2(0.9f, 0.13f), "", Color.white, new Color(0.03f, 0.07f, 0.12f));
        _banner.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;
        bool active = context != null && context.TurnPlayer != null && _board != null && _board.Visible;

        if (_strip.gameObject.activeSelf != active)
        {
            _strip.gameObject.SetActive(active);
        }

        if (!active)
        {
            _banner.gameObject.SetActive(false);
            _phase = GameContext.phase.None;
            _turnPlayer = null;
            _turnCount = -1;
            return;
        }

        bool yours = context.TurnPlayer.isYou;
        int turnCount = GManager.instance != null && GManager.instance.turnStateMachine != null ? GManager.instance.turnStateMachine.TurnCount : 0;
        Color lit = yours ? YourPhase : TheirPhase;

        _turn.text = (yours ? "YOUR TURN " : "OPP. TURN ") + turnCount;
        _turn.plateColor = lit;

        for (int i = 0; i < Phases.Length; i++)
        {
            bool current = Phases[i] == context.TurnPhase;
            _chips[i].plateColor = current ? lit : Idle;
            _chips[i].color = current ? Color.white : new Color(0.55f, 0.65f, 0.75f);
        }

        if (context.TurnPhase != _phase || context.TurnPlayer != _turnPlayer || turnCount != _turnCount)
        {
            bool newTurn = context.TurnPlayer != _turnPlayer || turnCount != _turnCount;
            bool first = _phase == GameContext.phase.None;
            _phase = context.TurnPhase;
            _turnPlayer = context.TurnPlayer;
            _turnCount = turnCount;
            string phaseName = PhaseName(_phase);

            if (phaseName != null)
            {
                string title = newTurn || first ? (yours ? "YOUR TURN " : "OPPONENT'S TURN ") + turnCount + "  -  " : "";
                ShowBanner(title + phaseName + " PHASE", lit);

                if (newTurn && yours)
                {
                    VRLaserPointer.Haptic(UnityEngine.XR.XRNode.LeftHand, 0.5f, 0.15f);
                    VRLaserPointer.Haptic(UnityEngine.XR.XRNode.RightHand, 0.5f, 0.15f);
                }
            }

            Debug.Log($"[DCGO VR] Phase: {(yours ? "you" : "opponent")} turn {turnCount} - {_phase}");
        }

        UpdateBanner();
    }

    static string PhaseName(GameContext.phase phase)
    {
        int index = System.Array.IndexOf(Phases, phase);
        return index >= 0 ? Names[index] : null;
    }

    void ShowBanner(string text, Color plate)
    {
        _banner.text = text;
        _banner.plateColor = Color.Lerp(plate, Color.black, 0.35f);
        _bannerStart = Time.unscaledTime;
        _bannerUntil = Time.unscaledTime + 1.6f;
        _banner.gameObject.SetActive(true);
    }

    void UpdateBanner()
    {
        if (!_banner.gameObject.activeSelf)
        {
            return;
        }

        if (Time.unscaledTime > _bannerUntil || _head == null)
        {
            _banner.gameObject.SetActive(false);
            return;
        }

        // Floats ahead of you, a little above the field, and pops in.
        Vector3 forward = _head.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 position = _head.position + forward * 1.1f + Vector3.up * 0.12f;
        float pop = Mathf.SmoothStep(0.6f, 1f, (Time.unscaledTime - _bannerStart) / 0.15f);
        _banner.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - _head.position, Vector3.up));
        _banner.transform.localScale = new Vector3(0.9f * pop, 0.13f * pop, 1f);
    }
}
#endif
