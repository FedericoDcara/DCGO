using UnityEngine;

// === DCGO-CUSTOM:vr ===
// The only way VR (or any non-flat UI) sends moves into the game.
// Every method:
//   1. re-checks the move against LegalActions.Current,
//   2. builds exactly the same packet the flat UI builds (PlayCardAction,
//      AttackPermanentAction, ActivateCardAction, ActivatePermanentAction,
//      PassAction) or calls the same breeding RPC,
//   3. sends it through TurnStateMachine.QueueMainPhaseAction.
// No new RPCs or packet formats, so VR stays cross-play compatible.
public static class GameActions
{
    public static bool Play(LegalPlay play)
    {
        if (!Validate(out TurnStateMachine tsm))
        {
            return Reject("play " + play + " - " + WhyNotReady());
        }

        if (play == null || !LegalActions.Current.Plays.Exists(p => Same(p, play)))
        {
            return Reject("play " + play + " - no longer in the legal move list (" + LegalActions.Current.Plays.Count + " plays available)");
        }

        switch (play.Kind)
        {
            case LegalPlayKind.PlayToField:
                Send(tsm, new PlayCardAction(play.Card.CardIndex, play.Card.PreferredFrame().FrameID, new int[0], -1, new int[0]));
                return true;

            case LegalPlayKind.Digivolve:
                Send(tsm, new PlayCardAction(play.Card.CardIndex, play.Frame.FrameID, new int[0], -1, new int[0]));
                return true;

            case LegalPlayKind.Option:
                Send(tsm, new PlayCardAction(play.Card.CardIndex, 0, new int[0], -1, new int[0]));
                return true;

            default:
                // DNA / Burst / App Fusion need an extra choice; use the overloads below once the VR picker exists.
                return Reject(play.Kind + " needs an extra selection (not supported by Play(LegalPlay) yet)");
        }
    }

    /// <summary>DNA digivolution onto Frame using two evolution roots.</summary>
    public static bool PlayJogress(LegalPlay play, Permanent rootA, Permanent rootB)
    {
        if (!Validate(out TurnStateMachine tsm) || play == null || play.Kind != LegalPlayKind.Jogress || rootA == null || rootB == null)
        {
            return Reject("jogress");
        }

        Send(tsm, new PlayCardAction(play.Card.CardIndex, play.Frame.FrameID, new[] { rootA.PermanentFrame.FrameID, rootB.PermanentFrame.FrameID }, -1, new int[0]));
        return true;
    }

    /// <summary>Burst digivolution onto Frame, returning the given Tamer.</summary>
    public static bool PlayBurst(LegalPlay play, Permanent tamer)
    {
        if (!Validate(out TurnStateMachine tsm) || play == null || play.Kind != LegalPlayKind.Burst || tamer == null)
        {
            return Reject("burst");
        }

        Send(tsm, new PlayCardAction(play.Card.CardIndex, play.Frame.FrameID, new int[0], tamer.PermanentFrame.FrameID, new int[0]));
        return true;
    }

    /// <summary>App Fusion onto Frame using one of TargetPermanent's linked cards.</summary>
    public static bool PlayAppFusion(LegalPlay play, CardSource linkedCard)
    {
        if (!Validate(out TurnStateMachine tsm) || play == null || play.Kind != LegalPlayKind.AppFusion || linkedCard == null)
        {
            return Reject("app fusion");
        }

        int linkIndex = play.TargetPermanent.LinkedCards.IndexOf(linkedCard);

        if (linkIndex < 0)
        {
            return Reject("app fusion: card is not linked to the target");
        }

        Send(tsm, new PlayCardAction(play.Card.CardIndex, play.Frame.FrameID, new int[0], -1, new[] { play.TargetPermanent.PermanentFrame.FrameID, linkIndex }));
        return true;
    }

