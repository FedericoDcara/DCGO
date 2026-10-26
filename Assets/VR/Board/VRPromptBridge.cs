#if DCGO_VR
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// === DCGO-CUSTOM:vr ===
// Reads the game's own prompt UI (the flat screen) so VR can show and answer the
// same prompts without touching the effect code:
//   - which cards are outlined (blue = can be chosen, orange = chosen)
//   - clicking a card = invoking the very same click handler the screen uses
//   - the prompt message, the revealed cards of the "select card" panel, and
//     every active button of the prompt panels (End selection, No selection,
//     Yes / No, effect choices...)
// Because the game itself still runs the prompt, the result goes through the
// normal selection RPCs: nothing new on the network.
public static class VRPromptBridge
{
    static int _frame = -1;
    static readonly Dictionary<CardSource, FieldPermanentCard> _fieldCards = new Dictionary<CardSource, FieldPermanentCard>();
    static readonly List<HandCard> _panelCards = new List<HandCard>();

    public static readonly Color Selectable = new Color(0.25f, 0.75f, 1f);
    public static readonly Color Selected = new Color(1f, 0.6f, 0.15f);

    /// <summary>True when the game is not waiting for a free action (i.e. a prompt or effect is running).</summary>
    public static bool PromptActive => LegalActions.Current == LegalActionSet.None || LegalActions.PromptOnScreen(GManager.instance);

    static void Refresh()
    {
        if (_frame == Time.frameCount)
        {
            return;
        }

        _frame = Time.frameCount;
        _fieldCards.Clear();
        _panelCards.Clear();

        GameContext context = BoardObserver.Context;

        if (context == null || context.You == null)
        {
            return;
        }

        foreach (Player player in new[] { context.You, context.Opponent })
        {
            foreach (Permanent permanent in player.GetFieldPermanents())
            {
                if (permanent.TopCard != null && permanent.ShowingPermanentCard != null)
                {
                    _fieldCards[permanent.TopCard] = permanent.ShowingPermanentCard;
                }
            }
        }

        SelectCardPanel panel = GManager.instance != null ? GManager.instance.selectCardPanel : null;

        if (panel != null && panel.gameObject.activeInHierarchy && panel.scrollRect != null)
        {
            Transform content = panel.scrollRect.content;

            for (int i = 0; i < content.childCount; i++)
            {
                HandCard handCard = content.GetChild(i).GetComponent<HandCard>();

                if (handCard != null && handCard.gameObject.activeInHierarchy)
                {
                    _panelCards.Add(handCard);
                }
            }
        }
    }

    /// <summary>
    /// Entries of the "select card" panel, in screen order. One per choice: two effects
    /// of the same card (effect order popup) are two entries.
    /// </summary>
    public static List<HandCard> PanelCards
    {
        get
        {
            Refresh();
            return _panelCards;
        }
    }

    /// <summary>Clicks one entry of the "select card" panel, exactly like the screen does.</summary>
    public static void ClickPanelCard(HandCard handCard)
    {
        if (handCard == null)
        {
            return;
        }

        if (handCard.OnClickAction != null)
        {
            handCard.OnClickAction.Invoke(handCard);
        }
        else if (GManager.instance != null && GManager.instance.selectCardPanel != null)
        {
            GManager.instance.selectCardPanel.OnClickHandCard(handCard);
        }

        Debug.Log("[DCGO VR] Prompt: clicked panel entry " + (handCard.cardSource != null ? LegalActions.Name(handCard.cardSource) : handCard.name));
    }

    /// <summary>Glow colour of a panel entry (blue = can be chosen, orange = chosen).</summary>
    public static Color? PanelOutline(HandCard handCard) => HandOutline(handCard);

