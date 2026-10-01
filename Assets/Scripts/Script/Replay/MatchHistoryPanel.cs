using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Home-screen match history list with watch / export / import / delete.
/// </summary>
public class MatchHistoryPanel : MonoBehaviour
{
    static MatchHistoryPanel _instance;

    GameObject _root;
    Text _statusText;
    Transform _listContent;
    readonly List<GameObject> _rows = new List<GameObject>();
    bool _open;

    public static MatchHistoryPanel EnsureExists()
    {
        if (_instance != null)
        {
            return _instance;
        }

        var go = new GameObject("MatchHistoryPanelHost");
        _instance = go.AddComponent<MatchHistoryPanel>();
        DontDestroyOnLoad(go);
        return _instance;
    }

    public static void ShowFromHome()
    {
        EnsureExists().Show();
    }

    public static void HideIfOpen()
    {
        if (_instance != null && _instance._open)
        {
            _instance.Hide();
        }
    }

    public void Show()
    {
        if (_root != null)
        {
            Destroy(_root);
            _root = null;
            _statusText = null;
            _listContent = null;
            _rows.Clear();
        }

        EnsureUi();
        _open = true;
        _root.SetActive(true);
        RefreshList();
    }

    public void Hide()
    {
        _open = false;
        if (_root != null)
        {
            _root.SetActive(false);
        }
    }

    void SetStatus(string msg)
    {
        if (_statusText != null)
        {
            _statusText.text = msg ?? "";
        }
    }

    void EnsureUi()
    {
        if (_root != null)
        {
            return;
        }

        Canvas canvas = null;
        if (Opening.instance != null && Opening.instance.canvasRect != null)
        {
            canvas = Opening.instance.canvasRect.GetComponentInParent<Canvas>();
        }

        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }

        Font font = Opening.instance != null && Opening.instance.VerText != null
            ? Opening.instance.VerText.font
            : Resources.GetBuiltinResource<Font>("Arial.ttf");

