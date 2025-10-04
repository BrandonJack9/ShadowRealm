using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;

public static class GitWhitelistPopulator
{
    private static string gitignorePath = Path.Combine(
        Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
        ".gitignore"
    );

    [MenuItem("Tools/Git/Auto-Whitelist Scene Assets")]
    private static void AutoWhitelist()
    {
        HashSet<string> assetPaths = new HashSet<string>();

        // Go through all open scenes
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // Collect all dependencies from objects in the scene
                Object[] deps = EditorUtility.CollectDependencies(new Object[] { root });
                foreach (Object dep in deps)
                {
                    string path = AssetDatabase.GetAssetPath(dep);
                    if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets"))
                    {
                        assetPaths.Add(path);
                    }
                }
            }
        }

        // Write whitelisted entries to .gitignore
        List<string> lines = new List<string>();
        if (File.Exists(gitignorePath))
            lines.AddRange(File.ReadAllLines(gitignorePath));

        using (StreamWriter sw = new StreamWriter(gitignorePath, true))
        {
            foreach (var path in assetPaths)
            {
                string rule = "!" + path;
                if (!lines.Contains(rule))
                {
                    sw.WriteLine(rule);
                    Debug.Log($"Whitelisted: {path}");
                }
            }
        }

        AssetDatabase.Refresh();
    }
}
