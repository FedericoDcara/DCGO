#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Status badges on field permanents, mirroring what the flat UI shows on
// FieldPermanentCard: current DP (red plate when lowered, green when raised),
// BLOCKER, CAN'T ATTACK, CAN'T SUSPEND, JUST PLAYED (summoning sickness) and
// lasting debuffs. Read-only: only queries the rules code, never changes it.
// Badges lie on the top card, always facing the local player's seat, and the
// expensive rule checks are refreshed a few times per second, not per frame.
public class StatusBadges
{
    const float Refresh = 0.35f;
    const float ChipW = 0.084f;
    const float ChipH = 0.019f;
    const float ChipStep = 0.021f;
    const int MaxChips = 4;

    static readonly Color DpNormal = new Color(0.05f, 0.09f, 0.14f);
    static readonly Color DpDown = new Color(0.62f, 0.07f, 0.07f);
    static readonly Color DpUp = new Color(0.06f, 0.45f, 0.16f);
    static readonly Color BlockerColor = new Color(0.10f, 0.32f, 0.70f);
    static readonly Color NoAttackColor = new Color(0.62f, 0.10f, 0.10f);
    static readonly Color NoSuspendColor = new Color(0.42f, 0.14f, 0.58f);
    static readonly Color SickColor = new Color(0.30f, 0.32f, 0.36f);
    static readonly Color NoUnsuspendColor = new Color(0.36f, 0.20f, 0.62f);
    static readonly Color NoDigivolveColor = new Color(0.55f, 0.30f, 0.08f);
    static readonly Color SecurityColor = new Color(0.62f, 0.10f, 0.32f);
    static readonly Color DeleteColor = new Color(0.45f, 0.05f, 0.05f);
    static readonly Color DebuffColor = new Color(0.70f, 0.36f, 0.05f);

    class Entry
    {
        public VRLabel Dp;
        public readonly List<VRLabel> Chips = new List<VRLabel>();
        public float NextRefresh;
        public string DpText;
        public Color DpPlate;
        public readonly List<KeyValuePair<string, Color>> Status = new List<KeyValuePair<string, Color>>();
        public bool Seen;
    }

    readonly Transform _parent;
    readonly Dictionary<Permanent, Entry> _entries = new Dictionary<Permanent, Entry>();
    readonly List<Permanent> _remove = new List<Permanent>();

    public StatusBadges(Transform parent)
    {
        _parent = parent;
    }

    public void BeginFrame()
    {
        foreach (Entry entry in _entries.Values)
        {
            entry.Seen = false;
        }
    }

    /// <summary>Show the badges of a permanent lying at <paramref name="center"/> (board space, on top of its stack).</summary>
    public void Place(Permanent permanent, Vector3 center)
    {
        if (permanent == null || permanent.TopCard == null)
        {
            return;
        }

        if (!_entries.TryGetValue(permanent, out Entry entry))
        {
            entry = new Entry();
            _entries[permanent] = entry;
        }

        entry.Seen = true;

        if (Time.unscaledTime >= entry.NextRefresh)
        {
            entry.NextRefresh = Time.unscaledTime + Refresh;
            Evaluate(permanent, entry);
        }

        Quaternion flat = Quaternion.Euler(90f, 0f, 0f);
        Vector3 lift = new Vector3(0f, 0.0025f, 0f);

        // DP plate across the lower part of the card (nearest the seat).
        if (entry.DpText != null)
        {
            if (entry.Dp == null)
            {
                entry.Dp = VRLabel.Create(_parent, Vector3.zero, flat, new Vector2(ChipW, ChipH + 0.003f), "", Color.white, DpNormal);
            }

            entry.Dp.gameObject.SetActive(true);
            entry.Dp.transform.localPosition = center + lift + new Vector3(0f, 0f, -0.045f);
            entry.Dp.transform.localRotation = flat;
            entry.Dp.text = entry.DpText;
            entry.Dp.plateColor = entry.DpPlate;
        }
        else if (entry.Dp != null)
        {
            entry.Dp.gameObject.SetActive(false);
        }

        // Status chips stacked above it.
        int shown = Mathf.Min(entry.Status.Count, MaxChips);

        while (entry.Chips.Count < shown)
        {
            entry.Chips.Add(VRLabel.Create(_parent, Vector3.zero, flat, new Vector2(ChipW, ChipH), "", Color.white, SickColor));
        }

        for (int i = 0; i < entry.Chips.Count; i++)
        {
            VRLabel chip = entry.Chips[i];

            if (i >= shown)
            {
                chip.gameObject.SetActive(false);
                continue;
            }

            chip.gameObject.SetActive(true);
            chip.transform.localPosition = center + lift + new Vector3(0f, 0f, -0.045f + ChipStep * (i + 1) + 0.002f);
            chip.transform.localRotation = flat;
            chip.text = entry.Status[i].Key;
            chip.plateColor = entry.Status[i].Value;
        }
    }

