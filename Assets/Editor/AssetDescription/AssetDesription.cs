using UnityEngine;

[System.Serializable]
public class AssetDescription : ScriptableObject
{
    [TextArea(3, 10)]
    public string description;
}
