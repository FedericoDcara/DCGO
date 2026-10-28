#if DCGO_VR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// === DCGO-CUSTOM:vr ===
// The pages of the native VR menu: how each flat screen is recognised and
// what VR buttons / cards it gets. See VRMenu.cs for layout and rendering.
public partial class VRMenu
{
    // Cached flat objects (found once per scene).
    YesNoObject[] _dialogs = new YesNoObject[0];
    EnterRoom _enterRoom;
    Opening _cachedFor;
    readonly List<GameObject> _windows = new List<GameObject>();
    bool _editorFilters;

    // Flat windows shown through the generic VR mirror (buttons, toggles, sliders, dropdowns, text fields).
    static readonly Dictionary<string, string> WindowTitles = new Dictionary<string, string>
    {
        { "OptionPanel", "OPTIONS" },
        { "LanguagePanel", "LANGUAGE" },
        { "ResizeWindow", "WINDOW SIZE" },
        { "SoundVolume", "SOUND" },
        { "ServerRegionPanel", "SERVER REGION" },
        { "GraphicsPanel", "GRAPHICS" },
        { "CardImagesPanel", "CARD IMAGES" },
        { "GameplayPanel", "GAMEPLAY" },
        { "PatchNotes", "PATCH NOTES" },
    };

    void Cache(Opening op)
    {
        if (_cachedFor == op)
        {
            return;
        }

        _cachedFor = op;
        _dialogs = FindObjectsOfType<YesNoObject>(true);
        _enterRoom = FindObjectOfType<EnterRoom>(true);
        _windows.Clear();
        _deckPage = 0;

        foreach (GameObject rootObject in op.gameObject.scene.GetRootGameObjects())
        {
            foreach (Transform t in rootObject.GetComponentsInChildren<Transform>(true))
            {
                if (WindowTitles.ContainsKey(t.name) || t.GetComponent("GameplayOptionPanel") != null)
                {
                    _windows.Add(t.gameObject);
                }
            }
        }

        if (op.patchNotesPanel != null && !_windows.Contains(op.patchNotesPanel.gameObject))
        {
            _windows.Add(op.patchNotesPanel.gameObject);
        }
    }

    static bool LoadingVisible(LoadingObject loading) =>
        loading != null && loading.gameObject.activeInHierarchy && loading.LoadingText != null && loading.LoadingText.gameObject.activeInHierarchy;

    static Button ButtonIn(Transform t) => t != null ? t.GetComponentInChildren<Button>(true) : null;

    static void ClickOr(Button button, Action fallback)
    {
        if (button != null && button.gameObject.activeInHierarchy)
        {
            FlatUI.Click(button.gameObject);
        }
        else
        {
            fallback?.Invoke();
        }
    }

    // ---------------------------------------------------------------- loading / dialogs

    bool DetectLoading(Opening op, Spec spec)
    {
        BattleMode battle = op.battle;
        LoadingObject[] all =
        {
            op.LoadingObject, op.LoadingObject_light, op.LoadingObject_Unload,
            op.home != null ? op.home.loadingObject : null,
            battle != null && battle.selectBattleMode != null ? battle.selectBattleMode.loadingObject : null,
            battle != null && battle.selectBattleDeck != null ? battle.selectBattleDeck.loadingObject : null,
            battle != null && battle.lobbyManager_RandomMatch != null ? battle.lobbyManager_RandomMatch.loadingObject : null,
            battle != null && battle.lobbyManager_RandomMatch != null ? battle.lobbyManager_RandomMatch.disconnectLoadingObject : null,
            battle != null && battle.roomManager != null ? battle.roomManager.loadingObject : null,
            battle != null && battle.roomManager != null ? battle.roomManager.disconnectLoadingObject : null,
            op.deck != null && op.deck.editDeck != null ? op.deck.editDeck.LoadingObjec : null,
        };

        foreach (LoadingObject loading in all)
        {
            if (LoadingVisible(loading))
            {
                spec.Page = Page.Loading;
                spec.Title = "PLEASE WAIT";
                spec.Body = loading.LoadingText.text;
                return true;
            }
        }

        return false;
    }

    bool DetectDialogs(Opening op, Spec first, List<Spec> open)
    {
        bool any = false;

        foreach (YesNoObject dialog in _dialogs)
        {
            if (dialog == null || !FlatUI.IsOpen(dialog.gameObject, dialog.anim))
            {
                continue;
            }

            Spec spec = any ? new Spec() : first;
            FillDialog(op, dialog, spec);

            if (any)
            {
                open.Add(spec);
            }

            any = true;
        }

        return any;
    }

