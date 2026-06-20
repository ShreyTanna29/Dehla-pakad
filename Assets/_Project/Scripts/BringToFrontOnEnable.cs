using UnityEngine;

/// <summary>
/// Moves this object to the last sibling position whenever it is enabled, so an overlay panel
/// always renders on top of whatever panel was open when it was triggered (e.g. opening the Shop
/// from inside the Player Profile panel). Standalone helper — modifies nothing else.
/// </summary>
[DisallowMultipleComponent]
public class BringToFrontOnEnable : MonoBehaviour
{
    private void OnEnable()
    {
        transform.SetAsLastSibling();
    }
}
