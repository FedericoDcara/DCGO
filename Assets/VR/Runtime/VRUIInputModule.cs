#if DCGO_VR
using UnityEngine;
using UnityEngine.EventSystems;

// === DCGO-CUSTOM:vr ===
// StandaloneInputModule ignores all pointer input while the application window
// is not focused. With a headset on, the desktop window (or the editor's Game
// view) is usually NOT focused, so laser clicks were silently dropped.
// This module behaves exactly like StandaloneInputModule but always processes.
// VRLaserPointer swaps it in for every StandaloneInputModule it finds.
public class VRUIInputModule : StandaloneInputModule
{
    // The base version releases any held pointer every frame while unfocused,
    // which would cancel drags. Its only other job (tracking mouse movement for
    // ShouldActivateModule) is not needed because that is overridden below.
    public override void UpdateModule()
    {
    }

    public override bool ShouldActivateModule() => enabled && gameObject.activeInHierarchy;

    public override void Process()
    {
        bool usedEvent = SendUpdateEventToSelectedObject();

        if (eventSystem.sendNavigationEvents)
        {
            if (!usedEvent)
            {
                usedEvent |= SendMoveEventToSelectedObject();
            }

            if (!usedEvent)
            {
                SendSubmitEventToSelectedObject();
            }
        }

        ProcessMouseEvent();
    }

    /// <summary>Replace a StandaloneInputModule with this one, copying its settings.</summary>
    public static VRUIInputModule Replace(StandaloneInputModule original)
    {
        if (original is VRUIInputModule existing)
        {
            return existing;
        }

        VRUIInputModule module = original.GetComponent<VRUIInputModule>();

        if (module == null)
        {
            module = original.gameObject.AddComponent<VRUIInputModule>();
            module.horizontalAxis = original.horizontalAxis;
            module.verticalAxis = original.verticalAxis;
            module.submitButton = original.submitButton;
            module.cancelButton = original.cancelButton;
            module.inputActionsPerSecond = original.inputActionsPerSecond;
            module.repeatDelay = original.repeatDelay;
        }

        original.enabled = false;
        module.enabled = true;
        return module;
    }
}
#endif
