using UnityEngine;
using UnityEditor;
using System.IO;

public class PuzzleAssetPostprocessor : AssetPostprocessor
{
    private const string TargetFolder = "Assets/Images/PuzzleBase";

    // アセットのインポート時に自動実行
    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith(TargetFolder))
        {
            TextureImporter importer = (TextureImporter)assetImporter;
            
            bool changed = false;
            
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }
            
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }

            if (importer.alphaIsTransparency == false)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (changed)
            {
                Debug.Log($"[PuzzleAssetPostprocessor] 自動設定を適用しました: {assetPath}");
            }
        }
    }

    // メニューから手動で実行できるようにする
    [MenuItem("Tools/Jigsaw/Fix Puzzle Image Settings")]
    public static void FixAllSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TargetFolder });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null)
            {
                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
                if (!importer.isReadable) { importer.isReadable = true; changed = true; }
                if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }

                if (changed)
                {
                    importer.SaveAndReimport();
                    count++;
                }
            }
        }

        Debug.Log($"[PuzzleAssetPostprocessor] {count} 個の画像を更新しました。");
        EditorUtility.DisplayDialog("Puzzle Settings", $"{count} 個の画像設定を修正しました。", "OK");
    }
}