    /// <summary>Hides (but keeps) the badges of a permanent, e.g. while its stack is opened in the viewer.</summary>
    public void Hide(Permanent permanent)
    {
        if (permanent != null && _entries.TryGetValue(permanent, out Entry entry))
        {
            entry.Seen = true;
            SetActive(entry, false);
        }
    }

    /// <summary>Destroys badges of permanents that left the field.</summary>
    public void EndFrame()
    {
        _remove.Clear();

        foreach (KeyValuePair<Permanent, Entry> pair in _entries)
        {
            if (!pair.Value.Seen)
            {
                _remove.Add(pair.Key);
            }
        }

        foreach (Permanent permanent in _remove)
        {
            Destroy(_entries[permanent]);
            _entries.Remove(permanent);
        }
    }

    public void Clear()
    {
        foreach (Entry entry in _entries.Values)
        {
            Destroy(entry);
        }

        _entries.Clear();
    }

    static void SetActive(Entry entry, bool active)
    {
        if (entry.Dp != null)
        {
            entry.Dp.gameObject.SetActive(active);
        }

        foreach (VRLabel chip in entry.Chips)
        {
            chip.gameObject.SetActive(active);
        }
    }

    static void Destroy(Entry entry)
    {
        if (entry.Dp != null)
        {
            UnityEngine.Object.Destroy(entry.Dp.gameObject);
        }

        foreach (VRLabel chip in entry.Chips)
        {
            if (chip != null)
            {
                UnityEngine.Object.Destroy(chip.gameObject);
            }
        }
    }

    // ---- rule queries (read-only) ----

