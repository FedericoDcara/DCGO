using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using WebP;

public class StreamingAssetsUtility
{
    static bool _seededBundledTextures;
    static bool _seededCosmeticsTextures;

    // === DCGO-CUSTOM:playarea begin ===
    public const string PlayMatsFolder = "PlayMats";
    public const string SleevesMainFolder = "Sleeves/Main";
    public const string SleevesEggFolder = "Sleeves/Egg";
    public const string ProfileIconsFolder = "ProfileIcons";

    [Serializable]
    class TextureCatalogJson
    {
        public List<string> files = new List<string>();
    }
    // === DCGO-CUSTOM:playarea end ===

    // === DCGO-CUSTOM:matchmusic begin ===
    public const string MatchMusicFolder = "MatchMusic";
    public const string MatchMusicTrackFull = "music1";
    public const string MatchMusicTrackHalf = "music2";
    public const string MatchMusicTrackCritical = "music3";
    static bool _seededMatchMusic;

    /// <summary>
    /// One pack folder under MatchMusic/ containing music1, music2, music3.
    /// Paths are always {name}/music1|music2|music3 (extension resolved at load).
    /// </summary>
    [Serializable]
    public class MatchMusicGroupJson
    {
        public string name = "pack1";

        public string FullRelativePath => BuildTrackPath(name, MatchMusicTrackFull);
        public string HalfRelativePath => BuildTrackPath(name, MatchMusicTrackHalf);
        public string CriticalRelativePath => BuildTrackPath(name, MatchMusicTrackCritical);

        public static string BuildTrackPath(string packName, string trackBaseName)
        {
            if (string.IsNullOrEmpty(packName))
                packName = "pack1";
            packName = packName.Replace("\\", "/").Trim().Trim('/');
            return $"{packName}/{trackBaseName}";
        }
    }

    [Serializable]
    public class MatchMusicCatalogJson
    {
        /// <summary>Pack folder names under MatchMusic/ (e.g. pack1, pack2).</summary>
        public string[] packs;

        // Optional object form: { "groups": [ { "name": "pack1" } ] }
        public MatchMusicGroupJson[] groups;

        /// <summary>
        /// Returns every pack group. Standard layout: MatchMusic/{pack}/music1|2|3.
        /// </summary>
        public List<MatchMusicGroupJson> GetGroups()
        {
            var result = new List<MatchMusicGroupJson>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddPack(string packName)
            {
                if (string.IsNullOrEmpty(packName))
                    return;

                packName = packName.Replace("\\", "/").Trim().Trim('/');
                if (string.IsNullOrEmpty(packName) || packName.Contains(".."))
                    return;
                // Only allow a single folder segment (no nested paths in pack name).
                if (packName.Contains("/"))
                    packName = packName.Split('/')[0];

                if (!seen.Add(packName))
                    return;

                result.Add(new MatchMusicGroupJson { name = packName });
            }

            if (packs != null)
            {
                foreach (string pack in packs)
                    AddPack(pack);
            }

            if (groups != null)
            {
                foreach (MatchMusicGroupJson g in groups)
                {
                    if (g != null)
                        AddPack(g.name);
                }
            }

            if (result.Count == 0)
                AddPack("pack1");

            return result;
        }

        public MatchMusicGroupJson PickRandomGroup()
        {
            List<MatchMusicGroupJson> list = GetGroups();
            if (list.Count == 1)
                return list[0];
            return list[UnityEngine.Random.Range(0, list.Count)];
        }
    }
    // === DCGO-CUSTOM:matchmusic end ===

    public static async Task<byte[]> ReadFile(string path)
    {
        using (FileStream fileStream = new FileStream(
            path, FileMode.Open, FileAccess.Read))
        {
            var resultBytes = new byte[fileStream.Length];
            await fileStream.ReadAsync(resultBytes, 0, (int)fileStream.Length);
            return resultBytes;
        }
    }