    static void FillDialog(Opening op, YesNoObject dialog, Spec spec)
    {
        spec.Page = Page.Dialog;
        spec.Key = "Dialog:" + dialog.GetInstanceID();
        bool battleMode = op.battle != null && op.battle.selectBattleMode != null && dialog == op.battle.selectBattleMode.selectBattleModeWindow;
        spec.Title = battleMode ? "BATTLE" : "DCGO";
        spec.Body = dialog.InfoText != null ? dialog.InfoText.text : "";

        foreach (CommandButton command in dialog.Buttons)
        {
            if (command == null || !command.gameObject.activeSelf)
            {
                continue;
            }

            Text label = command.GetComponentInChildren<Text>(true);
            GameObject target = command.gameObject;
            Button button = command.GetComponent<Button>();
            spec.Buttons.Add(new Btn
            {
                Label = label != null ? label.text : command.name,
                Act = () => FlatUI.Click(target),
                Enabled = button == null || button.interactable,
            });
        }

        if (dialog.CloseButton != null && dialog.CloseButton.activeSelf)
        {
            Button close = dialog.CloseButton.GetComponentInChildren<Button>(true);
            GameObject target = close != null ? close.gameObject : dialog.CloseButton;
            spec.Buttons.Add(new Btn { Label = "BACK", Act = () => FlatUI.Click(target) });
        }
    }

    // ---------------------------------------------------------------- title / home

    bool DetectTitle(Opening op, Spec spec)
    {
        Title title = op.title;

        if (title == null || !title.gameObject.activeInHierarchy || title.ClickToStart == null || !title.ClickToStart.activeInHierarchy)
        {
            return false;
        }

        spec.Page = Page.Title;
        spec.Title = "DCGO  VR";
        spec.Body = "Digimon Card Game Online, in virtual reality.\nPoint with the laser and pull the trigger.";
        Transform titleButton = FlatUI.Find(title, "TitleButton");
        GameObject start = FlatUI.TriggerCalling(title, "OnClick") ?? (titleButton != null ? titleButton.gameObject : null);
        spec.Buttons.Add(new Btn { Label = "START", Act = () => FlatUI.Click(start) });
        return true;
    }

    bool DetectHome(Opening op, Spec spec)
    {
        if (op.ModeButtons == null || !op.ModeButtons.activeInHierarchy)
        {
            return false;
        }

        spec.Page = Page.Home;
        spec.Title = "HOME";
        string name = ContinuousController.instance != null ? ContinuousController.instance.PlayerName : "";
        spec.Body = "Welcome" + (string.IsNullOrEmpty(name) ? "" : ", <b>" + name + "</b>") + ".";

        AddModeButton(spec, op, "BattleButton", "BATTLE");
        AddModeButton(spec, op, "DeckButton", "DECKS");
        AddModeButton(spec, op, "ConfigButton", "OPTIONS");

        InputField nameField = op.home != null && op.home.playerInfo != null ? op.home.playerInfo.PlayerNameInputField : null;

        if (nameField != null && nameField.gameObject.activeInHierarchy)
        {
            spec.Buttons.Add(new Btn { Label = "CHANGE NAME", Act = () => FlatUI.Focus(nameField) });
        }

        AddModeButton(spec, op, "PatchNotesButton", "PATCH NOTES");
        AddModeButton(spec, op, "ExitButton", "EXIT GAME");
        return true;
    }

    static void AddModeButton(Spec spec, Opening op, string name, string label)
    {
        Transform mode = FlatUI.Find(op.ModeButtons.transform, name);

        if (mode == null || !mode.gameObject.activeInHierarchy)
        {
            return;
        }

        Transform click = FlatUI.Find(mode, "Button");
        GameObject target = click != null ? click.gameObject : mode.gameObject;
        spec.Buttons.Add(new Btn { Label = label, Act = () => FlatUI.Click(target) });
    }

    // ---------------------------------------------------------------- battle

    bool DetectEnterRoom(Spec spec)
    {
        EnterRoom enter = _enterRoom;

        if (enter == null || !FlatUI.IsOpen(enter.gameObject, enter.anim))
        {
            return false;
        }

        spec.Page = Page.EnterRoom;
        spec.Title = "JOIN ROOM";
        string id = enter.RoomIDInputField != null ? enter.RoomIDInputField.text : "";
        spec.Body = "Room ID:  " + (string.IsNullOrEmpty(id) ? "<i>type or paste the ID your friend shared</i>" : "<b>" + id + "</b>");
        InputField field = enter.RoomIDInputField;
        spec.Buttons.Add(new Btn { Label = "TYPE ROOM ID", Act = () => FlatUI.Focus(field) });
        spec.Buttons.Add(new Btn
        {
            Label = "PASTE ID",
            Act = () =>
            {
                if (field != null)
                {
                    field.text = (GUIUtility.systemCopyBuffer ?? "").Trim();
                }
            },
        });
        Button join = enter.EnterRoomButton;
        spec.Buttons.Add(new Btn { Label = "JOIN", Act = () => FlatUI.Click(join != null ? join.gameObject : null), Enabled = join != null && join.interactable && !string.IsNullOrEmpty(id) });
        Button back = FlatUI.ButtonCalling(enter, "OnClickCloseEnterRoomWindow") ?? FindAnyButtonCalling("OnClickCloseEnterRoomWindow");
        spec.Buttons.Add(new Btn { Label = "BACK", Act = () => FlatUI.Click(back != null ? back.gameObject : null) });
        return true;
    }

