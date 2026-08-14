# PlayFab Ranked Ladder Setup

## Title setup (required for online login)

Title id in client: `Resources/Ranked/PlayFabConfig.json` → `titleId` (e.g. `11E74D`).

In **Game Manager → Settings → API Features**, enable:

- **Allow Login with Custom ID** (client `LoginWithCustomID`)
- **Allow client to create new users** (or any equivalent “player creation” / “create accounts with Custom ID” option)

If log shows `PlayerCreationDisabled` / `"Player creations have been disabled for this API"`:

1. Open title **11E74D** in PlayFab Game Manager  
2. **Settings → API Features**  
3. **Enable** client user creation (new guest Custom IDs cannot sign in while this is off)  
4. Re-enter Play Mode — you should see `[Ranked] PlayFab login OK` and a new player under **Players**

Without these, the client gets HTTP 400 and falls back to **offline ranked** (local MMR only; Players stay empty).

After a working login: **Players** shows new accounts; statistics `RankedMMR` / `RankedWins` / `RankedLosses` update after match report.

## Critical: redeploy Cloud Script after client fixes

Re-upload `RankedCloudScript.js` in:
**Live Ops → Automation → Cloud Script → new revision → make active.**

## Why stats never updated (fixed in client)

`EndGame` used to start the ranked report on `ContinuousController`, then immediately call `StopAllCoroutines()` on it — so PlayFab was never called. Reports now run on `RankedServices` and are not stopped.

Cloud Script now **settles on the first report** and calls `UpdatePlayerStatistics` immediately (second report is idempotent).

## Classic player statistics

Cloud Script uses classic APIs:

- `server.UpdatePlayerStatistics`
- `server.GetPlayerStatistics`

Exact names: `RankedMMR`, `RankedWins`, `RankedLosses`

Check **Players → [player] → Statistics**, not only the entity leaderboard screens.

## Guest identity across reinstalls

PlayFab login uses `LoginWithCustomID`. The guest CustomId is **stable on the same device**:

- Format: `dcgo-v2-` + SHA256(package + device fingerprint) truncated (no random `Guid`)
- PlayerPrefs caches the id; after uninstall/reinstall it is **recomputed the same way** when prefs are empty
- Existing installs keep their PlayerPrefs id until data is cleared (no mid-update migration)

**Survives (typical):** reinstall of the same Android package on the same user/device (Unity `deviceUniqueIdentifier` / app-scoped device id).

**Does not survive:** new phone, factory reset, some OEM privacy/device-id wipes, iOS when all apps from the same vendor are removed (IDFV reset), different Android user profile. Multi-device MMR needs real account linking (Google/Apple/etc.) — not implemented.

Offline ranked ids use the same device-stable hash (`offline-v2-...`).

## Verify

1. Finish a ranked match.
2. Console: `[Ranked] Reporting...` then `[Ranked] Stats updated...` (or an error).
3. Game Manager → that player → `RankedMMR` changed.
4. Uninstall + reinstall: same PlayFabId and MMR on that device.

## Offline

If log shows `offline=true` or console error about offline fallback:

- Only local PlayerPrefs MMR applies — PlayFab Players/stats do **not** update.
- In-game UI shows **Ranked (Offline): …** (player info + ranked queue) and **Offline rank** on the result screen.
- Fix: enable Custom ID login, confirm `titleId`, re-enter play mode; success log is `[Ranked] PlayFab login OK`.

Failed HTTP calls now log PlayFab `error` / `errorMessage` / `errorDetails` (not only `HTTP 400 Bad Request`).
