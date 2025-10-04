using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class GitIgnoreOverlay
{
    private static Texture2D ignoredIcon;
    private static Texture2D whitelistedIcon;
    private static string gitignorePath;

    static GitIgnoreOverlay()
    {
        ignoredIcon = EditorGUIUtility.IconContent("console.erroricon").image as Texture2D;
        whitelistedIcon = EditorGUIUtility.IconContent("TestPassed").image as Texture2D;
        gitignorePath = Path.Combine(Application.dataPath, "../.gitignore");

        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    private static void OnProjectWindowItemGUI(string guid, Rect rect)
    {
        if (!File.Exists(gitignorePath)) return;

        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        string[] rules = File.ReadAllLines(gitignorePath);

        if (System.Array.Exists(rules, r => r.Trim() == assetPath))
        {
            Rect iconRect = new Rect(rect.xMax - 20, rect.yMin, 16, 16);
            GUI.DrawTexture(iconRect, ignoredIcon);
        }
        else if (System.Array.Exists(rules, r => r.Trim() == "!" + assetPath))
        {
            Rect iconRect = new Rect(rect.xMax - 20, rect.yMin, 16, 16);
            GUI.DrawTexture(iconRect, whitelistedIcon);
        }
    }
}