    bool DetectSelectDeck(Opening op, Spec spec)
    {
        SelectBattleDeck select = op.battle != null ? op.battle.selectBattleDeck : null;

        if (select == null || select.SelectDeckObject == null || !FlatUI.IsOpen(select.SelectDeckObject, select.anim))
        {
            return false;
        }

        spec.Page = Page.SelectDeck;
        spec.Title = select.TitleText != null ? select.TitleText.text : "SELECT YOUR DECK";
        spec.Body = "Point at a deck to choose it, then press SELECT.";
        spec.Decks = ContinuousController.instance != null ? ContinuousController.instance.DeckDatas : null;
        DeckInfoPanel info = select.deckInfoPanel;
        spec.Selected = info != null ? info.ShowingDeckData : null;
        spec.OnDeck = deck => ClickDeck(select.deckInfoPrefabParentScroll, deck);
        spec.Side = DeckSummary(spec.Selected, info, select.InvalidDeckObject);
        bool has = spec.Selected != null;

        Button selectButton = select.SelectDeckButton;
        spec.Buttons.Add(new Btn { Label = "SELECT", Zone = Zone.Side, Act = () => FlatUI.Click(selectButton != null ? selectButton.gameObject : null), Enabled = selectButton != null && selectButton.interactable });
        AddDeckTools(spec, select, info, has, false);
        Transform back = FlatUI.Find(select, "Btn_CloseWindow01");
        spec.Buttons.Add(new Btn { Label = "BACK", Zone = Zone.Side, Act = () => FlatUI.Click(back != null ? back.gameObject : null) });
        return true;
    }

    // VIEW / TEST DRAW / CODE / EDIT (+ RENAME / DELETE in the deck browser): the deck info panel's own actions.
    static void AddDeckTools(Spec spec, Component panelRoot, DeckInfoPanel info, bool has, bool browser)
    {
        if (info == null)
        {
            return;
        }

        Button edit = ButtonIn(FlatUI.Find(panelRoot, "Edit"));
        spec.Buttons.Add(new Btn
        {
            Label = "EDIT",
            Zone = Zone.Side,
            Enabled = has,
            Act = () => ClickOr(edit, () =>
            {
                if (panelRoot is SelectBattleDeck battleDeck)
                {
                    battleDeck.OnClickEditDeckButton();
                }

                info.OnClickEditButton();
            }),
        });
        spec.Buttons.Add(new Btn { Label = "VIEW CARDS", Zone = Zone.Side, Enabled = has, Act = info.OnClickShowDeckButton });
        spec.Buttons.Add(new Btn { Label = "TEST DRAW", Zone = Zone.Side, Enabled = has, Act = info.OnClickTrial5DrawButton });
        spec.Buttons.Add(new Btn { Label = "COPY CODE", Zone = Zone.Side, Enabled = has, Act = info.OnClickGetDeckCodeButton });

        if (browser)
        {
            InputField name = info.DeckName;
            spec.Buttons.Add(new Btn { Label = "RENAME", Zone = Zone.Side, Enabled = has && name != null, Act = () => FlatUI.Focus(name) });
            spec.Buttons.Add(new Btn { Label = "DELETE", Zone = Zone.Side, Enabled = has, Act = info.OnClickDeleteDeckButton });
        }
    }

    static void ClickDeck(ScrollRect list, DeckData deck)
    {
        if (list == null || deck == null)
        {
            return;
        }

        foreach (DeckInfoPrefab prefab in list.content.GetComponentsInChildren<DeckInfoPrefab>(true))
        {
            if (prefab.thisDeckData == deck)
            {
                prefab.OnClick();
                Debug.Log("[DCGO VR] Menu: chose deck " + deck.DeckName);
                return;
            }
        }
    }

