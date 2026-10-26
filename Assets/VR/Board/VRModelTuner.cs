#if DCGO_VR
using System;
using System.Collections.Generic;
using UnityEngine;

// === DCGO-CUSTOM:vr ===
// Debug panel: resize each 3D model on the field by hand (−/+ per model, press the
// name to reset). Sizes are saved per model file (VRModelTuning) and apply to every
// future duel. Also tunes the opponent's standing cards and the global model size.
public class VRModelTuner : MonoBehaviour
{
    const float Step = 0.045f;
    const float Width = 0.46f;

    Board3D _board;
    Transform _frame;
    readonly List<GameObject> _rows = new List<GameObject>();
    float _nextRefresh;
    int _shownCount = -1;

    public bool IsOpen => gameObject.activeSelf;

    public static VRModelTuner Create(Transform parent, Board3D board)
    {
        GameObject go = new GameObject("ModelTuner") { layer = VRRig.Layer };
        go.transform.SetParent(parent, false);
        VRModelTuner tuner = go.AddComponent<VRModelTuner>();
        tuner._board = board;

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "Frame";
        frame.layer = VRRig.Layer;
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(go.transform, false);
        frame.GetComponent<Renderer>().sharedMaterial = VRMaterials.Unlit(new Color(0.05f, 0.09f, 0.14f));
        tuner._frame = frame.transform;

        go.SetActive(false);
        return tuner;
    }

    public void Open(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        gameObject.SetActive(true);
        Rebuild();
    }

    public void Close() => gameObject.SetActive(false);

    void Update()
    {
        // Models come and go during the duel: rebuild when the list changes.
        if (Time.unscaledTime < _nextRefresh)
        {
            return;
        }

        _nextRefresh = Time.unscaledTime + 1f;

        if (Tunables().Count != _shownCount)
        {
            Rebuild();
        }
    }

    List<KeyValuePair<string, string>> Tunables()
    {
        VRFieldModels models = _board != null ? _board.FieldModels : null;
        return models != null ? models.Tunables() : new List<KeyValuePair<string, string>>();
    }

    void Rebuild()
    {
        foreach (GameObject row in _rows)
        {
            Destroy(row);
        }

        _rows.Clear();
        List<KeyValuePair<string, string>> tunables = Tunables();
        _shownCount = tunables.Count;
        int count = 0;

        AddLabel("TUNE 3D MODEL SIZES", count++);
        AddRow(() => "Cards under models  x" + VRModelTuning.CardSize.ToString("0.##"), count++,
            delta => VRModelTuning.CardSize = delta == 0f ? 1f : VRModelTuning.CardSize * delta);
        AddRow(() => "All Digimon  x" + VRSettings.ModelScale.ToString("0.###"), count++,
            delta =>
            {
                VRSettings.ModelScale = delta == 0f ? 1f : Mathf.Clamp(VRSettings.ModelScale * delta, 0.05f, 10f);
                VRSession.Instance?.Board?.RefreshModels();
            });

        if (tunables.Count == 0)
        {
            AddLabel("No 3D models on the field yet", count++);
        }

        for (int i = 0; i < tunables.Count && i < 14; i++)
        {
            string label = tunables[i].Key;
            string key = tunables[i].Value;
            AddRow(() => label + "  x" + VRModelTuning.Get(key).ToString("0.###"), count++,
                delta =>
                {
                    if (delta == 0f) VRModelTuning.Clear(key); else VRModelTuning.Set(key, VRModelTuning.Get(key) * delta);
                });
        }

        AddButton("Close", count++, Close);

        float height = count * Step + 0.03f;
        float top = height * 0.5f;
        _frame.localScale = new Vector3(Width + 0.03f, height, 0.004f);
        _frame.localPosition = new Vector3(0f, 0f, 0.006f);

        for (int i = 0; i < _rows.Count; i++)
        {
            Vector3 p = _rows[i].transform.localPosition;
            _rows[i].transform.localPosition = new Vector3(p.x, top - 0.035f - (int)p.y * Step, -0.002f);
        }
    }

    // Rows are positioned in Rebuild: the row index is parked in localPosition.y.
    GameObject NewRow(int index)
    {
        GameObject row = new GameObject("Row") { layer = VRRig.Layer };
        row.transform.SetParent(transform, false);
        row.transform.localPosition = new Vector3(0f, index, 0f);
        _rows.Add(row);
        return row;
    }

    void AddLabel(string text, int index)
    {
        GameObject row = NewRow(index);
        VRLabel.Create(row.transform, Vector3.zero, Quaternion.identity, new Vector2(Width, 0.036f), text, new Color(0.55f, 0.95f, 1f), new Color(0.05f, 0.09f, 0.14f));
    }

    void AddButton(string text, int index, Action action)
    {
        GameObject row = NewRow(index);
        VRButton3D.CreatePlate(row.transform, Vector3.zero, new Vector2(Width, 0.038f), text, action);
    }

    // [ − ]  label (press = reset)  [ + ]
    void AddRow(Func<string> label, int index, Action<float> change)
    {
        GameObject row = NewRow(index);
        float middle = Width - 0.13f;
        VRButton3D name = null;
        Action refresh = () => name?.SetLabel(label());
        VRButton3D.CreatePlate(row.transform, new Vector3(-Width * 0.5f + 0.03f, 0f, 0f), new Vector2(0.055f, 0.038f), "-", () => { change(0.85f); refresh(); });
        name = VRButton3D.CreatePlate(row.transform, Vector3.zero, new Vector2(middle, 0.038f), label(), () => { change(0f); refresh(); });
        VRButton3D.CreatePlate(row.transform, new Vector3(Width * 0.5f - 0.03f, 0f, 0f), new Vector2(0.055f, 0.038f), "+", () => { change(1f / 0.85f); refresh(); });
    }
}
#endif
