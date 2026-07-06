using UnityEngine;

/// <summary>
/// BUG 1 FIX — "Blue Screen" on Home navigation.
///
/// Reusable screen navigation helper. The blue background appears whenever the current
/// screen is hidden but the target screen is not (re)activated, leaving the camera's
/// clear color showing. GoToHome avoids this by activating the Home screen FIRST and only
/// then hiding the current screen — so there is never a frame with no active panel.
///
/// Attach to a persistent GameObject (e.g. an empty "NavigationManager" under the Canvas
/// or a managers object). Wire a Home button's OnClick to GoToHome and drag the current
/// screen + home screen GameObjects into the two slots.
/// </summary>
public class NavigationManager : MonoBehaviour
{
    public static NavigationManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Switches to the Home screen safely. Activates <paramref name="homeScreen"/> first
    /// (and brings it to front), then deactivates <paramref name="currentScreen"/>.
    /// </summary>
    public void GoToHome(GameObject currentScreen, GameObject homeScreen)
    {
        if (homeScreen != null)
        {
            homeScreen.SetActive(true);
            homeScreen.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogWarning("[NavigationManager] GoToHome: homeScreen reference is null — cannot show Home (this is what causes the blue screen).");
        }

        if (currentScreen != null && currentScreen != homeScreen)
            currentScreen.SetActive(false);
    }
}
