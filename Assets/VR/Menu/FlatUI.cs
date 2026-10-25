#if DCGO_VR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// === DCGO-CUSTOM:vr ===
// Drives the (hidden) flat menu exactly like a mouse would: a click is the
// same enter / down / up / click / exit sequence the EventSystem sends, so
// Buttons, EventTriggers and CommandButtons all run their own handlers.
// The native VR menu never calls menu logic directly.
public static class FlatUI
{
    static readonly List<Button> _buttons = new List<Button>();

    public static bool Click(GameObject target)
    {
        if (target == null || !target.activeInHierarchy)
        {
            Debug.Log("[DCGO VR] Menu: can't click " + (target != null ? target.name + " (hidden)" : "(missing)"));
            return false;
        }

        if (target.TryGetComponent(out Selectable selectable) && !selectable.IsInteractable())
        {
            Debug.Log("[DCGO VR] Menu: " + target.name + " is not clickable right now");
            return false;
        }

        EventSystem system = EventSystem.current;
        PointerEventData data = new PointerEventData(system)
        {
            button = PointerEventData.InputButton.Left,
            position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
            clickCount = 1,
            clickTime = Time.unscaledTime,
            pointerPress = target,
            rawPointerPress = target,
            pointerEnter = target,
        };

        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerExitHandler);
        Debug.Log("[DCGO VR] Menu: clicked " + Path(target));
        return true;
    }

    /// <summary>The first Button under root whose onClick calls this method (persistent listener).</summary>
    public static Button ButtonCalling(Component root, string method)
    {
        if (root == null)
        {
            return null;
        }

        _buttons.Clear();
        root.GetComponentsInChildren(true, _buttons);

        foreach (Button button in _buttons)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == method)
                {
                    return button;
                }
            }
        }

        return null;
    }

    /// <summary>The first EventTrigger under root that calls this method on any event.</summary>
    public static GameObject TriggerCalling(Component root, string method)
    {
        if (root == null)
        {
            return null;
        }

        foreach (EventTrigger trigger in root.GetComponentsInChildren<EventTrigger>(true))
        {
            foreach (EventTrigger.Entry entry in trigger.triggers)
            {
                for (int i = 0; i < entry.callback.GetPersistentEventCount(); i++)
                {
                    if (entry.callback.GetPersistentMethodName(i) == method)
                    {
                        return trigger.gameObject;
                    }
                }
            }
        }

        return null;
    }

    public static Transform Find(Component root, string name)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>Open = active and, when its Animator has an "Open" int, that int is 1.</summary>
    public static bool IsOpen(GameObject go, Animator anim)
    {
        if (go == null || !go.activeInHierarchy)
        {
            return false;
        }

        if (anim == null || !anim.isActiveAndEnabled || anim.runtimeAnimatorController == null)
        {
            return true;
        }

        foreach (AnimatorControllerParameter parameter in anim.parameters)
        {
            if (parameter.name == "Open" && parameter.type == AnimatorControllerParameterType.Int)
            {
                return anim.GetInteger("Open") == 1;
            }
        }

        return true;
    }

    /// <summary>Selects a text field so the VR keyboard opens for it.</summary>
    public static void Focus(Selectable field)
    {
        if (field == null || EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(field.gameObject);

        if (field is InputField legacy)
        {
            legacy.ActivateInputField();
        }
        else if (field is TMPro.TMP_InputField tmp)
        {
            tmp.ActivateInputField();
        }
    }

    public static string TextOf(Text text) => text != null && text.gameObject.activeInHierarchy ? text.text : "";

    static string Path(GameObject go)
    {
        Transform t = go.transform;
        string path = t.name;

        for (int i = 0; i < 2 && t.parent != null; i++)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
#endif
