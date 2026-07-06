using System.Collections;
using UnityEngine;
using Firebase.Auth;

/// <summary>
/// Always-active boot helper that guarantees the Firebase friend-request / accept / invite
/// listeners in <see cref="PlayWithFriendsManager"/> are running, even though that manager lives
/// on an INACTIVE panel (Canvas/PlayWithFriendsPanel) until matchmaking / play-with-friends is
/// opened.
///
/// Without this, a player sitting on the home screen never binds the listeners, so friend
/// requests and game invites are silently dropped, and PlayWithFriendsManager.Instance stays
/// null (which made the Add-Friend / Replace buttons fail with "operation failed" / crash home).
///
/// Self-instantiates after the scene loads — no scene wiring required.
/// </summary>
public class SocialServiceBootstrap : MonoBehaviour
{
    static SocialServiceBootstrap _instance;
    public static SocialServiceBootstrap Instance => _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_instance != null) return;
        var go = new GameObject("SocialServiceBootstrap");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SocialServiceBootstrap>();
    }

    void Start() => StartCoroutine(BootRoutine());

    IEnumerator BootRoutine()
    {
        var wait = new WaitForSeconds(0.5f);

        while (true)
        {
            try
            {
                PlayWithFriendsManager mgr = ResolveManager();
                if (mgr != null)
                {
                    if (PlayWithFriendsManager.Instance == null)
                        PlayWithFriendsManager.Instance = mgr;

                    // Safe to call repeatedly; all internal binds are guarded. This also hooks the
                    // manager into Firebase auth so the listeners rebind to the real account id.
                    mgr.StartSocialServicesHeadless();

                    // Once Firebase reports a signed-in user or we have a valid simulated editor login,
                    // the listeners are bound to the correct account id and the manager's own auth hook
                    // or transition keeps them correct, so the bootstrap is complete.
                    string myId = mgr.GetAccountUserId();
                    if ((FirebaseAuth.DefaultInstance != null && FirebaseAuth.DefaultInstance.CurrentUser != null) ||
                        (Application.isEditor && !string.IsNullOrEmpty(myId) && myId.Contains("simulate")))
                    {
                        yield break;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SocialBootstrap] Waiting for Firebase initialization... " + e.Message);
            }

            yield return wait;
        }
    }

    static PlayWithFriendsManager ResolveManager()
    {
        if (PlayWithFriendsManager.Instance != null)
            return PlayWithFriendsManager.Instance;

        // The manager sits on an inactive panel, so the active-only finders won't see it.
        var all = Resources.FindObjectsOfTypeAll<PlayWithFriendsManager>();
        foreach (var m in all)
        {
            if (m == null) continue;
            if (!m.gameObject.scene.IsValid()) continue; // skip prefab/asset instances
            return m;
        }
        return null;
    }
}
