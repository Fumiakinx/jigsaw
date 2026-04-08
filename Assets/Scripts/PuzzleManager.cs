using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

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
    private AudioSource audioSource;
    public AudioClip snapSound;
    
    [Header("Clear Effects")]
    public UIDocument completionUIDoc;
    public PuzzleSelectionManager selectionManager;
    public AudioClip clearSound;

    private float startTime;
    private bool isFinished = false;

    private struct EdgeData
    {
        public int type; // 1: 凸, -1: 凹, 0: 平坦
        public float width;  // タブ全体の幅
        public float height; // タブの高さ
        public float offset; // 中心からのズレ
    }

    private EdgeData[,] horizontalEdges;
    private EdgeData[,] verticalEdges;

    private PuzzlePiece draggingPiece = null;

    void Start()
    {
        // 起動時にフルスクリーン設定を適用
        Resolution res = Screen.currentResolution;
        Screen.SetResolution(res.width, res.height, FullScreenMode.FullScreenWindow);

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

        foreach (var p in allPieces) if (p != null) Destroy(p.transform.root.gameObject);
        allPieces.Clear();

        float aspect = (float)sourceSprite.texture.width / sourceSprite.texture.height;
        rows = Mathf.RoundToInt(Mathf.Sqrt(targetPieces / aspect));
        cols = Mathf.RoundToInt(aspect * rows);
        totalPieces = cols * rows;

        if(backgroundGuideRenderer != null)
        {
            backgroundGuideRenderer.sprite = sourceSprite;
            Color c = backgroundGuideRenderer.color;
            c.a = 1.0f;
            backgroundGuideRenderer.color = c;
            backgroundGuideRenderer.sortingOrder = -10;
            backgroundGuideRenderer.gameObject.SetActive(false);
        }

        isFinished = false;
        if (completionUIDoc != null) completionUIDoc.gameObject.SetActive(false);
        startTime = Time.time;

        GeneratePuzzlePieces();
    }

    void Update()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (mouse == null || kb == null) return;
        if (isFinished) return;

        if (kb.spaceKey.wasPressedThisFrame) SetGuideVisible(true);
        else if (kb.spaceKey.wasReleasedThisFrame) SetGuideVisible(false);

        if (mouse.leftButton.wasPressedThisFrame)
        {
            PuzzlePiece piece = GetTopmostPiece(mouse.position.ReadValue());
            if (piece != null)
            {
                draggingPiece = piece;
                draggingPiece.BeginDrag(Camera.main.ScreenToWorldPoint(mouse.position.ReadValue()));
            }
        }
        else if (mouse.leftButton.isPressed && draggingPiece != null)
        {
            draggingPiece.OnPointerDrag(Camera.main.ScreenToWorldPoint(mouse.position.ReadValue()));
            UpdateOverlappedOutlines();
            if (mouse.rightButton.wasPressedThisFrame) draggingPiece.RotateGroup();
        }
        else if (mouse.leftButton.wasReleasedThisFrame && draggingPiece != null)
        {
            draggingPiece.EndDrag();
            UpdateAllOverlappedOutlines();
            draggingPiece = null;
        }
        else if (mouse.rightButton.wasPressedThisFrame && draggingPiece == null)
        {
            PuzzlePiece piece = GetTopmostPiece(mouse.position.ReadValue());
            if (piece != null) piece.RotateGroup();
        }
    }

    void ReturnToTitle()
    {
        isFinished = false;
        if (completionUIDoc != null) completionUIDoc.gameObject.SetActive(false);
        if (selectionManager != null) selectionManager.Open();
    }
    
    private PuzzlePiece GetTopmostPiece(Vector2 mousePosition)
    {
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray);
        
        PuzzlePiece topPiece = null;
        int maxOrder = int.MinValue;

        foreach (var hit in hits)
        {
            PuzzlePiece p = hit.collider.GetComponent<PuzzlePiece>();
            if (p != null && !p.isLocked)
            {
                int order = p.GetComponent<Renderer>().sortingOrder;
                if (order > maxOrder)
                {
                    maxOrder = order;
                    topPiece = p;
                }
            }
        }
        return topPiece;
    }

    void GeneratePuzzlePieces()
    {
        InitializeEdgeData();
        Texture2D tex = sourceSprite.texture;
        float ppu = sourceSprite.pixelsPerUnit;
        int blockWidth = tex.width / cols;
        int blockHeight = tex.height / rows;
        float pieceWorldWidth = (float)blockWidth / ppu;
        float pieceWorldHeight = (float)blockHeight / ppu;
        float halfWidth = (tex.width / ppu) / 2f;
        float halfHeight = (tex.height / ppu) / 2f;

        Material puzzleMaterial = new Material(Shader.Find("Sprites/Default"));
        puzzleMaterial.mainTexture = tex;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                GameObject cluster = new GameObject("Cluster_" + x + "_" + y);
                GameObject pieceObject = new GameObject("Piece_" + x + "_" + y);
                pieceObject.transform.SetParent(cluster.transform);
                pieceObject.AddComponent<MeshFilter>().mesh = CreateIdealPieceMesh(x, y, pieceWorldWidth, pieceWorldHeight, tex.width, tex.height, ppu);
                pieceObject.AddComponent<MeshRenderer>().material = puzzleMaterial;
                pieceObject.AddComponent<MeshCollider>().sharedMesh = pieceObject.GetComponent<MeshFilter>().sharedMesh;

                PuzzlePiece pieceLogic = pieceObject.AddComponent<PuzzlePiece>();
                pieceLogic.manager = this;
                allPieces.Add(pieceLogic);

                AddOutline(pieceObject, pieceObject.GetComponent<MeshFilter>().sharedMesh, pieceLogic);
                SampleEdgeColors(tex, x, y, blockWidth, blockHeight, pieceLogic);
                UpdateOutlineLighting(pieceLogic);
                
                pieceLogic.targetPosition = new Vector2(-halfWidth + pieceWorldWidth * (x + 0.5f), -halfHeight + pieceWorldHeight * (y + 0.5f));

                float cH = Camera.main.orthographicSize, cW = cH * Camera.main.aspect, pad = 1.0f;
                int side = Random.Range(0, 4);
                float px = 0, py = 0;
                if(side == 0) { px = Random.Range(-cW+pad, cW-pad); py = Random.Range(cH*0.6f, cH-pad); }
                else if(side == 1) { px = Random.Range(-cW+pad, cW-pad); py = Random.Range(-cH+pad, -cH*0.6f); }
                else if(side == 2) { px = Random.Range(-cW+pad, -cW*0.6f); py = Random.Range(-cH+pad, cH-pad); }
                else { px = Random.Range(cW*0.6f, cW-pad); py = Random.Range(-cH+pad, cH-pad); }

                cluster.transform.position = new Vector3(px, py, 0);
                cluster.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0, 4) * 90);
                pieceObject.GetComponent<Renderer>().sortingOrder = 10 + (y * cols + x);
            }
        }
        UpdateAllOverlappedOutlines();
    }

    private void AddOutline(GameObject target, Mesh mesh, PuzzlePiece piece)
    {
        piece.edgeL = CreateEdge(target, mesh.vertices[3], mesh.vertices[0], "Edge_L", Color.gray);
        piece.edgeB = CreateEdge(target, mesh.vertices[0], mesh.vertices[1], "Edge_B", Color.black);
        piece.edgeR = CreateEdge(target, mesh.vertices[1], mesh.vertices[2], "Edge_R", Color.black);
        piece.edgeT = CreateEdge(target, mesh.vertices[2], mesh.vertices[3], "Edge_T", Color.gray);
    }

    private GameObject CreateEdge(GameObject parent, Vector3 start, Vector3 end, string name, Color color)
    {
        GameObject o = new GameObject(name);
        o.transform.SetParent(parent.transform, false);
        o.transform.localPosition = new Vector3(0, 0, -0.01f);
        LineRenderer lr = o.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.widthMultiplier = 0.02f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.positionCount = 2; lr.SetPositions(new Vector3[] { start, end });
        lr.startColor = lr.endColor = color; lr.sortingOrder = 101;
        return o;
    }

    private void UpdateGroupOutlines(Transform root)
    {
        PuzzlePiece[] pieces = root.GetComponentsInChildren<PuzzlePiece>();
        float w = (float)sourceSprite.texture.width / cols / sourceSprite.pixelsPerUnit;
        float h = (float)sourceSprite.texture.height / rows / sourceSprite.pixelsPerUnit;
        foreach (var p1 in pieces)
        {
            bool hasL = false, hasR = false, hasT = false, hasB = false;
            foreach (var p2 in pieces)
            {
                if (p1 == p2) continue;
                Vector2 d = p2.targetPosition - p1.targetPosition;
                if (Mathf.Abs(d.x + w) < 0.1f && Mathf.Abs(d.y) < 0.1f) hasL = true;
                if (Mathf.Abs(d.x - w) < 0.1f && Mathf.Abs(d.y) < 0.1f) hasR = true;
                if (Mathf.Abs(d.y - h) < 0.1f && Mathf.Abs(d.x) < 0.1f) hasT = true;
                if (Mathf.Abs(d.y + h) < 0.1f && Mathf.Abs(d.x) < 0.1f) hasB = true;
            }
            if (p1.edgeL) p1.edgeL.SetActive(!hasL);
            if (p1.edgeR) p1.edgeR.SetActive(!hasR);
            if (p1.edgeT) p1.edgeT.SetActive(!hasT);
            if (p1.edgeB) p1.edgeB.SetActive(!hasB);
            UpdateOutlineLighting(p1);
        }
    }

    private void SampleEdgeColors(Texture2D tex, int px, int py, int bw, int bh, PuzzlePiece piece)
    {
        int sx = px * bw, sy = py * bh, d = 4;
        piece.edgeBaseColors[0] = GetAvgColor(tex, sx, sy, d, bh);
        piece.edgeBaseColors[1] = GetAvgColor(tex, sx, sy + bh - d, bw, d);
        piece.edgeBaseColors[2] = GetAvgColor(tex, sx + bw - d, sy, d, bh);
        piece.edgeBaseColors[3] = GetAvgColor(tex, sx, sy, bw, d);
    }

    private Color GetAvgColor(Texture2D tex, int x, int y, int w, int h)
    {
        Color[] cs = tex.GetPixels(x, y, w, h);
        float r = 0, g = 0, b = 0;
        foreach (var c in cs) { r += c.r; g += c.g; b += c.b; }
        return new Color(r/cs.Length, g/cs.Length, b/cs.Length);
    }

    private void UpdateOutlineLighting(PuzzlePiece p)
    {
        if (p.edgeL == null) return;
        SetEdgeLighting(p.edgeL, p.transform.TransformDirection(Vector3.left), p.edgeBaseColors[0]);
        SetEdgeLighting(p.edgeT, p.transform.TransformDirection(Vector3.up), p.edgeBaseColors[1]);
        SetEdgeLighting(p.edgeR, p.transform.TransformDirection(Vector3.right), p.edgeBaseColors[2]);
        SetEdgeLighting(p.edgeB, p.transform.TransformDirection(Vector3.down), p.edgeBaseColors[3]);
    }

    private void SetEdgeLighting(GameObject o, Vector3 d, Color bc)
    {
        if (o == null) return;
        LineRenderer lr = o.GetComponent<LineRenderer>();
        bool bright = d.x < -0.1f || d.y > 0.1f;
        lr.startColor = lr.endColor = bright ? Color.Lerp(bc, Color.white, 0.4f) : Color.Lerp(bc, Color.black, 0.4f);
    }

    private void UpdateOverlappedOutlines() { UpdateAllOverlappedOutlines(); }
    private void UpdateAllOverlappedOutlines()
    {
        List<PuzzlePiece> sp = new List<PuzzlePiece>(allPieces);
        sp.Sort((a, b) => b.GetComponent<Renderer>().sortingOrder.CompareTo(a.GetComponent<Renderer>().sortingOrder));
        foreach (var p in allPieces) UpdateGroupOutlines(p.transform.root);
        for (int i = 0; i < sp.Count; i++) {
            for (int j = i + 1; j < sp.Count; j++) {
                if (sp[i].transform.root == sp[j].transform.root) continue;
                if (sp[i].GetComponent<Renderer>().bounds.Intersects(sp[j].GetComponent<Renderer>().bounds)) {
                    if (sp[j].edgeL) sp[j].edgeL.SetActive(false);
                    if (sp[j].edgeR) sp[j].edgeR.SetActive(false);
                    if (sp[j].edgeT) sp[j].edgeT.SetActive(false);
                    if (sp[j].edgeB) sp[j].edgeB.SetActive(false);
                }
            }
        }
    }

    private void InitializeEdgeData()
    {
        horizontalEdges = new EdgeData[cols, rows + 1];
        verticalEdges = new EdgeData[cols + 1, rows];
        for (int x = 0; x < cols; x++) for (int y = 0; y <= rows; y++) horizontalEdges[x, y] = (y == 0 || y == rows) ? new EdgeData { type = 0 } : new EdgeData { type = Random.value > 0.5f ? 1 : -1, width=0.3f, height=0.3f };
        for (int x = 0; x <= cols; x++) for (int y = 0; y < rows; y++) verticalEdges[x, y] = (x == 0 || x == cols) ? new EdgeData { type = 0 } : new EdgeData { type = Random.value > 0.5f ? 1 : -1, width=0.3f, height=0.3f };
    }

    private Mesh CreateIdealPieceMesh(int x, int y, float w, float h, int texW, int texH, float ppu)
    {
        Mesh m = new Mesh(); List<Vector3> v = new List<Vector3> { new Vector3(-w/2,-h/2,0), new Vector3(w/2,-h/2,0), new Vector3(w/2,h/2,0), new Vector3(-w/2,h/2,0) };
        float uS = (w*ppu)/texW, vS = (h*ppu)/texH, uC = (float)(x*(texW/cols))/texW + uS/2f, vC = (float)(y*(texH/rows))/texH + vS/2f;
        Vector2[] uvs = new Vector2[5]; for(int i=0; i<4; i++) uvs[i] = new Vector2(uC + (v[i].x/w)*uS, vC + (v[i].y/h)*vS); uvs[4] = new Vector2(uC, vC);
        Vector3[] fw = new Vector3[5]; for(int i=0; i<4; i++) fw[i] = v[i]; fw[4] = Vector3.zero;
        m.vertices = fw; m.uv = uvs; m.triangles = new int[] { 4,1,0, 4,2,1, 4,3,2, 4,0,3 };
        m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    public void CheckForGroupSnap(PuzzlePiece activePiece)
    {
        Transform activeRoot = activePiece.transform.root;
        float angle = activeRoot.eulerAngles.z;
        PuzzlePiece[] activeGroup = activeRoot.GetComponentsInChildren<PuzzlePiece>();
        float w = (float)sourceSprite.texture.width / cols / sourceSprite.pixelsPerUnit;
        float h = (float)sourceSprite.texture.height / rows / sourceSprite.pixelsPerUnit;

        foreach (var pA in activeGroup) {
            foreach (var pO in allPieces) {
                Transform oR = pO.transform.root;
                if (oR == activeRoot) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(angle, oR.eulerAngles.z)) > 1f) continue;

                Vector2 gD = pA.targetPosition - pO.targetPosition;
                bool neighbor = (Mathf.Abs(Mathf.Abs(gD.x)-w) < 0.1f && Mathf.Abs(gD.y) < 0.1f) || (Mathf.Abs(Mathf.Abs(gD.y)-h) < 0.1f && Mathf.Abs(gD.x) < 0.1f);
                if (!neighbor) continue;

                Vector3 wTO = activeRoot.TransformDirection(pO.targetPosition - pA.targetPosition);
                if (((Vector2)pO.transform.position - (Vector2)pA.transform.position - (Vector2)wTO).sqrMagnitude < pA.snapThreshold * pA.snapThreshold) {
                    activeRoot.position += (pO.transform.position - wTO) - pA.transform.position;
                    while (activeRoot.childCount > 0) activeRoot.GetChild(0).SetParent(oR);
                    Destroy(activeRoot.gameObject);
                    UpdateGroupOutlines(oR); UpdateAllOverlappedOutlines(); CheckPuzzleComplete(); PlaySnapEffect(oR); return;
                }
            }
        }
        UpdateGroupOutlines(activeRoot); UpdateAllOverlappedOutlines();
    }

    public void CheckPuzzleComplete() 
    { 
        if (allPieces.Count > 0 && allPieces[0].transform.root.GetComponentsInChildren<PuzzlePiece>().Length >= totalPieces) 
        {
            if (!isFinished) ShowClearEffect(); 
        }
    }

    private void ShowClearEffect() 
    { 
        isFinished = true;
        float elapsed = Time.time - startTime;
        int min = Mathf.FloorToInt(elapsed / 60f), sec = Mathf.FloorToInt(elapsed % 60f);

        if (completionUIDoc != null) 
        {
            completionUIDoc.gameObject.SetActive(true);
            var root = completionUIDoc.rootVisualElement;
            Label l = root.Q<Label>("TimeValue");
            if (l != null) l.text = string.Format("{0:00}:{1:00}", min, sec);
            
            Button btn = root.Q<Button>("ReturnToSelectionButton");
            if (btn != null) btn.clicked += ReturnToTitle;
        }

        if (audioSource != null && clearSound != null) audioSource.PlayOneShot(clearSound); 
    }

    public void SetGuideVisible(bool visible) { 
        if(backgroundGuideRenderer != null) {
            backgroundGuideRenderer.gameObject.SetActive(visible);
            backgroundGuideRenderer.sortingOrder = visible ? 32000 : -10;
        }
    }
    
    public void BringToFront(Transform root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        foreach (var r in rs) r.sortingOrder = topSortingOrder++;
        if (topSortingOrder > 1000000) topSortingOrder = 3000;
    }

    private void PlaySnapEffect(Transform t) { if (audioSource != null && snapSound != null) audioSource.PlayOneShot(snapSound); StartCoroutine(SnapAnimation(t)); }
    private System.Collections.IEnumerator SnapAnimation(Transform t) { if (t == null) yield break; Vector3 s = t.localScale; float d = 0.15f, e = 0f; while (e < d) { e += Time.deltaTime; t.localScale = Vector3.Lerp(s, s * 1.05f, e / d); yield return null; } e = 0f; while (e < d) { e += Time.deltaTime; t.localScale = Vector3.Lerp(s * 1.05f, s, e / d); yield return null; } t.localScale = s; }
    private void SetupCamera() { Camera cam = Camera.main; if (cam == null) return; cam.clearFlags = CameraClearFlags.SolidColor; cam.orthographic = true; cam.orthographicSize = 6f; cam.transform.position = new Vector3(0, 0, -10f); cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f); }
}
