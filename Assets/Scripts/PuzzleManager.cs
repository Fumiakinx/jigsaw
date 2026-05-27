// Project: Jigsaw - Ver 1.4
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

public class PuzzleManager : MonoBehaviour
{
    #if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void CloseCanvasWindow();
    [DllImport("__Internal")]
    private static extern void OnPuzzleComplete(float elapsedTime);
    #endif

    public bool isLoadedFromWeb = false; // Webからの通常起動かどうかのフラグ
    public Sprite sourceSprite;
    public int targetPieces = 200;
    
    public SpriteRenderer backgroundGuideRenderer;
    public List<PuzzlePiece> allPieces = new List<PuzzlePiece>();

    private int rows;
    private int cols;
    private int totalPieces;
    private float adjacentDistanceThreshold;
    private int topSortingOrder = 3000;
    private float puzzleHalfWidth;
    private float puzzleHalfHeight;
    private AudioSource audioSource;
    private Camera mainCam;
    public AudioClip snapSound;
    
    [Header("Clear Effects")]
    [Header("UI References")]
    public UIDocument completionUIDoc;
    public UIDocument loadingUIDoc;
    public UIDocument pauseUIDoc;
    public UIDocument hudUIDoc; // 追加：スマホ用HUD UI
    private Button btnShowGuide; // 追加：見本表示用の目のボタン
    private Button btnPause; // 追加：一時停止用のボタン
    public PuzzleSelectionManager selectionManager;
    public AudioClip clearSound;

    private float startTime;
    private float totalPausedTime = 0f;
    private float pauseStartTime;
    private bool isPaused = false;
    private bool isFinished = false;

    // タッチ＆ダブルタップ回転用の変数
    private float lastClickTime = 0f;
    private int lastClickedPieceId = -1;
    private const float DOUBLE_TAP_TIME = 0.3f;
    private int lastTouchCount = 0;

    [Range(0, 0.1f)] public float bevelWidth = 0.02f; 
    public float bevelFalloff = 1.0f;
    public float edgeDarkness = 1.6f;
    public float specularPower = 32f;
    public float specularIntensity = 1.6f;

    private class EdgeData
    {
        public int type; // 1: 凸, -1: 凹, 0: 平ら
        public float headWidth;    
        public float headHeight;   
        public float neckDepth;    
        public float shoulderWaveL; 
        public float shoulderWaveR; 
        public float centerShift;  
        public float nW_ratio; 
        public List<Vector2> cachedNormalizedPoints;
    }

    private Material shadowMaterial;

    private EdgeData[,] horizontalEdges;
    private EdgeData[,] verticalEdges;
    private Vector2[,] cornerPoints;
    private PuzzlePiece[,] pieceGrid;
    private PuzzlePiece draggingPiece = null;
    private float _uStart, _vStart, _uScale, _vScale;

    private List<Material> trackedMaterials = new List<Material>();
    private List<Mesh> trackedMeshes = new List<Mesh>();
    private int maxConnectedPieces = 1;

    void Start()
    {
        // 静的な設定はStartで一度だけ行う
        UnityEngine.QualitySettings.antiAliasing = 0;
        Application.runInBackground = true; // 全画面切り替え時のポーズを防ぐ

        // FPS設定 (WebGLの特殊な挙動に対応)
#if UNITY_WEBGL && !UNITY_EDITOR
        QualitySettings.vSyncCount = 1; // 垂直同期を有効にしてブラウザの更新に合わせる
        Application.targetFrameRate = -1; 
#else
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 1;
#endif

        // ライトの無効化も一度だけでOK
        Light dl = FindAnyObjectByType<Light>(); 
        if (dl != null) dl.enabled = false;

        SetupCamera();
        gameObject.TryGetComponent(out audioSource);
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        if (completionUIDoc != null) completionUIDoc.gameObject.SetActive(false);
        if (pauseUIDoc != null) pauseUIDoc.gameObject.SetActive(false);
        if (hudUIDoc != null) hudUIDoc.gameObject.SetActive(false); // 初期状態ではHUDを非表示に
    }

    void OnDestroy()
    {
        ClearTrackedAssets();
    }

    private bool isGenerating = false;

    public void StartPuzzle(Sprite selectedSprite, int pieceCount = 200)
    {
        if (isGenerating) {
            Debug.LogWarning("[PuzzleManager] Already generating...");
            return;
        }
        if (selectedSprite == null) return;
        Debug.Log($"[PuzzleManager] StartPuzzle: {selectedSprite.name}, Texture: {selectedSprite.texture.name} ({selectedSprite.texture.width}x{selectedSprite.texture.height})");
        sourceSprite = selectedSprite;
        targetPieces = pieceCount;
        ClearExistingPuzzle();

        if (shadowMaterial == null) {
            Shader shadowShader = Resources.Load<Shader>("Shaders/JigsawShadow");
            if (shadowShader == null) shadowShader = Shader.Find("Custom/JigsawShadow");
            if (shadowShader != null) {
                shadowMaterial = new Material(shadowShader);
                shadowMaterial.SetColor("_Color", new Color(0.0f, 0.0f, 0.0f, 0.5f)); // 元の黒い影
            }
        }
        
        float aspect = sourceSprite.rect.width / sourceSprite.rect.height;
        cols = Mathf.RoundToInt(Mathf.Sqrt(pieceCount * aspect));
        rows = Mathf.CeilToInt((float)pieceCount / cols);
        totalPieces = cols * rows;

        cornerPoints = new Vector2[rows + 1, cols + 1];
        float ppu = sourceSprite.pixelsPerUnit;
        float pW = sourceSprite.rect.width / cols / ppu;
        float pH = sourceSprite.rect.height / rows / ppu;
        float hW = sourceSprite.rect.width / ppu / 2f;
        float hH = sourceSprite.rect.height / ppu / 2f;
        InitializeCornerPoints(pW, pH, hW, hH);
        InitializeEdgeData();

        if (backgroundGuideRenderer == null) {
            GameObject g = new GameObject("BackgroundGuide");
            backgroundGuideRenderer = g.AddComponent<SpriteRenderer>();
            g.SetActive(false);
        }

        if(backgroundGuideRenderer != null)
        {
            backgroundGuideRenderer.sprite = sourceSprite;
            backgroundGuideRenderer.transform.localScale = Vector3.one;
            SetGuideVisible(false); // 初期状態は背景ガイドとして設定
        }

        if (loadingUIDoc != null)
        {
            loadingUIDoc.gameObject.SetActive(true);
            var root = loadingUIDoc.rootVisualElement.Q<VisualElement>("LoadingRoot");
            if (root != null && sourceSprite != null) 
                root.style.backgroundImage = new StyleBackground(sourceSprite);
            
            var fill = loadingUIDoc.rootVisualElement.Q<VisualElement>("BarFill");
            if (fill != null) fill.style.width = Length.Percent(0);
        }
        
        startTime = Time.time;
        totalPausedTime = 0f;
        isPaused = false;
        maxConnectedPieces = 1;

        // スマホ用HUDのセットアップ
        SetupHUD();

        SetupCamera();
        ClearTrackedAssets();
        StopAllCoroutines();
        StartCoroutine(GeneratePuzzlePiecesCoroutine());
    }

