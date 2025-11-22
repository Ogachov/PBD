// WaterPBF.cs - GPU PBF + Marching Cubes with CPU emitter/recycle support
using UnityEngine;
using UnityEngine.Rendering;

public class WaterPBF : MonoBehaviour
{
    [Header("References")]
    public ComputeShader pbfCS;
    public ComputeShader mcCS;
    public Material surfaceMat;

    [Header("Particles / Emission")]
    public int maxParticles = 80000;
    public int preWarm = 20000;
    public int emitPerFrame = 800;
    public Vector3 emitterCenter = new Vector3(0, 0.6f, 0);
    public Vector3 emitterSize = new Vector3(0.5f, 0.05f, 0.5f);
    public Vector3 emitVelocity = new Vector3(0, -1.0f, 0);
    [Tooltip("When particle.y falls below this world Y, it will be recycled to the emitter.")]
    public float recycleY = -0.8f;

    [Header("Solver")]
    public Vector3 gridMin = new Vector3(-1,-1,-1);
    public Vector3Int gridRes = new Vector3Int(64,64,64);
    public float cellSize = 0.04f;
    public float h = 0.05f;
    public float restDensity = 1.0f;
    public float dt = 1/60f;
    public Vector3 gravity = new Vector3(0,-9.8f,0);
    [Range(0,0.3f)] public float xsph = 0.1f;
    public float boundaryDamp = 0.2f;
    [Range(1,12)] public int pbfIters = 4;

    [Header("Field/MC")]
    public Vector3Int fieldRes = new Vector3Int(64,64,64);
    public float fieldDx = 0.04f;
    public float isoValue = 0.6f;

    struct Particle { public Vector3 x, v; public float rho, lambda; }

    ComputeBuffer particles, oldPos, cellHead, next;
    ComputeBuffer field, triVerts, edgeTable, triTable, args;
    uint[] argsData = new uint[]{0,1,0,0};

    int kClearGrid, kBuildGrid, kPredict, kLambda, kDelta, kBoundary, kRecycle, kUpdateV;
    int kClearField, kSplat, kBuildTris;

    int aliveN = 0;
    int frameIndex = 0;
    Camera cam;

    void Start(){
        cam = Camera.main;
        Allocate();
        FindKernels();
        SetStaticParams();
        EmitCPU(Mathf.Min(preWarm, maxParticles));
    }

    void Allocate(){
        int Ncells = gridRes.x*gridRes.y*gridRes.z;
        particles = new ComputeBuffer(maxParticles, sizeof(float)*(3+3+1+1));
        oldPos    = new ComputeBuffer(maxParticles, sizeof(float)*3);
        cellHead  = new ComputeBuffer(Ncells, sizeof(int));
        next      = new ComputeBuffer(maxParticles, sizeof(int));

        int F = fieldRes.x*fieldRes.y*fieldRes.z;
        field    = new ComputeBuffer(F, sizeof(float));
        triVerts = new ComputeBuffer(1<<22, sizeof(float)*3, ComputeBufferType.Append);
        triVerts.SetCounterValue(0);

        edgeTable = new ComputeBuffer(256, sizeof(int));
        triTable  = new ComputeBuffer(256*16, sizeof(int));
        edgeTable.SetData(MCTables.EdgeTable);
        triTable.SetData(MCTables.TriTable);

        args = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        args.SetData(argsData);
    }

    void FindKernels(){
        kClearGrid = pbfCS.FindKernel("ClearGrid");
        kBuildGrid = pbfCS.FindKernel("BuildGrid");
        kPredict   = pbfCS.FindKernel("Predict");
        kLambda    = pbfCS.FindKernel("ComputeLambda");
        kDelta     = pbfCS.FindKernel("ComputeDelta");
        kBoundary  = pbfCS.FindKernel("ApplyBoundary");
        kRecycle   = pbfCS.FindKernel("RecycleBelow");
        kUpdateV   = pbfCS.FindKernel("UpdateVelocity");

        kClearField= mcCS.FindKernel("ClearField");
        kSplat     = mcCS.FindKernel("SplatDensity");
        kBuildTris = mcCS.FindKernel("BuildTriangles");
    }

    void SetStaticParams(){
        // PBF: static parts
        foreach (int k in new[]{kClearGrid,kBuildGrid,kPredict,kLambda,kDelta,kBoundary,kRecycle,kUpdateV}){
            pbfCS.SetInts("_GridRes", gridRes.x,gridRes.y,gridRes.z);
            pbfCS.SetVector("_GridMin", gridMin);
            pbfCS.SetFloat("_CellSize", cellSize);
            pbfCS.SetFloat("_H", h);
            pbfCS.SetFloat("_RestDensity", restDensity);
            pbfCS.SetFloat("_Epsilon", 1e-6f);
            pbfCS.SetFloat("_BoundaryDamp", boundaryDamp);

            pbfCS.SetBuffer(k, "_Particles", particles);
            pbfCS.SetBuffer(k, "_OldPos", oldPos);
            pbfCS.SetBuffer(k, "_CellHead", cellHead);
            pbfCS.SetBuffer(k, "_Next", next);
        }

        // MC
        foreach (int k in new[]{kClearField,kSplat,kBuildTris}){
            mcCS.SetInts("_FieldRes", fieldRes.x,fieldRes.y,fieldRes.z);
            mcCS.SetVector("_FieldMin", gridMin);
            mcCS.SetFloat("_FieldDx", fieldDx);
            mcCS.SetFloat("_Isovalue", isoValue);
            mcCS.SetFloat("_H", h);
            mcCS.SetFloat("_RestDensity", restDensity);

            mcCS.SetBuffer(k, "_Particles", particles);
            mcCS.SetBuffer(k, "_Field", field);
        }
        mcCS.SetBuffer(kBuildTris, "_TriVerts", triVerts);
        mcCS.SetBuffer(kBuildTris, "_EdgeTable", edgeTable);
        mcCS.SetBuffer(kBuildTris, "_TriTable", triTable);
    }

