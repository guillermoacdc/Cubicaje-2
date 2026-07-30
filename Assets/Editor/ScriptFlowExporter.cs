#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class ScriptFlowExporter
{
    private const string OutputDirectory = "Docs";
    private const string OutputFileName = "script-flow.md";

    private static readonly HashSet<string> TargetClassNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "StartMenuManager",
        "PieceScanManager",
        "CubicajeARManager",
        "CubicajeEngine",
        "PieceEntry"
    };

    [MenuItem("Tools/Export Script Flow")]
    public static void Export()
    {
        try
        {
            var report = new StringBuilder(64 * 1024);

            report.AppendLine("# Script Flow");
            report.AppendLine();
            report.AppendLine($"> Generated automatically on {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
            report.AppendLine();

            List<ScriptInfo> scripts = LoadTargetScripts();

            if (scripts.Count == 0)
            {
                report.AppendLine("_No target scripts were found under Assets._");
            }
            else
            {
                AppendOverview(report, scripts);
                AppendPerScriptDetails(report, scripts);
                AppendCrossReferences(report, scripts);
                AppendFileAccess(report, scripts);
                AppendLifecycleSummary(report, scripts);
                AppendMermaidGraph(report, scripts);
            }

            Directory.CreateDirectory(OutputDirectory);
            string outputPath = Path.Combine(OutputDirectory, OutputFileName);
            File.WriteAllText(outputPath, report.ToString(), new UTF8Encoding(false));

            AssetDatabase.Refresh();
            Debug.Log($"Script flow report generated at: {outputPath}");
            EditorUtility.RevealInFinder(outputPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static List<ScriptInfo> LoadTargetScripts()
    {
        string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
        var scripts = new List<ScriptInfo>();

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            string className = monoScript != null && monoScript.GetClass() != null
                ? monoScript.GetClass().Name
                : Path.GetFileNameWithoutExtension(assetPath);

            if (!TargetClassNames.Contains(className))
            {
                continue;
            }

            string absolutePath = Path.GetFullPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                continue;
            }

            string source = File.ReadAllText(absolutePath);
            scripts.Add(ParseScript(assetPath, className, source));
        }

        return scripts
            .OrderBy(script => script.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ScriptInfo ParseScript(string assetPath, string className, string source)
    {
        string sanitized = RemoveComments(source);

        return new ScriptInfo
        {
            AssetPath = assetPath,
            ClassName = className,
            Namespace = ExtractNamespace(sanitized),
            BaseTypes = ExtractBaseTypes(sanitized, className),
            Fields = ExtractFields(sanitized),
            Methods = ExtractMethods(sanitized),
            Events = ExtractEvents(sanitized),
            UnityEvents = ExtractUnityEvents(sanitized),
            ReferencedTypes = ExtractReferencedTargetTypes(sanitized, className),
            MethodCalls = ExtractMethodCalls(sanitized),
            FileReferences = ExtractFileReferences(source),
            UsesPersistentDataPath = source.Contains("Application.persistentDataPath", StringComparison.Ordinal),
            UsesStreamingAssetsPath = source.Contains("Application.streamingAssetsPath", StringComparison.Ordinal),
            UsesResourcesLoad = source.Contains("Resources.Load", StringComparison.Ordinal),
            UsesJsonUtility = source.Contains("JsonUtility", StringComparison.Ordinal),
            UsesFindObject = Regex.IsMatch(sanitized, @"\b(FindObjectOfType|FindFirstObjectByType|FindAnyObjectByType|GameObject\.Find)\b"),
            UsesDontDestroyOnLoad = source.Contains("DontDestroyOnLoad", StringComparison.Ordinal),
            UsesCoroutine = Regex.IsMatch(sanitized, @"\b(StartCoroutine|IEnumerator)\b"),
            Source = sanitized
        };
    }

    private static void AppendOverview(StringBuilder report, List<ScriptInfo> scripts)
    {
        report.AppendLine("## Overview");
        report.AppendLine();
        report.AppendLine("| Script | Namespace | Base types | Fields | Methods | Events |");
        report.AppendLine("|---|---|---|---:|---:|---:|");

        foreach (ScriptInfo script in scripts)
        {
            report.AppendLine(
                $"| `{Escape(script.ClassName)}` | `{Escape(script.Namespace)}` | `{Escape(string.Join(", ", script.BaseTypes))}` | {script.Fields.Count} | {script.Methods.Count} | {script.Events.Count + script.UnityEvents.Count} |");
        }

        report.AppendLine();
    }

    private static void AppendPerScriptDetails(StringBuilder report, List<ScriptInfo> scripts)
    {
        report.AppendLine("## Script details");
        report.AppendLine();

        foreach (ScriptInfo script in scripts)
        {
            report.AppendLine($"### `{Escape(script.ClassName)}`");
            report.AppendLine();
            report.AppendLine($"- Path: `{Escape(script.AssetPath)}`");
            report.AppendLine($"- Namespace: `{Escape(script.Namespace)}`");
            report.AppendLine($"- Base types: `{Escape(string.Join(", ", script.BaseTypes))}`");
            report.AppendLine();

            report.AppendLine("#### Fields");
            report.AppendLine();

            if (script.Fields.Count == 0)
            {
                report.AppendLine("_No fields detected._");
            }
            else
            {
                report.AppendLine("| Access | Static | Type | Name | Serialized |");
                report.AppendLine("|---|:---:|---|---|:---:|");

                foreach (FieldInfo field in script.Fields)
                {
                    report.AppendLine(
                        $"| `{Escape(field.Access)}` | {(field.IsStatic ? "Yes" : "No")} | `{Escape(field.Type)}` | `{Escape(field.Name)}` | {(field.IsSerialized ? "Yes" : "No")} |");
                }
            }

            report.AppendLine();
            report.AppendLine("#### Methods");
            report.AppendLine();

            if (script.Methods.Count == 0)
            {
                report.AppendLine("_No methods detected._");
            }
            else
            {
                report.AppendLine("| Access | Static | Return type | Method | Parameters | Unity lifecycle |");
                report.AppendLine("|---|:---:|---|---|---|:---:|");

                foreach (MethodInfo method in script.Methods)
                {
                    report.AppendLine(
                        $"| `{Escape(method.Access)}` | {(method.IsStatic ? "Yes" : "No")} | `{Escape(method.ReturnType)}` | `{Escape(method.Name)}` | `{Escape(method.Parameters)}` | {(method.IsUnityLifecycle ? "Yes" : "No")} |");
                }
            }

            report.AppendLine();
            report.AppendLine("#### Events");
            report.AppendLine();

            if (script.Events.Count == 0 && script.UnityEvents.Count == 0)
            {
                report.AppendLine("_No events detected._");
            }
            else
            {
                foreach (string eventText in script.Events)
                {
                    report.AppendLine($"- C# event: `{Escape(eventText)}`");
                }

                foreach (string unityEvent in script.UnityEvents)
                {
                    report.AppendLine($"- UnityEvent field: `{Escape(unityEvent)}`");
                }
            }

            report.AppendLine();
            report.AppendLine("#### Behaviour indicators");
            report.AppendLine();
            report.AppendLine($"- Uses `Application.persistentDataPath`: {(script.UsesPersistentDataPath ? "Yes" : "No")}");
            report.AppendLine($"- Uses `Application.streamingAssetsPath`: {(script.UsesStreamingAssetsPath ? "Yes" : "No")}");
            report.AppendLine($"- Uses `Resources.Load`: {(script.UsesResourcesLoad ? "Yes" : "No")}");
            report.AppendLine($"- Uses `JsonUtility`: {(script.UsesJsonUtility ? "Yes" : "No")}");
            report.AppendLine($"- Searches scene objects dynamically: {(script.UsesFindObject ? "Yes" : "No")}");
            report.AppendLine($"- Uses `DontDestroyOnLoad`: {(script.UsesDontDestroyOnLoad ? "Yes" : "No")}");
            report.AppendLine($"- Uses coroutines: {(script.UsesCoroutine ? "Yes" : "No")}");
            report.AppendLine();
        }
    }

    private static void AppendCrossReferences(StringBuilder report, List<ScriptInfo> scripts)
    {
        report.AppendLine("## Cross-script references");
        report.AppendLine();

        bool anyReference = false;

        foreach (ScriptInfo script in scripts)
        {
            foreach (string referencedType in script.ReferencedTypes.OrderBy(value => value))
            {
                anyReference = true;
                report.AppendLine($"- `{Escape(script.ClassName)}` references `{Escape(referencedType)}`");
            }
        }

        if (!anyReference)
        {
            report.AppendLine("_No direct references among target scripts were detected._");
        }

        report.AppendLine();
        report.AppendLine("## Method call candidates");
        report.AppendLine();

        foreach (ScriptInfo script in scripts)
        {
            report.AppendLine($"### `{Escape(script.ClassName)}`");
            report.AppendLine();

            var filteredCalls = script.MethodCalls
                .Where(call => !IsCommonFrameworkCall(call))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(call => call, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (filteredCalls.Count == 0)
            {
                report.AppendLine("_No relevant method-call candidates detected._");
            }
            else
            {
                foreach (string call in filteredCalls)
                {
                    report.AppendLine($"- `{Escape(call)}`");
                }
            }

            report.AppendLine();
        }
    }

    private static void AppendFileAccess(StringBuilder report, List<ScriptInfo> scripts)
    {
        report.AppendLine("## File and data references");
        report.AppendLine();

        bool any = false;

        foreach (ScriptInfo script in scripts)
        {
            if (script.FileReferences.Count == 0)
            {
                continue;
            }

            any = true;
            report.AppendLine($"### `{Escape(script.ClassName)}`");
            report.AppendLine();

            foreach (string fileReference in script.FileReferences.OrderBy(value => value))
            {
                report.AppendLine($"- `{Escape(fileReference)}`");
            }

            report.AppendLine();
        }

        if (!any)
        {
            report.AppendLine("_No probable file names or JSON paths were detected._");
            report.AppendLine();
        }
    }

    private static void AppendLifecycleSummary(StringBuilder report, List<ScriptInfo> scripts)
    {
        report.AppendLine("## Unity lifecycle summary");
        report.AppendLine();

        foreach (ScriptInfo script in scripts)
        {
            List<string> lifecycleMethods = script.Methods
                .Where(method => method.IsUnityLifecycle)
                .Select(method => method.Name)
                .Distinct()
                .OrderBy(name => LifecycleOrder(name))
                .ToList();

            report.AppendLine(
                $"- `{Escape(script.ClassName)}`: {(lifecycleMethods.Count > 0 ? string.Join(" → ", lifecycleMethods.Select(name => $"`{Escape(name)}`")) : "_none detected_")}");
        }

        report.AppendLine();
    }

    private static void AppendMermaidGraph(StringBuilder report, List<ScriptInfo> scripts)
    {
        report.AppendLine("## Dependency graph");
        report.AppendLine();
        report.AppendLine("```mermaid");
        report.AppendLine("graph TD");

        foreach (ScriptInfo script in scripts)
        {
            report.AppendLine($"    {SanitizeNode(script.ClassName)}[{script.ClassName}]");
        }

        foreach (ScriptInfo script in scripts)
        {
            foreach (string referencedType in script.ReferencedTypes.Distinct())
            {
                report.AppendLine(
                    $"    {SanitizeNode(script.ClassName)} --> {SanitizeNode(referencedType)}");
            }
        }

        foreach (ScriptInfo script in scripts)
        {
            if (script.UsesPersistentDataPath)
            {
                report.AppendLine(
                    $"    {SanitizeNode(script.ClassName)} --> PersistentData[(persistentDataPath)]");
            }

            if (script.UsesJsonUtility)
            {
                report.AppendLine(
                    $"    {SanitizeNode(script.ClassName)} --> JsonUtility[(JsonUtility)]");
            }
        }

        report.AppendLine("```");
        report.AppendLine();
    }

    private static string ExtractNamespace(string source)
    {
        Match match = Regex.Match(source, @"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)");
        return match.Success ? match.Groups[1].Value : "(global)";
    }

    private static List<string> ExtractBaseTypes(string source, string className)
    {
        Match match = Regex.Match(
            source,
            $@"\bclass\s+{Regex.Escape(className)}(?:\s*<[^>]+>)?\s*(?::\s*([^{{]+))?\s*{{");

        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
        {
            return new List<string>();
        }

        return match.Groups[1].Value
            .Split(',')
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();
    }

    private static List<FieldInfo> ExtractFields(string source)
    {
        var result = new List<FieldInfo>();

        var regex = new Regex(
            @"(?<attributes>(?:\[[^\]]+\]\s*)*)" +
            @"(?<access>public|private|protected|internal)?\s*" +
            @"(?<static>static\s+)?" +
            @"(?<readonly>readonly\s+)?" +
            @"(?<type>[A-Za-z_][A-Za-z0-9_<>,.\[\]\?\s]*)\s+" +
            @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*" +
            @"(?:=[^;]*)?;",
            RegexOptions.Multiline);

        foreach (Match match in regex.Matches(source))
        {
            string whole = match.Value;
            if (whole.Contains(" return ", StringComparison.Ordinal) ||
                whole.TrimStart().StartsWith("return ", StringComparison.Ordinal))
            {
                continue;
            }

            string type = NormalizeWhitespace(match.Groups["type"].Value);
            string name = match.Groups["name"].Value;
            string attributes = match.Groups["attributes"].Value;

            if (type.Contains("class ", StringComparison.Ordinal) ||
                type.Contains("namespace ", StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(new FieldInfo
            {
                Access = string.IsNullOrWhiteSpace(match.Groups["access"].Value)
                    ? "private (implicit)"
                    : match.Groups["access"].Value,
                IsStatic = match.Groups["static"].Success,
                Type = type,
                Name = name,
                IsSerialized =
                    match.Groups["access"].Value == "public" ||
                    attributes.Contains("SerializeField", StringComparison.Ordinal)
            });
        }

        return result
            .GroupBy(field => $"{field.Type}|{field.Name}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<MethodInfo> ExtractMethods(string source)
    {
        var result = new List<MethodInfo>();

        var regex = new Regex(
            @"(?<attributes>(?:\[[^\]]+\]\s*)*)" +
            @"(?<access>public|private|protected|internal)?\s*" +
            @"(?<static>static\s+)?" +
            @"(?<virtual>virtual\s+|override\s+|abstract\s+|async\s+|sealed\s+|new\s+)*" +
            @"(?<return>[A-Za-z_][A-Za-z0-9_<>,.\[\]\?\s]*)\s+" +
            @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*" +
            @"\((?<params>[^)]*)\)\s*" +
            @"(?:where[^{=>]+)?\s*(?:\{|=>)",
            RegexOptions.Multiline);

        foreach (Match match in regex.Matches(source))
        {
            string methodName = match.Groups["name"].Value;

            if (IsControlKeyword(methodName))
            {
                continue;
            }

            result.Add(new MethodInfo
            {
                Access = string.IsNullOrWhiteSpace(match.Groups["access"].Value)
                    ? "private (implicit)"
                    : match.Groups["access"].Value,
                IsStatic = match.Groups["static"].Success,
                ReturnType = NormalizeWhitespace(match.Groups["return"].Value),
                Name = methodName,
                Parameters = NormalizeWhitespace(match.Groups["params"].Value),
                IsUnityLifecycle = IsUnityLifecycleMethod(methodName)
            });
        }

        return result
            .GroupBy(method => $"{method.Name}|{method.Parameters}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractEvents(string source)
    {
        return Regex.Matches(
                source,
                @"\bevent\s+([A-Za-z_][A-Za-z0-9_<>,.\[\]\?\s]*)\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Cast<Match>()
            .Select(match => $"{NormalizeWhitespace(match.Groups[1].Value)} {match.Groups[2].Value}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractUnityEvents(string source)
    {
        return Regex.Matches(
                source,
                @"\b(UnityEvent(?:<[^>]+>)?)\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Cast<Match>()
            .Select(match => $"{NormalizeWhitespace(match.Groups[1].Value)} {match.Groups[2].Value}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> ExtractReferencedTargetTypes(string source, string currentClass)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);

        foreach (string target in TargetClassNames)
        {
            if (target == currentClass)
            {
                continue;
            }

            if (Regex.IsMatch(source, $@"\b{Regex.Escape(target)}\b"))
            {
                references.Add(target);
            }
        }

        if (Regex.IsMatch(source, @"\bARMarkerManager\b"))
        {
            references.Add("ARMarkerManager");
        }

        return references;
    }

    private static List<string> ExtractMethodCalls(string source)
    {
        var calls = new List<string>();

        foreach (Match match in Regex.Matches(
                     source,
                     @"\b(?:(?<target>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\("))
        {
            string method = match.Groups["method"].Value;
            if (IsControlKeyword(method))
            {
                continue;
            }

            string target = match.Groups["target"].Success
                ? match.Groups["target"].Value + "."
                : string.Empty;

            calls.Add(target + method + "()");
        }

        return calls;
    }

    private static List<string> ExtractFileReferences(string source)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(
                     source,
                     "\"(?<value>[^\"\\r\\n]*(?:\\.json|\\.txt|\\.csv|\\.xml|\\.dat)[^\"\\r\\n]*)\"",
                     RegexOptions.IgnoreCase))
        {
            references.Add(match.Groups["value"].Value);
        }

        return references.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string RemoveComments(string source)
    {
        string withoutBlockComments = Regex.Replace(
            source,
            @"/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);

        return Regex.Replace(
            withoutBlockComments,
            @"//.*?$",
            string.Empty,
            RegexOptions.Multiline);
    }

    private static string NormalizeWhitespace(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    }

    private static bool IsUnityLifecycleMethod(string name)
    {
        return new[]
        {
            "Reset", "Awake", "OnEnable", "Start", "FixedUpdate", "Update", "LateUpdate",
            "OnDisable", "OnDestroy", "OnApplicationPause", "OnApplicationFocus",
            "OnApplicationQuit", "OnValidate"
        }.Contains(name, StringComparer.Ordinal);
    }

    private static int LifecycleOrder(string name)
    {
        string[] order =
        {
            "Reset", "Awake", "OnEnable", "Start", "FixedUpdate", "Update", "LateUpdate",
            "OnDisable", "OnDestroy", "OnApplicationPause", "OnApplicationFocus",
            "OnApplicationQuit", "OnValidate"
        };

        int index = Array.IndexOf(order, name);
        return index >= 0 ? index : int.MaxValue;
    }

    private static bool IsControlKeyword(string value)
    {
        return new[]
        {
            "if", "for", "foreach", "while", "switch", "catch", "using", "lock",
            "return", "new", "typeof", "sizeof", "nameof"
        }.Contains(value, StringComparer.Ordinal);
    }

    private static bool IsCommonFrameworkCall(string call)
    {
        string[] ignoredPrefixes =
        {
            "Debug.", "Mathf.", "Vector3.", "Quaternion.", "Time.", "String.",
            "string.", "File.", "Path.", "Directory.", "JsonUtility.",
            "GameObject.", "Transform.", "Resources.", "Application.",
            "Convert.", "Enumerable.", "List.", "Dictionary."
        };

        return ignoredPrefixes.Any(prefix =>
            call.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string SanitizeNode(string value)
    {
        return Regex.Replace(value, @"[^A-Za-z0-9_]", "_");
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("|", "\\|");
    }

    private sealed class ScriptInfo
    {
        public string AssetPath;
        public string ClassName;
        public string Namespace;
        public List<string> BaseTypes;
        public List<FieldInfo> Fields;
        public List<MethodInfo> Methods;
        public List<string> Events;
        public List<string> UnityEvents;
        public HashSet<string> ReferencedTypes;
        public List<string> MethodCalls;
        public List<string> FileReferences;
        public bool UsesPersistentDataPath;
        public bool UsesStreamingAssetsPath;
        public bool UsesResourcesLoad;
        public bool UsesJsonUtility;
        public bool UsesFindObject;
        public bool UsesDontDestroyOnLoad;
        public bool UsesCoroutine;
        public string Source;
    }

    private sealed class FieldInfo
    {
        public string Access;
        public bool IsStatic;
        public string Type;
        public string Name;
        public bool IsSerialized;
    }

    private sealed class MethodInfo
    {
        public string Access;
        public bool IsStatic;
        public string ReturnType;
        public string Name;
        public string Parameters;
        public bool IsUnityLifecycle;
    }
}
#endif
