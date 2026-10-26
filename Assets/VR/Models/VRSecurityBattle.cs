#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Security check scene: when an attack breaks a security card, a small stage appears
// in front of you at eye height, seen from the side like an anime duel. The attacker
// (3D model) stands on the left, the revealed card flips in on the right; if it is a
// Digimon its model rises from the card and both fight (attack / hit / counter), the
// loser faints and the winner celebrates. Options / Tamers just show the card big.
// Works both ways (you attacking, or the opponent breaking your security).
// Read-only: it only watches SecurityCards / ExecutingCards / attackProcess.
public class VRSecurityBattle : MonoBehaviour
{
    const float StageDistance = 2.6f;
    const float StageBelowEye = 0.95f;
    const float Gap = 1.15f;              // each fighter's distance from the centre
    const float MinDuration = 5.2f;
    const float MaxDuration = 12f;

    sealed class Fighter
    {
        public Transform Root;    // stands on the stage, faces the other fighter
        public Transform Body;    // grows in, lunges
        public Transform Model;
        public string Path;
        public Animation Anim;
        public string Idle;
        public float Height = 1.2f;
        public bool Fainted;
        public float Yaw;
    }

    Transform _head;
    Transform _scene;
    VRLabel _title;
    VRLabel _leftDp;
    VRLabel _rightDp;
    VRLabel _result;
    Renderer _card;           // the revealed security card
    MaterialPropertyBlock _block;

    sealed class Pending
    {
        public Permanent Attacker;
        public CardSource AttackerCard;
        public CardSource Card;
        public Player Owner;
        public int AttackerDp;
    }

    readonly Queue<Pending> _queue = new Queue<Pending>();
    readonly HashSet<CardSource> _queued = new HashSet<CardSource>();

    readonly Dictionary<Player, HashSet<CardSource>> _lastSecurity = new Dictionary<Player, HashSet<CardSource>>();

    // Current scene.
    bool _running;
    bool _wasChecking;
    float _start;
    float _endAt = -1f;
    Player _defenderOwner;
    CardSource _revealed;
    CardSource _attackerCard;
    bool _digimonBattle;
    int _attackerDp;
    int _defenderDp;
    Fighter _left;
    Fighter _right;
    int _step;
    float _lungeStart = -1f;
    Fighter _lunging;
    int _token;

    public static VRSecurityBattle Create(Transform parent, Transform head)
    {
        GameObject go = new GameObject("SecurityBattle") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRSecurityBattle battle = go.AddComponent<VRSecurityBattle>();
        battle._head = head;
        battle._block = new MaterialPropertyBlock();
        return battle;
    }

    // ---------------------------------------------------------------- watching

    void LateUpdate()
    {
        GameContext context = BoardObserver.Context;

        if (context == null || context.You == null || context.Opponent == null)
        {
            _lastSecurity.Clear();
            _queue.Clear();
            _queued.Clear();

            if (_running)
            {
                Stop();
            }

            return;
        }

        // Diagnostics: log when the game starts / ends a security check.
        bool checkingNow = GManager.instance != null && GManager.instance.turnStateMachine != null && GManager.instance.turnStateMachine.isSecurityCehck;

        if (checkingNow != _wasChecking)
        {
            _wasChecking = checkingNow;
            Debug.Log($"[DCGO VR] Security check {(checkingNow ? "started" : "ended")} - scene {(VRSettings.SecurityBattle ? "on" : "off")}, running {_running}");
        }

        // Keep watching while a scene plays: Security +1 / +2 break more cards during it,
        // those wait in a queue and play one after the other.
        if (VRSettings.SecurityBattle)
        {
            DetectReveal(context, context.You);
            DetectReveal(context, context.Opponent);
        }

        if (!_running && _queue.Count > 0)
        {
            Pending next = _queue.Dequeue();

            try
            {
                Begin(next.AttackerCard, next.Card, next.Owner, next.AttackerDp);
            }
            catch (Exception exception)
            {
                Debug.LogError("[DCGO VR] Security check scene failed: " + exception);
                Stop();
            }
        }

        Remember(context.You);
        Remember(context.Opponent);

        if (_running)
        {
            try
            {
                Run();
            }
            catch (Exception exception)
            {
                Debug.LogError("[DCGO VR] Security check scene failed: " + exception);
                Stop();
            }
        }
    }