    void Update(){
        // Emit a bit each frame
        if (emitPerFrame > 0) EmitCPU(emitPerFrame);

        // dynamic params
        pbfCS.SetFloat("_DeltaTime", dt);
        pbfCS.SetVector("_Gravity", gravity);
        pbfCS.SetFloat("_XSPH", xsph);
        pbfCS.SetInt("_NumParticles", aliveN);
        pbfCS.SetInt("_Frame", frameIndex++);
        pbfCS.SetVector("_EmitterCenter", emitterCenter);
        pbfCS.SetVector("_EmitterSize", emitterSize);
        pbfCS.SetVector("_EmitVel", emitVelocity);
        pbfCS.SetFloat("_RecycleY", recycleY);

        mcCS.SetInt("_NumParticles", aliveN);

        StepPBF();
        ExtractSurface();
        Draw();
    }

    void StepPBF(){
        int threads = 128;
        int groupsP = Mathf.Max(1, Mathf.CeilToInt(aliveN/(float)threads));
        int groupsC = Mathf.CeilToInt(gridRes.x*gridRes.y*gridRes.z/(float)threads);

        pbfCS.Dispatch(kClearGrid, groupsC,1,1);
        pbfCS.Dispatch(kBuildGrid, groupsP,1,1);
        pbfCS.Dispatch(kPredict, groupsP,1,1);

        for (int it=0; it<pbfIters; ++it){
            pbfCS.Dispatch(kLambda, groupsP,1,1);
            pbfCS.Dispatch(kDelta,  groupsP,1,1);
            pbfCS.Dispatch(kBoundary,groupsP,1,1);
        }
        // recycle below threshold to keep flow going
        pbfCS.Dispatch(kRecycle, groupsP,1,1);

        pbfCS.Dispatch(kUpdateV, groupsP,1,1);
    }

    void ExtractSurface(){
        triVerts.SetCounterValue(0);

        mcCS.Dispatch(kClearField, Mathf.CeilToInt(fieldRes.x/8f), Mathf.CeilToInt(fieldRes.y/8f), Mathf.CeilToInt(fieldRes.z/8f));
        mcCS.Dispatch(kSplat, Mathf.CeilToInt(fieldRes.x/8f), Mathf.CeilToInt(fieldRes.y/8f), Mathf.CeilToInt(fieldRes.z/8f));
        mcCS.Dispatch(kBuildTris, Mathf.CeilToInt((fieldRes.x-1)/8f), Mathf.CeilToInt((fieldRes.y-1)/8f), Mathf.CeilToInt((fieldRes.z-1)/8f));

        ComputeBuffer.CopyCount(triVerts, args, 0);
        argsData[1] = 1; argsData[2] = 0; argsData[3] = 0;
        args.SetData(argsData);
    }

    void Draw(){
        if (!surfaceMat) return;
        surfaceMat.SetBuffer("_TriVerts", triVerts);
        var camMat = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
        surfaceMat.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
        surfaceMat.SetMatrix("_WorldToClip", camMat);
        Graphics.DrawProceduralIndirect(surfaceMat, new Bounds(transform.position, Vector3.one*1000f),
            MeshTopology.Triangles, args, 0);
    }

    void EmitCPU(int count){
        if (count <= 0 || aliveN >= maxParticles) return;
        count = Mathf.Min(count, maxParticles - aliveN);

        var spawn = new Particle[count];
        var old = new Vector3[count];
        var rnd = new System.Random(1234 + frameIndex);

        for (int i=0;i<count;i++){
            float rx = (float)rnd.NextDouble()*2f-1f;
            float ry = (float)rnd.NextDouble()*2f-1f;
            float rz = (float)rnd.NextDouble()*2f-1f;
            Vector3 jitter = new Vector3(rx, ry, rz)*0.5f;
            Vector3 pos = emitterCenter + Vector3.Scale(emitterSize, jitter);

            spawn[i].x = pos;
            spawn[i].v = emitVelocity;
            spawn[i].rho = restDensity;
            spawn[i].lambda = 0;

            old[i] = pos - emitVelocity * Mathf.Max(dt, 1e-4f);
        }

        particles.SetData(spawn, 0, aliveN, count);
        oldPos.SetData(old, 0, aliveN, count);
        aliveN += count;
    }

    void OnDestroy(){
        particles?.Dispose(); oldPos?.Dispose(); cellHead?.Dispose(); next?.Dispose();
        field?.Dispose(); triVerts?.Dispose(); edgeTable?.Dispose(); triTable?.Dispose(); args?.Dispose();
    }
}
