using UnityEngine;


[RequireComponent(typeof(Rigidbody2D), typeof(MeshFilter), typeof(MeshRenderer))]
public class SoftBlob : MonoBehaviour
{
    [Header("Shape")]
    public int nodeCount = 14;
    public float radius = 0.8f;
    public float nodeRadius = 0.1f;
    public float nodeMass = 0.2f;

    [Header("Softness")]
    public float coreFrequency = 4f;
    public float coreDamping = 0.3f;
    public float neighborFrequency = 8f;
    public float neighborDamping = 0.5f;

    [Header("Shape Memory")]
    public float shapeStiffness = 15f;   
    public float shapeDamping = 4f;
    [Range(0f, 1f)] public float squeezeFactor = 0.15f; 
    public bool useOppositeBrace = false;  
    public float crossFrequency = 3f;    
    public float nodeFriction = 0.3f;    
    public float maxStretch = 3f;        

    [Header("Look")]
    public Texture2D fillTexture;           
    public Color fillColor = new Color(0.55f, 0.1f, 0.15f);
    public Color outlineColor = new Color(0.25f, 0.03f, 0.06f);
    public float outlineWidth = 0.08f;
    [Range(1, 8)] public int smoothing = 5;   
    public int sortingOrder = 0;              

    [Header("Face")]
    public Transform face;
    public Vector2 faceOffset = new Vector2(0.2f, 0.1f);
    public float faceSpring = 120f;
    public float faceDrag = 8f;
    public float lookAmount = 0.25f;     
    public float lookSpeedForMax = 6f;   
    public float lookSmoothing = 8f;     
    public bool lookVertical = true;     

    Rigidbody2D core;
    Rigidbody2D[] nodes;
    Vector2[] restDirs;
    PhysicsMaterial2D nodeMat;
    Mesh mesh;
    Vector3[] verts;
    Vector2[] uvs;
    Vector3[] linePts;
    LineRenderer line;
    Vector2 faceVel;
    Vector2 look;

    public Vector2 Centroid
    {
        get
        {
            Vector2 sum = Vector2.zero;
            foreach (var n in nodes) sum += n.position;
            return sum / nodes.Length;
        }
    }

    void Awake()
    {
        core = GetComponent<Rigidbody2D>();
        core.freezeRotation = true;
        core.interpolation = RigidbodyInterpolation2D.Interpolate;

        nodes = new Rigidbody2D[nodeCount];
        restDirs = new Vector2[nodeCount];
        nodeMat = new PhysicsMaterial2D("BlobNode") { friction = nodeFriction, bounciness = 0f };
        for (int i = 0; i < nodeCount; i++)
        {
            float a = i * Mathf.PI * 2f / nodeCount;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            restDirs[i] = dir;

            var go = new GameObject("Node" + i);
            go.layer = gameObject.layer;
            go.transform.position = (Vector2)transform.position + dir * radius;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = nodeMass;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var cc = go.AddComponent<CircleCollider2D>();
            cc.radius = nodeRadius;
            cc.sharedMaterial = nodeMat;

            var sj = go.AddComponent<SpringJoint2D>();
            sj.connectedBody = core;
            sj.autoConfigureDistance = false;
            sj.distance = radius;
            sj.anchor = Vector2.zero;
            sj.connectedAnchor = Vector2.zero;
            sj.frequency = coreFrequency;
            sj.dampingRatio = coreDamping;

            nodes[i] = rb;
        }

        float side = 2f * radius * Mathf.Sin(Mathf.PI / nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            var sj = nodes[i].gameObject.AddComponent<SpringJoint2D>();
            sj.connectedBody = nodes[(i + 1) % nodeCount];
            sj.autoConfigureDistance = false;
            sj.distance = side;
            sj.anchor = Vector2.zero;
            sj.connectedAnchor = Vector2.zero;
            sj.frequency = neighborFrequency;
            sj.dampingRatio = neighborDamping;
        }

        
        int[] skips = useOppositeBrace ? new[] { 2, nodeCount / 2 } : new[] { 2 };
        foreach (int skip in skips)
        {
            float chord = 2f * radius * Mathf.Sin(Mathf.PI * skip / nodeCount);
            for (int i = 0; i < nodeCount; i++)
            {
                if (skip == nodeCount / 2 && i >= nodeCount / 2) continue;
                var sj = nodes[i].gameObject.AddComponent<SpringJoint2D>();
                sj.connectedBody = nodes[(i + skip) % nodeCount];
                sj.autoConfigureDistance = false;
                sj.distance = chord;
                sj.anchor = Vector2.zero;
                sj.connectedAnchor = Vector2.zero;
                sj.frequency = crossFrequency;
                sj.dampingRatio = neighborDamping;
            }
        }

        SetupRendering();
    }

