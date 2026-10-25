#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// === DCGO-CUSTOM:vr ===
// F9 in the editor or a development build: shows what LegalActions thinks you
// can do, lets you perform any of it through GameActions (the same path the VR
// table uses), and shows the board fingerprint + recent BoardObserver events.
// Use it to check, in the flat game, that the VR seams agree with the real UI.
public class VRDebugOverlay : MonoBehaviour
{
    bool _visible;
    Vector2 _scroll;
    Rect _window = new Rect(10, 10, 460, 560);
    readonly Queue<string> _events = new Queue<string>();
    BoardObserver _observer;
    BaseInputModule _blockedModule;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        GameObject go = new GameObject("DCGO_VRDebugOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<VRDebugOverlay>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            _visible = !_visible;
        }

        BlockGameClicksUnderWindow();

        if (_observer == null && BoardObserver.Instance != null)
        {
            _observer = BoardObserver.Instance;
            _observer.CardMoved += (card, from, to) => Log($"{LegalActions.Name(card)}: {from} -> {to}");
            _observer.SuspendedChanged += (permanent, suspended) => Log($"{LegalActions.Name(permanent.TopCard)} {(suspended ? "suspended" : "unsuspended")}");
            _observer.MemoryChanged += (from, to) => Log($"Memory {from} -> {to}");
            _observer.PhaseChanged += (phase, player) => Log($"Phase {phase} ({(player != null && player.isYou ? "you" : "opponent")})");
        }
    }

    // IMGUI does not stop uGUI from receiving the same click, so while the mouse
    // is over the window the game's input module is switched off.
    void BlockGameClicksUnderWindow()
    {
        Vector3 mouse = Input.mousePosition;
        bool over = _visible && _window.Contains(new Vector2(mouse.x, Screen.height - mouse.y));

        if (over && _blockedModule == null && EventSystem.current != null && EventSystem.current.currentInputModule != null)
        {
            _blockedModule = EventSystem.current.currentInputModule;
            _blockedModule.enabled = false;
        }
        else if (!over && _blockedModule != null)
        {
            _blockedModule.enabled = true;
            _blockedModule = null;
        }
    }

    void OnDisable()
    {
        if (_blockedModule != null)
        {
            _blockedModule.enabled = true;
            _blockedModule = null;
        }
    }

    void Log(string line)
    {
        _events.Enqueue(line);

        while (_events.Count > 12)
        {
            _events.Dequeue();
        }
    }

    void OnGUI()
    {
        if (_visible)
        {
            _window = GUILayout.Window(0x0DC60, _window, Draw, "DCGO VR seams (F9)");
        }
    }

    void Draw(int id)
    {
        LegalActionSet set = LegalActions.Current;
        GUILayout.Label("Fingerprint: " + BoardObserver.Fingerprint(BoardObserver.Context));
        GUILayout.Label(set == LegalActionSet.None ? "Not waiting for your input." : set.IsYourMainPhase ? "Your main phase" : set.IsYourBreedingPhase ? "Your breeding phase" : "Your turn (other phase)");

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(330));

        if (set.IsYourBreedingPhase)
        {
            if (GUILayout.Button($"Breeding action (hatch {set.CanHatch} / move {set.CanMoveFromBreeding})"))
            {
                GameActions.Breeding(true);
            }

            if (GUILayout.Button("Skip breeding"))
            {
                GameActions.Breeding(false);
            }
        }

        foreach (LegalPlay play in set.Plays)
        {
            GUI.enabled = play.IsSimple;

            if (GUILayout.Button(play.ToString()))
            {
                GameActions.Play(play);
            }
        }

        GUI.enabled = true;

        foreach (LegalAttack attack in set.Attacks)
        {
            if (GUILayout.Button(attack.ToString()))
            {
                GameActions.Attack(attack);
            }
        }

        foreach (LegalSkill skill in set.Skills)
        {
            GUI.enabled = skill.CanUse;

            if (GUILayout.Button(skill.ToString()))
            {
                GameActions.UseSkill(skill);
            }
        }

        GUI.enabled = true;

        if (set.CanPass && GUILayout.Button("Pass (end turn)"))
        {
            GameActions.Pass();
        }

        GUILayout.EndScrollView();
        GUILayout.Label("Recent board events:");

        foreach (string line in _events)
        {
            GUILayout.Label(line);
        }

        GUI.DragWindow();
    }
}
#endif