    public void ClearExistingPuzzle()
    {
        StopAllCoroutines();
        int count = 0;
        PuzzlePiece[] allPiecesInScene = GameObject.FindObjectsByType<PuzzlePiece>(FindObjectsInactive.Include);
        foreach (var p in allPiecesInScene)
        {
            if (p == null) continue;
            Transform t = p.transform;
            while (t.parent != null && (t.parent.name.Contains("PieceCluster") || t.parent.name.Contains("Piece_")))
                t = t.parent;
            DestroyImmediate(t.gameObject);
            count++;
        }

        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in roots)
        {
            if (root == null || root == gameObject) continue;
            if (root.name.Contains("PieceCluster") || root.name.Contains("Piece_")) {
                DestroyImmediate(root);
                count++;
            }
        }

        Debug.Log($"[PuzzleManager] {count} 個のパズルオブジェクトを完全に削除しました。");
        allPieces.Clear();
        if (backgroundGuideRenderer != null) backgroundGuideRenderer.gameObject.SetActive(false);
        ClearTrackedAssets();
    }

    private void ClearTrackedAssets()
    {
        foreach (var m in trackedMaterials) if (m != null) DestroyImmediate(m);
        foreach (var m in trackedMeshes) if (m != null) DestroyImmediate(m);
        trackedMaterials.Clear();
        trackedMeshes.Clear();
    }

    // 一時停止画面の「画像選択に戻る」ボタン用コールバック
    private void OnReturnToSelectionClicked()
    {
        TogglePause(); // ポーズ状態を解除
        ReturnToTitle(); // 画像選択画面に戻る
    }

    // 目玉ボタン（見本表示用）のコールバック群
    // Pointerイベント (モバイル/一部のPC環境用)
    private void OnGuideShowPointerDown(PointerDownEvent evt) { SetGuideVisible(true); }
    private void OnGuideShowPointerUp(PointerUpEvent evt) { SetGuideVisible(false); }
    private void OnGuideShowPointerLeave(PointerLeaveEvent evt) { SetGuideVisible(false); }

    // マウスイベント (PC環境用)
    private void OnGuideShowMouseDown(MouseDownEvent evt) { SetGuideVisible(true); }
    private void OnGuideShowMouseUp(MouseUpEvent evt) { SetGuideVisible(false); }
    private void OnGuideShowMouseLeave(MouseLeaveEvent evt) { SetGuideVisible(false); }

    private void SetupHUD()
    {
        if (hudUIDoc == null) return;
        
        // メニューのHTML統合に伴い、Unity側の右上丸ボタンHUDは非表示にします
        hudUIDoc.gameObject.SetActive(false);
    }

    private int GetTouchCount()
    {
        // 新しいInput Systemでタッチ数を取得
        var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
        if (touchscreen != null)
        {
            int activeTouches = 0;
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed) activeTouches++;
            }
            return activeTouches;
        }
        // フォールバックとして旧Inputを使用
        return UnityEngine.Input.touchCount;
    }

    void Update()
    {
        Vector2 mousePos;
        bool leftPressed, leftDown, leftUp, rightDown, spaceDown, spaceUp;

        var pointer = UnityEngine.InputSystem.Pointer.current;
        var kb = UnityEngine.InputSystem.Keyboard.current;

        if (pointer != null)
        {
            mousePos = pointer.position.ReadValue();
            leftPressed = pointer.press.isPressed;
            leftDown = pointer.press.wasPressedThisFrame;
            leftUp = pointer.press.wasReleasedThisFrame;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            rightDown = (mouse != null) && mouse.rightButton.wasPressedThisFrame;

            spaceDown = (kb != null) && kb.spaceKey.wasPressedThisFrame;
            spaceUp = (kb != null) && kb.spaceKey.wasReleasedThisFrame;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) TogglePause();
        }
        else
        {
            mousePos = UnityEngine.Input.mousePosition;
            leftPressed = UnityEngine.Input.GetMouseButton(0);
            leftDown = UnityEngine.Input.GetMouseButtonDown(0);
            leftUp = UnityEngine.Input.GetMouseButtonUp(0);
            rightDown = UnityEngine.Input.GetMouseButtonDown(1);
            spaceDown = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space);
            spaceUp = UnityEngine.Input.GetKeyUp(UnityEngine.KeyCode.Space);
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape)) TogglePause();
        }

        if (isPaused || isFinished) return;

        if (spaceDown) SetGuideVisible(true);
        else if (spaceUp) SetGuideVisible(false);

        if (rightDown)
        {
            PuzzlePiece target = (draggingPiece != null) ? draggingPiece : GetTopmostPiece(mousePos);
            if (target != null) target.RotateGroup(Camera.main.ScreenToWorldPoint(mousePos));
        }

        // スマホでのドラッグ中マルチタップによる回転の検知
        int currentTouchCount = GetTouchCount();
        if (draggingPiece != null && currentTouchCount >= 2 && lastTouchCount < 2)
        {
            // 掴んでいるピースを回転
            draggingPiece.RotateGroup(Camera.main.ScreenToWorldPoint(mousePos));
        }
        lastTouchCount = currentTouchCount;

        if (leftDown)
        {
            PuzzlePiece piece = GetTopmostPiece(mousePos);
            if (piece != null)
            {
                float currentTime = Time.time;
                if (piece.id == lastClickedPieceId && (currentTime - lastClickTime) < DOUBLE_TAP_TIME)
                {
                    // ダブルタップ成立
                    if (draggingPiece != null)
                    {
                        draggingPiece.EndDrag();
                        draggingPiece = null;
                    }
                    piece.RotateGroup(Camera.main.ScreenToWorldPoint(mousePos));
                    lastClickedPieceId = -1; // ダブルタップ後はIDリセット
                }
                else
                {
                    lastClickedPieceId = piece.id;
                    lastClickTime = currentTime;

                    draggingPiece = piece;
                    draggingPiece.BeginDrag(Camera.main.ScreenToWorldPoint(mousePos));
                }
            }
        }
        else if (leftPressed && draggingPiece != null)
        {
            draggingPiece.OnPointerDrag(Camera.main.ScreenToWorldPoint(mousePos));
        }
        else if (leftUp && draggingPiece != null)
        {
            draggingPiece.EndDrag();
            draggingPiece = null;
        }
    }

    private PuzzlePiece GetTopmostPiece(Vector2 mousePosition)
    {
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, 10f));
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);
        PuzzlePiece topPiece = null;
        int maxOrder = int.MinValue;

        foreach (var hit in hits)
        {
            PuzzlePiece p = hit.GetComponent<PuzzlePiece>();
            if (p != null)
            {
                int order = 0;
                var sg = p.GetComponentInParent<SortingGroup>();
                if (sg != null) order = sg.sortingOrder;
                else {
                    var r = p.GetComponent<Renderer>();
                    if (r != null) order = r.sortingOrder;
                }
                if (order > maxOrder) { maxOrder = order; topPiece = p; }
            }
        }
        return topPiece;
    }

    private IEnumerator GeneratePuzzlePiecesCoroutine()
    {
        try {
            float genStartTime = Time.realtimeSinceStartup;
            float ppu = sourceSprite.pixelsPerUnit;
            float tw = sourceSprite.rect.width;
            float th = sourceSprite.rect.height;
            float pWW = tw / cols / ppu;
            float pWH = th / rows / ppu;
            puzzleHalfWidth = tw / ppu / 2f;
            puzzleHalfHeight = th / ppu / 2f;
            adjacentDistanceThreshold = (pWW + pWH) * 0.08f;

            pieceGrid = new PuzzlePiece[rows, cols];
            float bWidth = Mathf.Min(pWW, pWH) * 0.15f;

            // シェーダーをResourcesからロード（ビルド時の欠落対策）
            Shader jigsawShader = Resources.Load<Shader>("Shaders/Jigsaw2D");
            if (jigsawShader == null) jigsawShader = Shader.Find("Custom/Jigsaw2D");
            if (jigsawShader == null) jigsawShader = Shader.Find("Sprites/Default");
            
            Material puzzleMaterial = new Material(jigsawShader);
            if (sourceSprite.texture != null) {
                puzzleMaterial.mainTexture = sourceSprite.texture;
                puzzleMaterial.SetTexture("_MainTex", sourceSprite.texture);
                puzzleMaterial.SetFloat("_EdgeDarkness", edgeDarkness);
                puzzleMaterial.SetFloat("_SpecularStrength", specularIntensity);
            }
            trackedMaterials.Add(puzzleMaterial);

            int total = rows * cols;
            int count = 0;
            float adaptiveBevelScale = total > 400 ? Mathf.Sqrt(16f / total) : Mathf.Sqrt(24f / total);
            float scaledBevelWidth = 0.15f * adaptiveBevelScale;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    Vector2 cBL = cornerPoints[y, x], cBR = cornerPoints[y, x + 1], cTR = cornerPoints[y + 1, x + 1], cTL = cornerPoints[y + 1, x];
                    GenerateSinglePiece(x, y, cBL, cBR, cTR, cTL, (int)tw, (int)th, ppu, puzzleMaterial, scaledBevelWidth);
                    count++;
                    if (loadingUIDoc != null) {
                        var fill = loadingUIDoc.rootVisualElement.Q<VisualElement>("BarFill");
                        if (fill != null) fill.style.width = Length.Percent((float)count / total * 100f);
                    }
                    if (count % 20 == 0) yield return null;
                }
            }
            if (loadingUIDoc != null) loadingUIDoc.gameObject.SetActive(false);
            if (backgroundGuideRenderer != null) {
                SetGuideVisible(false);
            }
            Debug.Log($"[LOG] Generation Complete in {Time.realtimeSinceStartup - genStartTime:F3}s");
            yield break;
        } finally {
            isGenerating = false;
            if (loadingUIDoc != null) loadingUIDoc.gameObject.SetActive(false);
        }
    }

    private void GenerateSinglePiece(int x, int y, Vector2 cBL, Vector2 cBR, Vector2 cTR, Vector2 cTL, int texWidth, int texHeight, float ppu, Material puzzleMaterial, float bWidth)
    {
        try {
            EdgeData eL = (x == 0) ? new EdgeData { type = 0 } : verticalEdges[y, x];
            EdgeData eR = (x == cols - 1) ? new EdgeData { type = 0 } : verticalEdges[y, x + 1];
            EdgeData eB = (y == 0) ? new EdgeData { type = 0 } : horizontalEdges[y, x];
            EdgeData eT = (y == rows - 1) ? new EdgeData { type = 0 } : horizontalEdges[y + 1, x];

            Vector2 pieceCenter = (cBL + cBR + cTR + cTL) / 4f;
            List<Vector2> edgeB = GenerateEdgePoints(cBL, cBR, eB);
            List<Vector2> edgeR = GenerateEdgePoints(cBR, cTR, eR);
            List<Vector2> edgeT = GenerateEdgePoints(cTL, cTR, eT);
            List<Vector2> edgeL = GenerateEdgePoints(cBL, cTL, eL);

            List<Vector2> verts = new List<Vector2>();
            float epsSqr = 1e-8f;
            void Collect(List<Vector2> s, bool rev) {
                if (s == null) return;
                if (rev) for (int i = s.Count-1; i >= 0; i--) { if (verts.Count > 0 && (verts[verts.Count-1]-s[i]).sqrMagnitude < epsSqr) continue; verts.Add(s[i]); }
                else for (int i = 0; i < s.Count; i++) { if (verts.Count > 0 && (verts[verts.Count-1]-s[i]).sqrMagnitude < epsSqr) continue; verts.Add(s[i]); }
            }
            Collect(edgeB, false); Collect(edgeR, false); Collect(edgeT, true); Collect(edgeL, true);
            if (verts.Count > 2 && (verts[verts.Count-1]-verts[0]).sqrMagnitude < epsSqr) verts.RemoveAt(verts.Count-1);

            for (int i = 0; i < verts.Count; i++) verts[i] -= pieceCenter;

            GameObject cluster = new GameObject($"PieceCluster_{x}_{y}");
            cluster.transform.SetParent(transform);
            cluster.transform.localPosition = (Vector3)pieceCenter;
            // cluster.AddComponent<SortingGroup>(); // パフォーマンスのため、初期状態では削除

            GameObject pieceObject = new GameObject($"Piece_{x}_{y}");
            pieceObject.transform.SetParent(cluster.transform);
            pieceObject.transform.localPosition = Vector3.zero;
            
            float pWW = (float)texWidth / ppu, pWH = (float)texHeight / ppu;
            Mesh pieceMesh = CreateIdealPieceMesh(pieceCenter, pWW, pWH, verts, bWidth);
            pieceObject.AddComponent<MeshFilter>().mesh = pieceMesh;
            var pieceRenderer = pieceObject.AddComponent<MeshRenderer>();
            pieceRenderer.sharedMaterial = puzzleMaterial;
            
            // 重要：本体を先に描き、影を後に描くことで、Zテストにより「自分の影」だけを消す
            // 初期状態は全て N=1 なので、19950ベースで sortingOrder を初期化（最も手前）
            int baseOrder = (2000 - 5) * 10 + (y * cols + x) % 10;
            pieceRenderer.sortingOrder = baseOrder; 
            
            // Z座標を設定：ピースを手前に、影を僅かに奥に
            float pieceZ = -baseOrder * 0.0001f;
            pieceObject.transform.localPosition = new Vector3(0, 0, pieceZ);
            
            // 影オブジェクトの生成
            GameObject shadowObject = new GameObject("Shadow");
            shadowObject.transform.SetParent(pieceObject.transform); 
            shadowObject.AddComponent<MeshFilter>().mesh = pieceMesh;
            if (shadowMaterial != null) {
                var sr = shadowObject.AddComponent<MeshRenderer>();
                sr.sharedMaterial = shadowMaterial;
                sr.sortingOrder = baseOrder + 1; // 本体(baseOrder)の直後に描画
            }
            // 影の位置設定：ワールド空間で右下にずらし、Zは本体より僅かに奥(0.00005f)
            shadowObject.transform.position = pieceObject.transform.position + new Vector3(0.05f, -0.05f, 0.00005f);

            try {
                // PolygonCollider2D は重いため、BoxCollider2D に変更して負荷を大幅に軽減
                var pc2d = pieceObject.AddComponent<BoxCollider2D>();
                // ピースのサイズに合わせてコライダーのサイズを調整
                pc2d.size = new Vector2(pWW / cols * 1.2f, pWH / rows * 1.2f); 
            } catch { }
            
            var pi = pieceObject.AddComponent<PuzzlePiece>();
            pi.id = y * cols + x; pi.gridX = x; pi.gridY = y; pi.manager = this; pi.correctPos = pieceCenter;
            pieceGrid[y, x] = pi; allPieces.Add(pi);
            pi.baseOrder = baseOrder;
            // SortingGroup はドラッグ時のみ動的に追加されるため、ここでは設定不要

            
            float cH = Camera.main.orthographicSize, cW = cH * Camera.main.aspect;
            cluster.transform.position = GetPeripheralPosition(cW, cH, Mathf.Max(pWW/cols, pWH/rows) * 0.8f);
            cluster.transform.rotation = Quaternion.Euler(0, 0, 90f * UnityEngine.Random.Range(0, 4));
            
            // 初期の回転と位置が決定した後、影のオフセットをワールド座標基準で同期する
            pi.UpdateShadowPosition();
        } catch (System.Exception e) { Debug.LogError($"[CRITICAL] Piece({x},{y}) Failed: {e}"); }
    }

    private List<Vector2> GenerateEdgePoints(Vector2 start, Vector2 end, EdgeData edge)
    {
        List<Vector2> points = new List<Vector2>();
        if (edge == null || edge.type == 0) { points.Add(start); points.Add(end); return points; }
        Vector2 dir = (end - start).normalized, normal = new Vector2(-dir.y, dir.x);
        float scale = (end - start).magnitude;
        float hW = edge.headWidth, hH = edge.headHeight, nD = edge.neckDepth;
        float sF = totalPieces > 400 ? 0.28f : 0.32f, hF = totalPieces > 400 ? 0.26f : 0.30f;
        if (hW/100f*scale > scale*sF) { float r = (scale*sF)/(hW/100f*scale); hW *= r; hH *= r; nD *= r; }
        if ((hH+nD)/100f*scale > scale*hF) { float r = (scale*hF)/((hH+nD)/100f*scale); hH *= r; nD *= r; }
        float sM = 28f + hW*0.7f, mid = Mathf.Clamp(50f + edge.centerShift, sM, 100f - sM), nW = hW * edge.nW_ratio;

        Vector2 pA = Vector2.zero;
        
        // ピースの個性をさらに際立たせるため、首の太さも左右非対称にする
        float neckW_L = nW + (edge.headWidth * 7.7f % 4f) - 2f;
        float neckW_R = nW + (edge.headHeight * 8.8f % 4f) - 2f;
        Vector2 pNeckL = new Vector2(mid - neckW_L, -nD);
        Vector2 pTop = new Vector2(mid, -nD - hH);
        Vector2 pNeckR = new Vector2(mid + neckW_R, -nD);
        Vector2 pB = new Vector2(100, 0);
        
        // WebGL（Flash版）での最適化：ピース数が多い時はポリゴン数を大胆に削減する
        int res = totalPieces >= 400 ? 16 : (totalPieces >= 100 ? 24 : 32); 
        int step = res / 2; // 各セグメントの分割数
        List<Vector2> nPts = new List<Vector2>();
        
        float bY = -nD - hH * 0.55f;
        float dipL = edge.shoulderWaveL;
        float dipR = edge.shoulderWaveR;
        
        // 波打ち始める位置（ネックからどれくらい離れた場所からディップを開始するか）
        // ピースごとに開始位置（waveWidth）と、肩の高さ（slant）をランダム（擬似乱数）に変動させ、
        // 直線部分の長さと曲がり具合に有機的なバリエーションを持たせます
        float waveWidthL = 16f + (edge.headWidth * 11.1f % 12f);  // 16f ~ 28f (直線部分の長さをランダム化)
        float waveWidthR = 16f + (edge.headHeight * 13.3f % 12f); // 16f ~ 28f
        float slantL = (edge.centerShift * 7.7f % 3f) - 1.5f;     // 肩の緩やかな傾き (-1.5 ~ 1.5)
        float slantR = (edge.neckDepth * 9.9f % 3f) - 1.5f;       // 肩の緩やかな傾き (-1.5 ~ 1.5)
        
        Vector2 pShoulderStartL = new Vector2(Mathf.Max(2f, pNeckL.x - waveWidthL), slantL);
        Vector2 pShoulderStartR = new Vector2(Mathf.Min(98f, pNeckR.x + waveWidthR), slantR);

        // 1. Base to ShoulderStartL (Organic gentle curve)
        // カド（pA）は90度を保つためY=0の接線、ShoulderStartLではY=slantLの水平な接線となり、緩やかなS字を描く
        Vector2 c1_0 = new Vector2(pShoulderStartL.x * 0.4f, 0);
        Vector2 c2_0 = new Vector2(pShoulderStartL.x * 0.6f, slantL);
        for(int i=0; i<step; i++) nPts.Add(GetBezierPoint(pA, c1_0, c2_0, pShoulderStartL, i/(float)step));

        // 2. ShoulderStartL to NeckL (The gentle dip)
        // 前のカーブの接線（水平：slantL）を完璧に引き継ぎ、カドの発生を防止
        Vector2 c1_1 = new Vector2(pShoulderStartL.x + waveWidthL * 0.4f, slantL);
        // その後、滑らかに波の頂上へ向かい、首へと落ちる
        Vector2 c2_1 = new Vector2(pNeckL.x, dipL * 1.2f);
        for(int i=0; i<step; i++) nPts.Add(GetBezierPoint(pShoulderStartL, c1_1, c2_1, pNeckL, i/(float)step));
        
        // ピースごとにユニークな形（非対称性）を持たせるための擬似乱数
        // ピース数が多いときでも個性が目立つように、意図的に変動幅を大きく取ります
        float skewX = (edge.headWidth * 13.5f % 8f) - 4f;    // -4 から 4 のズレ (頭の左右の傾き)
        float skewY = (edge.headHeight * 17.2f % 7f) - 3.5f; // -3.5 から 3.5 のズレ (頭の上下の歪み)
        float topFlatten = 0.3f + (edge.neckDepth * 11.3f % 0.65f); // 0.3 から 0.95 (頭頂部の丸み〜平坦さ)
        float topSlant = (edge.centerShift * 19.1f % 6f) - 3f; // -3 から 3 (頭頂部の接線の傾き)
        
        // 3. Neck to Bulb (left)
        // 非対称性を加えたバルブ位置
        Vector2 pBulbL = new Vector2(mid - hW * 0.5f + skewX, bY + skewY);
        // Y座標が逆転して余計な凹凸が出来ないよう、Yの移動量に対する割合で制御点を配置
        Vector2 c1_2 = new Vector2(pNeckL.x, pNeckL.y - (pNeckL.y - pBulbL.y) * 0.3f);
        Vector2 c2_2 = new Vector2(pBulbL.x, pBulbL.y + (pNeckL.y - pBulbL.y) * 0.4f);
        for(int i=0; i<step; i++) nPts.Add(GetBezierPoint(pNeckL, c1_2, c2_2, pBulbL, i/(float)step));
        
        // 4. Bulb to Top (left)
        // 頭の頂点も傾きに合わせて少しズラす
        Vector2 pTopSkewed = new Vector2(pTop.x + skewX * 1.5f, pTop.y);
        Vector2 c1_3 = new Vector2(pBulbL.x, pBulbL.y - (pBulbL.y - pTopSkewed.y) * 0.4f);
        
        // 頂上の接線（傾き）を計算。左右で同じ傾き（slope）を使うことで、尖りを防ぎ「完全に滑らかな斜めの頂上」を作る
        float slope = topSlant / 10f;
        float dx_L = (pTopSkewed.x - pBulbL.x) * topFlatten;
        Vector2 c2_3 = new Vector2(pTopSkewed.x - dx_L, pTopSkewed.y - dx_L * slope);
        for(int i=0; i<step; i++) nPts.Add(GetBezierPoint(pBulbL, c1_3, c2_3, pTopSkewed, i/(float)step));
        
        // 5. Top to Bulb (right)
        // 右側のバルブは Y座標のズレを逆にすることで、頭全体が少し傾いたようなユニークさを生む
        Vector2 pBulbR = new Vector2(mid + hW * 0.5f + skewX, bY - skewY);
        float dx_R = (pBulbR.x - pTopSkewed.x) * topFlatten;
        Vector2 c1_4 = new Vector2(pTopSkewed.x + dx_R, pTopSkewed.y + dx_R * slope);
        Vector2 c2_4 = new Vector2(pBulbR.x, pBulbR.y - (pBulbR.y - pTopSkewed.y) * 0.4f);
        for(int i=0; i<step; i++) nPts.Add(GetBezierPoint(pTopSkewed, c1_4, c2_4, pBulbR, i/(float)step));
        
        // 6. Bulb to Neck (right)
        Vector2 c1_5 = new Vector2(pBulbR.x, pBulbR.y + (pNeckR.y - pBulbR.y) * 0.4f);
        Vector2 c2_5 = new Vector2(pNeckR.x, pNeckR.y - (pNeckR.y - pBulbR.y) * 0.3f);
        for(int i=0; i<step; i++) nPts.Add(GetBezierPoint(pBulbR, c1_5, c2_5, pNeckR, i/(float)step));
        
        // 7. Neck to ShoulderStartR (The gentle dip)
        Vector2 c1_6 = new Vector2(pNeckR.x, dipR * 1.2f);
        // 肩への着地も水平（slantR）にしてスムーズに
        Vector2 c2_6 = new Vector2(pShoulderStartR.x - waveWidthR * 0.4f, slantR);
        for(int i=0; i<step; i++) nPts.Add(GetBezierPoint(pNeckR, c1_6, c2_6, pShoulderStartR, i/(float)step));

        // 8. ShoulderStartR to Base (Organic gentle curve)
        // 前のカーブの接線（水平：slantR）を引き継ぐ
        Vector2 c1_7 = new Vector2(pShoulderStartR.x + (100f - pShoulderStartR.x) * 0.4f, slantR);
        // カド（pB）は90度を保つためY=0の接線で着地
        Vector2 c2_7 = new Vector2(pShoulderStartR.x + (100f - pShoulderStartR.x) * 0.6f, 0);
        for(int i=0; i<=step; i++) nPts.Add(GetBezierPoint(pShoulderStartR, c1_7, c2_7, pB, i/(float)step));
        
        foreach(var n in nPts) points.Add(start + dir*(n.x/100f)*scale + normal*(n.y/100f)*scale*edge.type);
        return points;
    }

    private Vector2 GetBezierPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) { float u = 1-t; return u*u*u*p0 + 3*u*u*t*p1 + 3*u*t*t*p2 + t*t*t*p3; }


    private Mesh CreateIdealPieceMesh(Vector2 center, float puzzleW, float puzzleH, List<Vector2> outerVerts2D, float bWidth)
    {
        outerVerts2D = EnsureCCW(outerVerts2D);
        Mesh m = new Mesh();
        int n = outerVerts2D.Count;
        List<Vector3> combinedVerts = new List<Vector3>();
        List<Vector2> combinedUVs = new List<Vector2>();
        List<Color> combinedColors = new List<Color>();

        // 1. 外側の頂点
        for (int i = 0; i < n; i++) {
            Vector2 p = outerVerts2D[i];
            Vector2 dir = p.normalized; // 中心からの方向（ベベルの勾配方向）
            combinedVerts.Add(new Vector3(p.x, p.y, 0));
            combinedUVs.Add(new Vector2((p.x + center.x) / puzzleW + 0.5f, (p.y + center.y) / puzzleH + 0.5f));
            combinedColors.Add(new Color((dir.x + 1f) * 0.5f, (dir.y + 1f) * 0.5f, 0, 1.0f)); // Edge influence and direction
        }

        // 2. 内側の頂点 (中心に向かって単純に縮小させることで、自己交差を100%防ぐ)
        List<Vector2> innerVerts2D = new List<Vector2>();
        float pieceSize = Mathf.Min(puzzleW / cols, puzzleH / rows);
        
        // ピース数（サイズ）に応じた適応型（アダプティブ）ベベル幅の計算
        // 結合時の不自然な太さを解消するため、ベースを1.5%程度まで細くし、よりシャープな溝にします。
        float basePercentage = 0.060f; // 初期から見て4倍（さらに倍）に変更 (0.030f -> 0.060f)
        float pieceCountBoost = Mathf.Clamp(cols / 8f, 1.0f, 4.0f);
        float percentage = Mathf.Clamp(basePercentage * pieceCountBoost, 0.04f, 0.24f); // クランプ範囲も初期から見て4倍に変更 (0.02f->0.04f, 0.12f->0.24f)
        
        float scaleFactor = 1.0f - percentage;
        
        for (int i = 0; i < n; i++) {
            Vector2 p = outerVerts2D[i];
            // 単純に原点(0,0)に向かって縮小することで、形を保ったまま交差を防ぐ
            innerVerts2D.Add(p * scaleFactor);
        }

        for (int i = 0; i < n; i++) {
            Vector2 innerP = innerVerts2D[i];
            Vector2 dir = outerVerts2D[i].normalized; // 外側と同じ方向を使用
            combinedVerts.Add(new Vector3(innerP.x, innerP.y, 0f));
            combinedUVs.Add(new Vector2((innerP.x + center.x) / puzzleW + 0.5f, (innerP.y + center.y) / puzzleH + 0.5f));
            combinedColors.Add(new Color((dir.x + 1f) * 0.5f, (dir.y + 1f) * 0.5f, 0, 0.0f));
        }

        List<int> triangles = new List<int>();
        // 3. ベベル面 (側面)
        for (int i = 0; i < n; i++) {
            int oC = i, oN = (i + 1) % n;
            int iC = i + n, iN = ((i + 1) % n) + n;
            triangles.Add(oC); triangles.Add(oN); triangles.Add(iN);
            triangles.Add(oC); triangles.Add(iN); triangles.Add(iC);
        }

        // 4. 内側の面 (耳切り法による三角形分割)
        List<int> innerTris = TriangulatePolygon(innerVerts2D);
        foreach (int t in innerTris) triangles.Add(t + n);

        m.SetVertices(combinedVerts);
        m.SetUVs(0, combinedUVs);
        m.SetColors(combinedColors);
        m.SetTriangles(triangles, 0);
        
        Vector3[] normals = new Vector3[combinedVerts.Count];
        for (int i = 0; i < normals.Length; i++) normals[i] = new Vector3(0, 0, -1);
        m.normals = normals;
        m.RecalculateBounds();
        return m;
    }

    private List<int> TriangulatePolygon(List<Vector2> points)
    {
        List<int> triangles = new List<int>();
        int n = points.Count;
        if (n < 3) return triangles;

        List<int> indices = new List<int>(n);
        for (int i = 0; i < n; i++) indices.Add(i);

        int count = n;
        int iter = 0;
        int maxIter = n * 10;
        while (count > 2 && iter < maxIter)
        {
            iter++;
            bool earFound = false;
            for (int i = 0; i < count; i++)
            {
                int pIdx = indices[(i + count - 1) % count];
                int cIdx = indices[i];
                int nIdx = indices[(i + 1) % count];

                if (IsEar(pIdx, cIdx, nIdx, points, indices))
                {
                    triangles.Add(pIdx);
                    triangles.Add(cIdx);
                    triangles.Add(nIdx);
                    indices.RemoveAt(i);
                    count--;
                    earFound = true;
                    break;
                }
            }
            if (!earFound) {
                // 残りが3点なら、判定をスキップして最後の三角形として閉じる
                if (count == 3) {
                    triangles.Add(indices[0]);
                    triangles.Add(indices[1]);
                    triangles.Add(indices[2]);
                    break;
                }
                Debug.LogWarning($"[PuzzleManager] Triangulation failed to find an ear at iteration {iter}. Remaining vertices: {count}/{n}. Using fallback fan.");
                // フォールバック：残りの頂点を扇状に繋ぐ（穴を塞ぐ最低限の処理）
                Vector2 center = Vector2.zero;
                foreach (int idx in indices) center += points[idx];
                center /= count;
                // 中心点を最後に追加して繋ぐのは複雑なので、単に0番目を中心に扇状に繋ぐ
                int centerIdx = indices[0];
                for (int i = 1; i < count - 1; i++) {
                    triangles.Add(centerIdx);
                    triangles.Add(indices[i]);
                    triangles.Add(indices[i + 1]);
                }
                break; 
            }

        }
        
        if (iter >= maxIter) {
            Debug.LogError($"[PuzzleManager] Triangulation reached max iterations ({maxIter}). Polygon might be too complex or self-intersecting.");
        }
        
        return triangles;
    }

    private bool IsEar(int prev, int curr, int next, List<Vector2> points, List<int> currentIndices)
    {
        Vector2 a = points[prev];
        Vector2 b = points[curr];
        Vector2 c = points[next];

        // 凸角判定 (CCW)
        float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
        if (cross <= 1e-9f) return false;

        // 他の点が三角形の内部にあるかチェック
        for (int i = 0; i < currentIndices.Count; i++) {
            int idx = currentIndices[i];
            if (idx == prev || idx == curr || idx == next) continue;
            if (IsPointInTriangle(points[idx], a, b, c)) return false;
        }
        return true;
    }

    private bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float det = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
        if (Mathf.Abs(det) < 1e-10f) return false;
        float b1 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / det;
        float b2 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / det;
        float b3 = 1f - b1 - b2;
        return b1 >= -1e-6f && b2 >= -1e-6f && b3 >= -1e-6f;
    }

    private List<Vector2> EnsureCCW(List<Vector2> points)
    {
        float area = 0;
        for (int i = 0; i < points.Count; i++) area += (points[i].x * points[(i + 1) % points.Count].y - points[(i + 1) % points.Count].x * points[i].y);
        if (area < 0) { List<Vector2> res = new List<Vector2>(points); res.Reverse(); return res; }
        return points;
    }

    private void InitializeCornerPoints(float pw, float ph, float halfW, float halfH)
    {
        for (int y = 0; y <= rows; y++) {
            for (int x = 0; x <= cols; x++) {
                Vector2 pos = new Vector2(x * pw - halfW, y * ph - halfH);
                float jS = Mathf.Clamp(Mathf.Sqrt(100f / totalPieces), 0.3f, 1.0f), mJ = 0.15f * jS;
                if (x > 0 && x < cols && y > 0 && y < rows) { pos.x += UnityEngine.Random.Range(-mJ, mJ) * pw; pos.y += UnityEngine.Random.Range(-mJ, mJ) * ph; }
                else if ((x == 0 || x == cols) && (y > 0 && y < rows)) pos.y += UnityEngine.Random.Range(-0.1f * jS, 0.1f * jS) * ph;
                else if ((y == 0 || y == rows) && (x > 0 && x < cols)) pos.x += UnityEngine.Random.Range(-0.1f * jS, 0.1f * jS) * pw;
                cornerPoints[y, x] = pos;
            }
        }
    }

    private void InitializeEdgeData()
    {
        horizontalEdges = new EdgeData[rows + 1, cols]; verticalEdges = new EdgeData[rows, cols + 1];
        for (int r = 0; r <= rows; r++) for (int c = 0; c < cols; c++) horizontalEdges[r, c] = CreateRandomEdge(r == 0 || r == rows);
        for (int r = 0; r < rows; r++) for (int c = 0; c <= cols; c++) verticalEdges[r, c] = CreateRandomEdge(c == 0 || c == cols);
    }

    private int GetDiscreteRotationSteps(float eulerZ) { int steps = Mathf.RoundToInt(eulerZ / 90f); return ((steps % 4) + 4) % 4; }
    private Vector3 RotateVectorDiscrete(Vector3 v, int steps) { steps = ((steps % 4) + 4) % 4; if (steps == 1) return new Vector3(-v.y, v.x, v.z); if (steps == 2) return new Vector3(-v.x, -v.y, v.z); if (steps == 3) return new Vector3(v.y, -v.x, v.z); return v; }

    public void RealignGroup(Transform root, PuzzlePiece anchor = null)
    {
        if (root == null) return;
        PuzzlePiece[] pieces = root.GetComponentsInChildren<PuzzlePiece>(); if (pieces.Length == 0) return;
        PuzzlePiece targetAnchor = (anchor != null) ? anchor : pieces[0];
        Quaternion originalRot = root.rotation; Vector3 originalScale = root.localScale;
        root.rotation = Quaternion.identity; root.localScale = Vector3.one;
        Vector3 anchorLocal = targetAnchor.transform.localPosition;
        foreach (var p in pieces) {
            Vector3 gridDiff = (Vector3)p.correctPos - (Vector3)targetAnchor.correctPos;
            p.transform.localPosition = new Vector3(anchorLocal.x + gridDiff.x, anchorLocal.y + gridDiff.y, p.transform.localPosition.z);
            p.transform.localRotation = Quaternion.identity; 
        }
        root.rotation = originalRot; root.localScale = originalScale;
        foreach (var p in pieces) p.UpdateShadowPosition();
    }

    public void CheckForGroupSnap(PuzzlePiece pA)
    {
        Transform rootA = pA.transform.parent; float angle = rootA.eulerAngles.z;
        foreach (var p in rootA.GetComponentsInChildren<PuzzlePiece>()) {
            int[] dx = { -1, 1, 0, 0 }, dy = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++) {
                int nx = p.gridX + dx[i], ny = p.gridY + dy[i];
                if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                PuzzlePiece pO = pieceGrid[ny, nx]; if (pO == null) continue;
                Transform rootO = pO.transform.parent; if (rootO == rootA) continue;
                float angleO = rootO.eulerAngles.z; if (Mathf.Abs(Mathf.DeltaAngle(angle, angleO)) > 3f) continue;
                int steps = GetDiscreteRotationSteps(angleO);
                Vector3 wDiff = RotateVectorDiscrete((Vector3)(pO.correctPos - p.correctPos), steps);
                Vector2 actualDir = (Vector2)pO.transform.position - (Vector2)p.transform.position;
                if (actualDir.sqrMagnitude > 0.001f && Vector2.Dot(actualDir.normalized, ((Vector2)wDiff).normalized) < 0.7f) continue;
                Vector2 diff = (Vector2)pO.transform.position - (Vector2)p.transform.position - (Vector2)wDiff;
                float thresh = adjacentDistanceThreshold * Mathf.Clamp(650f / totalPieces, 0.9f, 1.3f);
                if (diff.sqrMagnitude < thresh * thresh) {
                    rootO.rotation = Quaternion.Euler(0, 0, Mathf.Round(angleO / 90f) * 90f);
                    rootA.rotation = rootO.rotation;
                    rootA.position += (pO.transform.position - RotateVectorDiscrete((Vector3)(pO.correctPos - p.correctPos), steps) - p.transform.position);
                    rootA.localScale = rootO.localScale = Vector3.one;
                    
                    while (rootA.childCount > 0) rootA.GetChild(0).SetParent(rootO);
                    Destroy(rootA.gameObject);
                    if (draggingPiece != null && draggingPiece.transform.parent == rootO) rootO.localScale = Vector3.one * 1.05f;
                    RealignGroup(rootO, pO);
                    
                    // 結合状態が変化したため、画面全体の SortingOrder を一括再計算して整理する
                    UpdateAllGroupsSortingOrder();
                    
                    int currentCount = rootO.GetComponentsInChildren<PuzzlePiece>().Length;
                    if (currentCount > maxConnectedPieces) maxConnectedPieces = currentCount;
                    CheckPuzzleComplete(); PlaySnapEffect(rootO); return;
                }
            }
        }
    }

    public void CheckPuzzleComplete() { if (maxConnectedPieces >= totalPieces && !isFinished) ShowClearEffect(); }

    private void ShowClearEffect() 
    { 
        isFinished = true; float elapsed = (Time.time - startTime) - totalPausedTime; TimeSpan ts = TimeSpan.FromSeconds(elapsed);

        #if UNITY_WEBGL && !UNITY_EDITOR
        OnPuzzleComplete(elapsed);
        #endif

        if (completionUIDoc != null) {
            completionUIDoc.gameObject.SetActive(true);
            var root = completionUIDoc.rootVisualElement;
            Label l = root.Q<Label>("TimeValue"); if (l != null) l.text = string.Format("{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            Button btn = root.Q<Button>("ReturnToSelectionButton"); if (btn != null) btn.clicked += ReturnToTitle;
        }
        if (hudUIDoc != null) hudUIDoc.gameObject.SetActive(false); // パズルクリア時にHUDを非表示に
        if (audioSource != null && clearSound != null) audioSource.PlayOneShot(clearSound); 
    }

    public void BringToFront(Transform root) {
        var sg = root.GetComponent<SortingGroup>();
        if (sg != null) { sg.sortingOrder = topSortingOrder; topSortingOrder += 10; }
        if (topSortingOrder > 30000) topSortingOrder = 3000;
    }

    public void TogglePause()
    {
        if (isFinished) return;
        isPaused = !isPaused;
        if (isPaused) {
            pauseStartTime = Time.time;
            if (pauseUIDoc != null) { 
                pauseUIDoc.sortingOrder = 9999; 
                pauseUIDoc.gameObject.SetActive(true); 
                
                // 表示された直後に安全にボタンイベントをバインド
                var root = pauseUIDoc.rootVisualElement;
                if (root != null)
                {
                    Button btn = root.Q<Button>("ReturnToSelectionButton");
                    if (btn != null)
                    {
                        btn.clicked -= OnReturnToSelectionClicked;
                        btn.clicked += OnReturnToSelectionClicked;
                    }
                }
            }
            if (hudUIDoc != null) hudUIDoc.gameObject.SetActive(false); // ポーズ中はHUDを非表示に
            foreach (var p in allPieces) if (p != null && p.transform.parent != null) p.transform.parent.gameObject.SetActive(false);
        } else {
            totalPausedTime += (Time.time - pauseStartTime);
            if (pauseUIDoc != null) pauseUIDoc.gameObject.SetActive(false);
            if (hudUIDoc != null) SetupHUD(); // ポーズ解除時にHUDを再表示＆イベント再バインド
            foreach (var p in allPieces) if (p != null && p.transform.parent != null) p.transform.parent.gameObject.SetActive(true);
        }
    }

    public void UpdateAllGroupsSortingOrder()
    {
        // シーン内の全ルートオブジェクトを検索するのではなく、PuzzleManager自身の子オブジェクトを直接走査して軽量化
        foreach (Transform r in transform)
        {
            if (r == null) continue;
            if (r.name.Contains("PieceCluster") || r.name.Contains("Piece_"))
            {
                PuzzlePiece[] pieces = r.GetComponentsInChildren<PuzzlePiece>();
                int N = pieces.Length;
                if (N == 0) continue;
                
                int targetBaseOrder = (2000 - N * 5) * 10;
                
                if (N == 1)
                {
                    // 単一ピースは SortingGroup を削除して描画バッチング（軽さ）を有効化
                    var sg = r.GetComponent<SortingGroup>();
                    if (sg != null) Destroy(sg);
                    
                    // 物理的Z座標を最前面（-0.2f）へ
                    foreach (var p in pieces)
                    {
                        p.transform.localPosition = new Vector3(p.transform.localPosition.x, p.transform.localPosition.y, -0.2f);
                        var renderer = p.GetComponent<Renderer>();
                        if (renderer != null) renderer.sortingOrder = targetBaseOrder + (p.id % 10);
                        
                        Transform shadow = p.transform.Find("Shadow");
                        if (shadow != null)
                        {
                            var sr = shadow.GetComponent<Renderer>();
                            if (sr != null) sr.sortingOrder = targetBaseOrder + (p.id % 10) + 1;
                        }
                    }
                }
                else
                {
                    // 2枚以上が結合した塊は SortingGroup を常時保持させてグループ一括描画（隙間チラつき100%防止）
                    var sg = r.GetComponent<SortingGroup>();
                    if (sg == null) sg = r.gameObject.AddComponent<SortingGroup>();
                    sg.sortingOrder = targetBaseOrder;
                    
                    // 物理的Z座標も結合数に応じて奥（0.0f付近）へ沈める
                    float targetZ = Mathf.Min(0.0f, -0.1f + N * 0.0005f);
                    foreach (var p in pieces)
                    {
                        // 塊内の各ピースは、グループ内で一貫したZ座標（Zバッファ競合防止）
                        p.transform.localPosition = new Vector3(p.transform.localPosition.x, p.transform.localPosition.y, targetZ - (p.id % 10) * 0.0001f);
                        
                        // SortingGroupが効いているため本体と影の順序のみ相対的に設定
                        var renderer = p.GetComponent<Renderer>();
                        if (renderer != null) renderer.sortingOrder = p.id % 10;
                        
                        Transform shadow = p.transform.Find("Shadow");
                        if (shadow != null)
                        {
                            var sr = shadow.GetComponent<Renderer>();
                            if (sr != null) sr.sortingOrder = (p.id % 10) + 1;
                        }
                    }
                }
            }
        }
    }

    private void ReturnToTitle() {
        isFinished = false;
        if (completionUIDoc != null) completionUIDoc.gameObject.SetActive(false);
        if (hudUIDoc != null) hudUIDoc.gameObject.SetActive(false); // タイトルに戻る際にHUDを非表示に
        
        #if UNITY_WEBGL && !UNITY_EDITOR
        if (isLoadedFromWeb) {
            SetGuideVisible(false);
            CloseCanvasWindow(); // Web通常起動時は、子ウィンドウ自体を閉じて親画面に戻る
            return;
        }
        #elif UNITY_EDITOR
        if (isLoadedFromWeb) {
            Debug.Log("[PuzzleManager] Web通常起動の終了要求：エディタ上なのでシミュレーションします。");
            SetGuideVisible(false);
            if (selectionManager != null) selectionManager.Open();
            return;
        }
        #endif

        if (selectionManager != null) selectionManager.Open();
        SetGuideVisible(false);
    }

    public void SetGuideVisibleFromWeb(string visibleStr)
    {
        bool visible = (visibleStr == "true" || visibleStr == "1");
        SetGuideVisible(visible);
    }

    private void SetGuideVisible(bool visible) { 
        if (backgroundGuideRenderer != null) {
            if (visible) {
                backgroundGuideRenderer.gameObject.SetActive(true);
                var sg = backgroundGuideRenderer.GetComponent<SortingGroup>();
                if (sg == null) sg = backgroundGuideRenderer.gameObject.AddComponent<SortingGroup>();
                sg.sortingOrder = 32700; 
                backgroundGuideRenderer.sortingOrder = 0;
                backgroundGuideRenderer.color = Color.white;
                backgroundGuideRenderer.transform.position = new Vector3(0, 0, -5f); 
            } else {
                backgroundGuideRenderer.gameObject.SetActive(false);
            }
        }
    }

    private void PlaySnapEffect(Transform t) { if (audioSource != null && snapSound != null) audioSource.PlayOneShot(snapSound); StartCoroutine(SnapAnimation(t)); }
    private IEnumerator SnapAnimation(Transform t) { if (t == null) yield break; Vector3 s = t.localScale; float d = 0.15f, e = 0f; while (e < d) { e += Time.deltaTime; t.localScale = Vector3.Lerp(s, s * 1.05f, e / d); yield return null; } e = 0f; while (e < d) { e += Time.deltaTime; t.localScale = Vector3.Lerp(s * 1.05f, s, e / d); yield return null; } t.localScale = s; }

    private Vector3 GetPeripheralPosition(float cW, float cH, float pad) {
        float minX = -cW + pad, maxX = cW - pad, minY = -cH + pad, maxY = cH - pad;
        float bLX = Mathf.Min(puzzleHalfWidth * 0.7f, Mathf.Max(0, maxX - 0.2f)), bLY = Mathf.Min(puzzleHalfHeight * 0.7f, Mathf.Max(0, maxY - 0.2f));
        int z = UnityEngine.Random.Range(0, 4); float x = 0, y = 0;
        switch (z) {
            case 0: x = UnityEngine.Random.Range(minX, -bLX); y = UnityEngine.Random.Range(minY, maxY); break;
            case 1: x = UnityEngine.Random.Range(bLX, maxX); y = UnityEngine.Random.Range(minY, maxY); break;
            case 2: x = UnityEngine.Random.Range(minX, maxX); y = UnityEngine.Random.Range(bLY, maxY); break;
            case 3: x = UnityEngine.Random.Range(minX, maxX); y = UnityEngine.Random.Range(minY, -bLY); break;
        }
        return new Vector3(Mathf.Clamp(x, minX, maxX), Mathf.Clamp(y, minY, maxY), 0);
    }

    private void SetupCamera() { 
        Camera cam = Camera.main; if (cam == null) return; 
        cam.clearFlags = CameraClearFlags.SolidColor; 
        cam.orthographic = true; 
        cam.orthographicSize = 6f; 
        cam.transform.position = new Vector3(0, 0, -10f); 
        cam.backgroundColor = GetDynamicBackgroundColor();
    }

    private Color GetDynamicBackgroundColor()
    {
        if (sourceSprite == null || sourceSprite.texture == null)
        {
            return new Color(0.55f, 0.55f, 0.55f); // デフォルト
        }

        try
        {
            Texture2D tex = sourceSprite.texture;
            // 1x1のRenderTextureを使ってGPU上で安全かつ高速に平均色を取得
            RenderTexture rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);

            RenderTexture activeRT = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D result = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            result.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            result.Apply();

            RenderTexture.active = activeRT;
            RenderTexture.ReleaseTemporary(rt);

            Color avgColor = result.GetPixel(0, 0);
            Destroy(result);

            // 輝度（Luminance）の算出
            float L = 0.299f * avgColor.r + 0.587f * avgColor.g + 0.114f * avgColor.b;

            // 画像が明るいほど背景を暗く（ドロップシャドウ視認性の限界 0.40f まで）、
            // 画像が暗いほど背景を明るく（目に優しいオフホワイト 0.82f まで）マッピング
            float bgLuminance = Mathf.Lerp(0.82f, 0.40f, L);

            return new Color(bgLuminance, bgLuminance, bgLuminance, 1.0f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PuzzleManager] Failed to calculate dynamic background: {e.Message}");
            return new Color(0.55f, 0.55f, 0.55f); // エラー時のフォールバック
        }
    }
    private EdgeData CreateRandomEdge(bool isBoundary)
    {
        EdgeData edge = new EdgeData();
        if (isBoundary) { edge.type = 0; return edge; }
        edge.type = UnityEngine.Random.value > 0.5f ? 1 : -1;
        
        // --- 参照画像に合わせた、より自然で滑らかな出っ張り ---
        // 頭の幅：画像を参考にコンパクトに。ピース数が多い時でも個体差が出るよう幅を持たせる
        edge.headWidth = UnityEngine.Random.Range(24f, 34f); 
        // 頭の高さ
        edge.headHeight = UnityEngine.Random.Range(22f, 31f); 
        edge.neckDepth = UnityEngine.Random.Range(6f, 10f); 
        edge.centerShift = UnityEngine.Random.Range(-5f, 5f); 
        
        // ★肩の盛り上がり（逆方向のカーブ）を追加
        // 画像のように、なだらかで広い波にするため深さを抑えめに調整
        edge.shoulderWaveL = UnityEngine.Random.Range(4f, 7f);
        edge.shoulderWaveR = UnityEngine.Random.Range(4f, 7f);
        
        // ネックの細さ
        edge.nW_ratio = UnityEngine.Random.Range(0.26f, 0.31f); 
        
        return edge;
    }
}