    static string DeckSummary(DeckData deck, DeckInfoPanel info, GameObject invalid)
    {
        if (deck == null)
        {
            return "<i>No deck chosen.</i>";
        }

        StringBuilder text = new StringBuilder();
        text.Append("<b>").Append(deck.DeckName).Append("</b>\n");

        if (info != null && info.DeckCountText != null)
        {
            text.Append("Cards: ").Append(info.DeckCountText.text).Append('\n');
        }
        else
        {
            text.Append("Cards: ").Append(deck.DeckCards().Count).Append(" + ").Append(deck.DigitamaDeckCards().Count).Append(" eggs\n");
        }

        bool valid = deck.IsValidDeckData();
        text.Append(valid ? "<color=#7CFF9A>Ready to play</color>" : "<color=#FF7A7A>Not playable yet (50 cards + up to 5 eggs).</color>");

        if (invalid != null && invalid.activeInHierarchy && valid)
        {
            text.Append("\n<color=#FF7A7A>Not allowed here.</color>");
        }

        return text.ToString();
    }

    bool DetectRandomLobby(Opening op, Spec spec)
    {
        LobbyManager_RandomMatch lobby = op.battle != null ? op.battle.lobbyManager_RandomMatch : null;

        if (lobby == null || !lobby.gameObject.activeInHierarchy)
        {
            return false;
        }

        spec.Page = Page.RandomLobby;
        spec.Title = "RANDOM MATCH";
        spec.Body = FlatUI.TextOf(lobby.MessageText) + "\n<size=120%><b>" + FlatUI.TextOf(lobby.TimeText) + "</b></size>";
        DeckData deck = lobby.deckInfoPanel != null ? lobby.deckInfoPanel.ShowingDeckData : ContinuousController.instance?.BattleDeckData;
        spec.Body += deck != null ? "\nDeck: " + deck.DeckName : "";

        if (lobby.ReturnButton != null && lobby.ReturnButton.activeInHierarchy)
        {
            Button cancel = lobby.ReturnButton.GetComponentInChildren<Button>(true);
            spec.Buttons.Add(new Btn { Label = "CANCEL", Act = () => FlatUI.Click(cancel != null ? cancel.gameObject : lobby.ReturnButton) });
        }

        return true;
    }

    bool DetectRoom(Opening op, Spec spec)
    {
        RoomManager room = op.battle != null ? op.battle.roomManager : null;
        GameObject visible = room != null ? (room.Parent != null ? room.Parent : room.gameObject) : null;

        if (visible == null || !visible.activeInHierarchy)
        {
            return false;
        }

        spec.Page = Page.Room;
        spec.Title = "ROOM   " + (room.RoomIDText != null ? room.RoomIDText.text : "");
        StringBuilder players = new StringBuilder();

        if (room.PlayerParent != null)
        {
            foreach (CPlayerElement player in room.PlayerParent.GetComponentsInChildren<CPlayerElement>(false))
            {
                string readyText = player.IsReady != null ? player.IsReady.text : "";
                players.Append("<b>").Append(player.PlayerName != null ? player.PlayerName.text : "?").Append("</b>   ").Append(readyText).Append('\n');
            }
        }

        spec.Body = players.Length > 0 ? players.ToString().TrimEnd() : "<i>Waiting for players...</i>";
        DeckData deck = ContinuousController.instance != null ? ContinuousController.instance.BattleDeckData : null;
        spec.Side = room.NoDeckSetObject != null && room.NoDeckSetObject.activeInHierarchy ? "<i>Choose a deck to get ready.</i>" : DeckSummary(deck, room.deckInfoPanel, room.InvalidDeckObject);

        Button choose = FlatUI.ButtonCalling(room, "OnClickSelectBattleDeckButton");
        spec.Buttons.Add(new Btn { Label = "CHOOSE DECK", Act = () => FlatUI.Click(choose != null ? choose.gameObject : null), Enabled = choose != null });
        Button ready = room.ReadyButton;
        spec.Buttons.Add(new Btn { Label = room.ReadyButtonText != null ? room.ReadyButtonText.text.ToUpperInvariant() : "READY", Act = () => FlatUI.Click(ready != null ? ready.gameObject : null), Enabled = ready != null && ready.interactable });
        Button copy = FlatUI.ButtonCalling(room, "OnClickCopyRoomIDButton");

        if (copy != null)
        {
            spec.Buttons.Add(new Btn { Label = "COPY ROOM ID", Act = () => FlatUI.Click(copy.gameObject) });
        }

        // Who goes first (room owner): the flat toggles, as buttons.
        foreach (Toggle toggle in visible.GetComponentsInChildren<Toggle>(false))
        {
            if (toggle.GetComponent("FirstPlayerIndexIdToggle") == null)
            {
                continue;
            }

            Text label = toggle.GetComponentInChildren<Text>(true);
            GameObject target = FlatUI.TriggerCalling(toggle, "OnClick") ?? toggle.gameObject;
            spec.Buttons.Add(new Btn
            {
                Label = "FIRST: " + (label != null ? label.text : toggle.name) + (toggle.isOn ? "  [x]" : ""),
                Enabled = toggle.interactable,
                Act = () => FlatUI.Click(target),
            });
        }

        Button leave = FlatUI.ButtonCalling(room, "CloseRoom") ?? FindAnyButtonCalling("CloseRoom");
        spec.Buttons.Add(new Btn { Label = "LEAVE ROOM", Act = () => FlatUI.Click(leave != null ? leave.gameObject : null) });
        return true;
    }