    /// <summary>
    /// Reads bytes from a normal filesystem path, or from StreamingAssets on Android (APK / jar).
    /// </summary>
    public static async Task<byte[]> ReadBytesFlexible(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (File.Exists(path))
            return await ReadFile(path);

#if UNITY_ANDROID && !UNITY_EDITOR
        // APK StreamingAssets are not normal files — must use UnityWebRequest.
        if (path.Contains(Application.streamingAssetsPath.Replace("\\", "/")) ||
            path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadStreamingAssetsBytes(path);
        }
#endif
        return null;
    }

    static async Task<byte[]> ReadStreamingAssetsBytes(string urlOrPath)
    {
        string url = urlOrPath.Replace("\\", "/");
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
                return null;

            return req.downloadHandler.data;
        }
    }

    #region image load
    public static Texture2D BinaryToTexture(byte[] bytes)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.LoadImage(bytes);
        return texture;
    }

    public static async Task<Sprite> GetSprite(string fileName, bool isCard = false, bool isLauncher = false)
    {
        await EnsureBundledTexturesSeeded();

        if (isCard)
        {
            if (fileName.Contains("-token"))
            {
                return await GetTokenImageData(Path.Combine(GetStreamingAssetPath("Textures", isLauncher), $"Card/{fileName}.png").Replace("\\", "/"));
            }
            else
            {
                string path = Path.Combine(GetStreamingAssetPath("Textures", isLauncher), $"Card/{fileName}.webp").Replace("\\", "/");

                if (!File.Exists(path))
                {
                    return await GetCardImageData(fileName, path);
                }
                else
                {
                    return await GetCardImageDataLocal(path);
                }
            }
        }
        else
        {
            return await GetSpriteImage(fileName, isLauncher);
        }
    }

    public static async Task<Sprite> GetSpriteImage(string fileName, bool isLauncher = false)
    {
        await EnsureBundledTexturesSeeded();

        // Prefer writable cache (persistent on Android / Assets layout on PC).
        string path = Path.Combine(GetStreamingAssetPath("Textures", isLauncher), $"{fileName}.jpg").Replace("\\", "/");
        if (!File.Exists(path))
            path = Path.Combine(GetStreamingAssetPath("Textures", isLauncher), $"{fileName}.png").Replace("\\", "/");

        byte[] imageBuff = null;
        if (File.Exists(path))
            imageBuff = await ReadFile(path);

        // PC standalone looks at Builds/Assets/Textures; Unity actually copies UI
        // textures into DCGO_Data/StreamingAssets. Fall back so a player folder works alone.
        if (imageBuff == null)
        {
            string saJpg = Path.Combine(Application.streamingAssetsPath, "Textures", $"{fileName}.jpg").Replace("\\", "/");
            string saPng = Path.Combine(Application.streamingAssetsPath, "Textures", $"{fileName}.png").Replace("\\", "/");
#if UNITY_ANDROID && !UNITY_EDITOR
            imageBuff = await ReadStreamingAssetsBytes(saJpg);
            if (imageBuff == null)
                imageBuff = await ReadStreamingAssetsBytes(saPng);
#else
            if (File.Exists(saJpg))
                imageBuff = await ReadFile(saJpg);
            else if (File.Exists(saPng))
                imageBuff = await ReadFile(saPng);
#endif
        }

        if (imageBuff == null)
            return null;

        Texture2D tex = BinaryToTexture(imageBuff);
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
    }

    public static async Task<Sprite> GetTokenImageData(string path)
    {
        byte[] imageBuff = await ReadBytesFlexible(path);
        if (imageBuff == null)
            return null;

        Texture2D tex = BinaryToTexture(imageBuff);
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
    }

    public static async Task<Sprite> GetCardImageDataLocal(string path)
    {
        if (File.Exists(path))
        {
            Debug.Log($"File Exists Locally: {path}");
            byte[] imageBuff = await ReadFile(path);
            Debug.Log($"Grabbing image bytes: {imageBuff}");
            Texture2D texture = Texture2DExt.CreateTexture2DFromWebP(imageBuff, lMipmaps: true, lLinear: false, lError: out WebP.Error lError);
            Debug.Log($"Converting WebP to Texture2D: {texture}");
            if (lError == WebP.Error.Success)
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);

                return sprite;
            }
            else
            {
                Debug.Log(lError.ToString());
            }
        }

        return null;
    }

    public static async Task<Sprite> GetCardImageData(string fileName, string filePath)
    {
        Sprite sprite;

        // Attempt to get the card image from repo
        sprite = await HandleCardImage(fileName, filePath);

        if (sprite != null) return sprite;
        else
        {

            // Attempt to get the card image from repo, this time with the sample suffix
            sprite = await HandleCardImage(fileName, filePath, isSample: true);

            if (sprite != null) return sprite;
            return null;
        }

    }

    public static async Task<Sprite> HandleCardImage(string fileName, string filePath, bool isSample = false)
    {
        string urlPath = $"https://raw.githubusercontent.com/TakaOtaku/Digimon-Card-App/main/src/assets/images/cards/{fileName}";
        if (isSample) urlPath += $"-Sample.webp";
        else urlPath += $".webp";

        UnityWebRequest webReq_CardImage = UnityWebRequest.Get(urlPath);
        UnityWebRequestAsyncOperation operation = webReq_CardImage.SendWebRequest();

        while (!operation.isDone)
        {
            await Task.Yield();
        }

        Debug.Log($"WebRequest isDone: {fileName}");
        if (webReq_CardImage.result == UnityWebRequest.Result.ConnectionError)
            return null;
        else if (webReq_CardImage.result == UnityWebRequest.Result.ProtocolError)
            return null;
        else
        {
            Debug.Log($"WebRequest Successful: Checking local file - {File.Exists(filePath)}");
            if (!File.Exists(filePath))
            {
                EnsureParentDirectory(filePath);
                File.WriteAllBytes(filePath, webReq_CardImage.downloadHandler.data);
            }

            Texture2D texture = Texture2DExt.CreateTexture2DFromWebP(webReq_CardImage.downloadHandler.data, lMipmaps: true, lLinear: false, lError: out WebP.Error lError);

            if (lError == WebP.Error.Success)
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
                return sprite;
            }
            else Debug.Log($"Failed to convert: {lError.ToString()}");
            return null;
        }
    }

    #endregion

    public static bool IsCardExists(CEntity_Base cEntity_Base)
    {
        string path = Path.Combine(GetStreamingAssetPath("Textures", false), $"Card/{cEntity_Base.CardSpriteName}.webp").Replace("\\", "/");

        if (cEntity_Base.CardSpriteName.Contains("token"))
            path = Path.Combine(GetStreamingAssetPath("Textures", false), $"Card/{cEntity_Base.CardSpriteName}.png").Replace("\\", "/");

        return File.Exists(path);
    }

    #region text
    public static string GetText(string fileName)
    {
        string path = Path.Combine(GetStreamingAssetPath("", false), $"{fileName}.txt").Replace("\\", "/");

        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        return "";
    }
    #endregion

    /// <summary>
    /// Returns a writable root for decks/textures.
    /// Editor and standalone use the historic Assets-relative layout; Android/iOS use persistentDataPath.
    /// </summary>
    public static string GetStreamingAssetPath(string subPath, bool isLauncher)
    {
        if (UsePersistentDataRoot())
        {
            string path = Application.persistentDataPath;
            if (!string.IsNullOrEmpty(subPath))
                path = Path.Combine(path, subPath);

            path = path.Replace("\\", "/");
            EnsureDirectoryExists(path);
            return path;
        }

        if (isLauncher)
        {
            string path = Application.streamingAssetsPath;

            path = GetOneUpperDirectoryPath(path);

            path = Path.Combine(path, $"Assets/{subPath}").Replace("\\", "/");

            return path;
        }

        else
        {
            string path = Application.streamingAssetsPath;

            path = GetOneUpperDirectoryPath(path);

            path = GetOneUpperDirectoryPath(path);

            path = Path.Combine(path, $"Assets/{subPath}").Replace("\\", "/");

            return path;
        }
    }

    static bool UsePersistentDataRoot()
    {
#if UNITY_EDITOR
        return false;
#elif UNITY_ANDROID || UNITY_IOS
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// Copy bundled StreamingAssets/Textures (UI, mats, card backs) into persistentDataPath once
    /// so File.Exists-based loaders work on Android.
    /// </summary>
    public static async Task EnsureBundledTexturesSeeded()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        await Task.Yield();
        // === DCGO-CUSTOM:playarea begin ===
        await EnsureCosmeticsTexturesSeeded();
        // === DCGO-CUSTOM:playarea end ===
        return;
#else
        if (_seededBundledTextures)
        {
            // === DCGO-CUSTOM:playarea begin ===
            await EnsureCosmeticsTexturesSeeded();
            // === DCGO-CUSTOM:playarea end ===
            return;
        }

        _seededBundledTextures = true;

        string marker = Path.Combine(Application.persistentDataPath, "Textures", ".seeded_ui_v1");
        if (File.Exists(marker))
        {
            // === DCGO-CUSTOM:playarea begin ===
            await EnsureCosmeticsTexturesSeeded();
            // === DCGO-CUSTOM:playarea end ===
            return;
        }

        string[] relativeFiles = new[]
        {
            "Textures/Background_home.png",
            "Textures/Background_battle.png",
            "Textures/card_back_main.png",
            "Textures/card_back_sub.png",
            "Textures/PlayMat_You.png",
            "Textures/PlayMat_Opponent.png",
            "Textures/SecurityIcon_You.png",
            "Textures/SecurityIcon_Opponent.png",
            "Textures/CurrentPhaseBar_You.png",
            "Textures/CurrentPhaseBar_Opponent.png",
        };

        foreach (string relative in relativeFiles)
        {
            string dest = Path.Combine(Application.persistentDataPath, relative).Replace("\\", "/");
            if (File.Exists(dest))
                continue;

            string src = Path.Combine(Application.streamingAssetsPath, relative).Replace("\\", "/");
            byte[] data = await ReadStreamingAssetsBytes(src);
            if (data == null || data.Length == 0)
                continue;

            EnsureParentDirectory(dest);
            File.WriteAllBytes(dest, data);
            Debug.Log($"[StreamingAssetsUtility] Seeded {relative}");
        }

        // Seed UI/ and Backgrounds/ folders if present in the APK.
        await SeedDirectoryListingFallback();

        EnsureParentDirectory(marker);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));

        // === DCGO-CUSTOM:playarea begin ===
        await EnsureCosmeticsTexturesSeeded();
        // === DCGO-CUSTOM:playarea end ===
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static async Task SeedDirectoryListingFallback()
    {
        // StreamingAssets on Android has no directory listing API.
        // Known Backgrounds / UI names can be added here if needed later.
        await Task.Yield();
    }