    void Remember(Player player)
    {
        if (!_lastSecurity.TryGetValue(player, out HashSet<CardSource> set))
        {
            set = new HashSet<CardSource>();
            _lastSecurity[player] = set;
        }

        set.Clear();

        foreach (CardSource card in player.SecurityCards)
        {
            set.Add(card);
        }
    }

    // Security check: while the game runs one (isSecurityCehck) a card leaves the top of
    // the defender's security. It goes to the executing area (or straight elsewhere): either way
    // that card is the one being checked.
    void DetectReveal(GameContext context, Player owner)
    {
        if (!_lastSecurity.TryGetValue(owner, out HashSet<CardSource> before) || before.Count == 0)
        {
            return;
        }

        GManager manager = GManager.instance;
        bool checking = manager != null && manager.turnStateMachine != null && manager.turnStateMachine.isSecurityCehck;
        AttackProcess attack = manager != null ? manager.attackProcess : null;
        Permanent attacker = attack != null ? attack.AttackingPermanent : null;

        if ((!checking && (attack == null || !attack.IsAttacking)) || attacker == null || attacker.TopCard == null)
        {
            return;
        }

        CardSource revealed = null;

        foreach (CardSource card in owner.ExecutingCards)
        {
            if (card != null && before.Contains(card))
            {
                revealed = card;
                break;
            }
        }

        if (revealed == null && checking)
        {
            foreach (CardSource card in before)
            {
                if (card != null && !owner.SecurityCards.Contains(card))
                {
                    revealed = card;
                    break;
                }
            }
        }

        if (revealed == null)
        {
            return;
        }

        if (revealed == _revealed || _queued.Contains(revealed))
        {
            return; // already playing / waiting
        }

        _queued.Add(revealed);
        _queue.Enqueue(new Pending { Attacker = attacker, AttackerCard = attacker.TopCard, Card = revealed, Owner = owner, AttackerDp = Safe(() => attacker.DP) });
        Debug.Log($"[DCGO VR] Security check: {LegalActions.Name(revealed)} revealed ({_queue.Count} waiting)");
    }

    // ---------------------------------------------------------------- scene

    // The attacker is passed as its card (captured at the reveal): by the time a queued
    // check plays, the attacking Digimon may already be gone from the field.
    void Begin(CardSource attackerCard, CardSource revealed, Player owner, int attackerDp)
    {
        _queued.Remove(revealed);
        Stop();
        _running = true;
        _token++;
        _start = Time.unscaledTime;
        _endAt = -1f;
        _step = 0;
        _revealed = revealed;
        _attackerCard = attackerCard;
        _defenderOwner = owner;
        _digimonBattle = Safe(() => revealed.IsDigimon);
        _attackerDp = attackerDp;
        _defenderDp = _digimonBattle ? Safe(() => revealed.CardDP) : 0;
        Debug.Log($"[DCGO VR] Security check scene: {LegalActions.Name(attackerCard)} ({_attackerDp}) vs {LegalActions.Name(revealed)} ({(_digimonBattle ? _defenderDp.ToString() : "no battle")})");

        BuildStage();

        _left = SpawnFighter(attackerCard, new Vector3(-Gap, 0f, 0f), 90f, delay: false);
        _right = _digimonBattle ? SpawnFighter(revealed, new Vector3(Gap, 0f, 0f), -90f, delay: true) : null;

        _leftDp.text = DpText(attackerCard, _attackerDp);
        _rightDp.text = _digimonBattle ? DpText(revealed, _defenderDp) : (Safe(() => revealed.IsTamer) ? "TAMER" : "OPTION");
        _rightDp.gameObject.SetActive(false);
        _result.gameObject.SetActive(false);
        _title.text = (owner.isYou ? "YOUR SECURITY" : "SECURITY CHECK") + "  -  " + LegalActions.Name(revealed);
        LoadArt(revealed, _card);
    }

    static string DpText(CardSource card, int dp)
    {
        string name = LegalActions.Name(card);
        return dp > 0 ? "<b>" + name + "</b>\nDP " + dp : "<b>" + name + "</b>";
    }

