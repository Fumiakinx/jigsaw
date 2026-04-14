// Project: Jigsaw - Ver 1.3
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Collections;
using System;
using UnityEngine.Rendering;

public class PuzzleManager : MonoBehaviour
{
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
    public UIDocument completionUIDoc;
    public UIDocument loadingUIDoc;
    public PuzzleSelectionManager selectionManager;
    public AudioClip clearSound;

    [Header("Piece Visuals")]
    public Color highlightColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
    [Range(0, 1)] public float highlightIntensity = 1.0f;
    [Range(0, 0.5f)] public float highlightWidth = 0.1f;
    public Color outlineColor = Color.black;

    private float startTime;
    private bool isFinished = false;

    public bool useBezierCurves = true;
    public float bevelWidth = 0.0f; // 0固定：検証用

    // BezierSegment 構造体はテンプレート方式への移行に伴い廃止

    // 固定の曲線定義を廃止し、EdgeData ごとに動的に組み立てる方式に変更

    private class EdgeData
    {
        public int type; // 1: 凸, -1: 凹, 0: 平ら
        public float offX, scaleX, scaleY;
        
        // 多様性を生む動的パラメータ
        public float headWidth;    // 頭の横幅
        public float headHeight;   // タブの高さ
        public float neckDepth;    // 首の深さ
        public float shoulderWaveL; // 左肩のうねり
        public float shoulderWaveR; // 右肩のうねり
        public float tilt;         // 頭の傾き
        public float centerShift;  // タブの位置ずれ
        public float scoopDepth;   // 肩のえぐれ具合
        public float cornerRigidity;// 角の曲がりにくさ
        
        public List<Vector2> cachedNormalizedPoints;
        public float nW_ratio; // 個別の首の太さ
        public float headBulbg; // 頭の膨らみ
    }

    private EdgeData[,] horizontalEdges;
    private EdgeData[,] verticalEdges;
    private Vector2[,] cornerPoints;
    private PuzzlePiece[,] pieceGrid;
    private PuzzlePiece draggingPiece = null;

    private List<Material> trackedMaterials = new List<Material>();
    private List<Mesh> trackedMeshes = new List<Mesh>();
    private Material sharedPieceMaterial;
    private int maxConnectedPieces = 1;

    void Start()
    {
        SetupCamera();
        gameObject.TryGetComponent(out audioSource);
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        if (completionUIDoc != null) completionUIDoc.gameObject.SetActive(false);
    }

