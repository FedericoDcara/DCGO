using System;
using System.Collections.Generic;

/// <summary>
/// Short-lived memo for rules queries made by DISPLAY code (field card DP/Level badges, hand cost text).
///
/// Every Permanent.DP / Level / HasBlocker / HasRush read walks every effect on the whole field, and
/// every EffectList() call re-runs each card script's CardEffects(), allocating new effect objects.
/// With a full board that is O(cards^2) rebuilds per refresh -> the FPS drop at ~16 cards.
///
/// Inside a Scope() those results are cached; when the outermost scope ends the cache is cleared.
/// Gameplay code never runs inside a scope, so rules resolution always sees live values.
/// Only open a scope around code that READS game state and does not change it.
/// </summary>
public static class EffectQueryCache
{
    static int _depth;
    static readonly Dictionary<(object owner, EffectTiming timing), List<ICardEffect>> _effects =
        new Dictionary<(object owner, EffectTiming timing), List<ICardEffect>>();
    static readonly Dictionary<(object owner, int statId), int> _stats =
        new Dictionary<(object owner, int statId), int>();

    public static bool IsActive => _depth > 0;

    public struct ScopeHandle : IDisposable
    {
        public void Dispose() => End();
    }

    public static ScopeHandle Scope()
    {
        _depth++;
        return new ScopeHandle();
    }

    static void End()
    {
        if (_depth > 0) _depth--;

        if (_depth == 0)
        {
            _effects.Clear();
            _stats.Clear();
        }
    }

    /// <summary>Returns a copy, so callers may modify the list freely.</summary>
    public static bool TryGetEffects(object owner, EffectTiming timing, out List<ICardEffect> effects)
    {
        if (_depth > 0 && _effects.TryGetValue((owner, timing), out List<ICardEffect> cached))
        {
            effects = new List<ICardEffect>(cached);
            return true;
        }

        effects = null;
        return false;
    }

    public static void StoreEffects(object owner, EffectTiming timing, List<ICardEffect> effects)
    {
        if (_depth > 0) _effects[(owner, timing)] = new List<ICardEffect>(effects);
    }

    public static bool TryGetStat(object owner, int statId, out int value)
    {
        if (_depth > 0) return _stats.TryGetValue((owner, statId), out value);
        value = 0;
        return false;
    }

    public static void StoreStat(object owner, int statId, int value)
    {
        if (_depth > 0) _stats[(owner, statId)] = value;
    }
}
