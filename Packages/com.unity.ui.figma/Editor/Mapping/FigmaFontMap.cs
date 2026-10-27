using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Resolves a Figma font family to a USS -unity-font-definition value (null when no project
    /// font matches), using the same project://database asset URI UI Builder emits.
    /// </summary>
    static class FigmaFontMap
    {
        // Populated by JsonUtility from the EditorJsonUtility PPtr round-trip, never assigned in code.
#pragma warning disable 649
        [Serializable]
        struct RawPPtr { public long fileID; public string guid; public int type; }

        [Serializable]
        struct RawContainer { public RawPPtr o; }
#pragma warning restore 649

        [Serializable]
        class PPtrContainer { public Object o; }

        public static string ResolveFontDefinition(string fontFamily)
        {
            var family = CleanFamily(fontFamily);
            if (string.IsNullOrEmpty(family))
                return null;

            var asset = FindFont(family);
            return asset == null ? null : MakeFontDefinition(asset);
        }

        static Object FindFont(string family)
        {
            // FontAsset (TextCore) first, then legacy Font; -unity-font-definition accepts either.
            foreach (var filter in new[] { "t:FontAsset", "t:Font" })
            {
                foreach (var guid in AssetDatabase.FindAssets(filter))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0)
                        return AssetDatabase.LoadMainAssetAtPath(path);
                }
            }

            return null;
        }

        /// <summary>
        /// The -unity-font-definition value for a Font/FontAsset.
        /// </summary>
        public static string MakeFontDefinition(Object asset)
        {
            return MakeAssetUrl(asset);
        }

        /// <summary>
        /// The canonical project://database URI for USS url(...) values. Mirrors the internal
        /// URIHelpers.MakeAssetUri via public APIs (an EditorJsonUtility PPtr round-trip).
        /// </summary>
        public static string MakeAssetUrl(Object asset)
        {
            return "url(\"" + MakeAssetUri(asset) + "\")";
        }

        /// <summary>
        /// The bare URI, for a UXML attribute. USS wants it wrapped in url(...), so both forms exist rather
        /// than one being unwrapped at a call site.
        /// </summary>
        public static string MakeAssetUri(Object asset)
        {
            var container = new PPtrContainer { o = asset };
            var raw = JsonUtility.FromJson<RawContainer>(EditorJsonUtility.ToJson(container)).o;

            var builder = new UriBuilder("project", "database", -1, AssetDatabase.GetAssetPath(asset))
            {
                Query = "fileID=" + raw.fileID + "&guid=" + raw.guid + "&type=" + raw.type,
                Fragment = asset.name
            };

            return builder.ToString();
        }

        static string CleanFamily(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily))
                return null;

            var family = fontFamily.Trim();
            var comma = family.IndexOf(',');
            if (comma >= 0)
                family = family.Substring(0, comma);

            return family.Trim().Trim('\'', '"').Trim();
        }
    }
}
