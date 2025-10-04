using UnityEditor;
using UnityEngine;
using System.IO;

public static class GitIgnoreTools
{
    private static string gitignorePath = Path.Combine(Application.dataPath, "../.gitignore");

    [MenuItem("Assets/Git/Ignore File", true)]
    private static bool ValidateIgnore() => Selection.activeObject != null;

    [MenuItem("Assets/Git/Ignore File")]
    private static void IgnoreFile()
    {
        string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        AddToGitignore(assetPath);
    }

    [MenuItem("Assets/Git/Whitelist File", true)]
    private static bool ValidateWhitelist() => Selection.activeObject != null;

    [MenuItem("Assets/Git/Whitelist File")]
    private static void WhitelistFile()
    {
        string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        AddToGitignore("!" + assetPath);
    }

    private static void AddToGitignore(string rule)
    {
        if (!File.Exists(gitignorePath))
            File.WriteAllText(gitignorePath, "");

        var lines = File.ReadAllLines(gitignorePath);
        if (System.Array.Exists(lines, l => l == rule)) return; // already present

        using (StreamWriter sw = File.AppendText(gitignorePath))
        {
            sw.WriteLine(rule);
        }

        Debug.Log($"Added rule to .gitignore: {rule}");
        AssetDatabase.Refresh();
    }
}
