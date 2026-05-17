// Project: Jigsaw - Ver 1.2
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.IO;

public class PuzzleSelectionManager : MonoBehaviour
{
    public UIDocument uiDoc;
    public PuzzleManager puzzleManager;
    public string imageFolderPath = "PuzzleBase"; // Resources フォルダ内からの相対パス

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

    // スクロール用変数
    private bool isDraggingGrid = false;
    private bool wasDraggingGrid = false;
    private Vector2 startMousePos = Vector2.zero;
    private Vector2 startScrollPos = Vector2.zero;
    private const float DragThreshold = 10f;

    private readonly int[] pieceOptions = { 24, 96, 216, 486 };

    private void OnGridPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0) return;
        isDraggingGrid = true;
        wasDraggingGrid = false;
        startMousePos = evt.position;
        startScrollPos = imageGrid.scrollOffset;
        // 最初からはキャプチャしない（ボタンのクリックを優先させるため）
    }

    private void OnGridPointerMove(PointerMoveEvent evt)
    {
        if (!isDraggingGrid) return;

        Vector2 delta = (Vector2)evt.position - startMousePos;
        if (!wasDraggingGrid && delta.magnitude > DragThreshold)
        {
            wasDraggingGrid = true;
            // 大きく動いたらキャプチャを開始して、以降のイベントを独占する
            imageGrid.CapturePointer(evt.pointerId);
        }

        if (wasDraggingGrid)
        {
            imageGrid.scrollOffset = startScrollPos - delta;
        }
    }

    private void OnGridPointerUp(PointerUpEvent evt)
    {
        if (isDraggingGrid)
        {
            if (wasDraggingGrid)
            {
                imageGrid.ReleasePointer(evt.pointerId);
            }
            isDraggingGrid = false;
            // wasDraggingGridは、ボタンのclickedイベントが処理されるまで維持する必要がある
            // UI ToolkitのクリックイベントはPointerUpの後に発生するため、少し遅らせてフラグをクリアする
            Invoke(nameof(ClearDraggingFlag), 0.1f);
        }
    }

    private void ClearDraggingFlag()
    {
        wasDraggingGrid = false;
    }

    void OnEnable()
    {
        Application.runInBackground = true; // UnityPlayの全画面ボタン対策
        if (puzzleManager != null) puzzleManager.ClearExistingPuzzle();
        
        if (uiDoc == null) uiDoc = GetComponent<UIDocument>();
        if (uiDoc == null) return;

        var root = uiDoc.rootVisualElement;
        imageGrid = root.Q<ScrollView>("ImageGrid");
        if (imageGrid != null)
        {
            imageGrid.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            imageGrid.mouseWheelScrollSize = 100f; // スクロール感度

            // ドラッグスクロール処理（PCマウス用）
            imageGrid.RegisterCallback<PointerDownEvent>(OnGridPointerDown);
            imageGrid.RegisterCallback<PointerMoveEvent>(OnGridPointerMove);
            imageGrid.RegisterCallback<PointerUpEvent>(OnGridPointerUp);
            imageGrid.RegisterCallback<PointerCaptureOutEvent>(evt => isDraggingGrid = false);
        }
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
        imageGrid.Clear(); // 既存の要素をクリアして重複を防止

        // パスのクリーニング（Inspectorでの設定ミス対策）
        string cleanPath = imageFolderPath.Replace("Assets/Resources/", "").Replace("Resources/", "");
        if (cleanPath != imageFolderPath)
        {
            Debug.LogWarning($"[PuzzleSelectionManager] パスを修正しました: {imageFolderPath} -> {cleanPath}");
            imageFolderPath = cleanPath;
        }

        if (cachedSprites == null)
        {
            cachedSprites = new List<(Sprite sprite, string name)>();
            Debug.Log($"[PuzzleSelectionManager] Resources/{imageFolderPath} から画像を読み込んでいます...");
            
            // 全てのオブジェクトをロードして、SpriteまたはTexture2Dとして処理
            Object[] assets = Resources.LoadAll(imageFolderPath);
            Debug.Log($"[PuzzleSelectionManager] {assets.Length} 個のアセットが見つかりました。");

            foreach (var asset in assets)
            {
                if (asset == null) continue;
                
                // 重複チェック
                if (cachedSprites.Exists(x => x.name == asset.name)) continue;

                if (asset is Sprite s)
                {
                    cachedSprites.Add((s, s.name));
                    Debug.Log($"[PuzzleSelectionManager] Spriteをロードしました: {s.name}");
                }
                else if (asset is Texture2D tex)
                {
                    Sprite newSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    newSprite.name = tex.name;
                    cachedSprites.Add((newSprite, tex.name));
                    Debug.Log($"[PuzzleSelectionManager] Texture2DからSpriteを作成しました: {tex.name}");
                }
            }

            if (cachedSprites.Count == 0)
            {
                Debug.LogError($"[PuzzleSelectionManager] Resources/{imageFolderPath} 内に画像が見つかりませんでした。パスが正しいか、アセットがResourcesフォルダに含まれているか確認してください。");
            }
        }

        foreach (var item in cachedSprites)
        {
            CreateButton(item.sprite, item.name);
        }
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
            // スクロール操作直後はクリックを無視
            if (wasDraggingGrid) return;
            
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
        if (puzzleManager != null) puzzleManager.ClearExistingPuzzle();
        
        lastOpenTime = Time.time;
        uiDoc.gameObject.SetActive(true);
        RefreshImageGrid();
    }
}
