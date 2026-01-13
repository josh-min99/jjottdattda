using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Text;

[InitializeOnLoad]
public static class AssetDescriptionOverlay
{
    private static string currentDescription = "";
    private static string currentLabel = "";
    private static string lastKey = "";
    private static bool editing = false;

    private const string ConsolePrefKey = "AssetDesc_ShowConsole";
    private const string OverlayPrefKey = "AssetDesc_ShowOverlay";
    private const string DescriptionFolderPath = "Assets/Editor/AssetDescription/Description/";

    private static GUIStyle boxStyle;
    private static GUIStyle titleStyle;
    private static GUIStyle editingStyle;
    private static GUIStyle bodyStyle;

    static AssetDescriptionOverlay()
    {
        EditorApplication.delayCall += () =>
        {
            Selection.selectionChanged += UpdateDescription;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.postprocessModifications += OnAssetsModified;
        };
    }

    private static bool showConsole => EditorPrefs.GetBool(ConsolePrefKey, true);
    private static bool showOverlay => EditorPrefs.GetBool(OverlayPrefKey, true);

    private static void InitStyles()
    {
        if (boxStyle != null) return;

        boxStyle = new GUIStyle(EditorStyles.helpBox);
        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0, 0, 0, 0.75f));
        bg.Apply();
        boxStyle.normal.background = bg;
        boxStyle.padding = new RectOffset(10, 10, 10, 10);

        titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = 14;
        titleStyle.normal.textColor = Color.white;

        editingStyle = new GUIStyle(EditorStyles.label);
        editingStyle.fontSize = 11;
        editingStyle.normal.textColor = Color.yellow;

        bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
        bodyStyle.fontSize = 12;
        bodyStyle.normal.textColor = Color.white;
    }

    private static UndoPropertyModification[] OnAssetsModified(UndoPropertyModification[] mods)
    {
        var obj = Selection.activeObject;
        if (!obj) return mods;

        string path = GetDescPath(obj);
        if (string.IsNullOrEmpty(path)) return mods;

        var desc = AssetDatabase.LoadAssetAtPath<AssetDescription>(path);
        if (!desc) return mods;

        editing = true;
        currentDescription = desc.description;
        currentLabel = GetLabel(obj);

        SceneView.RepaintAll();
        return mods;
    }

    private static void UpdateDescription()
    {
        editing = false;
        currentDescription = "";
        currentLabel = "";

        var obj = Selection.activeObject;
        if (!obj) return;

        string path = GetDescPath(obj);
        if (string.IsNullOrEmpty(path)) return;

        var desc = AssetDatabase.LoadAssetAtPath<AssetDescription>(path);
        if (!desc || string.IsNullOrWhiteSpace(desc.description)) return;

        currentDescription = desc.description;
        currentLabel = GetLabel(obj);

        if (showConsole && lastKey != path)
        {
            Debug.Log($"📦 {currentLabel} / {desc.description}");
            lastKey = path;
        }
    }

    private static void OnSceneGUI(SceneView view)
    {
        if (!showOverlay) return;
        if (string.IsNullOrEmpty(currentDescription)) return;

        InitStyles();

        float width = Mathf.Clamp(20 + Mathf.Max(
            titleStyle.CalcSize(new GUIContent(currentLabel)).x,
            bodyStyle.CalcHeight(new GUIContent(currentDescription), 500)
        ), 250, 500);

        float height = 40 +
            titleStyle.CalcHeight(new GUIContent(currentLabel), width - 20) +
            bodyStyle.CalcHeight(new GUIContent(currentDescription), width - 20);

        if (editing) height += 20;

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(15, 15, width, height), GUIContent.none, boxStyle);

        GUILayout.Label(currentLabel, titleStyle);
        if (editing)
            GUILayout.Label("(실시간 편집중)", editingStyle);

        GUILayout.Space(4);
        GUILayout.Label(currentDescription, bodyStyle);

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    public static string GetDescPath(UnityEngine.Object obj)
    {
        string ap = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(ap))
        {
            string guid = AssetDatabase.AssetPathToGUID(ap);
            if (string.IsNullOrEmpty(guid)) return null;
            return $"{DescriptionFolderPath}{guid}.asset";
        }

        var gid = GlobalObjectId.GetGlobalObjectIdSlow(obj);
        string raw = gid.ToString();

        var inv = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);

        foreach (char c in raw)
        {
            if (c == ' ' || c == '/' || c == '\\' || c == ':' || Array.IndexOf(inv, c) >= 0)
                sb.Append('_');
            else
                sb.Append(c);
        }

        return $"{DescriptionFolderPath}{sb}.asset";
    }

    private static string GetLabel(UnityEngine.Object obj)
    {
        string ap = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(ap)) return ap;

        var go = obj as GameObject;
        if (go)
        {
            string scene = string.IsNullOrEmpty(go.scene.name) ? "(Scene)" : go.scene.name;
            return scene + "/" + GetHierarchy(go.transform);
        }

        return obj.name;
    }

    private static string GetHierarchy(Transform t)
    {
        if (!t.parent) return t.name;
        return GetHierarchy(t.parent) + "/" + t.name;
    }
}
