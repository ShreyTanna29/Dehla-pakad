using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A lightweight dropdown built from existing UI children: a toggle bar, a label, an options panel
/// (hidden by default) containing one button per option, and a full-area blocker that closes the
/// menu on an outside click. Clicking the bar opens the list; clicking an option selects it, updates
/// the label, closes the menu and raises <see cref="OnSelected"/>. The builder wires the references;
/// this component only handles open/close/select behaviour.
/// </summary>
public class SimpleDropdown : MonoBehaviour
{
    public Button toggleButton;
    public TMP_Text label;
    public RectTransform optionsPanel;
    public GameObject blocker;
    public List<Button> optionButtons = new List<Button>();
    public List<string> optionValues = new List<string>();

    public int Current { get; private set; }

    /// <summary>Raised with (index, value) when the user selects an option.</summary>
    public Action<int, string> OnSelected;

    bool _wired;

    void OnEnable()
    {
        Wire();
        Close();
    }

    void Wire()
    {
        if (_wired) return;

        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(Toggle);
        }
        for (int i = 0; i < optionButtons.Count; i++)
        {
            int idx = i;
            if (optionButtons[i] == null) continue;
            optionButtons[i].onClick.RemoveAllListeners();
            optionButtons[i].onClick.AddListener(() => Select(idx));
        }
        if (blocker != null)
        {
            var bb = blocker.GetComponent<Button>();
            if (bb == null) bb = blocker.AddComponent<Button>();
            bb.transition = Selectable.Transition.None;
            bb.onClick.RemoveAllListeners();
            bb.onClick.AddListener(Close);
        }
        _wired = true;
    }

    public void Toggle()
    {
        bool open = optionsPanel != null && !optionsPanel.gameObject.activeSelf;
        SetOpen(open);
    }

    void SetOpen(bool open)
    {
        if (blocker != null)
        {
            blocker.SetActive(open);
            if (open) blocker.transform.SetAsLastSibling();
        }
        if (optionsPanel != null)
        {
            optionsPanel.gameObject.SetActive(open);
            if (open) optionsPanel.SetAsLastSibling();
        }
    }

    public void Close() => SetOpen(false);

    public void Select(int idx)
    {
        Current = idx;
        if (label != null && idx >= 0 && idx < optionValues.Count)
            label.text = optionValues[idx];
        Close();
        OnSelected?.Invoke(idx, idx >= 0 && idx < optionValues.Count ? optionValues[idx] : "");
    }

    /// <summary>Sets the current value/label without raising the callback.</summary>
    public void SetValueSilent(int idx)
    {
        Current = idx;
        if (label != null && idx >= 0 && idx < optionValues.Count)
            label.text = optionValues[idx];
    }
}
