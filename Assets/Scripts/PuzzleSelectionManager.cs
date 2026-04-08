using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.IO;

public class PuzzleSelectionManager : MonoBehaviour
{
    public UIDocument uiDoc;
    public PuzzleManager puzzleManager;
    public string imageFolderPath = "Assets/Images/PuzzleBase";

    private ScrollView imageGrid;
    private RadioButtonGroup pieceCountGroup;
    private float lastOpenTime;

    private readonly int[] pieceOptions = { 24, 96, 216, 486 };

    void OnEnable()
    {
        if (uiDoc == null) uiDoc = GetComponent<UIDocument>();
        if (uiDoc == null) return;

        var root = uiDoc.rootVisualElement;
        imageGrid = root.Q<ScrollView>("ImageGrid");
        pieceCountGroup = root.Q<RadioButtonGroup>("PieceCountGroup");

        RefreshImageGrid();
    }

    private void RefreshImageGrid()
    {
        if (imageGrid == null) return;
        imageGrid.Clear();

#if UNITY_EDITOR
        // t:Texture2D で検索し、すべての画像を確実に取得
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { imageFolderPath });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            // Spriteとして直接ロードを試みる
            Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            
            if (sprite == null) {
                // もしメインアセットがSpriteでない場合、サブアセットを探す
                Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
                foreach(var asset in assets) {
                    if (asset is Sprite s) {
                        sprite = s;
                        break;
                    }
                }
            }

            if (sprite != null)
            {
                CreateButton(sprite, Path.GetFileNameWithoutExtension(path));
            }
        }
#endif
    }

    private void CreateButton(Sprite sprite, string name)
    {
        Button btn = new Button();
        btn.AddToClassList("selection-button");

        VisualElement thumb = new VisualElement();
        thumb.AddToClassList("thumbnail-image");
        thumb.style.backgroundImage = new StyleBackground(sprite);
        btn.Add(thumb);

        Label lbl = new Label(name);
        lbl.AddToClassList("button-label");
        btn.Add(lbl);

        btn.clicked += () => {
            if (Time.time - lastOpenTime < 0.2f) return;
            if (puzzleManager != null) {
                // 選択されているインデックスからピース数を取得
                int selectedIndex = pieceCountGroup != null ? pieceCountGroup.value : 1;
                if (selectedIndex < 0 || selectedIndex >= pieceOptions.Length) selectedIndex = 1;
                int pieces = pieceOptions[selectedIndex];

                puzzleManager.StartPuzzle(sprite, pieces);
                uiDoc.gameObject.SetActive(false);
            }
        };

        imageGrid.Add(btn);
    }

    public void Open()
    {
        lastOpenTime = Time.time;
        uiDoc.gameObject.SetActive(true);
        RefreshImageGrid();
    }
}
