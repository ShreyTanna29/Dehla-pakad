using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Gives every account a short, unique, human-friendly 10-digit UID (PUBG / Free Fire style).
///
/// Firebase layout:
///   users/{firebaseUid}/gameUid = "5172834906"   (account -> its UID)
///   uids/{gameUid}              = firebaseUid     (UID -> account, used for search + uniqueness)
///
/// The UID is generated once on first login and then reused forever (server is source of truth,
/// PlayerPrefs holds a local copy for instant UI display).
/// </summary>
public static class GameUidService
{
    const string FirebaseDatabaseUrl = "https://dehlapakad-c207c-default-rtdb.firebaseio.com/";
    const string PrefsGameUid = "PlayerGameUid";
    const int UidLength = 10;
    const int MaxAttempts = 8;

    /// <summary>Locally cached UID for the signed-in player (empty until assigned). For instant UI.</summary>
    public static string LocalGameUid => PlayerPrefs.GetString(PrefsGameUid, "");

    static DatabaseReference Root =>
        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference;

    /// <summary>
    /// Ensures the given account has a UID. Returns the existing one if present, otherwise
    /// generates and reserves a new unique UID. Always invokes <paramref name="onReady"/> on the
    /// main thread (uid may be empty if Firebase is unavailable).
    /// </summary>
    public static void EnsureGameUid(string firebaseUid, Action<string> onReady = null)
    {
        if (string.IsNullOrEmpty(firebaseUid))
        {
            onReady?.Invoke(LocalGameUid);
            return;
        }

        Root.Child("users").Child(firebaseUid).Child("gameUid")
            .GetValueAsync().ContinueWithOnMainThread(task =>
            {
                if (!task.IsFaulted && !task.IsCanceled && task.Result != null && task.Result.Exists)
                {
                    string existing = task.Result.Value?.ToString();
                    if (!string.IsNullOrEmpty(existing))
                    {
                        CacheLocal(existing);
                        onReady?.Invoke(existing);
                        return;
                    }
                }

                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogWarning("[GameUid] Could not read existing UID: "
                        + (task.Exception?.Message ?? "unknown"));
                    onReady?.Invoke(LocalGameUid);
                    return;
                }

                GenerateUnique(firebaseUid, 0, onReady);
            });
    }

    static void GenerateUnique(string firebaseUid, int attempt, Action<string> onReady)
    {
        if (attempt >= MaxAttempts)
        {
            Debug.LogError("[GameUid] Could not allocate a unique UID after retries.");
            onReady?.Invoke(LocalGameUid);
            return;
        }

        string candidate = RandomUid();
        DatabaseReference uidRef = Root.Child("uids").Child(candidate);

        uidRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogWarning("[GameUid] UID lookup failed: " + (task.Exception?.Message ?? "unknown"));
                onReady?.Invoke(LocalGameUid);
                return;
            }

            // Candidate already taken -> try a fresh one.
            if (task.Result != null && task.Result.Exists)
            {
                GenerateUnique(firebaseUid, attempt + 1, onReady);
                return;
            }

            // Reserve UID and link it to the account in a single multi-path write.
            var updates = new Dictionary<string, object>
            {
                { "uids/" + candidate, firebaseUid },
                { "users/" + firebaseUid + "/gameUid", candidate }
            };

            Root.UpdateChildrenAsync(updates).ContinueWithOnMainThread(claim =>
            {
                if (claim.IsFaulted || claim.IsCanceled)
                {
                    Debug.LogError("[GameUid] Claim failed: " + (claim.Exception?.Message ?? "unknown"));
                    onReady?.Invoke(LocalGameUid);
                    return;
                }

                CacheLocal(candidate);
                Debug.Log($"[GameUid] Assigned UID {candidate} to {firebaseUid}");
                onReady?.Invoke(candidate);
            });
        });
    }

    /// <summary>Looks up which account owns a given UID. Returns null via callback if not found.</summary>
    public static void ResolveFirebaseUid(string gameUid, Action<string> onResult)
    {
        if (string.IsNullOrEmpty(gameUid))
        {
            onResult?.Invoke(null);
            return;
        }

        Root.Child("uids").Child(gameUid).GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists)
            {
                onResult?.Invoke(null);
                return;
            }
            onResult?.Invoke(task.Result.Value?.ToString());
        });
    }

    /// <summary>True if the text is a UID-shaped token (all digits, full length).</summary>
    public static bool LooksLikeUid(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length != UidLength) return false;
        for (int i = 0; i < text.Length; i++)
            if (!char.IsDigit(text[i])) return false;
        return true;
    }

    static void CacheLocal(string uid)
    {
        PlayerPrefs.SetString(PrefsGameUid, uid);
        PlayerPrefs.Save();
    }

    static string RandomUid()
    {
        var sb = new StringBuilder(UidLength);
        sb.Append((char)('1' + UnityEngine.Random.Range(0, 9))); // first digit 1-9 (always 10 digits)
        for (int i = 1; i < UidLength; i++)
            sb.Append((char)('0' + UnityEngine.Random.Range(0, 10)));
        return sb.ToString();
    }
}