    /// <summary>The outline the flat UI shows on this card right now, as a VR glow colour (or null).</summary>
    public static Color? OutlineColor(CardSource card, GameContext context)
    {
        Refresh();

        if (_fieldCards.TryGetValue(card, out FieldPermanentCard fieldCard))
        {
            if (fieldCard.Outline_Select != null && fieldCard.Outline_Select.gameObject.activeInHierarchy)
            {
                return IsOrange(fieldCard.Outline_Select.color) ? Selected : Selectable;
            }

            return null;
        }

        if (card.ShowingHandCard != null && context != null && context.You != null && context.You.HandCards.Contains(card))
        {
            return HandOutline(card.ShowingHandCard);
        }

        return null;
    }

    static Color? HandOutline(HandCard handCard)
    {
        if (handCard.Outline_Select == null || !handCard.Outline_Select.activeInHierarchy)
        {
            return null;
        }

        Color color = handCard.Outline_SelectImages != null && handCard.Outline_SelectImages.Count > 0 && handCard.Outline_SelectImages[0] != null
            ? handCard.Outline_SelectImages[0].color
            : DataBase.SelectColor_Blue;
        return IsOrange(color) ? Selected : Selectable;
    }

    static bool IsOrange(Color color) => color.r > 0.6f && color.b < 0.5f;

    /// <summary>
    /// If a prompt is running and this card is clickable on the screen, perform that click.
    /// Returns false when VR should handle the card itself (free actions: play, attack...).
    /// </summary>
    public static bool TryClick(CardSource card)
    {
        if (card == null || !PromptActive)
        {
            return false;
        }

        Refresh();

        if (_fieldCards.TryGetValue(card, out FieldPermanentCard fieldCard) && fieldCard.OnClickAction != null)
        {
            fieldCard.OnClickAction.Invoke(fieldCard);
            Debug.Log("[DCGO VR] Prompt: clicked " + LegalActions.Name(card) + " (field)");
            return true;
        }

        // A source card under a permanent: click the permanent.
        GameContext context = BoardObserver.Context;

        if (context != null)
        {
            foreach (Player player in new[] { context.You, context.Opponent })
            {
                foreach (Permanent permanent in player.GetFieldPermanents())
                {
                    if (permanent.cardSources.Contains(card) && permanent.ShowingPermanentCard != null && permanent.ShowingPermanentCard.OnClickAction != null)
                    {
                        permanent.ShowingPermanentCard.OnClickAction.Invoke(permanent.ShowingPermanentCard);
                        Debug.Log("[DCGO VR] Prompt: clicked " + LegalActions.Name(card) + " (field stack)");
                        return true;
                    }
                }
            }
        }

        if (card.ShowingHandCard != null && card.ShowingHandCard.OnClickAction != null)
        {
            card.ShowingHandCard.OnClickAction.Invoke(card.ShowingHandCard);
            Debug.Log("[DCGO VR] Prompt: clicked " + LegalActions.Name(card) + " (hand)");
            return true;
        }

        return false;
    }