#endif

    // === DCGO-CUSTOM:playarea begin ===
    /// <summary>
    /// Seed PlayMats / Sleeves catalogs (and catalog-listed images) into persistent storage on Android.
    /// Uses a separate marker so existing installs pick up cosmetics without re-seeding UI mats.
    /// </summary>
    public static async Task EnsureCosmeticsTexturesSeeded()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        // Ensure writable folders exist on PC/editor for drop-in files.
        EnsureDirectoryExists(Path.Combine(GetStreamingAssetPath("Textures", false), PlayMatsFolder).Replace("\\", "/"));
        EnsureDirectoryExists(Path.Combine(GetStreamingAssetPath("Textures", false), SleevesMainFolder).Replace("\\", "/"));
        EnsureDirectoryExists(Path.Combine(GetStreamingAssetPath("Textures", false), SleevesEggFolder).Replace("\\", "/"));
        EnsureDirectoryExists(Path.Combine(GetStreamingAssetPath("Textures", false), ProfileIconsFolder).Replace("\\", "/"));
        await Task.Yield();
        return;
#else
        if (_seededCosmeticsTextures)
            return;

        _seededCosmeticsTextures = true;

        string marker = Path.Combine(Application.persistentDataPath, "Textures", ".seeded_cosmetics_v2");
        if (File.Exists(marker))
            return;

        string[] catalogFolders = { PlayMatsFolder, SleevesMainFolder, SleevesEggFolder, ProfileIconsFolder };
        foreach (string folder in catalogFolders)
        {
            string catalogRelative = $"Textures/{folder}/catalog.json";
            await SeedSingleBundledFile(catalogRelative);

            List<string> catalogNames = await ReadCatalogFileNames(folder);
            foreach (string name in catalogNames)
            {
                string baseName = StripImageExtension(name);
                if (string.IsNullOrEmpty(baseName))
                    continue;

                await SeedSingleBundledFile($"Textures/{folder}/{baseName}.png");
                await SeedSingleBundledFile($"Textures/{folder}/{baseName}.jpg");
            }
        }

        EnsureParentDirectory(marker);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static async Task SeedSingleBundledFile(string relative)
    {
        string dest = Path.Combine(Application.persistentDataPath, relative).Replace("\\", "/");
        if (File.Exists(dest))
            return;

        string src = Path.Combine(Application.streamingAssetsPath, relative).Replace("\\", "/");
        byte[] data = await ReadStreamingAssetsBytes(src);
        if (data == null || data.Length == 0)
            return;

        EnsureParentDirectory(dest);
        File.WriteAllBytes(dest, data);
        Debug.Log($"[StreamingAssetsUtility] Seeded cosmetics {relative}");
    }
