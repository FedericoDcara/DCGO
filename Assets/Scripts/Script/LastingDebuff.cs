using System;
using System.Collections.Generic;

public static class LastingDebuff
{
    public static bool AppliesTo(Permanent permanent)
    {
        if (permanent == null || permanent.TopCard == null || permanent.TopCard.IsFlipped)
        {
            return false;
        }

        if (AnyApplies(permanent.EffectList(EffectTiming.None), permanent))
        {
            return true;
        }

        GameContext gameContext = GManager.instance != null && GManager.instance.turnStateMachine != null
            ? GManager.instance.turnStateMachine.gameContext
            : null;

        if (gameContext == null)
        {
            return false;
        }

        foreach (Player player in gameContext.Players)
        {
            if (player == null)
            {
                continue;
            }

            if (AnyApplies(player.EffectList(EffectTiming.None), permanent))
            {
                return true;
            }

            List<Permanent> field = player.GetFieldPermanents();
            for (int i = 0; i < field.Count; i++)
            {
                Permanent source = field[i];
                if (source == null || source == permanent)
                {
                    continue;
                }

                if (AnyApplies(source.EffectList(EffectTiming.None), permanent))
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool AnyApplies(List<ICardEffect> effects, Permanent permanent)
    {
        if (effects == null)
        {
            return false;
        }

        for (int i = 0; i < effects.Count; i++)
        {
            if (Applies(effects[i], permanent))
            {
                return true;
            }
        }

        return false;
    }

    static bool Applies(ICardEffect effect, Permanent permanent)
    {
        if (!IsActive(effect))
        {
            return false;
        }

        try
        {
            if (effect is IChangeDPEffect changeDP && changeDP.IsMinusDP() && changeDP.PermanentCondition(permanent))
            {
                return true;
            }

            if (effect is IChangeBaseDPEffect changeBaseDP && changeBaseDP.IsMinusDP() && changeBaseDP.PermanentCondition(permanent))
            {
                return true;
            }

            if (effect is IChangeCardDPEffect changeCardDP && changeCardDP.IsMinusDP() && changeCardDP.CardCondition(permanent.TopCard))
            {
                return true;
            }

            if (effect is IChangeSAttackEffect changeSAttack
                && changeSAttack.PermanentCondition(permanent)
                && IsSecurityAttackDebuff(changeSAttack, permanent))
            {
                return true;
            }

            if (effect is IChangeLinkMaxEffect changeLinkMax
                && changeLinkMax.isUpDown() == CalculateOrder.DownValue
                && changeLinkMax.PermanentCondition(permanent))
            {
                return true;
            }

            if (effect is ICanNotUnsuspendEffect canNotUnsuspend && canNotUnsuspend.CanNotUnsuspend(permanent))
            {
                return true;
            }

            if (effect is ICanNotSuspendEffect canNotSuspend && canNotSuspend.CanNotSuspend(permanent))
            {
                return true;
            }

            if (effect is ICanNotDigivolveEffect canNotDigivolve && BlocksDigivolution(canNotDigivolve, permanent))
            {
                return true;
            }

            if (effect is ICanNotAttackTargetDefendingPermanentEffect canNotAttack && BlocksAttack(canNotAttack, permanent))
            {
                return true;
            }

            if (effect is IAddDetailEffect detail
                && detail.PermanentCondition(permanent)
                && !string.IsNullOrEmpty(detail.GetDetail())
                && detail.GetDetail().StartsWith("Delete this"))
            {
                return true;
            }

            if (effect is IDisableCardEffect disable && DisablesAttackOrDigivolve(disable, permanent))
            {
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    static bool IsSecurityAttackDebuff(IChangeSAttackEffect effect, Permanent permanent)
    {
        CalculateOrder order = effect.isUpDown();
        if (order == CalculateOrder.DownValue || order == CalculateOrder.DownToConstant)
        {
            return true;
        }

        if (order == CalculateOrder.UpDownValue)
        {
            return effect.GetSAttack(0, permanent, 0) < 0;
        }

        return false;
    }

    static bool DisablesAttackOrDigivolve(IDisableCardEffect disable, Permanent permanent)
    {
        if (AnyDisabled(disable, permanent.EffectList(EffectTiming.OnAllyAttack))
            || AnyDisabled(disable, permanent.EffectList(EffectTiming.OnEnterFieldAnyone)))
        {
            return true;
        }

        return false;
    }

    static bool AnyDisabled(IDisableCardEffect disable, List<ICardEffect> effects)
    {
        if (effects == null)
        {
            return false;
        }

        for (int i = 0; i < effects.Count; i++)
        {
            ICardEffect cardEffect = effects[i];
            if (cardEffect == null || (!cardEffect.IsOnAttack && !cardEffect.IsWhenDigivolving))
            {
                continue;
            }

            try
            {
                if (disable.IsDisabled(cardEffect))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }

    static bool BlocksDigivolution(ICanNotDigivolveEffect effect, Permanent permanent)
    {
        if (DigivolutionBlocked(effect, permanent, permanent.TopCard))
        {
            return true;
        }

        Player owner = permanent.TopCard.Owner;
        if (owner == null || owner.HandCards == null)
        {
            return false;
        }

        for (int i = 0; i < owner.HandCards.Count; i++)
        {
            if (DigivolutionBlocked(effect, permanent, owner.HandCards[i]))
            {
                return true;
            }
        }

        return false;
    }

    static bool DigivolutionBlocked(ICanNotDigivolveEffect effect, Permanent permanent, CardSource card)
    {
        try
        {
            return effect.CanNotEvolve(permanent, card);
        }
        catch (Exception)
        {
            return false;
        }
    }

    static bool BlocksAttack(ICanNotAttackTargetDefendingPermanentEffect effect, Permanent permanent)
    {
        if (AttackBlocked(effect, permanent, null))
        {
            return true;
        }

        GameContext gameContext = GManager.instance.turnStateMachine.gameContext;
        foreach (Player player in gameContext.Players)
        {
            if (player == null)
            {
                continue;
            }

            List<Permanent> field = player.GetFieldPermanents();
            for (int i = 0; i < field.Count; i++)
            {
                if (AttackBlocked(effect, permanent, field[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool AttackBlocked(ICanNotAttackTargetDefendingPermanentEffect effect, Permanent attacker, Permanent defender)
    {
        try
        {
            return effect.CanNotAttackTargetDefendingPermanent(attacker, defender);
        }
        catch (Exception)
        {
            return false;
        }
    }

    static bool IsActive(ICardEffect effect)
    {
        if (effect == null)
        {
            return false;
        }

        try
        {
            return effect.CanUse(null);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