    static readonly System.Reflection.FieldInfo HandModeField =
        typeof(SelectHandEffect).GetField("_mode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// True while the game asks you to choose card(s) from your hand (any purpose:
    /// trash, deck top/bottom, under security, under a Tamer...). In VR those are
    /// chosen by loading them into the disk's tray and pushing it closed.
    /// (Name kept from the first, discard-only version.)
    /// </summary>
    public static bool IsDiscardPrompt
    {
        get
        {
            GameContext context = BoardObserver.Context;

            if (!PromptActive || context == null || context.You == null || GManager.instance == null)
            {
                return false;
            }

            bool anyHandChoice = false;

            foreach (CardSource card in context.You.HandCards)
            {
                if (card != null && OutlineColor(card, context).HasValue)
                {
                    anyHandChoice = true;
                    break;
                }
            }

            return anyHandChoice;
        }
    }

    /// <summary>What happens to the chosen hand cards, for the tray label (TRASH, DECK TOP...).</summary>
    public static string HandSelectionPurpose(out bool isTrash)
    {
        isTrash = false;
        SelectHandEffect effect = GManager.instance != null ? GManager.instance.GetComponent<SelectHandEffect>() : null;

        if (effect != null && HandModeField != null && HandModeField.GetValue(effect) is SelectHandEffect.Mode mode)
        {
            switch (mode)
            {
                case SelectHandEffect.Mode.Discard:
                    isTrash = true;
                    return "TRASH";
                case SelectHandEffect.Mode.PutLibraryTop:
                    return "DECK TOP";
                case SelectHandEffect.Mode.PutLibraryBottom:
                    return "DECK BOTTOM";
                case SelectHandEffect.Mode.PutSecurityBottom:
                    return "UNDER SECURITY";
            }
        }

        if (MentionsTrash(Message))
        {
            isTrash = true;
            return "TRASH";
        }

        return "SELECT";
    }

    /// <summary>Diagnostics: what the hand-selection effect is set to right now.</summary>
    public static string DescribeHandSelection()
    {
        SelectHandEffect effect = GManager.instance != null ? GManager.instance.GetComponent<SelectHandEffect>() : null;
        object mode = effect != null && HandModeField != null ? HandModeField.GetValue(effect) : null;
        return $"hand selection mode: {(mode != null ? mode.ToString() : "n/a")}, discard prompt: {IsDiscardPrompt}";
    }

    /// <summary>A hand card that can still be loaded into the trash slot (selectable, not chosen yet).</summary>
    public static bool WantsTrashFromHand(CardSource card)
    {
        GameContext context = BoardObserver.Context;

        if (card == null || context == null || context.You == null || !context.You.HandCards.Contains(card) || !IsDiscardPrompt)
        {
            return false;
        }

        Color? outline = OutlineColor(card, context);
        return outline.HasValue && outline.Value != Selected;
    }

    /// <summary>A hand card already loaded into the trash slot (chosen, waiting for End Selection).</summary>
    public static bool IsLoadedForTrash(CardSource card)
    {
        GameContext context = BoardObserver.Context;

        if (card == null || context == null || context.You == null || !context.You.HandCards.Contains(card) || !IsDiscardPrompt)
        {
            return false;
        }

        Color? outline = OutlineColor(card, context);
        return outline.HasValue && outline.Value == Selected;
    }

    public static bool MentionsTrash(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        string m = message.ToLowerInvariant();
        return m.Contains("trash") || m.Contains("discard") || m.Contains("トラッシュ") || m.Contains("捨て");
    }

    static readonly List<Button> _confirmButtons = new List<Button>();
    static readonly List<string> _confirmLabels = new List<string>();

    /// <summary>
    /// Presses the prompt's confirm button (End Selection / Decide / OK...). Returns false if
    /// there is no clear confirm button (then the player presses it on the panel).
    /// </summary>
    public static bool PressConfirm()
    {
        CollectButtons(_confirmButtons, _confirmLabels);
        Button best = null;

        for (int i = 0; i < _confirmButtons.Count; i++)
        {
            string label = _confirmLabels[i].ToLowerInvariant();
            string name = _confirmButtons[i].gameObject.name.ToLowerInvariant();

            if (label.Contains("end") || label.Contains("decide") || label.Contains("confirm") || label == "ok" || label.Contains("yes")
                || label.Contains("決定") || label.Contains("終了") || name.Contains("endselect"))
            {
                best = _confirmButtons[i];
                break;
            }
        }

        if (best == null && _confirmButtons.Count == 1)
        {
            best = _confirmButtons[0];
        }

        if (best == null)
        {
            Debug.Log("[DCGO VR] Confirm: no clear confirm button (" + string.Join(", ", _confirmLabels) + ")");
            return false;
        }

        Debug.Log("[DCGO VR] Confirm: pressing " + best.gameObject.name);
        best.onClick.Invoke();
        return true;
    }

    /// <summary>The prompt message currently shown on the screen, or empty.</summary>
    public static string Message
    {
        get
        {
            GManager g = GManager.instance;

            if (g == null)
            {
                return "";
            }

            if (g.resultObject != null && g.resultObject.gameObject.activeInHierarchy)
            {
                return ResultMessage(g.resultObject);
            }

            if (g.resultObject != null && g.resultObject.ReturnToResultButton != null && g.resultObject.ReturnToResultButton.gameObject.activeInHierarchy)
            {
                return "Duel over - checking the field";
            }

            if (g.selectCardPanel != null && g.selectCardPanel.gameObject.activeInHierarchy && g.selectCardPanel.MessageText != null)
            {
                return g.selectCardPanel.MessageText.text;
            }

            if (g.commandText != null && g.commandText.gameObject.activeInHierarchy && g.commandText.commandText != null)
            {
                return g.commandText.commandText.text;
            }

            return "";
        }
    }

    /// <summary>Every active, clickable button of the prompt panels, with its label.</summary>
    public static void CollectButtons(List<Button> buttons, List<string> labels)
    {
        buttons.Clear();
        labels.Clear();
        GManager g = GManager.instance;

        if (g == null)
        {
            return;
        }

        AddButtons(g.selectCommandPanel, buttons, labels);
        AddButtons(g.BackButton, buttons, labels);
        AddButtons(g.selectCardPanel, buttons, labels);

        // "View battle area" hides the panel and shows a separate button to come back.
        if (g.selectCardPanel != null && g.selectCardPanel.ReturnToSelectCardButton != null)
        {
            AddButtons(g.selectCardPanel.ReturnToSelectCardButton, buttons, labels);
        }
        AddButtons(g.selectJogressEffect, buttons, labels);
        AddButtons(g.selectDNACondition, buttons, labels);
        AddButtons(g.selectBurstDigivolutionEffect, buttons, labels);
        AddButtons(g.selectAppFusionEffect, buttons, labels);

        // End of the duel: the result screen's buttons (back to menu, check the field...), and the
        // button that brings the result back after "check the field".
        if (g.resultObject != null)
        {
            AddButtons(g.resultObject, buttons, labels);

            if (g.resultObject.ReturnToResultButton != null)
            {
                AddButtons(g.resultObject.ReturnToResultButton, buttons, labels);
            }
        }
    }

    static readonly System.Reflection.FieldInfo ResultWinImage = typeof(ResultObject).GetField("WinImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo ResultLoseImage = typeof(ResultObject).GetField("LoseImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.FieldInfo ResultTextField = typeof(ResultObject).GetField("ResultText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>Title for the result screen: VICTORY / DEFEAT / the game's result text.</summary>
    static string ResultMessage(ResultObject result)
    {
        string title = "DUEL OVER";

        try
        {
            if (ResultWinImage != null && ResultWinImage.GetValue(result) is Image win && win != null && win.gameObject.activeSelf)
            {
                title = "VICTORY";
            }
            else if (ResultLoseImage != null && ResultLoseImage.GetValue(result) is Image lose && lose != null && lose.gameObject.activeSelf)
            {
                title = "DEFEAT";
            }

            if (ResultTextField != null && ResultTextField.GetValue(result) is Text text && text != null && !string.IsNullOrWhiteSpace(text.text))
            {
                title += " - " + text.text.Trim();
            }
        }
        catch (System.Exception)
        {
            // keep the generic title
        }

        return title;
    }

    static void AddButtons(Component root, List<Button> buttons, List<string> labels)
    {
        if (root == null || !root.gameObject.activeInHierarchy)
        {
            return;
        }

        foreach (Button button in root.GetComponentsInChildren<Button>(false))
        {
            if (!button.isActiveAndEnabled || !button.interactable || buttons.Contains(button))
            {
                continue;
            }

            // Skip the card images inside the select panel: those are handled as cards.
            if (button.GetComponentInParent<HandCard>() != null)
            {
                continue;
            }

            string label = null;
            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(false);

            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
            {
                label = tmp.text;
            }
            else
            {
                Text text = button.GetComponentInChildren<Text>(false);

                if (text != null && !string.IsNullOrWhiteSpace(text.text))
                {
                    label = text.text;
                }
            }

            buttons.Add(button);
            labels.Add(string.IsNullOrEmpty(label) ? button.gameObject.name : label.Trim());
        }
    }
}
#endif
