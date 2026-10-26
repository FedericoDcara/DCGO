#if DCGO_VR
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Makes an arena figure pointable with the laser: a Digimon / Tamer (its permanent)
// or the opponent in person (= attack the player / their security). VRGrabber maps
// it to the same card on the desk, so every action works the same from the arena.
public class VRArenaTarget : MonoBehaviour
{
    public Permanent Permanent;
    public bool OpponentPlayer;

    /// <summary>Wins over figures in front of it on the same laser ray (the opponent's security shields:
    /// a Digimon standing in front must not block attacking the player).</summary>
    public bool Priority;

    /// <summary>What the laser points at in the arena this frame (set by VRGrabber).</summary>
    public static VRArenaTarget Hovered;
}
#endif