#endif

    /// <summary>
    /// Lists cosmetic texture basenames relative to Textures/ (no extension), including a default root name.
    /// </summary>
    public static async Task<List<string>> ListCosmeticTextureNames(string subfolder, string defaultRootName)
    {
        await EnsureBundledTexturesSeeded();
        await EnsureCosmeticsTexturesSeeded();

        var names = new List<string>();
        var seenFull = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;

            name = StripImageExtension(name.Replace("\\", "/").Trim());
            if (string.IsNullOrEmpty(name))
                return;

            string baseName = name;
            int slash = baseName.LastIndexOf('/');
            if (slash >= 0 && slash < baseName.Length - 1)
                baseName = baseName.Substring(slash + 1);

            // Skip path duplicates and basename duplicates (e.g. PlayMat_You vs PlayMats/PlayMat_You).
            if (!seenFull.Add(name) || !seenBase.Add(baseName))
                return;

            names.Add(name);
        }

        if (!string.IsNullOrEmpty(defaultRootName))
            Add(defaultRootName);

        string writableDir = Path.Combine(GetStreamingAssetPath("Textures", false), subfolder).Replace("\\", "/");
        if (Directory.Exists(writableDir))
        {
            foreach (string file in Directory.GetFiles(writableDir))
            {
                string ext = Path.GetExtension(file);
                if (!IsImageExtension(ext))
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(fileName, "catalog", StringComparison.OrdinalIgnoreCase))
                    continue;

                Add($"{subfolder}/{fileName}");
            }
        }

