using UnityEngine;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Shared utility for creating UI objects in code.
    /// Handles the RectTransform auto-creation that Unity does
    /// when parenting under a Canvas hierarchy.
    /// </summary>
    internal static class UIHelper
    {
        /// <summary>
        /// Create a new GameObject with a guaranteed RectTransform, parented to the given parent.
        /// </summary>
        public static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            EnsureRectTransform(go);
            return go;
        }

        /// <summary>
        /// Get or add a RectTransform on the given GameObject.
        /// Unity auto-adds RectTransform when parenting under a Canvas hierarchy,
        /// so calling AddComponent would fail. Always use this instead.
        /// </summary>
        public static RectTransform EnsureRectTransform(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt != null) return rt;
            return go.AddComponent<RectTransform>();
        }
    }
}
