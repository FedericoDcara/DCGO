#if DCGO_VR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// === DCGO-CUSTOM:vr ===
// Floating panel that mirrors whatever the game is currently asking:
//   title  : the prompt message ("Select 1 Digimon card...")
//   cards  : the revealed cards of the "select card" panel (deck top, trash...),
//            shown as real 3D cards; touch or laser them to choose
//   buttons: every button of the prompt (End Selection, No Selection, Yes/No,
//            choices...), pressed with touch or laser
// Placement keeps the field visible:
//   - prompts that only have buttons (you choose on the field or in your hand)
//     dock as a compact panel above the wrist disk, facing you;
//   - prompts with cards to choose from open to your LEFT, angled toward you.
// It closes when the prompt ends.
// Choosing Digimon / Tamers on the field or cards in your hand is done directly
// on the hologram / your hand (they glow blue; chosen ones glow orange).
public class VRPromptPanel : MonoBehaviour
{
    const float ScanInterval = 0.15f;
    const float CardScale = 0.75f;
    const float CellW = 0.08f;
    const float CellH = 0.11f;
    float ButtonW => _wrist ? 0.14f : 0.17f;
    const float ButtonH = 0.038f;
    int ButtonsPerRow => _wrist ? 2 : 3;
    float MinWidth => _wrist ? 0.32f : 0.6f;
    const float SideYaw = -38f;      // degrees to the left of where you look
    const float SideDistance = 0.62f;

    Board3D _board;
    Transform _root;
    Transform _frame;
    VRLabel _title;
    readonly List<VRButton3D> _plates = new List<VRButton3D>();
    readonly List<Button> _buttons = new List<Button>();
    readonly List<string> _labels = new List<string>();
    readonly List<Button> _boundButtons = new List<Button>();

    // One pressable card plate per choice of the select panel (with glow, effect name, order number).
    class Entry
    {
        public HandCard Source;
        public VRButton3D Card;
        public Renderer Glow;
        public VRLabel Name;
        public VRLabel Number;
    }

    readonly List<Entry> _entries = new List<Entry>();
    MaterialPropertyBlock _block;
    string _signature = "";
    float _nextScan;
    bool _open;
    Vector3 _position;     // board space
    Quaternion _rotation;  // board space
    float _cardsTop;
    bool _wrist;          // docked above the wrist disk
    bool _hadCards;

