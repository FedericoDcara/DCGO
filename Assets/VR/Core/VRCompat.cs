using System;
using System.Collections.Generic;
using System.Reflection;

// === DCGO-CUSTOM:vr ===
// Keeps the VR module independent of other fork-only features, so the same
// Assets/VR folder compiles on the official client (e.g. dev/vr-standard) and
// on the customised fork. Fork-only APIs are looked up by reflection once;
// when they are missing, a plain fallback is used.
public static class VRCompat
{
    /// <summary>Whether you may see this card's face on the field (stacks under Digimon / Tamers):
    /// face-up cards, and your own face-down cards (the flat detail panel shows them to their
    /// owner too, see CardInfo). The opponent's face-down cards stay hidden.</summary>
    public static bool CanSee(CardSource card) => card != null && (!card.IsFlipped || (card.Owner != null && card.Owner.isYou));

    static bool _resolved;
    static FieldInfo _replayField;
    static PropertyInfo _replayProperty;
    static MethodInfo _appliedText;
    static MethodInfo _lastingDebuff;

    static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        _replayField = typeof(GManager).GetField("IsReplay", instance);
        _replayProperty = _replayField == null ? typeof(GManager).GetProperty("IsReplay", instance) : null;
        _appliedText = typeof(PermanentDetail).GetMethod("BuildAppliedEffectText", statics, null, new[] { typeof(Permanent) }, null);

        Type lasting = typeof(GManager).Assembly.GetType("LastingDebuff");
        _lastingDebuff = lasting?.GetMethod("AppliesTo", statics, null, new[] { typeof(Permanent) }, null);
    }

    /// <summary>True while watching a replay (fork feature; always false on the official client).</summary>
    public static bool IsReplay
    {
        get
        {
            Resolve();
            GManager manager = GManager.instance;

            if (manager == null)
            {
                return false;
            }

            if (_replayField != null)
            {
                return _replayField.GetValue(manager) is bool value && value;
            }

            return _replayProperty != null && _replayProperty.GetValue(manager) is bool flag && flag;
        }
    }

    /// <summary>The flat UI's "lasting debuff" marker (fork feature; false when absent).</summary>
    public static bool HasLastingDebuff(Permanent permanent)
    {
        Resolve();
        return _lastingDebuff != null && _lastingDebuff.Invoke(null, new object[] { permanent }) is bool value && value;
    }

    /// <summary>The text the permanent detail panel shows (security attack, keywords, applied effects).</summary>
    public static string AppliedEffectText(Permanent permanent)
    {
        Resolve();

        if (_appliedText != null)
        {
            return _appliedText.Invoke(null, new object[] { permanent }) as string ?? "";
        }

        return BuildAppliedEffectText(permanent);
    }

    // Same rules as the official PermanentDetail.OpenUnitDetail text.
    static string BuildAppliedEffectText(Permanent permanent)
    {
        if (permanent == null)
        {
            return "";
        }

        List<ICardEffect> cardEffects = new List<ICardEffect>();

        foreach (ICardEffect cardEffect in permanent.EffectList(EffectTiming.None))
        {
            if (cardEffect is IDisableCardEffect || (cardEffect.CanUse(null) && !string.IsNullOrEmpty(cardEffect.EffectName)))
            {
                cardEffects.Add(cardEffect);
            }
        }

        string s = "";

        if (permanent.IsDigimon)
        {
            s += permanent.Strike_AllowMinus >= 0
                ? $"Security Attack : {permanent.Strike}\n"
                : $"Security Attack : {permanent.Strike} ({permanent.Strike_AllowMinus})\n";
            s += "-------------------------\n";
        }

        if (permanent.IsUnblockable) s += "- Unblockable\n";
        if (permanent.HasBlocker) s += "- Blocker\n";
        if (permanent.HasPierce) s += "- Pierce\n";
        if (permanent.HasReboot) s += "- Reboot\n";
        if (permanent.HasEvade) s += "- Evade\n";
        if (permanent.HasRush) s += "- Rush\n";
        if (permanent.HasAlliance) s += "- Alliance\n";
        if (permanent.HasBarrier) s += "- Barrier\n";

        if (permanent.HasRetaliation)
        {
            for (int i = 0; i < permanent.RetaliationCount; i++)
            {
                s += "- Retaliation\n";
            }
        }

        if (permanent.HasJamming) s += "- Jamming\n";
        if (permanent.HasRaid) s += "- Raid\n";
        if (permanent.HasMindLink) s += "- Mind Link\n";
        if (permanent.HasFortitude) s += "- Fortitude\n";
        if (permanent.HasBlitz) s += "- Blitz\n";
        if (permanent.HasScapegoat) s += "- Scapegoat\n";
        if (permanent.HasCollision) s += "- Collision\n";
        if (permanent.HasPartition) s += "- Partition\n";
        if (permanent.HasAscension) s += "- Ascension\n";
        if (permanent.HasGuard) s += "- Guard\n";
        if (permanent.HasEngage) s += "- Engage\n";
        if (permanent.HasVortex) s += "- Vortex\n";

        if (permanent.HasSecurityAttackChanges)
        {
            foreach (int change in permanent.SecurityAttackChanges)
            {
                s += change > 0 ? $"- Security Attack +{change}\n" : $"- Security Attack {change}\n";
            }
        }

        int linkChange = permanent.LinkedMax - 1;

        if (linkChange != 0)
        {
            s += linkChange > 0 ? $"- Link +{linkChange}\n" : $"- Link {linkChange}\n";
        }

        foreach (ICardEffect cardEffect in cardEffects)
        {
            if (cardEffect is IChangeSAttackEffect || cardEffect is ICanSuspendByDigisorptionEffect || cardEffect is IAddSkillEffect
                || cardEffect is IBlockerEffect || cardEffect is IRushEffect || cardEffect is IRebootEffect
                || cardEffect is IAddDigivolutionRequirementEffect
                || (cardEffect is CanNotBeDestroyedByBattleClass && cardEffect.EffectName == "Jamming")
                || cardEffect is IChangeLinkMaxEffect || cardEffect is IAddDetailEffect || cardEffect.IsNotShowUI)
            {
                continue;
            }

            s += $"- {cardEffect.EffectName}\n";
        }

        foreach (Player player in GManager.instance.turnStateMachine.gameContext.Players_ForTurnPlayer)
        {
            foreach (Permanent other in player.GetFieldPermanents())
            {
                AddDetails(other.EffectList(EffectTiming.None), permanent, ref s);
            }

            AddDetails(player.EffectList(EffectTiming.None), permanent, ref s);
        }

        return s.Replace("、", ",").Replace("，", ",");
    }

    static void AddDetails(List<ICardEffect> effects, Permanent permanent, ref string s)
    {
        foreach (ICardEffect cardEffect in effects)
        {
            if (cardEffect is IAddDetailEffect detail && cardEffect.CanTrigger(null) && detail.PermanentCondition(permanent))
            {
                s += $"- {detail.GetDetail()}\n";
            }
        }
    }
}