    static void Evaluate(Permanent permanent, Entry entry)
    {
        entry.Status.Clear();
        entry.DpText = null;

        try
        {
            if (permanent.TopCard.IsFlipped)
            {
                return;
            }

            if (permanent.IsDigimon && permanent.HasDP)
            {
                int dp = permanent.DP;
                int baseDp = permanent.BaseDP;

                if (dp >= 0)
                {
                    int delta = dp - baseDp;
                    entry.DpText = delta == 0 ? "DP " + dp : "DP " + dp + (delta > 0 ? "  (+" : "  (") + delta + ")";
                    entry.DpPlate = delta < 0 ? DpDown : delta > 0 ? DpUp : DpNormal;
                }
            }

            bool inBattleArea = permanent.PermanentFrame == null || permanent.PermanentFrame.IsBattleAreaFrame();

            if (permanent.IsDigimon && permanent.HasBlocker)
            {
                entry.Status.Add(new KeyValuePair<string, Color>("BLOCKER", BlockerColor));
            }

            if (permanent.IsDigimon && inBattleArea && CannotAttack(permanent))
            {
                entry.Status.Add(new KeyValuePair<string, Color>("CAN'T ATTACK", NoAttackColor));
            }
            else if (permanent.IsDigimon && inBattleArea && IsSummoningSick(permanent))
            {
                entry.Status.Add(new KeyValuePair<string, Color>("JUST PLAYED", SickColor));
            }

            if (inBattleArea && (permanent.IsDigimon || permanent.IsTamer) && !permanent.CanSuspend)
            {
                entry.Status.Add(new KeyValuePair<string, Color>("CAN'T SUSPEND", NoSuspendColor));
            }

            if (inBattleArea && (permanent.IsDigimon || permanent.IsTamer) && !permanent.CanUnsuspend)
            {
                entry.Status.Add(new KeyValuePair<string, Color>("CAN'T UNSUSPEND", NoUnsuspendColor));
            }

            Classify(permanent, out bool noDigivolve, out bool securityDown, out bool deleteLater);

            if (noDigivolve)
            {
                entry.Status.Add(new KeyValuePair<string, Color>("CAN'T DIGIVOLVE", NoDigivolveColor));
            }

            if (securityDown)
            {
                entry.Status.Add(new KeyValuePair<string, Color>("SEC ATTACK -", SecurityColor));
            }

            if (deleteLater)
            {
                entry.Status.Add(new KeyValuePair<string, Color>("WILL BE DELETED", DeleteColor));
            }

            // Generic marker only for lasting debuffs none of the specific badges
            // (or the red DP plate) already explain, e.g. disabled effects.
            if (entry.Status.Count == 0 || OnlyPositive(entry))
            {
                bool dpExplains = entry.DpPlate == DpDown && entry.DpText != null;

                if (!dpExplains && VRCompat.HasLastingDebuff(permanent))
                {
                    entry.Status.Add(new KeyValuePair<string, Color>("DEBUFF", DebuffColor));
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Status badge query failed: " + exception.Message);
        }
    }

    static bool OnlyPositive(Entry entry)
    {
        foreach (KeyValuePair<string, Color> status in entry.Status)
        {
            if (status.Key != "BLOCKER" && status.Key != "JUST PLAYED")
            {
                return false;
            }
        }

        return true;
    }

    // Looks through every active continuous effect once for the specific
    // restrictions the flat client folds into its single "debuff" marker.
    static void Classify(Permanent permanent, out bool noDigivolve, out bool securityDown, out bool deleteLater)
    {
        noDigivolve = false;
        securityDown = false;
        deleteLater = false;

        foreach (Player player in GManager.instance.turnStateMachine.gameContext.Players)
        {
            ClassifyList(player.EffectList(EffectTiming.None), permanent, ref noDigivolve, ref securityDown, ref deleteLater);

            foreach (Permanent source in player.GetFieldPermanents())
            {
                ClassifyList(source.EffectList(EffectTiming.None), permanent, ref noDigivolve, ref securityDown, ref deleteLater);
            }
        }
    }

    static void ClassifyList(List<ICardEffect> effects, Permanent permanent, ref bool noDigivolve, ref bool securityDown, ref bool deleteLater)
    {
        if (effects == null)
        {
            return;
        }

        foreach (ICardEffect effect in effects)
        {
            try
            {
                if (effect == null || !effect.CanUse(null))
                {
                    continue;
                }

                if (!noDigivolve && effect is ICanNotDigivolveEffect digivolve && BlocksDigivolution(digivolve, permanent))
                {
                    noDigivolve = true;
                }

                if (!securityDown && effect is IChangeSAttackEffect security && security.PermanentCondition(permanent))
                {
                    CalculateOrder order = security.isUpDown();
                    securityDown = order == CalculateOrder.DownValue || order == CalculateOrder.DownToConstant
                        || (order == CalculateOrder.UpDownValue && security.GetSAttack(0, permanent, 0) < 0);
                }

                if (!deleteLater && effect is IAddDetailEffect detail && detail.PermanentCondition(permanent))
                {
                    string text = detail.GetDetail();
                    deleteLater = !string.IsNullOrEmpty(text) && text.StartsWith("Delete this");
                }
            }
            catch (Exception)
            {
                // A single misbehaving effect must not hide the other badges.
            }
        }
    }

    static bool BlocksDigivolution(ICanNotDigivolveEffect effect, Permanent permanent)
    {
        if (effect.CanNotEvolve(permanent, permanent.TopCard))
        {
            return true;
        }

        Player owner = permanent.TopCard.Owner;

        if (owner?.HandCards == null)
        {
            return false;
        }

        foreach (CardSource card in owner.HandCards)
        {
            try
            {
                if (effect.CanNotEvolve(permanent, card))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }
        }

        return false;
    }

    static bool IsSummoningSick(Permanent permanent)
    {
        TurnStateMachine turn = GManager.instance.turnStateMachine;
        return permanent.EnterFieldTurnCount == turn.TurnCount
            && turn.gameContext.TurnPlayer == permanent.TopCard.Owner
            && !permanent.HasRush;
    }

    // True when a "can't attack" effect stops this Digimon from attacking the
    // player and every Digimon the opponent has (the same effects the rules use
    // in Permanent.CanAttackTargetDigimon).
    static bool CannotAttack(Permanent permanent)
    {
        if (!Restricted(permanent, null))
        {
            return false;
        }

        foreach (Permanent defender in permanent.TopCard.Owner.Enemy.GetBattleAreaDigimons())
        {
            if (!Restricted(permanent, defender))
            {
                return false;
            }
        }

        return true;
    }

    static bool Restricted(Permanent attacker, Permanent defender)
    {
        foreach (Player player in GManager.instance.turnStateMachine.gameContext.Players)
        {
            foreach (Permanent source in player.GetFieldPermanents())
            {
                if (Blocks(source.EffectList(EffectTiming.None), attacker, defender))
                {
                    return true;
                }
            }

            if (Blocks(player.EffectList(EffectTiming.None), attacker, defender))
            {
                return true;
            }
        }

        return false;
    }

    static bool Blocks(List<ICardEffect> effects, Permanent attacker, Permanent defender)
    {
        foreach (ICardEffect effect in effects)
        {
            if (effect is ICanNotAttackTargetDefendingPermanentEffect restriction
                && effect.CanUse(null)
                && restriction.CanNotAttackTargetDefendingPermanent(attacker, defender)
                && !attacker.TopCard.CanNotBeAffected(effect))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
