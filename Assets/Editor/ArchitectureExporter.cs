#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ArchitectureExporter
{
    private const string OutputDirectory = "Docs";
    private const string OutputFileName = "project-architecture.md";

    [MenuItem("Tools/Export Project Architecture")]
    public static void Export()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("Architecture export cancelled because there are unsaved scene changes.");
            return;
        }

        SceneSetup[] originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        var report = new StringBuilder(64 * 1024);
        var componentOccurrences = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        try
        {
            AppendHeader(report);
            AppendProjectInformation(report);
            AppendBuildSettings(report);
            AppendAssetScripts(report);
            AppendScenes(report, componentOccurrences);
            AppendDuplicateComponents(report, componentOccurrences);

            Directory.CreateDirectory(OutputDirectory);
            string outputPath = Path.Combine(OutputDirectory, OutputFileName);
            File.WriteAllText(outputPath, report.ToString(), new UTF8Encoding(false));

            AssetDatabase.Refresh();
            Debug.Log($"Architecture report generated at: {outputPath}");
            EditorUtility.RevealInFinder(outputPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
        }
    }

    private static void AppendHeader(StringBuilder report)
    {
        report.AppendLine("# Project Architecture");
        report.AppendLine();
        report.AppendLine($"> Generated automatically on {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
        report.AppendLine();
    }

    private static void AppendProjectInformation(StringBuilder report)
    {
        report.AppendLine("## Project information");
        report.AppendLine();
        report.AppendLine($"- Unity version: `{Application.unityVersion}`");
        report.AppendLine($"- Product name: `{PlayerSettings.productName}`");
        report.AppendLine($"- Company name: `{PlayerSettings.companyName}`");
        report.AppendLine($"- Project path: `{EscapeMarkdown(Directory.GetCurrentDirectory())}`");
        report.AppendLine();
    }

    private static void AppendBuildSettings(StringBuilder report)
    {
        report.AppendLine("## Build Settings scenes");
        report.AppendLine();

        EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;

        if (buildScenes.Length == 0)
        {
            report.AppendLine("_No scenes are configured in Build Settings._");
            report.AppendLine();
            return;
        }

        report.AppendLine("| Index | Enabled | Scene |");
        report.AppendLine("|---:|:---:|---|");

        for (int index = 0; index < buildScenes.Length; index++)
        {
            EditorBuildSettingsScene scene = buildScenes[index];
            report.AppendLine($"| {index} | {(scene.enabled ? "Yes" : "No")} | `{EscapeMarkdown(scene.path)}` |");
        }

        report.AppendLine();
    }

    private static void AppendAssetScripts(StringBuilder report)
    {
        report.AppendLine("## C# scripts under Assets");
        report.AppendLine();

        string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
        var scripts = new List<(string Path, string ClassName, string BaseType)>();

        foreach (string guid in scriptGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Type classType = monoScript != null ? monoScript.GetClass() : null;

            scripts.Add((
                path,
                classType != null ? classType.FullName : "(no compiled class)",
                classType?.BaseType?.FullName ?? string.Empty
            ));
        }

        if (scripts.Count == 0)
        {
            report.AppendLine("_No C# scripts were found under Assets._");
            report.AppendLine();
            return;
        }

        report.AppendLine("| Script | Class | Base type |");
        report.AppendLine("|---|---|---|");

        foreach (var script in scripts.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            report.AppendLine($"| `{EscapeMarkdown(script.Path)}` | `{EscapeMarkdown(script.ClassName)}` | `{EscapeMarkdown(script.BaseType)}` |");
        }

        report.AppendLine();
    }

    private static void AppendScenes(StringBuilder report, Dictionary<string, List<string>> componentOccurrences)
    {
        string[] scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        report.AppendLine("## Scenes");
        report.AppendLine();

        if (scenePaths.Length == 0)
        {
            report.AppendLine("_No scene assets were found._");
            report.AppendLine();
            return;
        }

        foreach (string scenePath in scenePaths)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            report.AppendLine($"### `{EscapeMarkdown(scenePath)}`");
            report.AppendLine();

            GameObject[] rootObjects = scene.GetRootGameObjects();

            if (rootObjects.Length == 0)
            {
                report.AppendLine("_The scene contains no root GameObjects._");
                report.AppendLine();
                continue;
            }

            foreach (GameObject rootObject in rootObjects.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase))
            {
                AppendGameObject(report, rootObject, 0, scenePath, componentOccurrences);
            }

            report.AppendLine();
        }
    }

    private static void AppendGameObject(
        StringBuilder report,
        GameObject gameObject,
        int depth,
        string scenePath,
        Dictionary<string, List<string>> componentOccurrences)
    {
        string indent = new string(' ', depth * 2);
        string activeState = gameObject.activeSelf ? "active" : "inactive";
        string objectPath = GetHierarchyPath(gameObject.transform);

        report.AppendLine($"{indent}- **{EscapeMarkdown(gameObject.name)}** `{activeState}`");

        Component[] components = gameObject.GetComponents<Component>();

        foreach (Component component in components)
        {
            string componentIndent = new string(' ', (depth + 1) * 2);

            if (component == null)
            {
                report.AppendLine($"{componentIndent}- ⚠️ `Missing Script`");
                continue;
            }

            Type componentType = component.GetType();
            string componentTypeName = componentType.FullName ?? componentType.Name;

            report.AppendLine($"{componentIndent}- `{EscapeMarkdown(componentTypeName)}`");

            if (!componentOccurrences.TryGetValue(componentTypeName, out List<string> locations))
            {
                locations = new List<string>();
                componentOccurrences.Add(componentTypeName, locations);
            }

            locations.Add($"{scenePath} :: {objectPath}");

            if (component is MonoBehaviour)
            {
                AppendSerializedProperties(report, component, new string(' ', (depth + 2) * 2));
            }
        }

        for (int childIndex = 0; childIndex < gameObject.transform.childCount; childIndex++)
        {
            AppendGameObject(
                report,
                gameObject.transform.GetChild(childIndex).gameObject,
                depth + 1,
                scenePath,
                componentOccurrences);
        }
    }

    private static void AppendSerializedProperties(StringBuilder report, Component component, string indent)
    {
        SerializedObject serializedObject;

        try
        {
            serializedObject = new SerializedObject(component);
        }
        catch
        {
            return;
        }

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (property.propertyPath == "m_Script")
            {
                continue;
            }

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            UnityEngine.Object reference = property.objectReferenceValue;

            if (reference == null)
            {
                report.AppendLine($"{indent}- `{EscapeMarkdown(property.propertyPath)}` → `None`");
                continue;
            }

            report.AppendLine($"{indent}- `{EscapeMarkdown(property.propertyPath)}` → {DescribeReference(reference)}");
        }
    }

    private static string DescribeReference(UnityEngine.Object reference)
    {
        if (reference is Component component)
        {
            return $"`{EscapeMarkdown(component.GetType().Name)}` on `{EscapeMarkdown(GetHierarchyPath(component.transform))}`";
        }

        if (reference is GameObject gameObject)
        {
            return $"GameObject `{EscapeMarkdown(GetHierarchyPath(gameObject.transform))}`";
        }

        string assetPath = AssetDatabase.GetAssetPath(reference);

        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            return $"asset `{EscapeMarkdown(assetPath)}`";
        }

        return $"`{EscapeMarkdown(reference.name)}` ({EscapeMarkdown(reference.GetType().Name)})";
    }

    private static void AppendDuplicateComponents(
        StringBuilder report,
        Dictionary<string, List<string>> componentOccurrences)
    {
        report.AppendLine("## Components with multiple occurrences");
        report.AppendLine();
        report.AppendLine("_This section is informational. Multiple occurrences are not necessarily errors._");
        report.AppendLine();

        var duplicates = componentOccurrences
            .Where(pair => pair.Value.Count > 1)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (duplicates.Count == 0)
        {
            report.AppendLine("_No repeated component types were found._");
            report.AppendLine();
            return;
        }

        foreach (var duplicate in duplicates)
        {
            report.AppendLine($"### `{EscapeMarkdown(duplicate.Key)}` — {duplicate.Value.Count} occurrences");
            report.AppendLine();

            foreach (string location in duplicate.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                report.AppendLine($"- `{EscapeMarkdown(location)}`");
            }

            report.AppendLine();
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        Transform current = transform;

        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static string EscapeMarkdown(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("|", "\\|");
    }
}
#endif