    static int Safe(Func<int> get)
    {
        try
        {
            return get();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    static bool Safe(Func<bool> get)
    {
        try
        {
            return get();
        }
        catch (Exception)
        {
            return false;
        }
    }

    void BuildStage()
    {
        if (_scene == null)
        {
            _scene = new GameObject("SecurityStage") { layer = VRRig.Layer }.transform;
            _scene.SetParent(transform, false);

            Box(_scene, new Vector3(0f, -0.04f, 0f), new Vector3(Gap * 2f + 1.6f, 0.08f, 1.3f), new Color(0.05f, 0.07f, 0.11f));
            Box(_scene, new Vector3(-(Gap + 0.8f) * 0.5f, 0.004f, -0.65f), new Vector3(Gap + 0.8f, 0.012f, 0.03f), new Color(0.25f, 0.8f, 1f));
            Box(_scene, new Vector3((Gap + 0.8f) * 0.5f, 0.004f, -0.65f), new Vector3(Gap + 0.8f, 0.012f, 0.03f), new Color(1f, 0.35f, 0.3f));

            _title = VRLabel.Create(_scene, new Vector3(0f, 2.35f, 0.2f), Quaternion.identity, new Vector2(2.4f, 0.22f), "", Color.white, new Color(0.08f, 0.05f, 0.12f));
            _leftDp = VRLabel.Create(_scene, new Vector3(-Gap, 0.18f, -0.7f), Quaternion.Euler(20f, 0f, 0f), new Vector2(1.1f, 0.26f), "", Color.white, new Color(0.05f, 0.12f, 0.22f));
            _rightDp = VRLabel.Create(_scene, new Vector3(Gap, 0.18f, -0.7f), Quaternion.Euler(20f, 0f, 0f), new Vector2(1.1f, 0.26f), "", Color.white, new Color(0.22f, 0.06f, 0.06f));
            _result = VRLabel.Create(_scene, new Vector3(0f, 1.95f, 0.1f), Quaternion.identity, new Vector2(1.8f, 0.3f), "", new Color(1f, 0.9f, 0.4f), new Color(0.1f, 0.08f, 0.02f));

            GameObject card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "RevealedCard";
            card.layer = VRRig.Layer;
            Destroy(card.GetComponent<Collider>());
            card.transform.SetParent(_scene, false);
            _card = card.GetComponent<Renderer>();
            _card.sharedMaterial = VRMaterials.Unlit(Color.white);

            GameObject lightGo = new GameObject("BattleLight") { layer = VRRig.Layer };
            lightGo.transform.SetParent(_scene, false);
            lightGo.transform.localRotation = Quaternion.Euler(35f, 20f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.cullingMask = 1 << VRRig.Layer;
            light.shadows = LightShadows.None;
        }

        // In front of you at eye height, a little below so the fighters fill your view.
        Vector3 forward = _head.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 position = _head.position + forward * StageDistance;
        position.y = _head.position.y - StageBelowEye;
        _scene.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        _scene.localScale = Vector3.one * 0.001f;
        _scene.gameObject.SetActive(true);
    }

    Fighter SpawnFighter(CardSource card, Vector3 position, float yaw, bool delay)
    {
        Fighter fighter = new Fighter();
        fighter.Root = new GameObject("Fighter") { layer = VRRig.Layer }.transform;
        fighter.Root.SetParent(_scene, false);
        fighter.Root.localPosition = position;
        fighter.Root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        fighter.Yaw = yaw;
        fighter.Body = new GameObject("Body") { layer = VRRig.Layer }.transform;
        fighter.Body.SetParent(fighter.Root, false);
        fighter.Body.localScale = delay ? Vector3.one * 0.001f : Vector3.one;

        int level = Safe(() => card.Level);
        fighter.Height = 0.5f + VRFieldModels.LevelHeight(level) * 0.45f;
        LoadFighter(fighter, card, _token);
        return fighter;
    }

    async void LoadFighter(Fighter fighter, CardSource card, int token)
    {
        string path = VRModelLibrary.Resolve(card);
        Transform holder = path != null ? await VRFieldModels.LoadPreview(path, fighter.Body) : null;

        if (token != _token || fighter.Body == null)
        {
            VRFieldModels.ReleasePreview(path, holder);
            return;
        }

        if (holder == null)
        {
            // No model: the card itself stands in.
            GameObject standee = GameObject.CreatePrimitive(PrimitiveType.Quad);
            standee.layer = VRRig.Layer;
            Destroy(standee.GetComponent<Collider>());
            standee.transform.SetParent(fighter.Body, false);
            float h = Mathf.Min(fighter.Height, 1.3f);
            standee.transform.localScale = new Vector3(h / 1.4f, h, 1f);
            standee.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            standee.transform.localRotation = Quaternion.Euler(0f, -fighter.Yaw, 0f); // card faces you, not the other fighter
            Renderer renderer = standee.GetComponent<Renderer>();
            renderer.sharedMaterial = VRMaterials.Unlit(Color.white);
            LoadArt(card, renderer);
            return;
        }

        fighter.Path = path;
        fighter.Model = holder;
        float tune = VRModelTuning.Get(VRModelTuning.KeyOf(path));
        VRFieldModels.Normalize(holder, fighter.Height * tune, out float width);

        // Keep very wide models inside the stage.
        float scaledWidth = width * holder.localScale.x;

        if (scaledWidth > 1.4f)
        {
            holder.localScale *= 1.4f / scaledWidth;
            holder.localPosition *= 1.4f / scaledWidth;
        }

        fighter.Anim = holder.GetComponentInChildren<Animation>(true);

        if (fighter.Anim != null)
        {
            fighter.Anim.playAutomatically = false;
            fighter.Anim.cullingType = AnimationCullingType.AlwaysAnimate;
            AnimationClip idle = VRFieldModels.Clip(fighter.Anim, "_bn01") ?? VRFieldModels.Clip(fighter.Anim, "_bn02");

            if (idle != null)
            {
                fighter.Idle = idle.name;
                fighter.Anim[idle.name].wrapMode = WrapMode.Loop;
                fighter.Anim.Play(idle.name);
            }
        }
    }

    // ---------------------------------------------------------------- timeline

    void Run()
    {
        float t = Time.unscaledTime - _start;

        // Stage pops in / out.
        float appear = Mathf.SmoothStep(0f, 1f, t / 0.35f);
        float vanish = _endAt > 0f ? Mathf.SmoothStep(1f, 0f, (Time.unscaledTime - _endAt) / 0.4f) : 1f;
        _scene.localScale = Vector3.one * Mathf.Max(0.001f, appear * vanish);

        // Revealed card: flips in above the right spot, then (Digimon) shrinks to the floor
        // under its model, or (Option / Tamer) stays big in the middle.
        UpdateCard(t);

        if (_right != null)
        {
            float grow = Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.5f);
            _right.Body.localScale = Vector3.one * Mathf.Max(0.001f, grow);
        }

        UpdateLunge();

        if (_step == 0 && t > 0.6f)
        {
            _step = 1;
            _rightDp.gameObject.SetActive(true);
        }

        if (_step == 1 && t > 1.5f)
        {
            _step = 2;
            Lunge(_left);
            Play(_left, "_ba01", true);
            Haptic(0.4f);
        }

        if (_step == 2 && t > 2.05f)
        {
            _step = 3;

            if (_right != null)
            {
                Play(_right, _defenderDp > _attackerDp ? "_bg01" : "_bd01", true);
            }
        }

        if (_step == 3 && t > 2.7f)
        {
            _step = 4;

            if (_right != null)
            {
                // Counter-attack.
                Lunge(_right);
                Play(_right, "_ba01", true);
            }
        }

        if (_step == 4 && t > 3.25f)
        {
            _step = 5;

            if (_right != null)
            {
                Play(_left, _attackerDp > _defenderDp ? "_bg01" : "_bd01", true);
                Haptic(0.4f);
            }
        }

        if (_step == 5 && t > 3.9f)
        {
            _step = 6;
            ShowResult();
        }

        // Ends once the game is done with the card (it left the executing area).
        if (_endAt < 0f && t > MinDuration && (_revealed == null || !_defenderOwner.ExecutingCards.Contains(_revealed) || t > MaxDuration))
        {
            _endAt = Time.unscaledTime;
        }

        if (_endAt > 0f && Time.unscaledTime - _endAt > 0.45f)
        {
            Stop();
        }
    }

    void ShowResult()
    {
        string text;

        if (_right == null)
        {
            text = Safe(() => _revealed.IsTamer) ? "TAMER FROM SECURITY" : "SECURITY EFFECT";
            Play(_left, "_bv01", true);
        }
        else if (_attackerDp > _defenderDp)
        {
            text = LegalActions.Name(_attackerCard) + " WINS";
            Faint(_right);
            Play(_left, "_bv01", true);
        }
        else if (_attackerDp < _defenderDp)
        {
            text = "SECURITY DIGIMON WINS";
            Faint(_left);
            Play(_right, "_bv01", true);
        }
        else
        {
            text = "BOTH DELETED";
            Faint(_left);
            Faint(_right);
        }

        _result.text = "<b>" + text + "</b>";
        _result.gameObject.SetActive(true);
        Haptic(0.6f);
    }

    void UpdateCard(float t)
    {
        if (_card == null)
        {
            return;
        }

        float flip = Mathf.SmoothStep(90f, 0f, t / 0.5f);
        Vector3 high = new Vector3(_digimonBattle ? Gap : 0.35f, 1.25f, 0.05f);
        Vector3 size = new Vector3(0.62f, 0.86f, 1f);

        if (_digimonBattle && t > 0.7f)
        {
            // Drops to the floor as a summoning card under the Digimon.
            float k = Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.5f);
            _card.transform.localPosition = Vector3.Lerp(high, new Vector3(Gap, 0.02f, 0f), k);
            _card.transform.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(90f, 0f, 0f), k);
            _card.transform.localScale = Vector3.Lerp(size, size * 0.9f, k);
            return;
        }

        if (!_digimonBattle && t > 0.6f)
        {
            // Option / Tamer: grows a little and gently floats.
            float k = Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.5f);
            _card.transform.localPosition = high + new Vector3(0f, 0.12f * k + Mathf.Sin(t * 2f) * 0.03f, 0f);
            _card.transform.localRotation = Quaternion.identity;
            _card.transform.localScale = size * (1f + 0.35f * k);
            return;
        }