    public void StartPuzzle(Sprite selectedSprite, int pieceCount = 200)
    {
        if (selectedSprite == null) return;
        sourceSprite = selectedSprite;
        targetPieces = pieceCount;

        // 以前のピースを破棄（リストにないものも含む全検索）
        var orphans = UnityEngine.Object.FindObjectsByType<PuzzlePiece>(FindObjectsInactive.Include);
        foreach (var p in orphans) 
        {
            if (p != null && p.transform.parent != null) 
                SafeDestroy(p.transform.parent.gameObject); // clusterを破棄
        }
        allPieces.Clear();

        float aspect = (float)sourceSprite.texture.width / sourceSprite.texture.height;
        rows = Mathf.RoundToInt(Mathf.Sqrt(targetPieces / aspect));
        cols = Mathf.RoundToInt(aspect * rows);
        totalPieces = cols * rows;

        cornerPoints = new Vector2[rows + 1, cols + 1];
        float ppu = sourceSprite.pixelsPerUnit;
        float pW = (float)sourceSprite.texture.width / cols / ppu;
        float pH = (float)sourceSprite.texture.height / rows / ppu;
        float hW = (float)sourceSprite.texture.width / ppu / 2f;
        float hH = (float)sourceSprite.texture.height / ppu / 2f;
        InitializeCornerPoints(pW, pH, hW, hH);
        InitializeEdgeData();

        if(backgroundGuideRenderer != null)
        {
            backgroundGuideRenderer.sprite = sourceSprite;
            backgroundGuideRenderer.sortingOrder = -10;
            backgroundGuideRenderer.gameObject.SetActive(true); // 生成中は表示
            backgroundGuideRenderer.color = Color.white; // 生成中ははっきり表示
            backgroundGuideRenderer.transform.localScale = Vector3.one;
            backgroundGuideRenderer.transform.position = Vector3.zero;
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
        
        isFinished = false;
        startTime = Time.time;
        maxConnectedPieces = 1;

        SetupCamera();
        highlightColor = CalculateAndSetDynamicColors(sourceSprite);

        ClearTrackedAssets();
        StopAllCoroutines();
        StartCoroutine(GeneratePuzzlePiecesCoroutine());
    }

    private void ClearTrackedAssets()
    {
        foreach (var m in trackedMaterials) if (m != null) SafeDestroy(m);
        foreach (var m in trackedMeshes) if (m != null) SafeDestroy(m);
        trackedMaterials.Clear();
        trackedMeshes.Clear();
    }

    void Update()
    {
        Vector2 mousePos;
        bool leftPressed, leftDown, leftUp, rightDown, spaceDown, spaceUp;

        var mouse = UnityEngine.InputSystem.Mouse.current;
        var kb = UnityEngine.InputSystem.Keyboard.current;

        if (mouse != null && kb != null)
        {
            mousePos = mouse.position.ReadValue();
            leftPressed = mouse.leftButton.isPressed;
            leftDown = mouse.leftButton.wasPressedThisFrame;
            leftUp = mouse.leftButton.wasReleasedThisFrame;
            rightDown = mouse.rightButton.wasPressedThisFrame;
            spaceDown = kb.spaceKey.wasPressedThisFrame;
            spaceUp = kb.spaceKey.wasReleasedThisFrame;
        }
        else
        {
            // レガシーInputへのフォールバック
            mousePos = UnityEngine.Input.mousePosition;
            leftPressed = UnityEngine.Input.GetMouseButton(0);
            leftDown = UnityEngine.Input.GetMouseButtonDown(0);
            leftUp = UnityEngine.Input.GetMouseButtonUp(0);
            rightDown = UnityEngine.Input.GetMouseButtonDown(1);
            spaceDown = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space);
            spaceUp = UnityEngine.Input.GetKeyUp(UnityEngine.KeyCode.Space);
        }

        if (isFinished) return;

        if (spaceDown) SetGuideVisible(true);
        else if (spaceUp) SetGuideVisible(false);

        if (rightDown)
        {
            PuzzlePiece target = (draggingPiece != null) ? draggingPiece : GetTopmostPiece(mousePos);
            if (target != null)
            {
                target.RotateGroup(Camera.main.ScreenToWorldPoint(mousePos));
            }
        }

        if (leftDown)
        {
            PuzzlePiece piece = GetTopmostPiece(mousePos);
            if (piece != null)
            {
                draggingPiece = piece;
                draggingPiece.BeginDrag(Camera.main.ScreenToWorldPoint(mousePos));
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
        // ScreenToWorldPoint に渡す Vector3 の z は、カメラからの距離（または 0）
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, 10f));
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);
        PuzzlePiece topPiece = null;
        int maxOrder = int.MinValue;
        if (hits.Length > 0) Debug.Log($"Raycast Hits: {hits.Length}");
        foreach (var hit in hits)
        {
            PuzzlePiece p = hit.GetComponent<PuzzlePiece>();
            if (p != null)
            {
                int order = p.GetComponent<Renderer>().sortingOrder;
                if (order > maxOrder) { maxOrder = order; topPiece = p; }
            }
        }
        return topPiece;
    }

    private IEnumerator GeneratePuzzlePiecesCoroutine()
    {
        float genStartTime = Time.realtimeSinceStartup;
        
        // 既存のパズルをクリア
        var oldClusters = GameObject.FindObjectsByType<SortingGroup>(FindObjectsInactive.Exclude);
        foreach(var oc in oldClusters) {
            if (oc.name.Contains("PieceCluster")) DestroyImmediate(oc.gameObject);
        }
        var oldPieces = GameObject.FindObjectsByType<PuzzlePiece>(FindObjectsInactive.Exclude);
        foreach(var op in oldPieces) DestroyImmediate(op.gameObject);

        Texture2D tex = sourceSprite.texture;
        float ppu = sourceSprite.pixelsPerUnit;
        int tw = tex.width;
        int th = tex.height;
        float pieceWorldWidth = (float)tw / cols / ppu;
        float pieceWorldHeight = (float)th / rows / ppu;
        float halfWidth = (float)tw / ppu / 2f;
        float halfHeight = (float)th / ppu / 2f;
        puzzleHalfWidth = halfWidth;
        puzzleHalfHeight = halfHeight;
        adjacentDistanceThreshold = (pieceWorldWidth + pieceWorldHeight) * 0.08f;

        pieceGrid = new PuzzlePiece[rows, cols];

        Material puzzleMaterial = new Material(Shader.Find("Custom/Jigsaw2D"));
        if (tex != null) {
            puzzleMaterial.mainTexture = tex;
        }
        puzzleMaterial.color = Color.white;
        trackedMaterials.Add(puzzleMaterial);

        Material shadowMaterial = new Material(Shader.Find("Sprites/Default"));
        shadowMaterial.color = new Color(0, 0, 0, 0.7f); // 本番用の黒い影
        trackedMaterials.Add(shadowMaterial);

        Material highlightMaterial = new Material(Shader.Find("Sprites/Default"));
        highlightMaterial.color = highlightColor; // 指定された色のハイライト
        trackedMaterials.Add(highlightMaterial);

        float cH = Camera.main.orthographicSize;
        float cW = cH * Camera.main.aspect;

        float pad = 0.5f;
        float zGap = 0.0001f;

        // スプライトがアトラスの一部である可能性を考慮してRectを使用
        Rect r = sourceSprite.rect;
        Texture2D fullTex = sourceSprite.texture;
        float uStart = r.x / fullTex.width;
        float vStart = r.y / fullTex.height;
        float uWidth = r.width / fullTex.width;
        float vHeight = r.height / fullTex.height;

        float uW = uWidth / cols;
        float vH = vHeight / rows;

        int total = rows * cols;
        int count = 0;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                // 4つのコーナー座標を取得
                Vector2 cBL = cornerPoints[y, x];
                Vector2 cBR = cornerPoints[y, x + 1];
                Vector2 cTR = cornerPoints[y + 1, x + 1];
                Vector2 cTL = cornerPoints[y + 1, x];

                GenerateSinglePiece(x, y, cBL, cBR, cTR, cTL, tex.width, tex.height, ppu, puzzleMaterial, shadowMaterial, highlightMaterial, cW, cH, pad, zGap, uStart, vStart, uWidth, vHeight);
                
                count++;
                if (loadingUIDoc != null)
                {
                    var fill = loadingUIDoc.rootVisualElement.Q<VisualElement>("BarFill");
                    if (fill != null) fill.style.width = Length.Percent((float)count / total * 100f);
                }

                // 数個おきにフレームを譲る（Piece数が多い場合のフリーズ防止）
                if (count % 5 == 0) yield return null;
            }
        }

        // 生成完了後のUI更新
        if (loadingUIDoc != null) loadingUIDoc.gameObject.SetActive(false);
        if (backgroundGuideRenderer != null) 
        {
            backgroundGuideRenderer.color = new Color(1, 1, 1, 0.2f); // プレイ中は薄く設定
            SetGuideVisible(false); // 初期状態は非表示
        }

        Debug.Log($"[LOG] Generation Complete in {Time.realtimeSinceStartup - genStartTime:F3}s");
        

        // 幾何学的検証ログ：Piece(0,0)の右辺 と Piece(1,0)の左辺 を比較
        VerifyBoundaryGeometry(0, 0, 1, 0);
        yield break;
    }

    private void VerifyBoundaryGeometry(int x1, int y1, int x2, int y2)
    {
        if (pieceGrid[y1, x1] == null || pieceGrid[y2, x2] == null) return;
        
        Mesh m1 = pieceGrid[y1, x1].GetComponent<MeshFilter>().sharedMesh;
        Mesh m2 = pieceGrid[y2, x2].GetComponent<MeshFilter>().sharedMesh;
        Vector3[] v1 = m1.vertices;
        Vector3[] v2 = m2.vertices;
        
        // Piece(x1,y1)の右辺は GenerateEdgePoints(pw2...) で生成。
        // Piece(x2,y2)の左辺は GenerateEdgePoints(-pw2...) で生成。
        // 両者のワールド座標を比較。
        Debug.Log($"[DEBUG] Verify {x1},{y1} vs {x2},{y2} started.");
        
        // 全ての頂点を比較して、隣接ピース間に「一致する頂点」がどれだけあるかを確認
        int matchCount = 0;
        float minDistance = float.MaxValue;

        foreach (Vector3 v1_local in v1)
        {
            // scattering（散布）後の transform.position ではなく、理論上の correctPos を使用して検証
            Vector3 v1_ideal = (Vector3)pieceGrid[y1, x1].correctPos + v1_local;
            foreach (Vector3 v2_local in v2)
            {
                Vector3 v2_ideal = (Vector3)pieceGrid[y2, x2].correctPos + v2_local;
                float d = Vector3.Distance(v1_ideal, v2_ideal);
                if (d < 0.0001f) matchCount++;
                if (d < minDistance) minDistance = d;
            }
        }

        Debug.Log($"[GEOMETRY] Total Vertices: P1:{v1.Length}, P2:{v2.Length}");
        Debug.Log($"[GEOMETRY] Exact Matches Found: {matchCount}");
        Debug.Log($"[GEOMETRY] Minimum Distance between any two vertices: {minDistance:F8}");
        
        if (matchCount < 10) Debug.LogError("[CRITICAL] Geometry Mismatch! Boundary vertices do not overlap.");
        else Debug.Log("[SUCCESS] Geometry Matches Perfectly at Pixel Level (shared boundary vertices are identical).");
    }
    private void GenerateSinglePiece(int x, int y, Vector2 cBL, Vector2 cBR, Vector2 cTR, Vector2 cTL, int texWidth, int texHeight, float ppu, Material puzzleMaterial, Material shadowMaterial, Material highlightMaterial, float cW, float cH, float pad, float zGap, float uStart, float vStart, float uTotalW, float vTotalH)
    {
        Debug.Log($"[GEN] Starting Piece ({x}, {y}) with Jittered Corners");
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

            // 【完成】非破壊的・絶対順序保証型の頂点収集ロジック
            List<Vector2> verts = new List<Vector2>();
            float epsSqr = 0.0001f * 0.0001f;

            // 元のリストを破壊せずに、順方向または逆方向で頂点を追加する
            void CollectPoints(List<Vector2> source, bool reverse) {
                if (source == null || source.Count == 0) return;
                if (reverse) {
                    for (int i = source.Count - 1; i >= 0; i--) {
                        Vector2 p = source[i];
                        if (verts.Count > 0 && (verts[verts.Count - 1] - p).sqrMagnitude < epsSqr) continue;
                        verts.Add(p);
                    }
                } else {
                    for (int i = 0; i < source.Count; i++) {
                        Vector2 p = source[i];
                        if (verts.Count > 0 && (verts[verts.Count - 1] - p).sqrMagnitude < epsSqr) continue;
                        verts.Add(p);
                    }
                }
            }

            // CCW（反時計回り）で一筆書きを構成: Bottom -> Right -> Top(rev) -> Left(rev)
            CollectPoints(edgeB, false);
            CollectPoints(edgeR, false);
            CollectPoints(edgeT, true);
            CollectPoints(edgeL, true);

            // ループを閉じる (最後の点が最初の点と重なる場合は除去)
            if (verts.Count > 1 && (verts[verts.Count - 1] - verts[0]).sqrMagnitude < epsSqr) {
                verts.RemoveAt(verts.Count - 1);
            }

            // 頂点を中心（pieceCenter）からの相対座標に変換
            for (int i = 0; i < verts.Count; i++) verts[i] -= pieceCenter;

            GameObject cluster = new GameObject($"PieceCluster_{x}_{y}");
            var sg = cluster.AddComponent<SortingGroup>();
            GameObject pieceObject = new GameObject($"Piece_{x}_{y}");
            pieceObject.transform.SetParent(cluster.transform);
            
            // UV計算用のパラメータ：パズルの左下を(0,0)、右上を(1,1)とした正規化座標の開始点と幅
            // ここでの uStart, vStart はアトラス内での位置、uTotalW, vTotalH は使用する領域の幅
            float puzzleWorldW = (float)texWidth / ppu;
            float puzzleWorldH = (float)texHeight / ppu;

            Mesh pieceMesh = CreateIdealPieceMesh(pieceCenter, puzzleWorldW, puzzleWorldH, verts, uStart, vStart, uTotalW, vTotalH);
            pieceObject.AddComponent<MeshFilter>().mesh = pieceMesh;
            var mr = pieceObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = puzzleMaterial;

            mr.material.color = Color.white;

            try {
                var pc2d = pieceObject.AddComponent<PolygonCollider2D>();
                Vector2[] points = new Vector2[verts.Count];
                for(int i=0; i<verts.Count; i++) points[i] = new Vector2(verts[i].x, verts[i].y);
                pc2d.points = points;
            } catch { }
            
            var pi = pieceObject.AddComponent<PuzzlePiece>();
            pi.id = y * cols + x;
            pi.gridX = x;
            pi.gridY = y;
            pi.manager = this; 
            pieceGrid[y, x] = pi;
            pi.correctPos = pieceCenter;
            pi.targetPosition = pi.correctPos; 
            pi.snapThreshold = adjacentDistanceThreshold;

            int pieceBaseOrder = 10000 + (y * cols + x) * 2;
            pi.baseOrder = pieceBaseOrder; 
            sg.sortingOrder = pieceBaseOrder; 
            sg.sortingOrder = pieceBaseOrder; 
            mr.sortingOrder = 0; 
            
            // 影 (Shadow) の作成
            GameObject shadowObj = new GameObject("Shadow");
            shadowObj.transform.SetParent(pieceObject.transform, false);
            shadowObj.AddComponent<MeshFilter>().mesh = pieceMesh;
            var smr = shadowObj.AddComponent<MeshRenderer>();
            smr.sharedMaterial = shadowMaterial;
            smr.sortingOrder = -1; // 本体(0)の背面

            pi.UpdateShadowPosition();
            
            // 初期配置：画面外周にバラバラの角度（90度刻み）で配置
            cluster.transform.position = GetPeripheralPosition(cW, cH, pad);
            cluster.transform.rotation = Quaternion.Euler(0, 0, 90f * UnityEngine.Random.Range(0, 4));
            
            allPieces.Add(pi);
        } catch (System.Exception e) {
            Debug.LogError($"[CRITICAL] Piece({x},{y}) Failed: {e}");
        }
    }

    private List<Vector2> GenerateEdgePoints(Vector2 start, Vector2 end, EdgeData edge)
    {
        List<Vector2> points = new List<Vector2>();
        if (edge == null || edge.type == 0)
        {
            points.Add(start);
            points.Add(end);
            return points;
        }

        Vector2 dir = (end - start).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x);
        float scale = (end - start).magnitude;
        
        edge.cachedNormalizedPoints = new List<Vector2>();
        
        float hW = edge.headWidth; 
        float hH = edge.headHeight;
        float nD = edge.neckDepth;

        // 【改善】辺の長さに応じてコブを適切な比率（25-32%）に調整
        // 以前より制限を緩和し、多ピース時でも接続部が細くなりすぎないよう調整
        float safetyFactor = totalPieces > 400 ? 0.30f : 0.32f;
        // 【重要】ボディの肉厚を徹底的に確保するため、奥行き方向の制限を再強化 (0.32/0.38 -> 0.28/0.30)
        float heightFactor = totalPieces > 400 ? 0.28f : 0.30f;
        
        float maxAllowedHW = scale * safetyFactor; 
        if (hW / 100f * scale > maxAllowedHW) {
            float ratio = maxAllowedHW / (hW / 100f * scale);
            hW *= ratio;
            hH *= ratio;
            nD *= ratio;
        }
        
        // 貫通深度（高さ+首の深さ）も辺の長さに比例して制限
        float totalDepth = (hH + nD) / 100f * scale;
        if (totalDepth > scale * heightFactor) {
            float ratio = (scale * heightFactor) / totalDepth;
            hH *= ratio;
            nD *= ratio;
        }

        // 【安全性向上】コーナー付近に凹が寄りすぎないよう、マージンを極限まで拡大
        // Entrance地点が 28% 以上内側に来るように制限（旧 23%）
        float safetyMargin = 28f + (hW * 0.7f);
        float mid = Mathf.Clamp(50f + edge.centerShift, safetyMargin, 100f - safetyMargin);
        
        float nW = hW * edge.nW_ratio;

        // アンカーポイントの定義 (0-100座標系) - 被り防止のために、よりコンパクトに配置
        Vector2 pA = new Vector2(0, 0);
        Vector2 pNeckL = new Vector2(mid - nW, -nD);    
        Vector2 pTop = new Vector2(mid, -nD - hH);      
        Vector2 pNeckR = new Vector2(mid + nW, -nD);    
        Vector2 pB = new Vector2(100, 0);               

        // 1セグメントあたりの解像度：多ピース時は計算量を減らして安定性を上げる
        int res = totalPieces > 400 ? 24 : totalPieces > 200 ? 32 : 40; 
        float k = 0.552f; 

        // 1. Entrance: A -> NeckL
        float c1x_entry = Mathf.Clamp(mid - hW * 0.7f, 5f, pNeckL.x - 3f);
        Vector2 c1_1 = new Vector2(c1x_entry, 0); 
        // 進入角度の深さを緩和（nD * 0.5f -> 0.3f）して、急激なくびれを抑制
        float nTang = Mathf.Max(-nD * 0.3f, -hH * 0.35f); 
        Vector2 c2_1 = new Vector2(pNeckL.x, nTang); 
        for(int i=0; i<res/2; i++) edge.cachedNormalizedPoints.Add(GetBezierPoint(pA, c1_1, c2_1, pNeckL, i/(float)(res/2)));

        // 2. Head Side Left (頸部から膨らみの最大点へ): NeckL -> BulbL
        float bY = -nD - hH * 0.35f; // 最大膨らみ位置を少し低めに設定してΩ感を出す
        Vector2 pBulbL = new Vector2(mid - hW * 0.5f, bY);
        Vector2 c1_2 = new Vector2(pNeckL.x, pNeckL.y - (pNeckL.y - bY) * k);
        Vector2 c2_2 = new Vector2(pBulbL.x, pBulbL.y + (pNeckL.y - bY) * k);
        for(int i=0; i<res/2; i++) edge.cachedNormalizedPoints.Add(GetBezierPoint(pNeckL, c1_2, c2_2, pBulbL, i/(float)(res/2)));

        // 3. Head Top Left (膨らみ最大点から頂点へ): BulbL -> Top
        Vector2 c1_3 = new Vector2(pBulbL.x, pBulbL.y - (bY - pTop.y) * k);
        Vector2 c2_3 = new Vector2(pTop.x - (pTop.x - pBulbL.x) * k, pTop.y);
        for(int i=0; i<res/2; i++) edge.cachedNormalizedPoints.Add(GetBezierPoint(pBulbL, c1_3, c2_3, pTop, i/(float)(res/2)));

        // 4. Head Top Right (頂点から反対の膨らみ最大点へ): Top -> BulbR
        Vector2 pBulbR = new Vector2(mid + hW * 0.5f, bY);
        Vector2 c1_4 = new Vector2(pTop.x + (pBulbR.x - pTop.x) * k, pTop.y);
        Vector2 c2_4 = new Vector2(pBulbR.x, pBulbR.y - (bY - pTop.y) * k);
        for(int i=0; i<res/2; i++) edge.cachedNormalizedPoints.Add(GetBezierPoint(pTop, c1_4, c2_4, pBulbR, i/(float)(res/2)));

        // 5. Head Side Right (膨らみ最大点から反対のネックへ): BulbR -> NeckR
        Vector2 c1_5 = new Vector2(pBulbR.x, pBulbR.y + (pNeckR.y - bY) * k);
        Vector2 c2_5 = new Vector2(pNeckR.x, pNeckR.y - (pNeckR.y - bY) * k);
        for(int i=0; i<res/2; i++) edge.cachedNormalizedPoints.Add(GetBezierPoint(pBulbR, c1_5, c2_5, pNeckR, i/(float)(res/2)));

        // 6. Exit: NeckR -> B
        Vector2 c1_6 = new Vector2(pNeckR.x, nTang);
        float c2x_exit = Mathf.Clamp(mid + hW * 0.7f, pNeckR.x + 3f, 95f);
        Vector2 c2_6 = new Vector2(c2x_exit, 0);
        for(int i=0; i<=res/2; i++) edge.cachedNormalizedPoints.Add(GetBezierPoint(pNeckR, c1_6, c2_6, pB, i/(float)(res/2)));

        foreach (var norm in edge.cachedNormalizedPoints)
        {
            Vector2 worldPos = start + dir * (norm.x / 100f) * scale + normal * (norm.y / 100f) * scale * edge.type;
            points.Add(worldPos);
        }

        return points;
    }

    // 3次ベジェ曲線補間
    private Vector2 GetBezierPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector2 p = uuu * p0; // (1-t)^3 * p0
        p += 3 * uu * t * p1; // 3(1-t)^2 * t * p1
        p += 3 * u * tt * p2; // 3(1-t) * t^2 * p2
        p += ttt * p3;         // t^3 * p3
        return p;
    }

    private float hV_right(float hW, float tilt) { return hW/2f + tilt; } // 簡易的な補助


    private Vector3 GetCubicBezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    private Mesh CreateIdealPieceMesh(Vector2 center, float puzzleW, float puzzleH, List<Vector2> verts2D, float uStart, float vStart, float uTotalW, float vTotalH)
    {
        Mesh m = new Mesh();
        
        // 3D変換
        List<Vector3> verts3D = new List<Vector3>();
        foreach(var v in verts2D) verts3D.Add(new Vector3(v.x, v.y, 0));

        // 三角形分割と頂点クリーンアップを同期
        Vector3[] finalVerts;
        int[] triangles;
        TriangulateAndClean(verts3D, out finalVerts, out triangles);

        // UVマッピング（クリーンアップ後の頂点に対して行う）
        Vector2[] allUVs = new Vector2[finalVerts.Length];
        float halfPW = puzzleW / 2f;
        float halfPH = puzzleH / 2f;

        for (int i = 0; i < finalVerts.Length; i++)
        {
            Vector2 globalPos = (Vector2)finalVerts[i] + center;
            float normX = (globalPos.x + halfPW) / puzzleW;
            float normY = (globalPos.y + halfPH) / puzzleH;
            allUVs[i] = new Vector2(uStart + normX * uTotalW, vStart + normY * vTotalH);
        }

        m.vertices = finalVerts;
        m.uv = allUVs;
        m.triangles = triangles;
        
        m.RecalculateNormals();
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }

    private void TriangulateAndClean(List<Vector3> points, out Vector3[] finalVerts, out int[] triangles)
    {
        Debug.Log($"<color=cyan>[PuzzleMesh] Triangulating {points.Count} points via Ear Clipping.</color>");
        
        // 1. 重複・近接頂点の除去 (判定を少し甘くして安定させる)
        List<Vector3> cleanPoints = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            if (cleanPoints.Count > 0 && (p - cleanPoints[cleanPoints.Count - 1]).sqrMagnitude < 0.0000000001f) continue;
            cleanPoints.Add(p);
        }
        if (cleanPoints.Count > 1 && (cleanPoints[0] - cleanPoints[cleanPoints.Count - 1]).sqrMagnitude < 0.0000000001f) {
            cleanPoints.RemoveAt(cleanPoints.Count - 1);
        }

        if (cleanPoints.Count < 3) {
            finalVerts = points.ToArray(); triangles = new int[0]; return;
        }

        finalVerts = cleanPoints.ToArray();
        int n = cleanPoints.Count;
        List<int> indices = new List<int>();
        List<int> V = new List<int>();
        for (int i = 0; i < n; i++) V.Add(i);

        // 外周の巻順を確認
        float area = 0;
        for (int i = 0; i < n; i++) {
            Vector2 p1 = (Vector2)cleanPoints[i];
            Vector2 p2 = (Vector2)cleanPoints[(i + 1) % n];
            area += (p1.x * p2.y - p2.x * p1.y);
        }
        
        // CCW（左回り）に統一（もしCWなら反転させる）
        if (area < 0) {
            V.Reverse();
            // area = -area; // デバッグ用
        }

        int count = n;
        int timeout = 5000;
        int stagnation = 0;

        while (V.Count > 3 && timeout > 0)
        {
            timeout--;
            bool earFound = false;
            
            // stagnation (行き詰まり) 対策: 判定をさらに緩和する
            float epsMultiplier = (stagnation > 50) ? 10.0f : 1.0f;

            for (int i = 0; i < V.Count; i++)
            {
                int prev = V[(i + V.Count - 1) % V.Count];
                int curr = V[i];
                int next = V[(i + 1) % V.Count];

                if (IsEar(prev, curr, next, V, cleanPoints, epsMultiplier))
                {
                    indices.Add(prev);
                    indices.Add(curr);
                    indices.Add(next);
                    V.RemoveAt(i);
                    earFound = true;
                    stagnation = 0;
                    break;
                }
            }

            if (!earFound) {
                stagnation++;
                if (stagnation > 50) {
                    // リカバリ: 一定回数耳が見つからない場合、最もマシな（面積が正の）三角形を削る
                    for (int i = 0; i < V.Count; i++) {
                        int prev = V[(i + V.Count - 1) % V.Count];
                        int curr = V[i];
                        int next = V[(i + 1) % V.Count];
                        Vector2 a = (Vector2)cleanPoints[prev], b = (Vector2)cleanPoints[curr], c = (Vector2)cleanPoints[next];
                        if (((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) > 0) {
                            indices.Add(prev); indices.Add(curr); indices.Add(next);
                            V.RemoveAt(i); earFound = true; break;
                        }
                    }
                }
                if (stagnation > 200) break; // 完全に行き詰まったら終了
            }
        }

        if (V.Count == 3) {
            indices.Add(V[0]);
            indices.Add(V[1]);
            indices.Add(V[2]);
        }

        triangles = indices.ToArray();
    }

    private bool IsEar(int pIdx, int cIdx, int nIdx, List<int> V, List<Vector3> points, float epsMult = 1.0f)
    {
        Vector2 a = (Vector2)points[pIdx], b = (Vector2)points[cIdx], c = (Vector2)points[nIdx];
        
        // 1. 向きのチェック (常にCCWであるべき)
        float cp = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (cp <= 1e-10f) return false; // 凹頂点または退化した三角形（直線）

        // 2. 三角形内に他の点が含まれていないかチェック
        for (int i = 0; i < V.Count; i++) {
            int idx = V[i];
            if (idx == pIdx || idx == cIdx || idx == nIdx) continue;
            if (PointInTriangle((Vector2)points[idx], a, b, c, epsMult)) return false;
        }
        return true;
    }

    private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float epsMult = 1.0f)
    {
        float d1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        float d2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
        float d3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
        
        // epsMult を使用して判定をわずかに緩めることで、境界上の点を無視しやすくする
        const float epsBase = -1e-6f;
        float eps = epsBase * epsMult;
        return (d1 >= eps && d2 >= eps && d3 >= eps);
    }

    private void InitializeCornerPoints(float pw, float ph, float halfW, float halfH)
    {
        for (int y = 0; y <= rows; y++)
        {
            for (int x = 0; x <= cols; x++)
            {
                // 正方形グリッド上の基本座標
                Vector2 pos = new Vector2(x * pw - halfW, y * ph - halfH);
                
                // 【適応型ジッター】ピース数が多い場合に揺らぎを抑制して形状破綻を防ぐ
                // 100ピースを基準(0.15f)とし、ピース数の増加に従って減衰させる
                float jitterScale = Mathf.Clamp(Mathf.Sqrt(100f / totalPieces), 0.3f, 1.0f);
                float maxJitter = 0.15f * jitterScale;

                if (x > 0 && x < cols && y > 0 && y < rows)
                {
                    pos.x += UnityEngine.Random.Range(-maxJitter, maxJitter) * pw;
                    pos.y += UnityEngine.Random.Range(-maxJitter, maxJitter) * ph;
                }
                // 外周の角についても、辺に沿った方向にのみ揺らすことを許可（オプション）
                else if ((x == 0 || x == cols) && (y > 0 && y < rows))
                {
                    // 左右の辺：垂直方向にのみ揺らす
                    float edgeJitter = 0.10f * jitterScale;
                    pos.y += UnityEngine.Random.Range(-edgeJitter, edgeJitter) * ph;
                }
                else if ((y == 0 || y == rows) && (x > 0 && x < cols))
                {
                    // 上下の辺：水平方向にのみ揺らす
                    float edgeJitter = 0.10f * jitterScale;
                    pos.x += UnityEngine.Random.Range(-edgeJitter, edgeJitter) * pw;
                }

                cornerPoints[y, x] = pos;
            }
        }
    }

    private void InitializeEdgeData()
    {
        horizontalEdges = new EdgeData[rows + 1, cols];
        verticalEdges = new EdgeData[rows, cols + 1];

        for (int r = 0; r <= rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                horizontalEdges[r, c] = CreateRandomEdge(r == 0 || r == rows);
            }
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c <= cols; c++)
            {
                verticalEdges[r, c] = CreateRandomEdge(c == 0 || c == cols);
            }
        }
    }

    private EdgeData CreateRandomEdge(bool isBoundary)
    {
        EdgeData edge = new EdgeData();
        if (isBoundary)
        {
            edge.type = 0;
            return edge;
        }

        edge.type = UnityEngine.Random.value > 0.5f ? 1 : -1;
        edge.offX = 0f; 
        edge.scaleX = 1f; 
        edge.scaleY = 1.0f;
        
        // 以前より全体的にボリュームを持たせ、接続部（ネック）を太く頑丈に調整
        // ただし、ボディへの圧迫を避けるため headWidth は 30-33 に微調整（旧 32-35）
        edge.headWidth = UnityEngine.Random.Range(30f, 33f); 
        edge.headHeight = UnityEngine.Random.Range(22f, 26f); 
        edge.neckDepth = UnityEngine.Random.Range(8f, 11f); 
                edge.shoulderWaveL = UnityEngine.Random.Range(-2f, 2f); 
        edge.shoulderWaveR = UnityEngine.Random.Range(-2f, 2f);
        edge.centerShift = UnityEngine.Random.Range(-3f, 3f); 
        
        // nW_ratio: 0.41-0.46 = 太く頑丈な首回り (旧 0.32-0.42)
        edge.nW_ratio = UnityEngine.Random.Range(0.41f, 0.46f); 
        edge.headBulbg = 1.0f;
        return edge;
    }
    /// グループ内の全ピースを、基準ピースに対する理想的な相対座標に強制的に再整列させます。
    /// これにより、回転や移動による微小な浮動小数点誤差（隙間の原因）を完全に排除します。
    /// </summary>
    public void RealignGroup(Transform root, PuzzlePiece anchor = null)
    {
        if (root == null) return;
        PuzzlePiece[] pieces = root.GetComponentsInChildren<PuzzlePiece>();
        if (pieces.Length == 0) return;

        // 基準（アンカー）の選定：引数がない場合は最初のピースを使用
        PuzzlePiece targetAnchor = anchor;
        if (targetAnchor == null) targetAnchor = pieces[0];

        Vector3 anchorLocal = targetAnchor.transform.localPosition;
        Vector2 anchorTarget = targetAnchor.targetPosition;

        foreach (var p in pieces)
        {
            // ターゲット座標（ボード上の理想的な中心座標）の差分を、ローカル座標の差分として厳密に適用
            // Z座標は重なり防止のためにピースごとの baseOrder に基づく値を維持
            float targetZ = -0.0001f * p.baseOrder;
            p.transform.localPosition = new Vector3(
                anchorLocal.x + (p.targetPosition.x - anchorTarget.x),
                anchorLocal.y + (p.targetPosition.y - anchorTarget.y),
                targetZ
            );
            p.UpdateShadowPosition();
        }
    }
    public void CheckForGroupSnap(PuzzlePiece activePiece)
    {
        Transform activeRoot = activePiece.transform.root;
        float angle = activeRoot.eulerAngles.z;
        PuzzlePiece[] activeGroup = activeRoot.GetComponentsInChildren<PuzzlePiece>();
        
        foreach (var pA in activeGroup) {
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++) {
                int nx = pA.gridX + dx[i];
                int ny = pA.gridY + dy[i];
                if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                
                PuzzlePiece pO = pieceGrid[ny, nx];
                if (pO == null) continue;
                    
                Transform oR = pO.transform.root;
                if (oR == activeRoot) continue;
                // 角度判定を少し緩和（3度以内なら吸着）
                if (Mathf.Abs(Mathf.DeltaAngle(angle, oR.eulerAngles.z)) > 3f) continue;
                
                // 吸着判定
                Vector3 wTO = activeRoot.TransformDirection(pO.targetPosition - pA.targetPosition);
                Vector2 diff = (Vector2)pO.transform.position - (Vector2)pA.transform.position - (Vector2)wTO;
                
                if (diff.sqrMagnitude < pA.snapThreshold * pA.snapThreshold) {
                    // 1. 角度をターゲットグループに完全に一致させる
                    activeRoot.rotation = oR.rotation;
                    
                    // 2. 角度同期後の状態で位置を再計算して正確に吸着
                    Vector3 adjustedWTO = activeRoot.TransformDirection(pO.targetPosition - pA.targetPosition);
                    activeRoot.position += (pO.transform.position - adjustedWTO) - pA.transform.position;

                    // 3. 子要素（ピース）をターゲットのルートへ移動
                    while (activeRoot.childCount > 0) 
                    {
                        activeRoot.GetChild(0).SetParent(oR);
                    }
                    Destroy(activeRoot.gameObject);

                    // 4. 数学的に正しいグリッド位置へ全てのピースを強制再配置（微小な隙間の排除）
                    // 最初に吸着したターゲットピース pO を基準にして、結合後のグループ全体を再整列
                    RealignGroup(oR, pO);
                    
                    // 結合後のサイズをチェック
                    int currentCount = oR.GetComponentsInChildren<PuzzlePiece>().Length;
                    if (currentCount > maxConnectedPieces) maxConnectedPieces = currentCount;

                    CheckPuzzleComplete(); 
                    PlaySnapEffect(oR); 
                    return;
                }
            }
        }
    }


    public void CheckPuzzleComplete() 
    { 
        if (maxConnectedPieces >= totalPieces) 
        {
            if (!isFinished) ShowClearEffect(); 
        }
    }

    private void ShowClearEffect() 
    { 
        isFinished = true;
        float elapsed = Time.time - startTime;
        TimeSpan ts = TimeSpan.FromSeconds(elapsed);
        if (completionUIDoc != null) 
        {
            completionUIDoc.gameObject.SetActive(true);
            var root = completionUIDoc.rootVisualElement;
            Label l = root.Q<Label>("TimeValue");
            if (l != null) l.text = string.Format("{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            Button btn = root.Q<Button>("ReturnToSelectionButton");
            if (btn != null) btn.clicked += ReturnToTitle;
        }
        if (audioSource != null && clearSound != null) audioSource.PlayOneShot(clearSound); 
    }

    public void BringToFront(Transform root)
    {
        var sg = root.GetComponent<SortingGroup>();
        if (sg != null)
        {
            sg.sortingOrder = topSortingOrder;
            topSortingOrder += 10;
        }
        if (topSortingOrder > 1000000) topSortingOrder = 3000;
    }

    private void ReturnToTitle()
    {
        isFinished = false;
        if (completionUIDoc != null) completionUIDoc.gameObject.SetActive(false);
        if (selectionManager != null) selectionManager.Open();
        SetGuideVisible(false);
    }
    private void SetGuideVisible(bool visible) { 
        if (backgroundGuideRenderer != null) {
            backgroundGuideRenderer.gameObject.SetActive(visible);
            if (visible) {
                // 最前面、且つ不透明（透過なし）で表示
                backgroundGuideRenderer.color = Color.white;
                backgroundGuideRenderer.sortingOrder = 2000000; 
                backgroundGuideRenderer.transform.position = new Vector3(0, 0, -5f); 
            }
        }
    }

    private void PlaySnapEffect(Transform t) { if (audioSource != null && snapSound != null) audioSource.PlayOneShot(snapSound); StartCoroutine(SnapAnimation(t)); }
    private IEnumerator SnapAnimation(Transform t) { if (t == null) yield break; Vector3 s = t.localScale; float d = 0.15f, e = 0f; while (e < d) { e += Time.deltaTime; t.localScale = Vector3.Lerp(s, s * 1.05f, e / d); yield return null; } e = 0f; while (e < d) { e += Time.deltaTime; t.localScale = Vector3.Lerp(s * 1.05f, s, e / d); yield return null; } t.localScale = s; }
    private Color CalculateAndSetDynamicColors(Sprite sprite)
    {
        if (sprite == null) return Color.white;
        Texture2D tex = sprite.texture;
        int step = 20, count = 0;
        float r = 0, g = 0, b = 0;
        
        // テクスチャが読み取り可能でない場合のフォールバック
        try {
            for (int y = 0; y < tex.height; y += step) {
                for (int x = 0; x < tex.width; x += step) {
                    Color c = tex.GetPixel(x, y);
                    r += c.r; g += c.g; b += c.b;
                    count++;
                }
            }
        } catch {
            return new Color(0.8f, 0.8f, 0.8f, 1f);
        }

        if (count == 0) return Color.white;
        Color averageColor = new Color(r / count, g / count, b / count, 1.0f);

        // 背景色の設定（輝度ベースでコントラストを確保）
        if (Camera.main != null) {
            float lum = (0.299f * averageColor.r + 0.587f * averageColor.g + 0.114f * averageColor.b);
            float targetL = (lum > 0.5f) ? 0.15f : 0.4f;
            Camera.main.backgroundColor = new Color(targetL, targetL, targetL * 1.05f);
        }
        
        return averageColor;
    }

    private Vector3 GetPeripheralPosition(float cW, float cH, float pad)
    {
        // 画面の有効範囲（はみ出し防止用）
        float minX = -cW + pad;
        float maxX = cW - pad;
        float minY = -cH + pad;
        float maxY = cH - pad;

        // ボードの内側寄りへの配置をもう少し許容する（内側の範囲を広げる）
        // パズルの中心から見て、ボードのサイズの 70% 程度まで内側に入り込むのを許可
        float boardLimitX = puzzleHalfWidth * 0.7f;
        float boardLimitY = puzzleHalfHeight * 0.7f;

        int zone = UnityEngine.Random.Range(0, 4); // 0:Left, 1:Right, 2:Top, 3:Bottom
        float x = 0, y = 0;

        switch (zone)
        {
            case 0: // Left
                x = UnityEngine.Random.Range(minX, -boardLimitX);
                y = UnityEngine.Random.Range(minY, maxY);
                break;
            case 1: // Right
                x = UnityEngine.Random.Range(boardLimitX, maxX);
                y = UnityEngine.Random.Range(minY, maxY);
                break;
            case 2: // Top
                x = UnityEngine.Random.Range(minX, maxX);
                y = UnityEngine.Random.Range(boardLimitY, maxY);
                break;
            case 3: // Bottom
                x = UnityEngine.Random.Range(minX, maxX);
                y = UnityEngine.Random.Range(minY, -boardLimitY);
                break;
        }

        // 領域が逆転している（ボードが画面に対して大きすぎる）場合に備えたクランプ
        x = Mathf.Clamp(x, minX, maxX);
        y = Mathf.Clamp(y, minY, maxY);

        return new Vector3(x, y, 0);
    }

    private void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void SetupCamera() { 
        Camera cam = Camera.main; 
        if (cam == null) return; 
        cam.clearFlags = CameraClearFlags.SolidColor; cam.orthographic = true; cam.orthographicSize = 6f; cam.transform.position = new Vector3(0, 0, -10f); cam.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
        // アンチエイリアスを無効化（色が混ざるのを防ぐ）
        UnityEngine.QualitySettings.antiAliasing = 0;
        Light dl = FindAnyObjectByType<Light>(); if (dl != null) dl.enabled = false;
    }
}
