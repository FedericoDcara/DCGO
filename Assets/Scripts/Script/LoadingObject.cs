using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class LoadingObject : MonoBehaviour
{
    public Animator anim;

    public Text LoadingText;

    public GameObject AnimationParent;

    public GameObject Meat;
    public GameObject Agumon;

    Vector3 defaultAgumonPos = new Vector3(250, 0 ,0); 

    public float speed = 700f;

    public IEnumerator StartLoading(string DefaultString)
    {
        this.transform.parent.gameObject.SetActive(true);
        this.gameObject.SetActive(true);
        // === DCGO-CUSTOM:replay begin ===
        EnsureOffReceiverOnAnimator();
        // === DCGO-CUSTOM:replay end ===
        // === DCGO-CUSTOM:reconnect begin ===
        if (anim != null)
        {
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            anim.SetInteger("Close", 0);
        }
        LoadingText.gameObject.SetActive(true);

        yield return new WaitWhile(() => !this.gameObject.activeSelf || !this.transform.parent.gameObject.activeSelf);

        StopLoadingTextCoroutine();

        if(ContinuousController.instance != null)
        {
            setLoadingTextHost = ContinuousController.instance;
            setLoadingTextCoroutine = ContinuousController.instance.StartCoroutine(SetLoadingText(DefaultString));
        }
        else
        {
            setLoadingTextHost = this;
            setLoadingTextCoroutine = StartCoroutine(SetLoadingText(DefaultString));
        }
        // === DCGO-CUSTOM:reconnect end ===

        if (AnimationParent != null && AnimationParent.activeSelf)
        {
            Agumon.transform.localPosition = defaultAgumonPos;
            // === DCGO-CUSTOM:replay begin ===
            // Host on ContinuousController so rewind/seek never StartCoroutine on an inactive LoadingObject.
            if (ContinuousController.instance != null)
            {
                moveAgumonCoroutine = ContinuousController.instance.StartCoroutine(moveAgumonIEnumerator());
            }
            else if (this.gameObject.activeInHierarchy)
            {
                moveAgumonCoroutine = StartCoroutine(moveAgumonIEnumerator());
            }
            // === DCGO-CUSTOM:replay end ===
        }
    }

    Coroutine moveAgumonCoroutine = null;
    // === DCGO-CUSTOM:reconnect begin ===
    Coroutine setLoadingTextCoroutine = null;
    MonoBehaviour setLoadingTextHost = null;
    // === DCGO-CUSTOM:reconnect end ===

    IEnumerator SetLoadingText(string DefaultString)
    {
        float waitTime = 0.18f;

        int count = 0;

        // === DCGO-CUSTOM:reconnect begin ===
        while (LoadingText)
        {
            count++;

            if(count >= 4)
            {
                count = 0;
            }

            LoadingText.text = DefaultString;

            for(int i=0;i<count;i++)
            {
                LoadingText.text += ".";
            }

            yield return new WaitForSecondsRealtime(waitTime);
        }

        setLoadingTextCoroutine = null;
        setLoadingTextHost = null;
        // === DCGO-CUSTOM:reconnect end ===
    }

    // === DCGO-CUSTOM:reconnect begin ===
    void StopLoadingTextCoroutine()
    {
        if (setLoadingTextCoroutine == null)
        {
            return;
        }

        if (setLoadingTextHost != null)
        {
            setLoadingTextHost.StopCoroutine(setLoadingTextCoroutine);
        }

        setLoadingTextCoroutine = null;
        setLoadingTextHost = null;
    }
    // === DCGO-CUSTOM:reconnect end ===

    IEnumerator moveAgumonIEnumerator()
    {
        while(true)
        {
            // === DCGO-CUSTOM:reconnect begin ===
            Agumon.transform.localPosition -= new Vector3(speed * Time.unscaledDeltaTime, 0, 0);

            if (Mathf.Abs(Agumon.transform.localPosition.x - Meat.transform.localPosition.x) < speed * Time.unscaledDeltaTime * 2)
            {
                Agumon.transform.localPosition = Meat.transform.localPosition;
                yield break;
            }
            // === DCGO-CUSTOM:reconnect end ===

            yield return null;
        }
    }

    public IEnumerator EndLoading()
    {
        StopLoadingTextCoroutine();

        if(moveAgumonCoroutine != null)
        {
            // === DCGO-CUSTOM:replay begin ===
            if (ContinuousController.instance != null)
            {
                ContinuousController.instance.StopCoroutine(moveAgumonCoroutine);
            }
            else
            {
                StopCoroutine(moveAgumonCoroutine);
            }
            // === DCGO-CUSTOM:replay end ===

            moveAgumonCoroutine = null;
        }

        if(AnimationParent != null && AnimationParent.activeSelf)
        {
            bool end = false;
            Sequence sequence = DOTween.Sequence();

            sequence
                .Append(Agumon.transform.DOLocalMove(Meat.transform.localPosition, 0.1f))
                .AppendCallback(() => end = true)
                // === DCGO-CUSTOM:reconnect begin ===
                .SetUpdate(true);
                // === DCGO-CUSTOM:reconnect end ===

            sequence.Play();

            yield return new WaitWhile(() => !end);
            end = false;
        }
        
        // === DCGO-CUSTOM:reconnect begin ===
        // === DCGO-CUSTOM:replay begin ===
        // AnimationEvent 'Off' is often on a child named Parent without this script —
        // install a relay before triggering Close so the event has a receiver.
        EnsureOffReceiverOnAnimator();
        // === DCGO-CUSTOM:replay end ===
        if (anim != null)
        {
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            anim.SetInteger("Close", 1);
        }
        // === DCGO-CUSTOM:reconnect end ===

        if (LoadingText)
        {
            LoadingText.gameObject.SetActive(false);
        }

        // === DCGO-CUSTOM:reconnect begin ===
        float closeWait = 0f;
        while (this.gameObject.activeSelf && closeWait < 3f)
        {
            closeWait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (this.gameObject.activeSelf)
        {
            Off();
        }
        // === DCGO-CUSTOM:reconnect end ===

        if (AnimationParent != null && AnimationParent.activeSelf)
        {
            Agumon.transform.localPosition = defaultAgumonPos;
        }
    }

    // === DCGO-CUSTOM:replay begin ===
    void EnsureOffReceiverOnAnimator()
    {
        if (anim == null)
        {
            return;
        }

        var host = anim.gameObject;
        if (host.GetComponent<LoadingObject>() != null)
        {
            return;
        }

        var relay = host.GetComponent<LoadingCloseRelay>();
        if (relay == null)
        {
            relay = host.AddComponent<LoadingCloseRelay>();
        }

        relay.Bind(this);
    }
    // === DCGO-CUSTOM:replay end ===

    public void Off()
    {
        this.gameObject.SetActive(false);
        // === DCGO-CUSTOM:replay begin ===
        if (transform.parent != null)
        {
            transform.parent.gameObject.SetActive(false);
        }
        // === DCGO-CUSTOM:replay end ===
    }
}

// === DCGO-CUSTOM:replay begin ===
/// <summary>
/// Receives AnimationEvent Off on the Animator host (often named Parent).
/// </summary>
public class LoadingCloseRelay : MonoBehaviour
{
    LoadingObject _owner;

    public void Bind(LoadingObject owner)
    {
        _owner = owner;
    }

    public void Off()
    {
        if (_owner != null)
        {
            _owner.Off();
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
// === DCGO-CUSTOM:replay end ===