        _root = new GameObject("MatchHistoryPanel", typeof(RectTransform), typeof(Image));
        _root.transform.SetParent(canvas.transform, false);
        var rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = new Vector2(1000f, 720f);
        rootRt.anchoredPosition = Vector2.zero;
        _root.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.16f, 0.96f);

        CreateText(_root.transform, "Title", font, 28, TextAnchor.MiddleLeft,
            new Vector2(28f, -28f), new Vector2(400f, 40f)).text =
            LocalizeUtility.GetLocalizedString(EngMessage: "Match History", JpnMessage: "対戦履歴");

        CreateButton(_root.transform, "Import", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Import", JpnMessage: "インポート"),
            new Vector2(620f, -22f), new Vector2(160f, 48f), OnClickImport);

        CreateButton(_root.transform, "Close", font, "X",
            new Vector2(920f, -22f), new Vector2(52f, 48f), Hide);

        _statusText = CreateText(_root.transform, "Status", font, 18, TextAnchor.MiddleLeft,
            new Vector2(28f, -78f), new Vector2(900f, 32f));

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(_root.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.offsetMin = new Vector2(24f, 24f);
        scrollRt.offsetMax = new Vector2(-24f, -120f);
        scrollGo.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.1f, 0.9f);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        viewportRt.pivot = new Vector2(0.5f, 0.5f);

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 8f;
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = viewportRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        _listContent = contentGo.transform;

        _root.SetActive(false);
    }

    void RefreshList()
    {
        if (_listContent == null)
        {
            return;
        }

        foreach (var row in _rows)
        {
            if (row != null)
            {
                Destroy(row);
            }
        }

        _rows.Clear();

        Font font = Opening.instance != null && Opening.instance.VerText != null
            ? Opening.instance.VerText.font
            : Resources.GetBuiltinResource<Font>("Arial.ttf");

        var index = MatchHistoryStore.LoadIndex();
        if (index.entries == null || index.entries.Count == 0)
        {
            var empty = CreateText(_listContent, "Empty", font, 20, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(800f, 40f));
            empty.text = LocalizeUtility.GetLocalizedString(
                EngMessage: "No saved matches yet. Finish a match to record a replay.",
                JpnMessage: "保存された対戦がありません。対戦を終えるとリプレイが記録されます。");
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            _rows.Add(empty.gameObject);
            SetStatus(MatchHistoryStore.GetRootDir());
            return;
        }

        SetStatus($"{index.entries.Count} matches  ·  {MatchHistoryStore.GetRootDir()}");

        for (int i = 0; i < index.entries.Count; i++)
        {
            _rows.Add(CreateRow(index.entries[i], font));
        }
    }

    GameObject CreateRow(MatchHistoryEntry entry, Font font)
    {
        var row = new GameObject("Match_" + entry.id, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(_listContent, false);
        row.GetComponent<Image>().color = new Color(0.14f, 0.16f, 0.22f, 1f);
        row.GetComponent<LayoutElement>().preferredHeight = 72f;
        row.GetComponent<LayoutElement>().minHeight = 72f;

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 12, 12, 12);
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlHeight = true;
        hlg.childControlWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = false;

        string date = entry.timestampIso;
        if (DateTime.TryParse(entry.timestampIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            date = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        string summary = $"{date}  ·  {entry.mode}  ·  vs {entry.OpponentDisplayName()}  ·  {entry.ResultLabel()}  ·  T{entry.finalTurnCount}";

        var summaryGo = new GameObject("Summary", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        summaryGo.transform.SetParent(row.transform, false);
        var summaryText = summaryGo.GetComponent<Text>();
        summaryText.font = font;
        summaryText.fontSize = 18;
        summaryText.alignment = TextAnchor.MiddleLeft;
        summaryText.color = Color.white;
        summaryText.raycastTarget = false;
        summaryText.horizontalOverflow = HorizontalWrapMode.Overflow;
        summaryText.verticalOverflow = VerticalWrapMode.Truncate;
        summaryText.text = summary;
        var summaryLe = summaryGo.GetComponent<LayoutElement>();
        summaryLe.flexibleWidth = 1f;
        summaryLe.minWidth = 200f;

        string id = entry.id;
        CreateRowButton(row.transform, "Watch", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Watch", JpnMessage: "視聴"),
            110f, () => StartReplay(id));
        CreateRowButton(row.transform, "Export", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Export", JpnMessage: "書出"),
            110f, () => OnClickExport(id));
        CreateRowButton(row.transform, "Delete", font,
            LocalizeUtility.GetLocalizedString(EngMessage: "Del", JpnMessage: "削除"),
            90f, () => OnClickDelete(id));

        return row;
    }

    static Button CreateRowButton(Transform parent, string name, Font font, string label, float width, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.18f, 0.42f, 0.85f, 0.95f);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        le.preferredHeight = 48f;
        le.flexibleWidth = 0f;

        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var text = labelGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        text.raycastTarget = false;
        var lrt = text.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        return button;
    }

    void StartReplay(string id)
    {
        var data = MatchHistoryStore.LoadReplay(id);
        if (data == null || !data.IsValid())
        {
            SetStatus("Failed to load replay.");
            return;
        }

        Hide();
        ContinuousController.instance.StartCoroutine(StartReplayCoroutine(data, 0));
    }

    public static IEnumerator StartReplayCoroutine(ReplayData data, int seekTurn)
    {
        var cc = ContinuousController.instance;
        if (cc == null || Opening.instance == null)
        {
            yield break;
        }

        Time.timeScale = 1f;
        cc.isReplay = true;
        cc.isAI = true;
        cc.isRandomMatch = false;
        cc.isRanked = false;
        cc.isTournament = false;
        cc.isFriendDuel = false;
        cc.ActiveReplay = data;
        cc.ReplaySeekTurn = seekTurn;
        cc.ReturnToMatchHistoryAfterBattle = true;
        cc.BattleDeckData = new DeckData(data.viewerPlayerId == 0 ? data.player0DeckCode : data.player1DeckCode);

        ContinuousController.instance.StartCoroutine(Opening.instance.OpeningBGM.FadeOut(0.1f));
        yield return ContinuousController.instance.StartCoroutine(Opening.instance.LoadingObject.StartLoading("Now Loading"));

        foreach (Camera camera in Opening.instance.openingCameras)
        {
            camera.gameObject.SetActive(false);
        }

        Opening.instance.OffYesNoObjects();
        MatchHistoryPanel.HideIfOpen();
        FriendListPanel.HideIfOpen();

        yield return new WaitForSecondsRealtime(0.1f);
        var load = SceneManager.LoadSceneAsync(ContinuousController.BattleSceneName, LoadSceneMode.Additive);
        if (load != null)
        {
            yield return load;
        }

        float waitReady = 0f;
        while (GManager.instance == null && waitReady < 10f)
        {
            waitReady += Time.unscaledDeltaTime;
            yield return null;
        }

        if (Opening.instance.LoadingObject != null)
        {
            yield return ContinuousController.instance.StartCoroutine(Opening.instance.LoadingObject.EndLoading());
            Opening.instance.LoadingObject.gameObject.SetActive(false);
        }
    }

    void OnClickExport(string id)
    {
        if (ReplayFileBridge.UsesSystemShare)
        {
            string shared = MatchHistoryStore.ExportReplay(id, Application.temporaryCachePath);
            if (shared != null && ReplayFileBridge.Share(shared))
            {
                SetStatus(LocalizeUtility.GetLocalizedString(
                    EngMessage: "Choose where to send the replay.",
                    JpnMessage: "リプレイの送信先を選んでください。"));
            }
            else
            {
                SetStatus("Export failed.");
            }

            return;
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop))
        {
            desktop = MatchHistoryStore.GetRootDir();
        }

        string path = MatchHistoryStore.ExportReplay(id, desktop);
        if (path != null)
        {
            GUIUtility.systemCopyBuffer = path;
            SetStatus(LocalizeUtility.GetLocalizedString(
                EngMessage: $"Exported to Desktop (path copied):\n{path}",
                JpnMessage: $"デスクトップに書き出しました（パスをコピー済み）:\n{path}"));
        }
        else
        {
            SetStatus("Export failed.");
        }
    }

    void OnClickImport()
    {
        if (ReplayFileBridge.UsesSystemShare)
        {
            SetStatus(LocalizeUtility.GetLocalizedString(
                EngMessage: "Select a .dcgoreplay file.",
                JpnMessage: ".dcgoreplay ファイルを選択してください。"));
            ReplayFileBridge.Pick(OnMobileReplayPicked);
            return;
        }

        string path = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            SetStatus(LocalizeUtility.GetLocalizedString(
                EngMessage: "Copy a .dcgoreplay file path to the clipboard, then press Import.",
                JpnMessage: ".dcgoreplay のパスをクリップボードにコピーしてから Import を押してください。"));
            return;
        }

        ApplyImportedReplay(MatchHistoryStore.ImportReplay(path));
    }

    void OnMobileReplayPicked(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            SetStatus(LocalizeUtility.GetLocalizedString(
                EngMessage: "Import cancelled.",
                JpnMessage: "インポートをキャンセルしました。"));
            return;
        }

        ApplyImportedReplay(MatchHistoryStore.ImportReplay(path));
    }

    void ApplyImportedReplay(ReplayData data)
    {
        if (data != null)
        {
            SetStatus(LocalizeUtility.GetLocalizedString(
                EngMessage: $"Imported {data.id}",
                JpnMessage: $"インポートしました: {data.id}"));
            RefreshList();
        }
        else
        {
            SetStatus("Import failed (invalid file or version).");
        }
    }

    void OnClickDelete(string id)
    {
        if (MatchHistoryStore.DeleteReplay(id))
        {
            RefreshList();
            SetStatus("Deleted.");
        }
    }

    static Text CreateText(Transform parent, string name, Font font, int size, TextAnchor anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    static Button CreateButton(Transform parent, string name, Font font, string label, Vector2 pos, Vector2 sizeDelta, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        go.GetComponent<Image>().color = new Color(0.18f, 0.42f, 0.85f, 0.95f);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var text = labelGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        text.raycastTarget = false;
        var lrt = text.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        return button;
    }
}
