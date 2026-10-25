using System.Collections.Generic;
using System.Linq;

// === DCGO-CUSTOM:vr ===
// Read-only list of what the local player may do right now, computed with the
// same engine predicates TurnStateMachine.SetMainPhase uses to light up the
// flat UI. The VR table uses it to glow legal zones while a card is held, and
// GameActions uses it to refuse anything illegal before it reaches the network.

public enum LegalPlayKind
{
    /// <summary>Digimon / Tamer into an empty battle-area slot (engine picks the slot: PreferredFrame).</summary>
    PlayToField,
    /// <summary>Normal digivolve on top of TargetPermanent.</summary>
    Digivolve,
    /// <summary>DNA digivolve; needs a second root chosen by the player.</summary>
    Jogress,
    /// <summary>Burst digivolve; needs a Tamer chosen by the player.</summary>
    Burst,
    /// <summary>App Fusion; needs a linked card chosen by the player.</summary>
    AppFusion,
    /// <summary>Option card.</summary>
    Option,
}

public sealed class LegalPlay
{
    public CardSource Card;
    public LegalPlayKind Kind;
    public FieldCardFrame Frame;
    public Permanent TargetPermanent;

    /// <summary>True when this play can be sent as-is (no extra choice such as DNA partner or Burst tamer).</summary>
    public bool IsSimple => Kind == LegalPlayKind.PlayToField || Kind == LegalPlayKind.Digivolve || Kind == LegalPlayKind.Option;

    public override string ToString() =>
        $"{Kind}: {LegalActions.Name(Card)}" + (TargetPermanent != null ? $" -> {LegalActions.Name(TargetPermanent.TopCard)}" : "") + (Frame != null ? $" [frame {Frame.FrameID}]" : "");
}

public sealed class LegalAttack
{
    public Permanent Attacker;
    /// <summary>null = attack the player (security).</summary>
    public Permanent Target;

    public override string ToString() =>
        $"Attack: {LegalActions.Name(Attacker.TopCard)} -> " + (Target != null ? LegalActions.Name(Target.TopCard) : "Security");
}

public sealed class LegalSkill
{
    /// <summary>Set for [Main] effects of a card in hand.</summary>
    public CardSource HandCard;
    /// <summary>Set for [Main] effects of a permanent on the field.</summary>
    public Permanent Permanent;
    public ICardEffect Effect;
    /// <summary>Index used by the network packet (CanDeclareSkillList for hand, EffectList(OnDeclaration) for field).</summary>
    public int SkillIndex;
    public bool CanUse;

    public override string ToString() =>
        $"Skill: {(HandCard != null ? LegalActions.Name(HandCard) : LegalActions.Name(Permanent.TopCard))} - {Effect?.EffectName}" + (CanUse ? "" : " (can't use now)");
}

public sealed class LegalActionSet
{
    public static readonly LegalActionSet None = new LegalActionSet();

    public bool IsYourMainPhase;
    public bool IsYourBreedingPhase;
    public bool CanHatch;
    public bool CanMoveFromBreeding;
    public readonly List<LegalPlay> Plays = new List<LegalPlay>();
    public readonly List<LegalAttack> Attacks = new List<LegalAttack>();
    public readonly List<LegalSkill> Skills = new List<LegalSkill>();

    public bool CanPass => IsYourMainPhase;

    public IEnumerable<LegalPlay> PlaysFor(CardSource card) => Plays.Where(p => p.Card == card);
    public IEnumerable<LegalAttack> AttacksFor(Permanent attacker) => Attacks.Where(a => a.Attacker == attacker);
}

public static class LegalActions
{
    static LegalActionSet _cached;
    static bool _dirty = true;
    static float _computedAt;
    static int _stateKey;

    // Recomputing runs every legality check in the engine (hundreds of calls),
    // so the result is cached until the game state changes (BoardObserver calls
    // Invalidate on any zone / memory / phase change), the engine starts or stops
    // executing, or a short safety timeout passes.
    const float MaxAgeSeconds = 1f;

    /// <summary>The engine is idle and waiting for the local player (not mid-effect, not in a prompt).</summary>
    public static bool IsAwaitingLocalInput(out TurnStateMachine tsm)
    {
        tsm = GManager.instance != null ? GManager.instance.turnStateMachine : null;

        if (tsm == null || tsm.gameContext == null || VRCompat.IsReplay)
        {
            return false;
        }

        return tsm.gameContext.TurnPlayer != null
               && tsm.gameContext.TurnPlayer.isYou
               && !tsm.isExecuting
               && !tsm.isSecurityCehck
               && !PromptOnScreen(GManager.instance);
    }

    /// <summary>
    /// True while the game shows a prompt (card selection, effect order, command buttons...).
    /// Some prompts (e.g. ordering triggered effects) run without the engine's
    /// "executing" flag, so the screen itself is checked too.
    /// </summary>
    public static bool PromptOnScreen(GManager g)
    {
        if (g == null)
        {
            return false;
        }

        SelectCardPanel panel = g.selectCardPanel;

        if (panel != null && (panel.gameObject.activeInHierarchy
                              || (panel.ReturnToSelectCardButton != null && panel.ReturnToSelectCardButton.gameObject.activeInHierarchy)))
        {
            return true;
        }

        if (g.commandText != null && g.commandText.gameObject.activeInHierarchy)
        {
            return true;
        }

        if (g.selectCommandPanel != null && g.selectCommandPanel.gameObject.activeInHierarchy
            && g.selectCommandPanel.GetComponentInChildren<UnityEngine.UI.Button>(false) != null)
        {
            return true;
        }

        return false;
    }