    static Button FindAnyButtonCalling(string method)
    {
        foreach (Button button in FindObjectsOfType<Button>(true))
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

    // ---------------------------------------------------------------- decks

    bool DetectDecks(Opening op, Spec spec)
    {
        SelectDeck select = op.deck != null ? op.deck.selectDeck : null;

        if (select == null || select.SelectDeckObject == null || !FlatUI.IsOpen(select.SelectDeckObject, select.anim))
        {
            return false;
        }

        spec.Page = Page.Decks;
        spec.Title = "MY DECKS";
        spec.Body = "Point at a deck to see it. NEW DECK starts one from scratch or from a copied deck code.";
        spec.Decks = ContinuousController.instance != null ? ContinuousController.instance.DeckDatas : null;
        DeckInfoPanel info = select.deckInfoPanel;
        spec.Selected = info != null ? info.ShowingDeckData : null;
        spec.OnDeck = deck => ClickDeck(select.deckInfoPrefabParentScroll, deck);
        spec.Side = DeckSummary(spec.Selected, info, null);
        AddDeckTools(spec, select, info, spec.Selected != null, true);

        CreateNewDeckButton create = select.deckInfoPrefabParentScroll != null ? select.deckInfoPrefabParentScroll.content.GetComponentInChildren<CreateNewDeckButton>(true) : null;
        Button createButton = create != null ? create.GetComponentInChildren<Button>(true) : null;
        spec.Buttons.Add(new Btn { Label = "NEW DECK", Zone = Zone.Side, Enabled = createButton != null, Act = () => FlatUI.Click(createButton != null ? createButton.gameObject : null) });

        Transform back = FlatUI.Find(select, "Btn_CloseWindow01");
        spec.Buttons.Add(new Btn { Label = "BACK", Zone = Zone.Side, Act = () => FlatUI.Click(back != null ? back.gameObject : null) });
        return true;
    }

    bool DetectDeckList(Opening op, Spec spec)
    {
        DeckListPanel list = op.deck != null ? op.deck.deckListPanel : null;

        if (list == null || !FlatUI.IsOpen(list.gameObject, list.anim) || list.DeckScroll == null)
        {
            return false;
        }

        spec.Page = Page.DeckList;
        spec.Title = list.DeckNameText != null ? list.DeckNameText.text : "DECK";
        spec.Body = "Point at a card to read it.";
        spec.Cards = new List<CardItem>();
        spec.Cols = 7;
        spec.Rows = 3;
        bool pick = false;

        // One VR card per different card, with the number of copies.
        foreach (IGrouping<int, CardPrefab_CreateDeck> group in list.DeckScroll.content.GetComponentsInChildren<CardPrefab_CreateDeck>(true)
                     .Where(p => p.cEntity_Base != null)
                     .GroupBy(p => p.cEntity_Base.CardIndex))
        {
            CardPrefab_CreateDeck first = group.First();
            Action act = first.OnClickAction != null ? new Action(() => first.OnClickAction()) : null;
            pick |= act != null;
            spec.Cards.Add(new CardItem { Card = first.cEntity_Base, Note = "x" + group.Count(), Act = act });
        }

        if (pick)
        {
            spec.Body = "Pull the trigger on a card to choose it.";
        }

        spec.Side = list.DeckCountText != null ? "Cards: " + list.DeckCountText.text : "";
        Button close = FlatUI.ButtonCalling(list, "Close");
        spec.Buttons.Add(new Btn { Label = "CLOSE", Zone = Zone.Side, Act = () => ClickOr(close, list.Close) });
        return true;
    }

    bool DetectTrialDraw(Opening op, Spec spec)
    {
        TrialDraw draw = op.deck != null ? op.deck.trialDraw : null;

        if (draw == null || !FlatUI.IsOpen(draw.gameObject, draw.anim) || draw.CardScroll == null)
        {
            return false;
        }

        spec.Page = Page.TrialDraw;
        spec.Title = "TEST DRAW";
        spec.Body = "Your opening hand. DRAW 1 adds the next card from the deck.";
        spec.Cards = new List<CardItem>();
        spec.Cols = 7;
        spec.Rows = 2;

        foreach (CardPrefab_CreateDeck prefab in draw.CardScroll.content.GetComponentsInChildren<CardPrefab_CreateDeck>(false))
        {
            if (prefab.cEntity_Base != null)
            {
                spec.Cards.Add(new CardItem { Card = prefab.cEntity_Base });
            }
        }

        spec.Side = draw.DeckCountText != null ? "Deck: " + draw.DeckCountText.text : "";
        Button drawOne = draw.drawButton;
        spec.Buttons.Add(new Btn { Label = "DRAW 1", Zone = Zone.Side, Enabled = drawOne != null && drawOne.interactable && drawOne.gameObject.activeInHierarchy, Act = () => ClickOr(drawOne, draw.OnClick1DrawButton) });
        Button redraw = FlatUI.ButtonCalling(draw, "OnClickRedrawButton");
        spec.Buttons.Add(new Btn { Label = "REDRAW", Zone = Zone.Side, Act = () => ClickOr(redraw, draw.OnClickRedrawButton) });
        Button close = FlatUI.ButtonCalling(draw, "OnClickCloseButton");
        spec.Buttons.Add(new Btn { Label = "CLOSE", Zone = Zone.Side, Act = () => ClickOr(close, draw.OnClickCloseButton) });
        return true;
    }

    // ---------------------------------------------------------------- deck editor

    bool DetectEditor(Opening op, Spec spec)
    {
        EditDeck editor = op.deck != null ? op.deck.editDeck : null;

        if (editor == null || editor.CreateDeckObject == null || !editor.CreateDeckObject.activeInHierarchy || editor.EdittingDeckData == null)
        {
            _editorFilters = false;
            return false;
        }

        // The VR editor (card wall, thousands of pool cards) costs too many frames:
        // the flat editor is used instead. The page is still reported, without
        // building anything, so dialogs and test draw opened from it stack on top.
        if (!EditorInVR)
        {
            _editorFilters = false;
            spec.Page = Page.Editor;
            spec.Key = "Editor:flat";
            return true;
        }

        DeckData deck = editor.EdittingDeckData;
        spec.Page = Page.Editor;
        spec.Key = _editorFilters ? "Editor:filters" : "Editor";
        spec.Wide = true;
        spec.Title = "DECK EDITOR";

        // Right side: the deck itself.
        spec.Side = "<b>" + deck.DeckName + "</b>\nCards: " + (editor.DeckCountText != null ? editor.DeckCountText.text : "") +
                    (deck.IsValidDeckData() ? "   <color=#7CFF9A>playable</color>" : "   <color=#FFB070>not playable yet</color>");
        spec.Lines = new List<Btn>();

        foreach (IGrouping<int, CEntity_Base> group in deck.DigitamaDeckCards().Concat(deck.DeckCards()).GroupBy(c => c.CardIndex))
        {
            CEntity_Base card = group.First();
            bool egg = card.cardKind.Contains(CardKind.DigiEgg);
            spec.Lines.Add(new Btn
            {
                Label = group.Count() + "x " + (egg ? "[Egg] " : "") + card.CardName_ENG,
                Act = () => editor.StartCoroutine(editor.RemoveDeckCards(card)),
            });
        }

        Button save = FlatUI.ButtonCalling(editor, "OnClickCompleteEditButton");
        Button cancel = FlatUI.ButtonCalling(editor, "OnClickCancelEditButton");
        spec.Buttons.Add(new Btn { Label = "SAVE", Zone = Zone.Side, Act = () => ClickOr(save, editor.OnClickCompleteEditButton) });
        spec.Buttons.Add(new Btn { Label = "CANCEL", Zone = Zone.Side, Act = () => ClickOr(cancel, editor.OnClickCancelEditButton) });
        InputField name = editor.DeckNameInputField;
        spec.Buttons.Add(new Btn { Label = "RENAME", Zone = Zone.Side, Enabled = name != null, Act = () => FlatUI.Focus(name) });
        spec.Buttons.Add(new Btn { Label = "KEY CARD", Zone = Zone.Side, Act = editor.OnClickSetDeckIconButton });
        spec.Buttons.Add(new Btn { Label = "TEST DRAW", Zone = Zone.Side, Act = editor.OnClickTrial5DrawButton });
        spec.Buttons.Add(new Btn { Label = "CLEAR", Zone = Zone.Side, Act = editor.OnClickClearDeckButton });

        FilterCardList filters = editor.filterCardList;

        if (_editorFilters && filters != null)
        {
            spec.Body = "Change the filters, then APPLY.";
            MirrorControls(filters.transform, spec, Zone.Main, skipSearch: true);
            spec.Buttons.Add(new Btn { Label = "APPLY", Zone = Zone.Bar, Act = () => { filters.OnClickSearchButton(); _editorFilters = false; _cardPage = 0; } });
            spec.Buttons.Add(new Btn { Label = "RESET", Zone = Zone.Bar, Act = filters.OnClickResetButton });
            spec.Buttons.Add(new Btn { Label = "BACK TO CARDS", Zone = Zone.Bar, Act = () => _editorFilters = false });
            return true;
        }

        // Left side: the card pool page the flat editor shows, as real cards.
        spec.Body = "Pull the trigger on a card to add it. Tap a line on the right to remove one.";
        spec.Cards = new List<CardItem>();
        spec.Cols = 7;
        spec.Rows = 3;
        Dictionary<int, int> inDeck = deck.AllDeckCards().GroupBy(c => c.CardIndex).ToDictionary(g => g.Key, g => g.Count());

        foreach (CardPrefab_CreateDeck prefab in editor.CardPoolCardPrefabs_CreateDeck)
        {
            if (prefab == null || !prefab.isActive || prefab.cEntity_Base == null || !prefab.gameObject.activeSelf)
            {
                continue;
            }

            CEntity_Base card = prefab.cEntity_Base;
            inDeck.TryGetValue(card.CardIndex, out int count);
            bool canAdd = DeckBuildingRule.CanAddCard(card, deck);
            spec.Cards.Add(new CardItem
            {
                Card = card,
                Note = count > 0 ? "in deck: " + count : null,
                Dim = !canAdd,
                Act = () => editor.StartCoroutine(editor.AddDeckCards(card)),
            });
        }

        string pageText = editor.ShowDisplayPageIndexText != null ? editor.ShowDisplayPageIndexText.text : "";
        Button down = editor.DownDisplayPageIndexButton;
        Button up = editor.UpDisplayPageIndexButton;
        spec.Buttons.Add(new Btn { Label = "< LIST", Zone = Zone.Bar, Enabled = down != null && down.interactable, Act = () => { editor.DownDisplayPageIndex(); _cardPage = 0; } });
        spec.Buttons.Add(new Btn { Label = "LIST " + pageText + " >", Zone = Zone.Bar, Enabled = up != null && up.interactable, Act = () => { editor.UpDisplayPageIndex(); _cardPage = 0; } });

        if (filters != null && filters.SearchInputField != null)
        {
            InputField search = filters.SearchInputField;
            string query = search.text;
            spec.Buttons.Add(new Btn { Label = string.IsNullOrEmpty(query) ? "SEARCH" : "SEARCH: " + query, Zone = Zone.Bar, Act = () => FlatUI.Focus(search) });
        }

        if (filters != null)
        {
            spec.Buttons.Add(new Btn { Label = "FILTERS", Zone = Zone.Bar, Act = () => _editorFilters = true });
        }

        return true;
    }

    // ---------------------------------------------------------------- generic windows

    bool DetectWindows(Opening op, Spec first, List<Spec> open)
    {
        bool any = false;

        foreach (GameObject window in _windows)
        {
            if (window == null || !window.activeInHierarchy || !FlatUI.IsOpen(window, window.GetComponentInChildren<Animator>(true)))
            {
                continue;
            }

            Spec spec = any ? new Spec() : first;
            spec.Page = Page.Window;
            spec.Key = "Window:" + window.name;
            spec.Title = WindowTitles.TryGetValue(window.name, out string title) ? title : Pretty(window.name);
            string text = LongestText(window.transform);

            if (text.Length > 160)
            {
                spec.Side = text.Length > 1500 ? text.Substring(0, 1500) + "..." : text;
            }
            else
            {
                spec.Body = text;
            }

            MirrorControls(window.transform, spec, Zone.Main, skipSearch: false);

            if (any)
            {
                open.Add(spec);
            }

            any = true;
        }

        return any;
    }

    // Every active control of a flat window as a VR button (label from the window itself).
    static void MirrorControls(Transform root, Spec spec, Zone zone, bool skipSearch)
    {
        Btn close = null;

        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(false))
        {
            if (selectable is Scrollbar || !selectable.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (selectable.GetComponentInParent<Dropdown>() != null && !(selectable is Dropdown))
            {
                continue; // items of an open dropdown list
            }

            string label = LabelOf(selectable);

            switch (selectable)
            {
                case Button button:
                {
                    GameObject target = button.gameObject;

                    if (button.name.StartsWith("Btn_CloseWindow"))
                    {
                        close = new Btn { Label = "CLOSE", Zone = zone, Act = () => FlatUI.Click(target) };
                        break;
                    }

                    spec.Buttons.Add(new Btn { Label = label, Zone = zone, Enabled = button.interactable, Act = () => FlatUI.Click(target) });
                    break;
                }

                case Toggle toggle:
                {
                    Toggle target = toggle;
                    spec.Buttons.Add(new Btn { Label = label + ": " + (toggle.isOn ? "ON" : "OFF"), Zone = zone, Enabled = toggle.interactable, Act = () => target.isOn = !target.isOn });
                    break;
                }

                case Slider slider:
                {
                    Slider target = slider;
                    float step = (slider.maxValue - slider.minValue) * 0.1f;
                    int percent = Mathf.RoundToInt(Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value) * 100f);
                    spec.Buttons.Add(new Btn { Label = label + " -   (" + percent + "%)", Zone = zone, Enabled = slider.interactable, Act = () => target.value = Mathf.Max(target.minValue, target.value - step) });
                    spec.Buttons.Add(new Btn { Label = label + " +", Zone = zone, Enabled = slider.interactable, Act = () => target.value = Mathf.Min(target.maxValue, target.value + step) });
                    break;
                }

                case Dropdown dropdown:
                {
                    Dropdown target = dropdown;
                    int count = dropdown.options.Count;
                    string value = count > 0 && dropdown.value < count ? dropdown.options[dropdown.value].text : "";
                    spec.Buttons.Add(new Btn { Label = label + ": " + value + "  >", Zone = zone, Enabled = dropdown.interactable && count > 1, Act = () => target.value = (target.value + 1) % Mathf.Max(1, target.options.Count) });

                    if (count > 6)
                    {
                        spec.Buttons.Add(new Btn { Label = "< " + label, Zone = zone, Enabled = dropdown.interactable, Act = () => target.value = (target.value - 1 + target.options.Count) % Mathf.Max(1, target.options.Count) });
                    }

                    break;
                }

                case InputField input:
                {
                    if (skipSearch && input.name.Contains("Search"))
                    {
                        break;
                    }

                    InputField target = input;
                    spec.Buttons.Add(new Btn { Label = label + ": " + (string.IsNullOrEmpty(input.text) ? "(type)" : input.text), Zone = zone, Enabled = input.interactable, Act = () => FlatUI.Focus(target) });
                    break;
                }

                case TMP_InputField tmpInput:
                {
                    if (skipSearch && tmpInput.name.Contains("Search"))
                    {
                        break;
                    }

                    TMP_InputField target = tmpInput;
                    spec.Buttons.Add(new Btn { Label = label + ": " + (string.IsNullOrEmpty(tmpInput.text) ? "(type)" : tmpInput.text), Zone = zone, Enabled = tmpInput.interactable, Act = () => FlatUI.Focus(target) });
                    break;
                }
            }
        }

        if (close != null)
        {
            spec.Buttons.Add(close);
        }
    }

