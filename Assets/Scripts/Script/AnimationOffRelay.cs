using UnityEngine;

/// <summary>
/// Forwards AnimationEvent "Off" from an Animator GameObject to an OffAnimation
/// that lives on a different object in the hierarchy.
/// </summary>
public class AnimationOffRelay : MonoBehaviour
{
    OffAnimation _target;

    public void Bind(OffAnimation target)
    {
        _target = target;
    }

    public void Off()
    {
        if (_target != null)
        {
            _target.Off();
        }
    }
}
