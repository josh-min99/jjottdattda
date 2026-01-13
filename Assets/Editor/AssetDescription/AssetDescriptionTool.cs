using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

public class AssetDescriptionTool : EditorWindow
{
    private UnityEngine.Object selectedAsset;
    private AssetDescription descriptionAsset;
    private Vector2 scroll;

    private const string ConsolePrefKey = "AssetDesc_ShowConsole";
    private const string OverlayPrefKey = "AssetDesc_ShowOverlay";
    private const string DescriptionFolderPath = "Assets/Editor/AssetDescription/Description/";

    private static bool showConsole
    {
        get { return EditorPrefs.GetBool(ConsolePrefKey, true); }
    }

    private static bool showOverlay
    {
        get { return EditorPrefs.GetBool(OverlayPrefKey, true); }
    }

    [MenuItem("Tools/에셋 설명 에디터")]
    public static void ShowWindow()
    {
        var window = GetWindow<AssetDescriptionTool>();

        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/AssetDescription/icon.png");
        Texture iconTexture = icon != null
            ? (Texture)icon
            : EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image;

        window.titleContent = new GUIContent("에셋 설명", iconTexture);
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        GUILayout.Label("출력 설정", EditorStyles.boldLabel);

        bool consoleToggle = EditorGUILayout.ToggleLeft("📢 콘솔 출력", showConsole);
        if (consoleToggle != showConsole)
            EditorPrefs.SetBool(ConsolePrefKey, consoleToggle);

        bool overlayToggle = EditorGUILayout.ToggleLeft("🖼 오버레이 출력", showOverlay);
        if (overlayToggle != showOverlay)
            EditorPrefs.SetBool(OverlayPrefKey, overlayToggle);

        GUILayout.Space(10);
        GUILayout.Box(GUIContent.none, GUILayout.Height(1), GUILayout.ExpandWidth(true));

        selectedAsset = Selection.activeObject;
        if (selectedAsset == null)
        {
            EditorGUILayout.HelpBox("에셋 또는 씬 오브젝트를 선택하세요.", MessageType.Info);
            return;
        }

        EnsureDescriptionFolderExists();

        string descAssetPath = GetDescriptionAssetPathForObject(selectedAsset);
        if (string.IsNullOrEmpty(descAssetPath))
        {
            EditorGUILayout.HelpBox("설명 파일 경로를 만들 수 없는 대상입니다.", MessageType.Warning);
            return;
        }

        descriptionAsset = AssetDatabase.LoadAssetAtPath<AssetDescription>(descAssetPath);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        string label = GetDisplayLabel(selectedAsset);
        EditorGUILayout.LabelField("📄 선택된 대상", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(label);
        EditorGUILayout.Space();

        if (descriptionAsset == null)
        {
            if (GUILayout.Button("📝 설명 작성"))
            {
                var newAsset = ScriptableObject.CreateInstance<AssetDescription>();
                newAsset.name = selectedAsset.name;

                AssetDatabase.CreateAsset(newAsset, descAssetPath);
                EditorUtility.SetDirty(newAsset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                descriptionAsset = AssetDatabase.LoadAssetAtPath<AssetDescription>(descAssetPath);
                Repaint();
            }
        }

        if (descriptionAsset != null)
        {
            var so = new SerializedObject(descriptionAsset);
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("description"), true);
            so.ApplyModifiedProperties();

            GUILayout.Space(5);
            if (GUILayout.Button("❌ 설명 삭제"))
            {
                AssetDatabase.DeleteAsset(descAssetPath);
                AssetDatabase.Refresh();
                descriptionAsset = null;
                Repaint();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void EnsureDescriptionFolderExists()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Editor"))
            AssetDatabase.CreateFolder("Assets", "Editor");

        if (!AssetDatabase.IsValidFolder("Assets/Editor/AssetDescription"))
            AssetDatabase.CreateFolder("Assets/Editor", "AssetDescription");

        if (!AssetDatabase.IsValidFolder("Assets/Editor/AssetDescription/Description"))
            AssetDatabase.CreateFolder("Assets/Editor/AssetDescription", "Description");
    }

    private static string GetDescriptionAssetPathForObject(UnityEngine.Object obj)
    {
        string assetPath = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(assetPath))
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                return null;

            return DescriptionFolderPath + guid + ".asset";
        }

        GlobalObjectId gid = GlobalObjectId.GetGlobalObjectIdSlow(obj);
        string raw = gid.ToString();

        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == ' ' || c == ':' || c == '/' || c == '\\' || System.Array.IndexOf(invalid, c) >= 0)
                sb.Append('_');
            else
                sb.Append(c);
        }

        string fileName = sb.ToString();
        return DescriptionFolderPath + fileName + ".asset";
    }

    private static string GetDisplayLabel(UnityEngine.Object obj)
    {
        string assetPath = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(assetPath))
            return assetPath;

        GameObject go = obj as GameObject;
        if (go != null)
        {
            var scene = go.scene;
            string sceneName = string.IsNullOrEmpty(scene.name) ? "(Untitled Scene)" : scene.name;
            return sceneName + " / " + GetHierarchyPath(go.transform);
        }

        return obj.name;
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t.parent == null)
            return t.name;

        return GetHierarchyPath(t.parent) + "/" + t.name;
    }
}