    static string LabelOf(Selectable selectable)
    {
        // A text inside the control (button caption, toggle label)...
        if (!(selectable is Dropdown) && !(selectable is InputField) && !(selectable is TMP_InputField) && !(selectable is Slider))
        {
            foreach (Text text in selectable.GetComponentsInChildren<Text>(false))
            {
                if (!string.IsNullOrWhiteSpace(text.text) && text.text.Length < 40)
                {
                    return text.text.Trim();
                }
            }

            foreach (TextMeshProUGUI text in selectable.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                if (!string.IsNullOrWhiteSpace(text.text) && text.text.Length < 40)
                {
                    return text.text.Trim();
                }
            }
        }

        // ...or a title text next to it (slider "BGM", option rows)...
        Transform parent = selectable.transform.parent;

        if (parent != null && (selectable is Slider || selectable is Toggle))
        {
            foreach (Transform sibling in parent)
            {
                if (sibling != selectable.transform && sibling.TryGetComponent(out Text text) && !string.IsNullOrWhiteSpace(text.text) && text.text.Length < 30)
                {
                    return text.text.Trim();
                }
            }
        }

        // ...or its own name.
        return Pretty(selectable.name);
    }

    static string Pretty(string name)
    {
        name = Regex.Replace(name, @"\(.*?\)|Dropdown|Toggle|Button|Slider|InputField|Panel|Btn_?", "");
        name = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").Replace('_', ' ').Trim();
        return string.IsNullOrEmpty(name) ? "OPTION" : name.ToUpperInvariant();
    }

    static string LongestText(Transform root)
    {
        string best = "";

        foreach (Text text in root.GetComponentsInChildren<Text>(false))
        {
            if (text.GetComponentInParent<Selectable>() == null && text.text.Length > best.Length)
            {
                best = text.text;
            }
        }

        foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(false))
        {
            if (text.GetComponentInParent<Selectable>() == null && text.text.Length > best.Length)
            {
                best = text.text;
            }
        }

        return best.Trim();
    }
}
#endif
