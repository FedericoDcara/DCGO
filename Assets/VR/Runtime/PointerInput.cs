using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Single entry point for pointer input used by the game's UI code.
// In the normal (flat) game Provider is null and every call falls through to
// UnityEngine.Input / Screen, so behaviour is identical to before.
// In VR, VRLaserPointer installs itself as the Provider so the controller laser
// drives the same click / drag / right-click code paths as the mouse.
public static class PointerInput
{
    public interface IProvider
    {
        Vector3 MousePosition { get; }
        bool GetMouseButton(int button);
        bool GetMouseButtonDown(int button);
        bool GetMouseButtonUp(int button);
        Vector2 MouseScrollDelta { get; }
        int ScreenWidth { get; }
        int ScreenHeight { get; }
    }

    public static IProvider Provider { get; set; }

    public static Vector3 mousePosition => Provider != null ? Provider.MousePosition : Input.mousePosition;

    public static bool GetMouseButton(int button) =>
        Provider != null ? Provider.GetMouseButton(button) : Input.GetMouseButton(button);

    public static bool GetMouseButtonDown(int button) =>
        Provider != null ? Provider.GetMouseButtonDown(button) : Input.GetMouseButtonDown(button);

    public static bool GetMouseButtonUp(int button) =>
        Provider != null ? Provider.GetMouseButtonUp(button) : Input.GetMouseButtonUp(button);

    public static Vector2 mouseScrollDelta => Provider != null ? Provider.MouseScrollDelta : Input.mouseScrollDelta;

    /// <summary>Width of the surface the game UI is rendered to (the window, or the VR game screen).</summary>
    public static int screenWidth => Provider != null ? Provider.ScreenWidth : Screen.width;

    /// <summary>Height of the surface the game UI is rendered to (the window, or the VR game screen).</summary>
    public static int screenHeight => Provider != null ? Provider.ScreenHeight : Screen.height;
}