#if !UNITY_ANDROID || UNITY_EDITOR
        string saDir = Path.Combine(Application.streamingAssetsPath, "Textures", subfolder).Replace("\\", "/");
        if (Directory.Exists(saDir) && !string.Equals(saDir, writableDir, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string file in Directory.GetFiles(saDir))
            {
                string ext = Path.GetExtension(file);
                if (!IsImageExtension(ext))
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(fileName, "catalog", StringComparison.OrdinalIgnoreCase))
                    continue;

                Add($"{subfolder}/{fileName}");
            }
        }
#endif

        foreach (string catalogName in await ReadCatalogFileNames(subfolder))
        {
            string baseName = StripImageExtension(catalogName);
            if (string.IsNullOrEmpty(baseName))
                continue;

            if (baseName.Contains("/"))
                Add(baseName);
            else
                Add($"{subfolder}/{baseName}");
        }

        return names;
    }

    static async Task<List<string>> ReadCatalogFileNames(string subfolder)
    {
        var result = new List<string>();
        string relative = Path.Combine("Textures", subfolder, "catalog.json").Replace("\\", "/");

        string writable = Path.Combine(GetStreamingAssetPath("Textures", false), subfolder, "catalog.json").Replace("\\", "/");
        byte[] data = null;
        if (File.Exists(writable))
            data = await ReadFile(writable);

        if (data == null)
        {
            string sa = Path.Combine(Application.streamingAssetsPath, relative).Replace("\\", "/");
#if UNITY_ANDROID && !UNITY_EDITOR
            data = await ReadStreamingAssetsBytes(sa);
#else
            if (File.Exists(sa))
                data = await ReadFile(sa);
#endif
        }

        if (data == null || data.Length == 0)
            return result;

        try
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            TextureCatalogJson catalog = JsonUtility.FromJson<TextureCatalogJson>(json);
            if (catalog?.files != null)
            {
                foreach (string file in catalog.files)
                {
                    if (!string.IsNullOrEmpty(file))
                        result.Add(file);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StreamingAssetsUtility] Failed to parse {relative}: {e.Message}");
        }

        return result;
    }

    static bool IsImageExtension(string ext)
    {
        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    static string StripImageExtension(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        string ext = Path.GetExtension(name);
        if (IsImageExtension(ext))
            return name.Substring(0, name.Length - ext.Length).Replace("\\", "/");

        return name.Replace("\\", "/");
    }
    // === DCGO-CUSTOM:playarea end ===

    public static void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    // === DCGO-CUSTOM:matchmusic begin ===
    /// <summary>
    /// Seed MatchMusic catalog + listed clips into persistent storage on Android.
    /// Ensures the drop-in folder exists on PC/editor.
    /// </summary>
    public static async Task EnsureMatchMusicSeeded()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        EnsureDirectoryExists(Path.Combine(GetStreamingAssetPath("Audio", false), MatchMusicFolder).Replace("\\", "/"));
        string saDir = Path.Combine(Application.streamingAssetsPath, "Audio", MatchMusicFolder).Replace("\\", "/");
        EnsureDirectoryExists(saDir);
        await Task.Yield();
        return;
#else
        if (_seededMatchMusic)
            return;

        _seededMatchMusic = true;

        string marker = Path.Combine(Application.persistentDataPath, "Audio", ".seeded_matchmusic_v3");
        if (File.Exists(marker))
            return;

        await SeedSingleBundledFile($"Audio/{MatchMusicFolder}/catalog.json");

        // Read catalog bytes directly to avoid re-entering EnsureMatchMusicSeeded.
        MatchMusicCatalogJson catalog = new MatchMusicCatalogJson();
        string catalogWritable = Path.Combine(Application.persistentDataPath, "Audio", MatchMusicFolder, "catalog.json").Replace("\\", "/");
        byte[] catalogData = null;
        if (File.Exists(catalogWritable))
            catalogData = await ReadFile(catalogWritable);
        if (catalogData == null || catalogData.Length == 0)
        {
            string saCatalog = Path.Combine(Application.streamingAssetsPath, "Audio", MatchMusicFolder, "catalog.json").Replace("\\", "/");
            catalogData = await ReadStreamingAssetsBytes(saCatalog);
        }
        if (catalogData != null && catalogData.Length > 0)
        {
            try
            {
                catalog = JsonUtility.FromJson<MatchMusicCatalogJson>(System.Text.Encoding.UTF8.GetString(catalogData))
                    ?? new MatchMusicCatalogJson();
            }
            catch { /* keep defaults */ }
        }

        foreach (MatchMusicGroupJson group in catalog.GetGroups())
        {
            // Seed all common extensions for music1/2/3 under the pack folder.
            await SeedMatchMusicTrack(group.name, MatchMusicTrackFull);
            await SeedMatchMusicTrack(group.name, MatchMusicTrackHalf);
            await SeedMatchMusicTrack(group.name, MatchMusicTrackCritical);
        }

        EnsureParentDirectory(marker);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static async Task SeedMatchMusicTrack(string packName, string trackBaseName)
    {
        string relativeBase = MatchMusicGroupJson.BuildTrackPath(packName, trackBaseName);
        await SeedMatchMusicClip(relativeBase + ".ogg");
        await SeedMatchMusicClip(relativeBase + ".mp3");
        await SeedMatchMusicClip(relativeBase + ".wav");
    }

    static async Task SeedMatchMusicClip(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;

        string relative = NormalizeMatchMusicRelativePath(fileName);
        if (string.IsNullOrEmpty(relative))
            return;

        await SeedSingleBundledFile($"Audio/{MatchMusicFolder}/{relative}");
    }
#endif

    public static async Task<MatchMusicCatalogJson> ReadMatchMusicCatalog()
    {
        await EnsureMatchMusicSeeded();

        string relative = Path.Combine("Audio", MatchMusicFolder, "catalog.json").Replace("\\", "/");
        string writable = Path.Combine(GetStreamingAssetPath("Audio", false), MatchMusicFolder, "catalog.json").Replace("\\", "/");
        byte[] data = null;
        if (File.Exists(writable))
            data = await ReadFile(writable);

        if (data == null)
        {
            string sa = Path.Combine(Application.streamingAssetsPath, relative).Replace("\\", "/");
#if UNITY_ANDROID && !UNITY_EDITOR
            data = await ReadStreamingAssetsBytes(sa);
#else
            if (File.Exists(sa))
                data = await ReadFile(sa);
#endif
        }

        if (data == null || data.Length == 0)
            return new MatchMusicCatalogJson();

        try
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            MatchMusicCatalogJson catalog = JsonUtility.FromJson<MatchMusicCatalogJson>(json);
            return catalog ?? new MatchMusicCatalogJson();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StreamingAssetsUtility] Failed to parse MatchMusic catalog: {e.Message}");
            return new MatchMusicCatalogJson();
        }
    }

    /// <summary>
    /// Loads a MatchMusic clip by catalog filename (flat or subfolder relative path).
    /// Writable override first, then StreamingAssets. Uses streamed AudioClips.
    /// </summary>
    public static async Task<AudioClip> LoadMatchMusicClip(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        await EnsureMatchMusicSeeded();

        string relative = NormalizeMatchMusicRelativePath(fileName);
        if (string.IsNullOrEmpty(relative))
            return null;

        string writable = Path.Combine(GetStreamingAssetPath("Audio", false), MatchMusicFolder, relative).Replace("\\", "/");
        if (File.Exists(writable))
        {
            AudioClip clip = await LoadAudioClipFromPath(writable, streamAudio: true);
            if (clip != null)
                return clip;
        }

        // Alternate extensions in the same folder as the catalog entry.
        string relativeDir = Path.GetDirectoryName(relative)?.Replace("\\", "/") ?? "";
        string baseName = Path.GetFileNameWithoutExtension(relative);
        string[] extensions = { ".ogg", ".mp3", ".wav" };
        string writableDir = string.IsNullOrEmpty(relativeDir)
            ? Path.Combine(GetStreamingAssetPath("Audio", false), MatchMusicFolder).Replace("\\", "/")
            : Path.Combine(GetStreamingAssetPath("Audio", false), MatchMusicFolder, relativeDir).Replace("\\", "/");

        if (Directory.Exists(writableDir))
        {
            foreach (string ext in extensions)
            {
                string alt = Path.Combine(writableDir, baseName + ext).Replace("\\", "/");
                if (File.Exists(alt))
                {
                    AudioClip clip = await LoadAudioClipFromPath(alt, streamAudio: true);
                    if (clip != null)
                        return clip;
                }
            }
        }

        string sa = Path.Combine(Application.streamingAssetsPath, "Audio", MatchMusicFolder, relative).Replace("\\", "/");
#if UNITY_ANDROID && !UNITY_EDITOR
        {
            AudioClip clip = await LoadAudioClipFromPath(sa, streamAudio: true);
            if (clip != null)
                return clip;
        }
#else
        if (File.Exists(sa))
        {
            AudioClip clip = await LoadAudioClipFromPath(sa, streamAudio: true);
            if (clip != null)
                return clip;
        }

        string saDir = string.IsNullOrEmpty(relativeDir)
            ? Path.Combine(Application.streamingAssetsPath, "Audio", MatchMusicFolder).Replace("\\", "/")
            : Path.Combine(Application.streamingAssetsPath, "Audio", MatchMusicFolder, relativeDir).Replace("\\", "/");

        if (Directory.Exists(saDir))
        {
            foreach (string ext in extensions)
            {
                string alt = Path.Combine(saDir, baseName + ext).Replace("\\", "/");
                if (File.Exists(alt))
                {
                    AudioClip clip = await LoadAudioClipFromPath(alt, streamAudio: true);
                    if (clip != null)
                        return clip;
                }
            }
        }
#endif
        return null;
    }

    /// <summary>Normalizes a catalog path to something under MatchMusic/ (no .. traversal).</summary>
    public static string NormalizeMatchMusicRelativePath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        string relative = fileName.Replace("\\", "/").Trim().TrimStart('/');
        if (relative.StartsWith("Audio/MatchMusic/", StringComparison.OrdinalIgnoreCase))
            relative = relative.Substring("Audio/MatchMusic/".Length);
        if (relative.StartsWith("MatchMusic/", StringComparison.OrdinalIgnoreCase))
            relative = relative.Substring("MatchMusic/".Length);

        if (relative.Contains(".."))
            return null;

        return relative;
    }

    static async Task<AudioClip> LoadAudioClipFromPath(string path, bool streamAudio = false)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        string url = path.Replace("\\", "/");
        if (!url.Contains("://"))
        {
            try
            {
                url = new Uri(path).AbsoluteUri;
            }
            catch
            {
                url = "file:///" + url.TrimStart('/');
            }
        }

        AudioType audioType = GuessAudioType(path);
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
        {
            if (req.downloadHandler is DownloadHandlerAudioClip dh)
            {
                // Stream from disk instead of decoding the whole file into RAM up front.
                dh.streamAudio = streamAudio;
                dh.compressed = true;
            }

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[StreamingAssetsUtility] Failed to load audio {path}: {req.error}");
                return null;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip != null)
                clip.name = Path.GetFileNameWithoutExtension(path);
            return clip;
        }
    }

    static AudioType GuessAudioType(string path)
    {
        string ext = Path.GetExtension(path);
        if (string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase))
            return AudioType.MPEG;
        if (string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
            return AudioType.WAV;
        if (string.Equals(ext, ".ogg", StringComparison.OrdinalIgnoreCase))
            return AudioType.OGGVORBIS;
        return AudioType.OGGVORBIS;
    }
    // === DCGO-CUSTOM:matchmusic end ===

    static void EnsureParentDirectory(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        string directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            EnsureDirectoryExists(directory);
    }

    static string GetOneUpperDirectoryPath(string path)
    {
        if (String.IsNullOrEmpty(path)) return "";
        path = path.Replace("\\", "/");
        if (!path.Contains("/")) return path;

        path = path.Substring(0, path.LastIndexOf("/") + 1);

        if (path.Length >= 1)
        {
            if (path[path.Length - 1] == '/')
            {
                path = path.Substring(0, path.LastIndexOf("/"));
            }
        }

        return path.Substring(0, path.LastIndexOf("/") + 1);
    }
}