    public static bool Attack(LegalAttack attack)
    {
        if (!Validate(out TurnStateMachine tsm) || attack == null
            || !LegalActions.Current.Attacks.Exists(a => a.Attacker == attack.Attacker && a.Target == attack.Target))
        {
            return Reject("attack " + attack);
        }

        GameContext context = tsm.gameContext;
        int attackerIndex = context.TurnPlayer.GetFieldPermanents().IndexOf(attack.Attacker);
        int targetIndex = attack.Target != null ? context.NonTurnPlayer.GetFieldPermanents().IndexOf(attack.Target) : -1;

        if (attackerIndex < 0 || (attack.Target != null && targetIndex < 0))
        {
            return Reject("attack: permanent not found on field");
        }

        Send(tsm, new AttackPermanentAction(attackerIndex, targetIndex));
        return true;
    }

    public static bool UseSkill(LegalSkill skill)
    {
        if (!Validate(out TurnStateMachine tsm))
        {
            return Reject("skill " + skill + " - " + WhyNotReady());
        }

        // Effects are recreated by the engine on every query, so match by owner + index
        // (exactly what the network packet uses), not by object identity.
        if (skill == null || !skill.CanUse
            || !LegalActions.Current.Skills.Exists(s => s.HandCard == skill.HandCard && s.Permanent == skill.Permanent && s.SkillIndex == skill.SkillIndex && s.CanUse))
        {
            return Reject("skill " + skill + " - no longer usable");
        }

        if (skill.HandCard != null)
        {
            Send(tsm, new ActivateCardAction(skill.HandCard.CardIndex, skill.SkillIndex));
            return true;
        }

        int permanentIndex = tsm.gameContext.TurnPlayer.GetFieldPermanents().IndexOf(skill.Permanent);

        if (permanentIndex < 0)
        {
            return Reject("skill: permanent not found on field");
        }

        Send(tsm, new ActivatePermanentAction(permanentIndex, skill.SkillIndex));
        return true;
    }

    /// <summary>End the main phase (same as the flat "next phase" button).</summary>
    public static bool Pass()
    {
        if (!Validate(out TurnStateMachine tsm) || !LegalActions.Current.CanPass || tsm.IsSelecting)
        {
            return Reject("pass");
        }

        Send(tsm, new PassAction());
        return true;
    }

    /// <summary>Breeding phase: true = hatch / move out of the breeding area (whichever the engine offers), false = do nothing.</summary>
    public static bool Breeding(bool doAction)
    {
        if (!Validate(out TurnStateMachine tsm) || !LegalActions.Current.IsYourBreedingPhase)
        {
            return Reject("breeding");
        }

        tsm.SendShouldHatch(doAction);
        _lockedUntil = Time.unscaledTime + SendLockSeconds;
        LegalActions.Invalidate();
        return true;
    }

    // Short lock after each send so a double grab/click can't queue the same move twice
    // before the RPC comes back and the engine starts executing it.
    const float SendLockSeconds = 0.75f;
    static float _lockedUntil;

    static bool Validate(out TurnStateMachine tsm)
    {
        if (Time.unscaledTime < _lockedUntil)
        {
            tsm = null;
            return false;
        }

        return LegalActions.IsAwaitingLocalInput(out tsm);
    }

    static string WhyNotReady()
    {
        if (Time.unscaledTime < _lockedUntil)
        {
            return "previous move still being sent";
        }

        TurnStateMachine tsm = GManager.instance != null ? GManager.instance.turnStateMachine : null;

        if (tsm == null || tsm.gameContext == null)
        {
            return "no game running";
        }

        if (tsm.gameContext.TurnPlayer == null || !tsm.gameContext.TurnPlayer.isYou)
        {
            return "not your turn";
        }

        if (tsm.isExecuting)
        {
            return "the game is still resolving an effect";
        }

        if (tsm.isSecurityCehck)
        {
            return "security check in progress";
        }

        return "not waiting for input";
    }

    static bool Same(LegalPlay a, LegalPlay b) =>
        a.Card == b.Card && a.Kind == b.Kind && a.TargetPermanent == b.TargetPermanent;

    static void Send(TurnStateMachine tsm, MainPhaseAction action)
    {
        tsm.QueueMainPhaseAction(tsm.gameContext.TurnPlayer, action);
        _lockedUntil = Time.unscaledTime + SendLockSeconds;
        LegalActions.Invalidate();
    }

    static bool Reject(string what)
    {
        Debug.LogWarning("[DCGO VR] Action rejected: " + what);
        return false;
    }
}
