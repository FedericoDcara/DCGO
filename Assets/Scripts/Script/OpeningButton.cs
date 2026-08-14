using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class OpeningButton : MonoBehaviour
{
    const float TitleButtonSECooldown = 0.08f;
    static float _lastTitleButtonSETime = float.NegativeInfinity;

    [Header("Button Animator")]
    public Animator ButtonAnimator;

    [Header("選択表示オブジェクト")]
    public GameObject selectedObject;
    public void OnSelect()
    {
        if (selectedObject != null)
        {
            selectedObject.SetActive(true);

            if (ContinuousController.instance == null || Opening.instance == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastTitleButtonSETime >= TitleButtonSECooldown)
            {
                _lastTitleButtonSETime = now;
                ContinuousController.instance.PlaySE(Opening.instance.TitleButtonSE);
            }
        }
    }

    public void OnExit()
    {
        if (selectedObject != null)
        {
            selectedObject.SetActive(false);
        }
    }
}
