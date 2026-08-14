using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// PlayFab login (device CustomId) and optional Photon custom authentication token.
/// Guest CustomId is deterministic from package + device so reinstall on the same device
/// recovers the same PlayFab player (no random Guid). Existing PlayerPrefs ids are kept.
/// </summary>
public class PlayFabAuthService
{
    const string DeviceIdPrefKey = "RankedPlayFabCustomId";
    const string OfflineIdPrefKey = "RankedOfflinePlayerId";

    public string PlayFabId { get; private set; }
    public string PhotonAuthToken { get; private set; }
    public bool IsOfflineMode { get; private set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(PlayFabId);

    /// <summary>
    /// PlayFab LoginWithCustomID guest id. Cached in PlayerPrefs when present;
    /// after wipe/reinstall, recomputed from device (stable v2, no per-install Guid).
    /// </summary>
    public string GetOrCreateCustomId()
    {
        if (PlayerPrefs.HasKey(DeviceIdPrefKey))
        {
            string existing = PlayerPrefs.GetString(DeviceIdPrefKey);
            if (!string.IsNullOrEmpty(existing))
            {
                Debug.Log("[Ranked] Using cached PlayFab CustomId from PlayerPrefs");
                return existing;
            }
        }

        string id = BuildStableCustomId(onlineGuest: true);
        PlayerPrefs.SetString(DeviceIdPrefKey, id);
        PlayerPrefs.Save();
        Debug.Log("[Ranked] Computed stable PlayFab CustomId from package+device (reinstall-safe)");
        return id;
    }

    public IEnumerator EnsureLoggedIn(Action<bool, string> onComplete = null)
    {
        var config = PlayFabConfig.Current;

        if (!config.HasTitleId)
        {
            if (!config.allowOfflineFallback)
            {
                onComplete?.Invoke(false, "PlayFab TitleId is not configured.");
                yield break;
            }

            IsOfflineMode = true;
            PlayFabId = GetOrCreateOfflineId();
            PlayFabClientApi.ClearSession();
            Debug.Log("[Ranked] Offline PlayFab fallback active (set Resources/Ranked/PlayFabConfig.json titleId for production).");
            onComplete?.Invoke(true, null);
            yield break;
        }

        IsOfflineMode = false;

        if (PlayFabClientApi.IsLoggedIn && PlayFabClientApi.PlayFabId == PlayFabId && !string.IsNullOrEmpty(PlayFabId))
        {
            onComplete?.Invoke(true, null);
            yield break;
        }

        bool done = false;
        bool ok = false;
        string error = null;

        string customId = GetOrCreateCustomId();

        yield return PlayFabClientApi.LoginWithCustomId(
            config.titleId,
            customId,
            true,
            result =>
            {
                ok = result.success;
                error = result.errorMessage;
                if (ok)
                {
                    PlayFabId = PlayFabClientApi.PlayFabId;
                }

                done = true;
            });

        while (!done)
        {
            yield return null;
        }

        if (!ok)
        {
            string customIdPreview = customId != null && customId.Length > 12
                ? customId.Substring(0, 12) + "…"
                : customId;

            if (config.allowOfflineFallback)
            {
                string tip = "Check Game Manager → Settings → API Features: " +
                    "Allow Login with Custom ID + Allow client to create new users.";
                if (!string.IsNullOrEmpty(error) &&
                    (error.IndexOf("PlayerCreationDisabled", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     error.IndexOf("Player creations have been disabled", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    tip =
                        "PlayFab error PlayerCreationDisabled: enable \"Allow client to create new users\" " +
                        "(Settings → API Features) for this title, then restart Play Mode.";
                }

                Debug.LogError(
                    $"[Ranked] PlayFab login failed; using offline fallback. " +
                    $"titleId={config.titleId} customId={customIdPreview} error={error}. " +
                    "Ranked will NOT update PlayFab players/statistics until login succeeds. " +
                    tip);
                IsOfflineMode = true;
                PlayFabId = GetOrCreateOfflineId();
                onComplete?.Invoke(true, null);
                yield break;
            }

            onComplete?.Invoke(false, error ?? "PlayFab login failed");
            yield break;
        }

        Debug.Log($"[Ranked] PlayFab login OK. playFabId={PlayFabId} titleId={config.titleId}");

        // Best-effort display name sync
        if (!string.IsNullOrEmpty(ContinuousController.instance?.PlayerName))
        {
            bool nameDone = false;
            yield return PlayFabClientApi.UpdateDisplayName(
                config.titleId,
                ContinuousController.instance.PlayerName,
                _ => { nameDone = true; });
            while (!nameDone) yield return null;
        }

        if (config.usePhotonCustomAuth)
        {
            string photonAppId = PhotonNetwork.PhotonServerSettings?.AppSettings?.AppIdRealtime;
            if (!string.IsNullOrEmpty(photonAppId))
            {
                bool tokenDone = false;
                yield return PlayFabClientApi.GetPhotonAuthenticationToken(
                    config.titleId,
                    photonAppId,
                    (result, token) =>
                    {
                        if (result.success)
                        {
                            PhotonAuthToken = token;
                        }
                        else
                        {
                            Debug.LogWarning($"[Ranked] GetPhotonAuthenticationToken failed: {result.errorMessage}");
                        }

                        tokenDone = true;
                    });
                while (!tokenDone) yield return null;
            }
        }

        onComplete?.Invoke(true, null);
    }

    public void ApplyPhotonAuthValues()
    {
        var config = PlayFabConfig.Current;
        if (IsOfflineMode || !config.usePhotonCustomAuth || string.IsNullOrEmpty(PhotonAuthToken) || string.IsNullOrEmpty(PlayFabId))
        {
            PhotonNetwork.AuthValues = new AuthenticationValues(PlayFabId ?? ContinuousController.instance.PlayerName);
            return;
        }

        var auth = new AuthenticationValues
        {
            AuthType = CustomAuthenticationType.Custom,
            UserId = PlayFabId,
        };
        auth.AddAuthParameter("username", PlayFabId);
        auth.AddAuthParameter("token", PhotonAuthToken);
        PhotonNetwork.AuthValues = auth;
    }

    /// <summary>
    /// Offline mode id: same device-stable hash as online guest, with offline- prefix,
    /// so local Elo also recovers after reinstall on the same device.
    /// </summary>
    string GetOrCreateOfflineId()
    {
        if (PlayerPrefs.HasKey(OfflineIdPrefKey))
        {
            string id = PlayerPrefs.GetString(OfflineIdPrefKey);
            if (!string.IsNullOrEmpty(id))
            {
                Debug.Log("[Ranked] Using cached offline player id from PlayerPrefs");
                return id;
            }
        }

        string created = BuildStableCustomId(onlineGuest: false);
        PlayerPrefs.SetString(OfflineIdPrefKey, created);
        PlayerPrefs.Save();
        Debug.Log("[Ranked] Computed stable offline player id from package+device (reinstall-safe)");
        return created;
    }

    /// <summary>
    /// Deterministic guest id: package + device fingerprint, no per-install Guid.
    /// Format: dcgo-v2-/offline-v2- + 32 hex chars (SHA256 truncated).
    /// </summary>
    static string BuildStableCustomId(bool onlineGuest)
    {
        string package = Application.identifier;
        if (string.IsNullOrEmpty(package))
        {
            package = Application.productName ?? "dcgo";
        }

        string device = GetStableDeviceFingerprint();
        string material = $"{package}|{device}";
        string hash32 = Sha256HexPrefix(material, 32);
        string prefix = onlineGuest ? "dcgo-v2-" : "offline-v2-";
        return prefix + hash32;
    }

    static string GetStableDeviceFingerprint()
    {
        string duid = SystemInfo.deviceUniqueIdentifier;
        if (!string.IsNullOrEmpty(duid) &&
            duid != SystemInfo.unsupportedIdentifier)
        {
            return duid;
        }

#if UNITY_EDITOR
        // Editor has no reliable DUID — machine + device name is good enough for local testing.
        return $"editor|{SystemInfo.deviceName}|{Environment.MachineName}|{Environment.UserName}";
#else
        // Rare store-build path when DUID unsupported: degrade but still deterministic for the session hardware.
        return $"fallback|{SystemInfo.deviceModel}|{SystemInfo.processorType}|{SystemInfo.systemMemorySize}";
#endif
    }

    static string Sha256HexPrefix(string input, int hexCharCount)
    {
        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(hexCharCount);
            for (int i = 0; i < bytes.Length && sb.Length < hexCharCount; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }

            // Ensure exact length requested (already 64 max from full hash)
            if (sb.Length > hexCharCount)
            {
                return sb.ToString(0, hexCharCount);
            }

            return sb.ToString();
        }
    }
}
