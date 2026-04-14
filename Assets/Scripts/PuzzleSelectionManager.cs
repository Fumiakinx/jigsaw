// Project: Jigsaw - Ver 1.2
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
    private List<(Sprite sprite, string name)> cachedSprites;

    [Header("Confirmation UI")]
    private VisualElement confirmationOverlay;
    private VisualElement confirmImage;
    private Label confirmPieces;
    private Button btnYes;
    private Button btnNo;

    private Sprite pendingSprite;
    private int pendingPieces;

    private readonly int[] pieceOptions = { 24, 96, 216, 486 };

    void OnEnable()
    {
        if (uiDoc == null) uiDoc = GetComponent<UIDocument>();
        if (uiDoc == null) return;

        var root = uiDoc.rootVisualElement;
        imageGrid = root.Q<ScrollView>("ImageGrid");
        pieceCountGroup = root.Q<RadioButtonGroup>("PieceCountGroup");

        confirmationOverlay = root.Q<VisualElement>("ConfirmationOverlay");
        confirmImage = root.Q<VisualElement>("ConfirmImage");
        confirmPieces = root.Q<Label>("ConfirmPieces");
        btnYes = root.Q<Button>("ConfirmYes");
        btnNo = root.Q<Button>("ConfirmNo");

        if (btnYes != null) {
            btnYes.clicked -= OnConfirmYes;
            btnYes.clicked += OnConfirmYes;
        }
        if (btnNo != null) {
            btnNo.clicked -= OnConfirmNo;
            btnNo.clicked += OnConfirmNo;
        }

        RefreshImageGrid();
    }

    private void RefreshImageGrid()
    {
        if (imageGrid == null) return;

#if UNITY_EDITOR
        if (cachedSprites == null)
        {
            cachedSprites = new List<(Sprite sprite, string name)>();
            Debug.Log($"[PuzzleSelectionManager] {imageFolderPath} から画像をキャッシュに読み込んでいます...");
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { imageFolderPath });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) {
                    Texture2D tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null) {
                        sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    }
                }
                if (sprite != null) {
                    cachedSprites.Add((sprite, Path.GetFileNameWithoutExtension(path)));
                }
            }
        }

        // 既に要素が作成されている場合は再生成をスキップ
        if (imageGrid.contentContainer.childCount == cachedSprites.Count) return;

        imageGrid.Clear();
        foreach (var item in cachedSprites)
        {
            CreateButton(item.sprite, item.name);
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
            
            // 選択されているインデックスからピース数を取得
            int selectedIndex = pieceCountGroup != null ? pieceCountGroup.value : 1;
            if (selectedIndex < 0 || selectedIndex >= pieceOptions.Length) selectedIndex = 1;
            int pieces = pieceOptions[selectedIndex];

            ShowConfirmation(sprite, pieces);
        };

        imageGrid.Add(btn);
    }

    private void ShowConfirmation(Sprite sprite, int pieces)
    {
        pendingSprite = sprite;
        pendingPieces = pieces;

        if (confirmImage != null) confirmImage.style.backgroundImage = new StyleBackground(sprite);
        if (confirmPieces != null) confirmPieces.text = $"ピース数: {pieces} 枚";
        
        if (confirmationOverlay != null) confirmationOverlay.style.display = DisplayStyle.Flex;
    }

    private void OnConfirmYes()
    {
        if (puzzleManager != null && pendingSprite != null) {
            puzzleManager.StartPuzzle(pendingSprite, pendingPieces);
            uiDoc.gameObject.SetActive(false);
            if (confirmationOverlay != null) confirmationOverlay.style.display = DisplayStyle.None;
        }
    }

    private void OnConfirmNo()
    {
        if (confirmationOverlay != null) confirmationOverlay.style.display = DisplayStyle.None;
    }

    public void Open()
    {
        lastOpenTime = Time.time;
        uiDoc.gameObject.SetActive(true);
        RefreshImageGrid();
    }
}
