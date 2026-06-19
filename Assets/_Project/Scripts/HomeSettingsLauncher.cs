using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lives on an always-active Home object and wires the existing home "Button_Settings" to open the
/// (otherwise inactive) Settings panel. Resolves both by name on Start so no fragile inspector
/// references are required, and re-resolves defensively if the panel was rebuilt.
/// </summary>
public class HomeSettingsLauncher : MonoBehaviour
{
    public string settingsButtonName = "Button_Settings";
    public string settingsPanelName = "Panel_Settings";

    Button _button;
    HomeSettingsController _panel;

    void Start() { Bind(); }
    void OnEnable() { Bind(); }

    void Bind()
    {
        if (_button == null)
        {
            Transform bt = FindDeepInScene(settingsButtonName);
            if (bt != null) _button = bt.GetComponent<Button>();
        }
        if (_panel == null)
        {
            HomeSettingsController[] all = Resources.FindObjectsOfTypeAll<HomeSettingsController>();
            foreach (var c in all)
                if (c != null && c.gameObject.scene.IsValid()) { _panel = c; break; }
        }
        if (_button != null && _panel != null)
        {
            _button.onClick.RemoveListener(OpenPanel);
            _button.onClick.AddListener(OpenPanel);
        }
    }

    void OpenPanel()
    {
        if (_panel == null) Bind();
        if (_panel != null) _panel.Open();
    }

    static Transform FindDeepInScene(string name)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == name && t.gameObject.scene.IsValid())
                return t;
        return null;
    }
}