    /// <summary>Legal actions for the local player, cached for the current frame.</summary>
    public static LegalActionSet Current
    {
        get
        {
            int key = StateKey();
            float now = UnityEngine.Time.unscaledTime;

            if (_dirty || _cached == null || key != _stateKey || now - _computedAt > MaxAgeSeconds)
            {
                _cached = Compute();
                _dirty = false;
                _stateKey = key;
                _computedAt = now;
            }

            return _cached;
        }
    }

    public static void Invalidate() => _dirty = true;

    static int StateKey()
    {
        TurnStateMachine tsm = GManager.instance != null ? GManager.instance.turnStateMachine : null;

        if (tsm == null || tsm.gameContext == null)
        {
            return 0;
        }

        GameContext c = tsm.gameContext;
        return ((int)c.TurnPhase + 1) * 1000003
               ^ (c.TurnPlayer != null ? c.TurnPlayer.PlayerID + 1 : 0) * 7919
               ^ (tsm.isExecuting ? 1 : 0) * 31 ^ (tsm.isSecurityCehck ? 1 : 0) * 61 ^ (tsm.IsSelecting ? 1 : 0) * 127
               ^ c.Memory * 17;
    }

    public static LegalActionSet Compute()
    {
        if (!IsAwaitingLocalInput(out TurnStateMachine tsm))
        {
            return LegalActionSet.None;
        }

        GameContext context = tsm.gameContext;
        Player you = context.TurnPlayer;
        LegalActionSet set = new LegalActionSet();

        if (context.TurnPhase == GameContext.phase.Breeding)
        {
            set.IsYourBreedingPhase = true;
            set.CanHatch = you.CanHatch;
            set.CanMoveFromBreeding = you.CanMove;
            return set;
        }

        if (context.TurnPhase != GameContext.phase.Main)
        {
            return set;
        }

        set.IsYourMainPhase = true;

        AddHandActions(you, set);
        AddFieldActions(you, context.NonTurnPlayer, set);
        return set;
    }

    static void AddHandActions(Player you, LegalActionSet set)
    {
        foreach (CardSource card in you.HandCards.ToList())
        {
            if (card == null)
            {
                continue;
            }

            if (card.CanPlayFromHandDuringMainPhase)
            {
                if (card.IsPermanent)
                {
                    bool canPlayToEmpty = false;

                    foreach (FieldCardFrame frame in you.fieldCardFrames)
                    {
                        Permanent target = frame.GetFramePermanent();

                        if (frame.IsEmptyFrame())
                        {
                            if (!card.IsOption && card.CanPlayCardTargetFrame(frame, true, null))
                            {
                                canPlayToEmpty = true;
                            }

                            continue;
                        }

                        if (target == null)
                        {
                            continue;
                        }

                        if (card.CanPlayCardTargetFrame(frame, true, null))
                        {
                            set.Plays.Add(new LegalPlay { Card = card, Kind = LegalPlayKind.Digivolve, Frame = frame, TargetPermanent = target });
                        }

                        if (card.CanJogressFromTargetPermanent(target, true))
                        {
                            set.Plays.Add(new LegalPlay { Card = card, Kind = LegalPlayKind.Jogress, Frame = frame, TargetPermanent = target });
                        }

                        if (card.CanBurstDigivolutionFromTargetPermanent(target, true))
                        {
                            set.Plays.Add(new LegalPlay { Card = card, Kind = LegalPlayKind.Burst, Frame = frame, TargetPermanent = target });
                        }

                        if (card.CanAppFusionFromTargetPermanent(target, true))
                        {
                            set.Plays.Add(new LegalPlay { Card = card, Kind = LegalPlayKind.AppFusion, Frame = frame, TargetPermanent = target });
                        }
                    }

                    if (canPlayToEmpty)
                    {
                        set.Plays.Add(new LegalPlay { Card = card, Kind = LegalPlayKind.PlayToField, Frame = card.PreferredFrame() });
                    }
                }

                if (card.IsOption && !card.CanNotPlayThisOption)
                {
                    set.Plays.Add(new LegalPlay { Card = card, Kind = LegalPlayKind.Option });
                }
            }

            if (card.CanDeclareSkill)
            {
                List<ICardEffect> effects = card.CanDeclareSkillList;

                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i] is ActivateICardEffect)
                    {
                        set.Skills.Add(new LegalSkill { HandCard = card, Effect = effects[i], SkillIndex = i, CanUse = effects[i].CanUse(null) });
                    }
                }
            }
        }
    }

    static void AddFieldActions(Player you, Player opponent, LegalActionSet set)
    {
        foreach (Permanent permanent in you.GetFieldPermanents())
        {
            if (permanent == null || permanent.TopCard == null)
            {
                continue;
            }

            if (permanent.CanDeclareSkill())
            {
                List<ICardEffect> effects = permanent.EffectList(EffectTiming.OnDeclaration);

                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i] is ActivateICardEffect)
                    {
                        set.Skills.Add(new LegalSkill { Permanent = permanent, Effect = effects[i], SkillIndex = i, CanUse = effects[i].CanUse(null) });
                    }
                }
            }

            if (permanent.IsDigimon && permanent.CanAttack(null))
            {
                foreach (Permanent defender in opponent.GetBattleAreaDigimons())
                {
                    if (permanent.CanAttackTargetDigimon(defender, null))
                    {
                        set.Attacks.Add(new LegalAttack { Attacker = permanent, Target = defender });
                    }
                }

                if (permanent.CanAttackTargetDigimon(null, null))
                {
                    set.Attacks.Add(new LegalAttack { Attacker = permanent, Target = null });
                }
            }
        }
    }

    public static string Name(CardSource card) =>
        card == null ? "?" : $"{card.BaseENGCardNameFromEntity} ({card.CardID})";
}
