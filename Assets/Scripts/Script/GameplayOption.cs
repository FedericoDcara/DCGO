using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameplayOption : OffAnimation
{
    private static readonly int CloseHash = Animator.StringToHash("Close");
    private static readonly int OpenHash = Animator.StringToHash("Open");
    [SerializeField] Animator _anim;
    [SerializeField] Toggle _reverseOpponentsCardsToggle;
    [SerializeField] Toggle _showCutInAnimationToggle;
    [SerializeField] Toggle _turnSuspendedCardsToggle;
    [SerializeField] Toggle _checkBeforeEndingSelectionToggle;
    [SerializeField] Toggle _suspendedCardsDirectionIsLeftToggle;
    [SerializeField] Toggle _autoEffectOrderToggle;
    [SerializeField] Toggle _autoDeckBottomOrderToggle;
    [SerializeField] Toggle _autoDeckTopOrderToggle;
    [SerializeField] Toggle _autoMinDigivolutionCostToggle;
    [SerializeField] Toggle _autoMaxCardCountToggle;
    [SerializeField] Toggle _autoHatchToggle;
    [SerializeField] Toggle _banlistToggle;

    // === DCGO-CUSTOM:matchmusic begin ===
    Toggle _reactiveMatchMusicToggle;
    bool _reactiveToggleInjected;
    // === DCGO-CUSTOM:matchmusic end ===

    public void Close()
    {
        Close_(true);
    }

    public void Close_(bool playSE)
    {
        if (playSE)
        {
            if (Opening.instance != null)
            {
                Opening.instance.PlayCancelSE();
            }

            else if (GManager.instance != null)
            {
                GManager.instance.PlayCancelSE();
            }
        }

        _anim.SafeSetInt(OpenHash, 0);
        _anim.SafeSetInt(CloseHash, 1);
    }

    public void Init()
    {
        Off();
    }

    public void Open()
    {
        // === DCGO-CUSTOM:matchmusic begin ===
        EnsureReactiveMatchMusicToggle();
        // === DCGO-CUSTOM:matchmusic end ===

        if (ContinuousController.instance != null)
        {
            OptionUtility.InitToggle(
                toggle: _reverseOpponentsCardsToggle,
                onToggleChanged: OnReverseOpponentsCardsToggleChanged,
                value: ContinuousController.instance.reverseOpponentsCards
            );

            OptionUtility.InitToggle(
               toggle: _showCutInAnimationToggle,
               onToggleChanged: OnShowCutInAnimationToggleChanged,
               value: ContinuousController.instance.showCutInAnimation
            );

            OptionUtility.InitToggle(
                toggle: _turnSuspendedCardsToggle,
                onToggleChanged: OnTurnSuspendedCardsToggleChanged,
                value: ContinuousController.instance.turnSuspendedCards
            );

            OptionUtility.InitToggle(
                toggle: _checkBeforeEndingSelectionToggle,
                onToggleChanged: OnCheckBeforeEndingSelectionToggleChanged,
                value: ContinuousController.instance.checkBeforeEndingSelection
            );

            OptionUtility.InitToggle(
                toggle: _suspendedCardsDirectionIsLeftToggle,
                onToggleChanged: OnSuspendedCardsDirectionIsLeftToggleChanged,
                value: ContinuousController.instance.suspendedCardsDirectionIsLeft
            );

            OptionUtility.InitToggle(
                toggle: _autoEffectOrderToggle,
                onToggleChanged: OnAutoEffectOrderToggleChanged,
                value: ContinuousController.instance.autoEffectOrder
            );

            OptionUtility.InitToggle(
                toggle: _autoDeckBottomOrderToggle,
                onToggleChanged: OnAutoDeckBottomOrderToggleChanged,
                value: ContinuousController.instance.autoDeckBottomOrder
            );

            OptionUtility.InitToggle(
                toggle: _autoDeckTopOrderToggle,
                onToggleChanged: OnAutoDeckTopOrderToggleChanged,
                value: ContinuousController.instance.autoDeckTopOrder
            );

            OptionUtility.InitToggle(
                toggle: _autoMinDigivolutionCostToggle,
                onToggleChanged: OnAutoMinDigivolutionCostToggleChanged,
                value: ContinuousController.instance.autoMinDigivolutionCost
            );

            OptionUtility.InitToggle(
                toggle: _autoMaxCardCountToggle,
                onToggleChanged: OnAutoMaxCardCountToggleChanged,
                value: ContinuousController.instance.autoMaxCardCount
            );

            OptionUtility.InitToggle(
                toggle: _autoHatchToggle,
                onToggleChanged: OnAutoHatchToggleChanged,
                value: ContinuousController.instance.autoHatch
            );

            OptionUtility.InitToggle(
               toggle: _banlistToggle,
               onToggleChanged: OnUseBanlistToggleChanged,
               value: ContinuousController.instance.useBanlist
           );

            // === DCGO-CUSTOM:matchmusic begin ===
            OptionUtility.InitToggle(
                toggle: _reactiveMatchMusicToggle,
                onToggleChanged: OnReactiveMatchMusicToggleChanged,
                value: ContinuousController.instance.useReactiveMatchMusic
            );
            // === DCGO-CUSTOM:matchmusic end ===

            if (ContinuousController.instance.BanList.Restrictions.Count == 0 || PhotonNetwork.InRoom)
                _banlistToggle.interactable = false;
        }

        gameObject.SetActive(true);
        _anim.SafeSetInt(OpenHash, 1);
        _anim.SafeSetInt(CloseHash, 0);
    }

    // === DCGO-CUSTOM:matchmusic begin ===
    void EnsureReactiveMatchMusicToggle()
    {
        if (_reactiveToggleInjected && _reactiveMatchMusicToggle != null)
        {
            Transform existingRow = FindContentRow(_reactiveMatchMusicToggle.transform);
            if (existingRow != null)
                ApplyReactiveMatchMusicLabel(existingRow);
            return;
        }

        Toggle template = _showCutInAnimationToggle != null
            ? _showCutInAnimationToggle
            : _autoHatchToggle != null ? _autoHatchToggle : _banlistToggle;

        if (template == null)
            return;

        // Rows live under Content (GridLayoutGroup). The Toggle is nested:
        // Content / ShowCutInAnimation / ShowCutInAnimationButton / Toggle
        // Cloning the Toggle alone nests a duplicate switch on the same row.
        Transform templateRow = FindContentRow(template.transform);
        if (templateRow == null)
            return;

        Transform content = templateRow.parent;
        if (content == null)
            return;

        GameObject clone = Instantiate(templateRow.gameObject, content);
        clone.name = "ReactiveMatchMusic";
        clone.SetActive(true);
        clone.transform.SetSiblingIndex(templateRow.GetSiblingIndex() + 1);

        _reactiveMatchMusicToggle = clone.GetComponentInChildren<Toggle>(true);
        if (_reactiveMatchMusicToggle == null)
        {
            Destroy(clone);
            return;
        }

        _reactiveMatchMusicToggle.onValueChanged.RemoveAllListeners();

        // Row buttons use persistent Inspector onClick (HandleShowCutIn...); replace them.
        foreach (Button button in clone.GetComponentsInChildren<Button>(true))
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(HandleReactiveMatchMusicToggle);
        }

        ApplyReactiveMatchMusicLabel(clone.transform);
        if (content is RectTransform contentRt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
        _reactiveToggleInjected = true;
    }

    /// <summary>
    /// Walks up from a nested toggle to the direct child of the scroll Content grid.
    /// </summary>
    static Transform FindContentRow(Transform from)
    {
        if (from == null)
            return null;

        Transform t = from;
        while (t.parent != null)
        {
            if (t.parent.name == "Content" || t.parent.GetComponent<GridLayoutGroup>() != null)
                return t;
            t = t.parent;
        }

        // Fallback: grandparent of the toggle (Button's parent is usually the row).
        if (from.parent != null && from.parent.parent != null)
            return from.parent.parent;

        return null;
    }

    void ApplyReactiveMatchMusicLabel(Transform root)
    {
        if (root == null)
            return;

        string eng = "Reactive Match Music";
        string jpn = "リアクティブ対戦BGM";
        string localized = LocalizeUtility.GetLocalizedString(EngMessage: eng, JpnMessage: jpn);

        foreach (LocalizeTMPro localize in root.GetComponentsInChildren<LocalizeTMPro>(true))
        {
            localize._text_ENG = eng;
            localize._text_JPN = jpn;
        }

        foreach (Text text in root.GetComponentsInChildren<Text>(true))
        {
            if (string.Equals(text.text, "Switch_01", StringComparison.OrdinalIgnoreCase))
                continue;
            if (text.gameObject.name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            text.text = localized;
        }

        foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.Equals(tmp.text, "Switch_01", StringComparison.OrdinalIgnoreCase))
                continue;
            if (tmp.gameObject.name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            tmp.text = localized;
        }
    }

    public void OnReactiveMatchMusicToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _reactiveMatchMusicToggle,
            onToggleChanged: OnReactiveMatchMusicToggleChanged,
            settingRef: ref ContinuousController.instance.useReactiveMatchMusic,
            saveAction: ContinuousController.instance.SaveUseReactiveMatchMusic
        );
    }

    public void HandleReactiveMatchMusicToggle()
    {
        if (_reactiveMatchMusicToggle == null) return;
        OnReactiveMatchMusicToggleChanged(!_reactiveMatchMusicToggle.isOn);
    }
    // === DCGO-CUSTOM:matchmusic end ===

    #region Show cut in animation
    public void OnShowCutInAnimationToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _showCutInAnimationToggle,
            onToggleChanged: OnShowCutInAnimationToggleChanged,
            settingRef: ref ContinuousController.instance.showCutInAnimation,
            saveAction: ContinuousController.instance.SaveShowCutInAnimation
        );
    }

    public void HandleShowCutInAnimationToggle()
    {
        OnShowCutInAnimationToggleChanged(!_showCutInAnimationToggle.isOn);
    }
    #endregion

    #region Reverse opponent's cards
    public void OnReverseOpponentsCardsToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _reverseOpponentsCardsToggle,
            onToggleChanged: OnReverseOpponentsCardsToggleChanged,
            settingRef: ref ContinuousController.instance.reverseOpponentsCards,
            saveAction: ContinuousController.instance.SaveReverseOpponentsCards
        );

        GManager.OnReverseOpponentsCardsChanged?.Invoke();
    }

    public void HandleReverseOpponentsCardsToggle()
    {
        OnReverseOpponentsCardsToggleChanged(!_reverseOpponentsCardsToggle.isOn);
    }
    #endregion

    #region Turn suspended cards
    public void OnTurnSuspendedCardsToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _turnSuspendedCardsToggle,
            onToggleChanged: OnTurnSuspendedCardsToggleChanged,
            settingRef: ref ContinuousController.instance.turnSuspendedCards,
            saveAction: ContinuousController.instance.SaveTurnSuspendedCards
        );
    }

    public void HandleTurnSuspendedCardsToggle()
    {
        OnTurnSuspendedCardsToggleChanged(!_turnSuspendedCardsToggle.isOn);
    }
    #endregion

    #region Check before ending selection
    public void OnCheckBeforeEndingSelectionToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _checkBeforeEndingSelectionToggle,
            onToggleChanged: OnCheckBeforeEndingSelectionToggleChanged,
            settingRef: ref ContinuousController.instance.checkBeforeEndingSelection,
            saveAction: ContinuousController.instance.SaveCheckBeforeEndingSelection
        );
    }

    public void HandleCheckBeforeEndingSelectionToggle()
    {
        OnCheckBeforeEndingSelectionToggleChanged(!_checkBeforeEndingSelectionToggle.isOn);
    }
    #endregion

    #region Suspended card is left
    public void OnSuspendedCardsDirectionIsLeftToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _suspendedCardsDirectionIsLeftToggle,
            onToggleChanged: OnSuspendedCardsDirectionIsLeftToggleChanged,
            settingRef: ref ContinuousController.instance.suspendedCardsDirectionIsLeft,
            saveAction: ContinuousController.instance.SaveSuspendedCardsDirectionIsLeft
        );
    }

    public void HandleSuspendedCardsDirectionIsLeftToggle()
    {
        OnSuspendedCardsDirectionIsLeftToggleChanged(!_suspendedCardsDirectionIsLeftToggle.isOn);
    }
    #endregion

    #region Auto effect order

    public void OnAutoEffectOrderToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _autoEffectOrderToggle,
            onToggleChanged: OnAutoEffectOrderToggleChanged,
            settingRef: ref ContinuousController.instance.autoEffectOrder,
            saveAction: ContinuousController.instance.SaveAutoEffectOrder
        );
    }

    public void HandleAutoEffectOrderToggle()
    {
        OnAutoEffectOrderToggleChanged(!_autoEffectOrderToggle.isOn);
    }
    #endregion

    #region Auto deck bottom order

    public void OnAutoDeckBottomOrderToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _autoDeckBottomOrderToggle,
            onToggleChanged: OnAutoDeckBottomOrderToggleChanged,
            settingRef: ref ContinuousController.instance.autoDeckBottomOrder,
            saveAction: ContinuousController.instance.SaveAutoDeckBottomOrder
        );
    }

    public void HandleAutoDeckBottomOrderToggle()
    {
        OnAutoDeckBottomOrderToggleChanged(!_autoDeckBottomOrderToggle.isOn);
    }
    #endregion

    #region Auto deck top order

    public void OnAutoDeckTopOrderToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _autoDeckTopOrderToggle,
            onToggleChanged: OnAutoDeckTopOrderToggleChanged,
            settingRef: ref ContinuousController.instance.autoDeckTopOrder,
            saveAction: ContinuousController.instance.SaveAutoDeckTopOrder
        );
    }

    public void HandleAutoDeckTopOrderToggle()
    {
        OnAutoDeckTopOrderToggleChanged(!_autoDeckTopOrderToggle.isOn);
    }
    #endregion

    #region Auto min digivolution cost

    public void OnAutoMinDigivolutionCostToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _autoMinDigivolutionCostToggle,
            onToggleChanged: OnAutoMinDigivolutionCostToggleChanged,
            settingRef: ref ContinuousController.instance.autoMinDigivolutionCost,
            saveAction: ContinuousController.instance.SaveAutoMinDigivolutionCost
        );
    }

    public void HandleAutoMinDigivolutionCostToggle()
    {
        OnAutoMinDigivolutionCostToggleChanged(!_autoMinDigivolutionCostToggle.isOn);
    }
    #endregion

    #region Auto max card count

    public void OnAutoMaxCardCountToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _autoMaxCardCountToggle,
            onToggleChanged: OnAutoMaxCardCountToggleChanged,
            settingRef: ref ContinuousController.instance.autoMaxCardCount,
            saveAction: ContinuousController.instance.SaveAutoMaxCardCount
        );
    }

    public void HandleAutoMaxCardCountToggle()
    {
        OnAutoMaxCardCountToggleChanged(!_autoMaxCardCountToggle.isOn);
    }
    #endregion

    #region Auto hatch

    public void OnAutoHatchToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _autoHatchToggle,
            onToggleChanged: OnAutoHatchToggleChanged,
            settingRef: ref ContinuousController.instance.autoHatch,
            saveAction: ContinuousController.instance.SaveAutoHatch
        );
    }

    public void HandleAutoHatchToggle()
    {
        OnAutoHatchToggleChanged(!_autoHatchToggle.isOn);
    }
    #endregion

    #region Use Banlist

    public void OnUseBanlistToggleChanged(bool value)
    {
        if (ContinuousController.instance == null) return;

        OptionUtility.OnToggleChanged(
            value: value,
            toggle: _banlistToggle,
            onToggleChanged: OnUseBanlistToggleChanged,
            settingRef: ref ContinuousController.instance.useBanlist,
            saveAction: ContinuousController.instance.SaveUseBanlist
        );
    }

    public void HandleUseBanlistToggle()
    {
        OnUseBanlistToggleChanged(!_banlistToggle.isOn);
    }
    #endregion
}