        _card.transform.localPosition = high;
        _card.transform.localRotation = Quaternion.Euler(0f, flip, 0f);
        _card.transform.localScale = size;
    }

    void Lunge(Fighter fighter)
    {
        if (fighter == null)
        {
            return;
        }

        _lunging = fighter;
        _lungeStart = Time.unscaledTime;
    }

    // Dash towards the other fighter and back (along the body's forward).
    void UpdateLunge()
    {
        foreach (Fighter fighter in new[] { _left, _right })
        {
            if (fighter == null || fighter.Body == null || fighter.Fainted)
            {
                continue;
            }

            float offset = 0f;

            if (fighter == _lunging && _lungeStart > 0f)
            {
                float k = (Time.unscaledTime - _lungeStart) / 0.7f;
                offset = k < 1f ? Mathf.Sin(k * Mathf.PI) * (Gap - 0.55f) : 0f;
            }

            fighter.Body.localPosition = new Vector3(0f, 0f, offset);
        }
    }

    void Faint(Fighter fighter)
    {
        if (fighter == null)
        {
            return;
        }

        fighter.Fainted = true;

        if (!Play(fighter, "_bf01", false))
        {
            // No faint clip: tip over.
            fighter.Body.localRotation = Quaternion.Euler(-80f, 0f, 0f);
        }
    }

    bool Play(Fighter fighter, string suffix, bool backToIdle)
    {
        if (fighter == null || fighter.Anim == null)
        {
            return false;
        }

        AnimationClip clip = VRFieldModels.Clip(fighter.Anim, suffix);

        if (clip == null)
        {
            return false;
        }

        fighter.Anim[clip.name].wrapMode = suffix == "_bf01" ? WrapMode.ClampForever : WrapMode.Once;
        fighter.Anim.Stop();
        fighter.Anim.Play(clip.name);

        if (backToIdle && fighter.Idle != null)
        {
            fighter.Anim.PlayQueued(fighter.Idle, QueueMode.CompleteOthers);
        }

        return true;
    }

    static void Haptic(float strength)
    {
        VRLaserPointer.Haptic(UnityEngine.XR.XRNode.LeftHand, strength, 0.06f);
        VRLaserPointer.Haptic(UnityEngine.XR.XRNode.RightHand, strength, 0.06f);
    }

    void Stop()
    {
        _token++;
        Release(_left);
        Release(_right);
        _left = null;
        _right = null;
        _running = false;
        _revealed = null;
        _lunging = null;

        if (_scene != null)
        {
            _scene.gameObject.SetActive(false);
        }
    }

    static void Release(Fighter fighter)
    {
        if (fighter == null)
        {
            return;
        }

        if (fighter.Model != null)
        {
            VRFieldModels.ReleasePreview(fighter.Path, fighter.Model);
        }

        if (fighter.Root != null)
        {
            Destroy(fighter.Root.gameObject);
        }
    }

    // ---------------------------------------------------------------- helpers

    static void Box(Transform parent, Vector3 position, Vector3 size, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.layer = VRRig.Layer;
        Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(color);
    }

    async void LoadArt(CardSource card, Renderer target)
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
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetTexture("_BaseMap", texture);
            block.SetTexture("_MainTex", texture);
            block.SetVector("_BaseMap_ST", st);
            block.SetVector("_MainTex_ST", st);
            target.SetPropertyBlock(block);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Security card art failed: " + exception.Message);
        }
    }

    void OnDestroy() => Stop();
}
#endif
