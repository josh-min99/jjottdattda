using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text;

[InitializeOnLoad]
public static class AssetInspectorDescription
{
    private const string DescriptionFolderPath = "Assets/Editor/AssetDescription/Description/";

    static AssetInspectorDescription()
    {
        Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
    }

    private static void OnHeaderGUI(Editor editor)
    {
        var obj = editor.target;
        if (obj == null) return;

        string path = GetDescPath(obj);
        if (string.IsNullOrEmpty(path)) return;

        var desc = AssetDatabase.LoadAssetAtPath<AssetDescription>(path);

        GUILayout.Space(6);
        GUILayout.BeginVertical("box");
        GUILayout.Label("Description", EditorStyles.boldLabel);

        if (desc == null)
        {
            if (GUILayout.Button("Create Description"))
            {
                var newAsset = ScriptableObject.CreateInstance<AssetDescription>();
                newAsset.name = obj.name;

                Directory.CreateDirectory(DescriptionFolderPath);
                AssetDatabase.CreateAsset(newAsset, path);
                AssetDatabase.SaveAssets();
            }
        }
        else
        {
            var so = new SerializedObject(desc);
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("description"), true);
            so.ApplyModifiedProperties();

            if (GUILayout.Button("Delete Description"))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
            }
        }

        GUILayout.EndVertical();
    }

    private static string GetDescPath(Object obj)
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
            if (c == ' ' || c == '/' || c == '\\' || c == ':' || System.Array.IndexOf(inv, c) >= 0)
                sb.Append('_');
            else
                sb.Append(c);
        }

        return $"{DescriptionFolderPath}{sb}.asset";
    }
}