    public static VRPromptPanel Create(Board3D board)
    {
        GameObject go = new GameObject("PromptPanel") { layer = VRRig.Layer };
        go.transform.SetParent(board.transform, false);
        VRPromptPanel panel = go.AddComponent<VRPromptPanel>();
        panel._board = board;
        panel._root = go.transform;

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "Frame";
        frame.layer = VRRig.Layer;
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(go.transform, false);
        frame.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.05f, 0.09f, 0.14f));
        panel._frame = frame.transform;

        panel._title = VRLabel.Create(go.transform, Vector3.zero, Quaternion.identity, new Vector2(0.56f, 0.06f), "", new Color(0.85f, 0.95f, 1f), new Color(0.05f, 0.09f, 0.14f));
        panel._title.enableWordWrapping = true;
        panel.SetVisible(false);
        return panel;
    }

    void SetVisible(bool visible)
    {
        _frame.gameObject.SetActive(visible);
        _title.gameObject.SetActive(visible);

        foreach (VRButton3D plate in _plates)
        {
            if (plate != null)
            {
                plate.gameObject.SetActive(visible);
            }
        }
    }

    void Update()
    {
        if (_board == null)
        {
            return;
        }

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + ScanInterval;
            Scan();
        }

        if (_open)
        {
            UpdateEntries();

            if (_wrist && _board.Head != null)
            {
                // Docked on the arm: always turn to face you.
                Vector3 away = _root.position - _board.Head.position;

                if (away.sqrMagnitude > 1e-4f)
                {
                    _root.rotation = Quaternion.LookRotation(away, Vector3.up);
                }
            }
        }
    }

    void Scan()
    {
        string message = VRPromptBridge.Message;
        VRPromptBridge.CollectButtons(_buttons, _labels);
        List<HandCard> cards = VRPromptBridge.PanelCards;

        bool hasPrompt = _buttons.Count > 0 || cards.Count > 0;

        if (!hasPrompt)
        {
            if (_open)
            {
                Close();
            }

            return;
        }

        StringBuilder sb = new StringBuilder(message);
        sb.Append('|').Append(cards.Count);

        foreach (HandCard card in cards)
        {
            sb.Append(',').Append(card.GetInstanceID());
        }

        foreach (string label in _labels)
        {
            sb.Append('|').Append(label);
        }

        string signature = sb.ToString();

        bool hasCards = cards.Count > 0;

        if (!_open || hasCards != _hadCards)
        {
            _hadCards = hasCards;
            Open();
        }

        if (signature != _signature)
        {
            _signature = signature;
            Rebuild(message, cards.Count);
        }
    }

    void Open()
    {
        Transform head = _board.Head;

        if (head == null)
        {
            return;
        }

        VRDuelDisk disk = _board.Disk;
        _wrist = !_hadCards && _board.DuelStyle && disk != null && disk.isActiveAndEnabled;

        if (_wrist)
        {
            // Compact panel floating just above the wrist disk.
            _root.SetParent(disk.transform, false);
            _root.localPosition = new Vector3(-0.1f, 0.15f, 0.02f);
            _root.localScale = Vector3.one;
        }
        else
        {
            // To the left of where you are looking, angled toward you, slightly low.
            Vector3 forward = head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            Vector3 side = Quaternion.Euler(0f, SideYaw, 0f) * forward;
            Vector3 world = head.position + side * SideDistance + Vector3.down * 0.08f;
            Quaternion worldRotation = Quaternion.LookRotation(side, Vector3.up) * Quaternion.Euler(8f, 0f, 0f);

            _root.SetParent(_board.transform, false);
            _position = _board.transform.InverseTransformPoint(world);
            _rotation = Quaternion.Inverse(_board.transform.rotation) * worldRotation;
            _root.localPosition = _position;
            _root.localRotation = _rotation;
            // Undo the board's scale so the panel keeps its real size on the hologram.
            float boardScale = _board.transform.lossyScale.x;
            _root.localScale = Vector3.one / Mathf.Max(0.01f, boardScale);
        }

        _open = true;
        _signature = "";
        SetVisible(true);
        Debug.Log("[DCGO VR] Prompt panel opened: " + VRPromptBridge.Message + " | " + VRPromptBridge.DescribeHandSelection());
    }

    void Close()
    {
        _open = false;
        _signature = "";
        ClearEntries();
        SetVisible(false);
    }

    void Rebuild(string message, int cardCount)
    {
        foreach (VRButton3D plate in _plates)
        {
            if (plate != null)
            {
                Destroy(plate.gameObject);
            }
        }

        _plates.Clear();
        _boundButtons.Clear();
        ClearEntries();

        int cols = cardCount <= 0 ? 0 : Mathf.Min(7, cardCount);
        int cardRows = cols == 0 ? 0 : Mathf.CeilToInt(cardCount / (float)cols);
        int buttonRows = Mathf.CeilToInt(_buttons.Count / (float)ButtonsPerRow);

        float titleH = string.IsNullOrEmpty(message) ? 0f : 0.07f;
        float cardsH = cardRows * CellH;
        float buttonsH = buttonRows * (ButtonH + 0.01f);
        float totalH = titleH + cardsH + buttonsH + 0.04f;
        float width = Mathf.Max(MinWidth, cols * CellW + 0.04f, Mathf.Min(_buttons.Count, ButtonsPerRow) * (ButtonW + 0.01f) + 0.04f);
        float top = totalH * 0.5f;

        _frame.localPosition = new Vector3(0f, 0f, 0.006f);
        _frame.localScale = new Vector3(width, totalH, 0.004f);

        _title.gameObject.SetActive(titleH > 0f);
        _title.text = message;
        _title.Size = new Vector2(width - 0.04f, 0.06f);
        _title.transform.localPosition = new Vector3(0f, top - 0.02f - 0.03f, -0.001f);

        _cardsTop = top - 0.02f - titleH;

        float buttonsTop = _cardsTop - cardsH;
        BuildEntries(cols);

        for (int i = 0; i < _buttons.Count; i++)
        {
            int row = i / ButtonsPerRow;
            int inRow = Mathf.Min(ButtonsPerRow, _buttons.Count - row * ButtonsPerRow);
            int col = i % ButtonsPerRow;
            float x = (col - (inRow - 1) * 0.5f) * (ButtonW + 0.01f);
            float y = buttonsTop - row * (ButtonH + 0.01f) - ButtonH * 0.5f - 0.005f;
            Button source = _buttons[i];
            VRButton3D plate = VRButton3D.CreatePlate(_root, new Vector3(x, y, -0.002f), new Vector2(ButtonW, ButtonH), _labels[i], () => Press(source));
            _plates.Add(plate);
            _boundButtons.Add(source);
        }
    }

    void Press(Button button)
    {
        if (button != null && button.isActiveAndEnabled && button.interactable)
        {
            Debug.Log("[DCGO VR] Prompt button: " + button.gameObject.name);
            button.onClick.Invoke();
            _nextScan = 0f; // refresh right away
        }
    }

    void BuildEntries(int cols)
    {
        List<HandCard> cards = VRPromptBridge.PanelCards;

        if (cards.Count == 0)
        {
            return;
        }

        if (_block == null)
        {
            _block = new MaterialPropertyBlock();
        }

        int rows = Mathf.CeilToInt(cards.Count / (float)cols);
        Vector2 size = new Vector2(Card3D.Width * CardScale, Card3D.Height * CardScale);

        for (int i = 0; i < cards.Count; i++)
        {
            HandCard source = cards[i];
            int row = i / cols;
            int inRow = row == rows - 1 ? cards.Count - row * cols : cols;
            float x = (i % cols - (inRow - 1) * 0.5f) * CellW;
            float y = _cardsTop - row * CellH - CellH * 0.5f;

            Entry entry = new Entry { Source = source };
            Sprite sprite = source.CardImage != null ? source.CardImage.sprite : null;
            entry.Card = VRButton3D.CreateCard(_root, new Vector3(x, y + 0.008f, -0.004f), size, sprite, () => VRPromptBridge.ClickPanelCard(source));

            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Glow";
            glow.layer = VRRig.Layer;
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(entry.Card.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            glow.transform.localScale = new Vector3(size.x + 0.012f, size.y + 0.012f, 1f);
            entry.Glow = glow.GetComponent<Renderer>();
            entry.Glow.sharedMaterial = VRMaterials.Unlit(Color.white);
            glow.SetActive(false);

            // Effect name under the card (effect order popup), order number badge on top.
            entry.Name = VRLabel.Create(_root, new Vector3(x, y - size.y * 0.5f - 0.004f, -0.004f), Quaternion.identity, new Vector2(CellW - 0.006f, 0.016f), "", new Color(0.85f, 0.95f, 1f), new Color(0.05f, 0.09f, 0.14f));
            entry.Number = VRLabel.Create(entry.Card.transform, new Vector3(-size.x * 0.32f, size.y * 0.36f, -0.006f), Quaternion.identity, new Vector2(0.024f, 0.024f), "", Color.white, VRPromptBridge.Selected);
            entry.Name.gameObject.SetActive(false);
            entry.Number.gameObject.SetActive(false);
            _entries.Add(entry);
        }
    }

    void ClearEntries()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Name != null)
            {
                Destroy(entry.Name.gameObject);
            }

            if (entry.Card != null)
            {
                Destroy(entry.Card.gameObject);
            }
        }

        _entries.Clear();
    }

    // Keeps each card plate in sync with its entry on the screen: picture, glow
    // (blue = can be chosen, orange = chosen), effect name and order number.
    void UpdateEntries()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Source == null || entry.Card == null)
            {
                continue;
            }

            Color? glow = VRPromptBridge.PanelOutline(entry.Source);
            entry.Glow.gameObject.SetActive(glow.HasValue);

            if (glow.HasValue)
            {
                entry.Glow.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", glow.Value);
                _block.SetColor("_Color", glow.Value);
                entry.Glow.SetPropertyBlock(_block);
            }

            if (entry.Source.CardImage != null)
            {
                entry.Card.SetSprite(entry.Source.CardImage.sprite);
            }

            string skill = entry.Source.SkillNameText != null && entry.Source.SkillNameText.gameObject.activeInHierarchy ? entry.Source.SkillNameText.text : "";

            if (string.IsNullOrWhiteSpace(skill) && entry.Source.titleText != null && entry.Source.titleText.gameObject.activeInHierarchy)
            {
                skill = entry.Source.titleText.text;
            }

            bool hasSkill = !string.IsNullOrWhiteSpace(skill);
            entry.Name.gameObject.SetActive(hasSkill);

            if (hasSkill)
            {
                entry.Name.text = skill;
            }

            string number = entry.Source.cardPositionText != null && entry.Source.cardPositionText.gameObject.activeInHierarchy ? entry.Source.cardPositionText.text : "";
            bool hasNumber = !string.IsNullOrWhiteSpace(number);
            entry.Number.gameObject.SetActive(hasNumber);

            if (hasNumber)
            {
                entry.Number.text = number;
            }
        }
    }
}
#endif
