using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCGO.DSTS
{
    [Serializable]
    public class DSTSClipInfo
    {
        [Tooltip("Animator state name / friendly name, e.g. \"Attack 01\"")]
        public string friendlyName;
        [Tooltip("Game code, e.g. \"ba01\"")]
        public string code;
        public bool loop;
        public float length;
    }

    /// <summary>
    /// Added to every generated DSTS prefab. Holds model info and lets gameplay code play
    /// animations by friendly name ("Attack 01") or game code ("ba01").
    /// Non-looping states automatically return to the default (Idle) state when they finish.
    /// </summary>
    [DisallowMultipleComponent]
    public class DSTSModel : MonoBehaviour
    {
        public string slug;
        public string displayName;
        public string chrId;
        public string category;

        [Tooltip("Scale the game uses for this model in its field guide (from the GLB metadata).")]
        public float fieldGuideScale = 1f;

        [Tooltip("State the Animator starts in and returns to.")]
        public string defaultState = "Idle";

        public List<DSTSClipInfo> clips = new List<DSTSClipInfo>();

        [SerializeField] Animator animator;

        public Animator Animator
        {
            get
            {
                if (animator == null) animator = GetComponent<Animator>();
                return animator;
            }
        }

        public DSTSClipInfo Find(string nameOrCode)
        {
            if (string.IsNullOrEmpty(nameOrCode)) return null;
            foreach (var c in clips)
            {
                if (string.Equals(c.friendlyName, nameOrCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.code, nameOrCode, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        public bool HasClip(string nameOrCode) => Find(nameOrCode) != null;

        /// <summary>Play by friendly name ("Attack 01") or code ("ba01"). Returns false if missing.</summary>
        public bool Play(string nameOrCode, float crossFadeSeconds = 0.15f)
        {
            var c = Find(nameOrCode);
            if (c == null || Animator == null) return false;
            Animator.CrossFadeInFixedTime(c.friendlyName, crossFadeSeconds, 0);
            return true;
        }

        /// <summary>Play the first clip whose code starts with a prefix, e.g. "ba" = any attack, "bd" = damage.</summary>
        public bool PlayFirst(string codePrefix, float crossFadeSeconds = 0.15f)
        {
            foreach (var c in clips)
                if (c.code != null && c.code.StartsWith(codePrefix, StringComparison.OrdinalIgnoreCase))
                    return Play(c.friendlyName, crossFadeSeconds);
            return false;
        }

        /// <summary>Play a random clip whose code starts with a prefix.</summary>
        public bool PlayRandom(string codePrefix, float crossFadeSeconds = 0.15f)
        {
            var matches = clips.FindAll(c => c.code != null && c.code.StartsWith(codePrefix, StringComparison.OrdinalIgnoreCase));
            if (matches.Count == 0) return false;
            return Play(matches[UnityEngine.Random.Range(0, matches.Count)].friendlyName, crossFadeSeconds);
        }

        public void PlayIdle(float crossFadeSeconds = 0.15f) => Play(defaultState, crossFadeSeconds);

        // Common card-game shortcuts
        public bool PlayAttack()  => PlayRandom("ba");
        public bool PlayHit()     => Play("bd01") || PlayFirst("bd");
        public bool PlayGuard()   => Play("bg01") || PlayFirst("bg");
        public bool PlayVictory() => Play("bv01") || Play("fe02");
        public bool PlaySpecial() => PlayRandom("bs");
    }
}
