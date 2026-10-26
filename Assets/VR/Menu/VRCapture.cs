#if DCGO_VR
using System.IO;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Development aid (editor / development builds only): renders what the player
// sees in front of the VR menu into a PNG under Logs/VRCaptures, so menu
// pages can be reviewed (and shared) without a screen recorder.
public static class VRCapture
{
    static Camera _camera;
    static RenderTexture _target;

    /// <summary>Off by default: each capture renders 1080p, reads it back and PNG-encodes it on the
    /// main thread (100-300 ms hitch). Turn on only when you want review screenshots.</summary>
    public static bool Enabled = false;

    public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "VRCaptures"));

    /// <summary>Renders the VR room from <paramref name="eye"/> looking along <paramref name="forward"/>.</summary>
    public static void Capture(string name, Vector3 eye, Quaternion rotation, float fov = 62f)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Enabled)
        {
            return;
        }

        try
        {
            if (_camera == null)
            {
                GameObject go = new GameObject("DCGO_VR_CaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
                _camera = go.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.stereoTargetEye = StereoTargetEyeMask.None;
                _camera.cullingMask = 1 << VRRig.Layer;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
                _camera.nearClipPlane = 0.02f;
                _camera.farClipPlane = 100f;
                _target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            }

            _camera.transform.SetPositionAndRotation(eye, rotation);
            _camera.fieldOfView = fov;
            _camera.targetTexture = _target;
            _camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            Texture2D image = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, name + ".png");
            File.WriteAllBytes(file, image.EncodeToPNG());
            Object.Destroy(image);
            Debug.Log("[DCGO VR] Captured " + file);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[DCGO VR] Capture failed: " + exception.Message);
        }
#endif
    }
}
#endif
