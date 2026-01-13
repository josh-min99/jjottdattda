using UnityEditor;
using UnityEngine;
using System.IO;

public class AssetDescriptionAutoCleaner : AssetModificationProcessor
{
    private const string DescriptionFolderPath = "Assets/Editor/AssetDescription/Description/";

    static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (!string.IsNullOrEmpty(guid))
        {
            string desc = $"{DescriptionFolderPath}{guid}.asset";
            if (File.Exists(desc))
            {
                AssetDatabase.DeleteAsset(desc);
                AssetDatabase.SaveAssets();
            }
        }

        return AssetDeleteResult.DidNotDelete;
    }
}