    void FixedUpdate()
    {
        
        for (int i = 0; i < nodeCount; i++)
        {
            var n = nodes[i];
            Vector2 target = core.position + restDirs[i] * radius;
            Vector2 err = target - n.position;

            
            if (err.magnitude > radius * maxStretch)
            {
                n.position = target;
                n.linearVelocity = core.linearVelocity;
                continue;
            }

            Vector2 relVel = n.linearVelocity - core.linearVelocity;
            
            bool squeezed = n.IsTouchingLayers(~(1 << gameObject.layer));
            float k = squeezed ? squeezeFactor : 1f;
            n.AddForce((err * shapeStiffness * k - relVel * shapeDamping) * n.mass);
        }
    }

    void SetupRendering()
    {
        int total = nodeCount * smoothing;

        
        var mr = GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = fillColor;                  
        if (fillTexture != null) mat.mainTexture = fillTexture;
        mr.material = mat;
        mr.sortingOrder = sortingOrder;

        mesh = new Mesh();
        mesh.MarkDynamic();
        verts = new Vector3[total + 1];
        uvs = new Vector2[total + 1];
        var tris = new int[total * 3];
        for (int i = 0; i < total; i++)
        {
            tris[i * 3] = total;
            tris[i * 3 + 1] = i;
            tris[i * 3 + 2] = (i + 1) % total;
        }
        mesh.vertices = verts;
        mesh.triangles = tris;
        GetComponent<MeshFilter>().mesh = mesh;

        
        var lgo = new GameObject("Outline");
        lgo.transform.SetParent(transform, false);
        line = lgo.AddComponent<LineRenderer>();
        line.loop = true;
        line.useWorldSpace = true;
        line.positionCount = total;
        line.widthMultiplier = outlineWidth;
        line.numCornerVertices = 4;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = outlineColor;
        line.sortingOrder = sortingOrder + 1;
        linePts = new Vector3[total];
    }

    
    static Vector2 Spline(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void LateUpdate()
    {
        int total = nodeCount * smoothing;
        int k = 0;
        Vector2 c = Centroid;
        for (int i = 0; i < nodeCount; i++)
        {
            Vector2 p0 = nodes[(i - 1 + nodeCount) % nodeCount].position;
            Vector2 p1 = nodes[i].position;
            Vector2 p2 = nodes[(i + 1) % nodeCount].position;
            Vector2 p3 = nodes[(i + 2) % nodeCount].position;
            for (int s = 0; s < smoothing; s++, k++)
            {
                Vector2 p = Spline(p0, p1, p2, p3, s / (float)smoothing);
                
                p += (p - c).normalized * nodeRadius;
                linePts[k] = p;
                verts[k] = transform.InverseTransformPoint(p);
                
                
                float ang = (i + s / (float)smoothing) * Mathf.PI * 2f / nodeCount;
                uvs[k] = new Vector2(0.5f + 0.5f * Mathf.Cos(ang), 0.5f + 0.5f * Mathf.Sin(ang));
            }
        }

        verts[total] = transform.InverseTransformPoint(c);
        uvs[total] = Vector2.one * 0.5f;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        line.SetPositions(linePts);

        if (face != null)
        {
            
            Vector2 v = core.linearVelocity / lookSpeedForMax;
            if (!lookVertical) v.y = 0f;
            v = Vector2.ClampMagnitude(v, 1f);
            look = Vector2.Lerp(look, v, 1f - Mathf.Exp(-lookSmoothing * Time.deltaTime));

            Vector2 target = c + faceOffset + look * lookAmount;
            Vector2 pos = face.position;
            faceVel += (target - pos) * faceSpring * Time.deltaTime;
            faceVel -= faceVel * faceDrag * Time.deltaTime;
            face.position = new Vector3(pos.x + faceVel.x * Time.deltaTime,
                                       pos.y + faceVel.y * Time.deltaTime,
                                       face.position.z);
        }
    }
}