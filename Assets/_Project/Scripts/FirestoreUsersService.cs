using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;

/// <summary>
/// Shared Firestore access for <c>users/{uid}</c> and <c>uids/{gameUid}</c>.
/// Field names match the project schema: username, avatar_id, coins, lastActive, isActiveNow, isBot.
/// Document ID for real players is the Firebase Auth UID (bots may use a display-name doc id).
/// </summary>
public static class FirestoreUsersService
{
    public const string FieldUsername = "username";
    public const string FieldAvatarId = "avatar_id";
    public const string FieldCoins = "coins";
    public const string FieldLastActive = "lastActive";
    public const string FieldIsActiveNow = "isActiveNow";
    public const string FieldIsBot = "isBot";
    public const string FieldCreatedAt = "createdAt";
    public const string FieldFirebaseUid = "firebaseUid";

    /// <summary>Legacy RTDB/early-migration field — still accepted when reading.</summary>
    public const string FieldAvatarIndexLegacy = "avatarIndex";

    public static FirebaseFirestore Db
    {
        get
        {
            try { return FirebaseFirestore.DefaultInstance; }
            catch (Exception e)
            {
                Debug.LogWarning("[Firestore] DefaultInstance unavailable: " + e.Message);
                return null;
            }
        }
    }

    public static CollectionReference Users => Db?.Collection("users");
    public static CollectionReference Uids => Db?.Collection("uids");

    public static DocumentReference UserDoc(string uid)
    {
        if (Users == null || string.IsNullOrEmpty(uid)) return null;
        return Users.Document(uid);
    }

    public static DocumentReference UidDoc(string gameUid)
    {
        if (Uids == null || string.IsNullOrEmpty(gameUid)) return null;
        return Uids.Document(gameUid);
    }

    /// <summary>Merge-write fields onto <c>users/{uid}</c>. Always stamps <c>lastActive</c>.</summary>
    public static void MergeUser(string uid, Dictionary<string, object> fields, Action<bool> onDone = null)
    {
        DocumentReference doc = UserDoc(uid);
        if (doc == null)
        {
            onDone?.Invoke(false);
            return;
        }

        if (fields == null)
            fields = new Dictionary<string, object>();

        if (!fields.ContainsKey(FieldLastActive))
            fields[FieldLastActive] = FieldValue.ServerTimestamp;

        doc.SetAsync(fields, SetOptions.MergeAll).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                string msg = task.Exception?.Flatten().Message ?? "unknown";
                if (IsPermissionDenied(msg))
                    Debug.LogWarning("[Firestore] MergeUser permission denied for users/" + uid + ". Publish firestore.rules, then retry.");
                else
                    Debug.LogError("[Firestore] MergeUser failed: " + msg);
                onDone?.Invoke(false);
                return;
            }
            onDone?.Invoke(true);
        });
    }

    public static void GetUser(string uid, Action<DocumentSnapshot> onDone)
    {
        DocumentReference doc = UserDoc(uid);
        if (doc == null)
        {
            onDone?.Invoke(null);
            return;
        }

        doc.GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                string msg = task.Exception?.Flatten().Message ?? "unknown";
                if (IsPermissionDenied(msg))
                {
                    Debug.LogWarning(
                        "[Firestore] Permission denied reading users/" + uid + ". " +
                        "Open Firebase Console → Firestore → Rules, paste project firestore.rules, then Publish. " +
                        "Until then the game uses local profile data only.");
                }
                else
                {
                    Debug.LogError("[Firestore] GetUser failed: " + msg);
                }
                onDone?.Invoke(null);
                return;
            }
            onDone?.Invoke(task.Result);
        });
    }

    public static void DeleteUser(string uid, Action<bool> onDone = null)
    {
        DocumentReference doc = UserDoc(uid);
        if (doc == null)
        {
            onDone?.Invoke(false);
            return;
        }

        doc.DeleteAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[Firestore] DeleteUser failed: " + (task.Exception?.Flatten().Message ?? "unknown"));
                onDone?.Invoke(false);
                return;
            }
            onDone?.Invoke(true);
        });
    }

    public static T GetField<T>(DocumentSnapshot snap, string key, T fallback = default)
    {
        if (snap == null || !snap.Exists || string.IsNullOrEmpty(key)) return fallback;
        try
        {
            if (!snap.ContainsField(key)) return fallback;
            object raw = snap.GetValue<object>(key);
            if (raw == null) return fallback;
            if (raw is T typed) return typed;
            return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch
        {
            return fallback;
        }
    }

    public static bool TryGetAvatarId(DocumentSnapshot snap, out int avatarId)
    {
        avatarId = -1;
        if (snap == null || !snap.Exists) return false;

        if (TryParseIntField(snap, FieldAvatarId, out avatarId))
            return true;
        return TryParseIntField(snap, FieldAvatarIndexLegacy, out avatarId);
    }

    public static string ResolveUsername(DocumentSnapshot snap)
    {
        if (snap == null || !snap.Exists) return null;

        string fromField = GetField<string>(snap, FieldUsername, null)?.Trim();
        if (!string.IsNullOrEmpty(fromField))
            return fromField;

        // Bot/dummy docs may use the display name as the document ID with no username field.
        string docId = snap.Id?.Trim();
        return string.IsNullOrEmpty(docId) ? null : docId;
    }

    static bool TryParseIntField(DocumentSnapshot snap, string key, out int value)
    {
        value = -1;
        if (!snap.ContainsField(key)) return false;
        object raw = snap.GetValue<object>(key);
        return raw != null && int.TryParse(raw.ToString(), out value);
    }

    static bool IsPermissionDenied(string message)
    {
        return !string.IsNullOrEmpty(message)
            && message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
