using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class MC33PrepareCS
{
    private int _MC_DVE = 1 << 12; // 分割する単位

    // ============================================================
    // ComputeShader移植しやすい「テーブル化」セクション
    // ============================================================

    // 8コーナーのローカルオフセット（x,y,z は 0/1）
    // Vertex order は既存コードの V[0..7] と一致させる
    //   0:(0,0,0) 1:(0,1,0) 2:(0,1,1) 3:(0,0,1)
    //   4:(1,0,0) 5:(1,1,0) 6:(1,1,1) 7:(1,0,1)
    private static readonly int3[] s_cornerOffset =
    {
        new int3(0,0,0), new int3(0,1,0), new int3(0,1,1), new int3(0,0,1),
        new int3(1,0,0), new int3(1,1,0), new int3(1,1,1), new int3(1,0,1),
    };

    // 12エッジが結ぶコーナー対（EdgeId -> (cornerA, cornerB)）
    // EdgeId の並びは既存 find_case の case 0..11 と対応させる
    private static readonly int2[] s_edgeCorners =
    {
        new int2(0,1),  // 0: 0-1 (Y)
        new int2(1,2),  // 1: 1-2 (Z)
        new int2(3,2),  // 2: 3-2 (Y)
        new int2(0,3),  // 3: 0-3 (Z)
        new int2(4,5),  // 4: 4-5 (Y)
        new int2(5,6),  // 5: 5-6 (Z)
        new int2(7,6),  // 6: 7-6 (Y)
        new int2(4,7),  // 7: 4-7 (Z)
        new int2(0,4),  // 8: 0-4 (X)
        new int2(1,5),  // 9: 1-5 (X)
        new int2(2,6),  //10: 2-6 (X)
        new int2(3,7),  //11: 3-7 (X)
    };

    // ============================================================
    // 出力先を抽象化：CPUではNativeList、GPUではAppendBuffer相当
    // ============================================================
    private interface ISurfaceWriter
    {
        int AddVertex(float3 pos, float3 normal, Color color);
        void AddTriangle(int a, int b, int c);
    }

    private struct CpuSurfaceWriter : ISurfaceWriter
    {
        public NativeList<float3> V;
        public NativeList<float3> N;
        public NativeList<Color> C;
        public List<int> T;

        public int AddVertex(float3 pos, float3 normal, Color color)
        {
            V.Add(pos);
            N.Add(normal);
            C.Add(color);
            return V.Length - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            T.Add(a);
            T.Add(b);
            T.Add(c);
        }
    }

    private float3 _MC_O;
    private float3 _MC_D;
    private int3 _MCn;

    private float[] _Volumes;

    private NativeArray<float3> _gradients;
    private int _volumeStrideXY;  // フラット化した３次元配列のZ方向のストライド値

    private void init_temp_isosurface(MC33Grid grd)
    {
        _MCn = grd.N;
        _MC_O = grd.r0;
        _MC_D = grd.d;

        _volumeStrideXY = (_MCn.x + 1) * (_MCn.y + 1);
    }

    private int VolumeIndex(int x, int y, int z)
    {
        return x + y * (_MCn.x + 1) + z * _volumeStrideXY;
    }

    private float SampleVolume(int x, int y, int z)
    {
        return _Volumes[VolumeIndex(x, y, z)];
    }

    private void PrecalcGradients(float[] volumes)
    {
        var nx1 = _MCn.x + 1;
        var ny1 = _MCn.y + 1;
        var nz1 = _MCn.z + 1;

        _gradients = new NativeArray<float3>(nx1 * ny1 * nz1, Allocator.Temp);

        for (int z = 0; z < nz1; z++)
        {
            for (int y = 0; y < ny1; y++)
            {
                for (int x = 0; x < nx1; x++)
                {
                    float gx, gy, gz;

                    if (x == 0) gx = volumes[VolumeIndex(0, y, z)] - volumes[VolumeIndex(1, y, z)];
                    else if (x == _MCn.x) gx = volumes[VolumeIndex(x - 1, y, z)] - volumes[VolumeIndex(x, y, z)];
                    else gx = 0.5f * (volumes[VolumeIndex(x - 1, y, z)] - volumes[VolumeIndex(x + 1, y, z)]);

                    if (y == 0) gy = volumes[VolumeIndex(x, 0, z)] - volumes[VolumeIndex(x, 1, z)];
                    else if (y == _MCn.y) gy = volumes[VolumeIndex(x, y - 1, z)] - volumes[VolumeIndex(x, y, z)];
                    else gy = 0.5f * (volumes[VolumeIndex(x, y - 1, z)] - volumes[VolumeIndex(x, y + 1, z)]);

                    if (z == 0) gz = volumes[VolumeIndex(x, y, 0)] - volumes[VolumeIndex(x, y, 1)];
                    else if (z == _MCn.z) gz = volumes[VolumeIndex(x, y, z - 1)] - volumes[VolumeIndex(x, y, z)];
                    else gz = 0.5f * (volumes[VolumeIndex(x, y, z - 1)] - volumes[VolumeIndex(x, y, z + 1)]);

                    _gradients[VolumeIndex(x, y, z)] = new float3(gx, gy, gz);
                }
            }
        }
    }

    private float3 GetGridGradient(int x, int y, int z)
    {
        return _gradients[VolumeIndex(x, y, z)];
    }

    // ============================================================
    // Compute移植に向けた「共通補間」ユーティリティ
    // ============================================================
    private float3 GridToWorld(float3 gridPos)
    {
        return gridPos * _MC_D + _MC_O;
    }

    private float3 Normalized(float3 n)
    {
#if MC_Normal_neg
        return -math.normalize(n);
#else
        return math.normalize(n);
#endif
    }

    private int WriteGridVertex(ISurfaceWriter writer, int gx, int gy, int gz, Color col)
    {
        var p = new float3(gx, gy, gz);
        var n = GetGridGradient(gx, gy, gz);
        return writer.AddVertex(GridToWorld(p), Normalized(n), col);
    }

    private int WriteEdgeVertex(
        ISurfaceWriter writer,
        int volumeX, int volumeY, int volumeZ,
        int edgeId,
        float isoMinusValuesV0toV7, // ダミー（署名固定用。Compute移植時に整理する）
        float[] v, // v[0..7] をそのまま受ける（GPU側も float v[8] になる想定）
        Color col)
    {
        // Edge -> cornerA/cornerB
        int2 cc = s_edgeCorners[edgeId];
        int ca = cc.x;
        int cb = cc.y;

        float va = v[ca];
        float vb = v[cb];

        // 端点がちょうど0ならそのグリッド点を返す（既存挙動維持）
        int3 oa = s_cornerOffset[ca];
        int3 ob = s_cornerOffset[cb];

        int ax = volumeX + oa.x;
        int ay = volumeY + oa.y;
        int az = volumeZ + oa.z;

        int bx = volumeX + ob.x;
        int by = volumeY + ob.y;
        int bz = volumeZ + ob.z;

        if (va == 0.0f) return WriteGridVertex(writer, ax, ay, az, col);
        if (vb == 0.0f) return WriteGridVertex(writer, bx, by, bz, col);

        // 線形補間 t
        float t = va / (va - vb);

        // 位置補間（グリッド空間）
        float3 pa = new float3(ax, ay, az);
        float3 pb = new float3(bx, by, bz);
        float3 p = math.lerp(pa, pb, t);

        // 法線（勾配）も線形補間
        float3 ga = GetGridGradient(ax, ay, az);
        float3 gb = GetGridGradient(bx, by, bz);
        float3 n = math.lerp(ga, gb, t);

        return writer.AddVertex(GridToWorld(p), Normalized(n), col);
    }

    // ============================================================
    // 既存ロジック：face_tests / face_test1 / interior_test は大枠維持
    // （Compute移植時もそのまま関数として持っていける）
    // ============================================================
    private int face_tests(Span<int> face, int ind, int sw, float[] v)
    {
        // ※元コードの配列 face[6] を Span<int> に変更（new削除）
        if ((ind & 0x80) != 0)
        {
            face[0] = ((ind & 0xCC) == 0x84 ? (v[0] * v[5] < v[1] * v[4] ? -sw : sw) : 0);
            face[3] = ((ind & 0x99) == 0x81 ? (v[0] * v[7] < v[3] * v[4] ? -sw : sw) : 0);
            face[4] = ((ind & 0xF0) == 0xA0 ? (v[0] * v[2] < v[1] * v[3] ? -sw : sw) : 0);
        }
        else
        {
            face[0] = ((ind & 0xCC) == 0x48 ? (v[0] * v[5] < v[1] * v[4] ? sw : -sw) : 0);
            face[3] = ((ind & 0x99) == 0x18 ? (v[0] * v[7] < v[3] * v[4] ? sw : -sw) : 0);
            face[4] = ((ind & 0xF0) == 0x50 ? (v[0] * v[2] < v[1] * v[3] ? sw : -sw) : 0);
        }

        if ((ind & 0x02) != 0)
        {
            face[1] = ((ind & 0x66) == 0x42 ? (v[1] * v[6] < v[2] * v[5] ? -sw : sw) : 0);
            face[2] = ((ind & 0x33) == 0x12 ? (v[3] * v[6] < v[2] * v[7] ? -sw : sw) : 0);
            face[5] = ((ind & 0x0F) == 0x0A ? (v[4] * v[6] < v[5] * v[7] ? -sw : sw) : 0);
        }
        else
        {
            face[1] = ((ind & 0x66) == 0x24 ? (v[1] * v[6] < v[2] * v[5] ? sw : -sw) : 0);
            face[2] = ((ind & 0x33) == 0x21 ? (v[3] * v[6] < v[2] * v[7] ? sw : -sw) : 0);
            face[5] = ((ind & 0x0F) == 0x05 ? (v[4] * v[6] < v[5] * v[7] ? sw : -sw) : 0);
        }

        return face[0] + face[1] + face[2] + face[3] + face[4] + face[5];
    }

    private int face_test1(int face, float[] v)
    {
        switch (face)
        {
            case 0: return (v[0] * v[5] < v[1] * v[4] ? 0x48 : 0x84);
            case 1: return (v[1] * v[6] < v[2] * v[5] ? 0x24 : 0x42);
            case 2: return (v[3] * v[6] < v[2] * v[7] ? 0x21 : 0x12);
            case 3: return (v[0] * v[7] < v[3] * v[4] ? 0x18 : 0x81);
            case 4: return (v[0] * v[2] < v[1] * v[3] ? 0x50 : 0xA0);
            case 5: return (v[4] * v[6] < v[5] * v[7] ? 0x05 : 0x0A);
        }
        return 0;
    }

    private int interior_test(int i, int flagtplane, float[] v)
    {
        var At = v[4] - v[0];
        var Bt = v[5] - v[1];
        var Ct = v[6] - v[2];
        var Dt = v[7] - v[3];
        var t = At * Ct - Bt * Dt;

        if ((i & 0x01) != 0)
        {
            if (t <= 0.0f) return 0;
        }
        else
        {
            if (t >= 0.0f) return 0;
        }

        t = 0.5f * (v[3] * Bt + v[1] * Dt - v[2] * At - v[0] * Ct) / t;
        if (t <= 0.0f || t >= 1.0f) return 0;

        At = v[0] + At * t;
        Bt = v[1] + Bt * t;
        Ct = v[2] + Ct * t;
        Dt = v[3] + Dt * t;

        // Mathf 依存を減らす：符号比較は math.sign を使用
        float sBt = math.sign(Bt);
        float sDt = math.sign(Dt);
        float sAt = math.sign(At);
        float sCt = math.sign(Ct);
        float sVi = math.sign(v[i]);

        if ((i & 0x01) != 0)
        {
            if (At * Ct < Bt * Dt && sBt == sDt)
                return (sBt == sVi) ? 1 : 0 + flagtplane;
        }
        else
        {
            if (At * Ct > Bt * Dt && sAt == sCt)
                return (sAt == sVi) ? 1 : 0 + flagtplane;
        }

        return 0;
    }

    // ============================================================
    // FindCaseAndEmit：頂点生成部を「Edge補間関数」に寄せ、配列newを排除
    // ============================================================
    private void FindCaseAndEmit(ISurfaceWriter writer, int x, int y, int z, int cubeIndex, float[] v)
    {
        var pcase = MC33LookUpTable.Case.Case_1;
        var caseIndex = 0;

        int m, c;
        m = cubeIndex & 0x80;
        c = MC33LookUpTable.Case_Index[(m != 0) ? (cubeIndex ^ 0xff) : cubeIndex];

        Span<int> face = stackalloc int[6];

        switch (c >> 8)
        {
            case 1:
                if ((c & 0x0080) != 0) m ^= 0x80;
                pcase = MC33LookUpTable.Case.Case_1;
                caseIndex = c & 0x7F;
                break;
            case 2:
                if ((c & 0x0080) != 0) m ^= 0x80;
                pcase = MC33LookUpTable.Case.Case_2;
                caseIndex = c & 0x7F;
                break;
            case 3:
                if ((c & 0x0080) != 0) m ^= 0x80;
                if (((m != 0 ? cubeIndex : cubeIndex ^ 0xFF) & face_test1((c & 0x7F) >> 1, v)) != 0)
                {
                    pcase = MC33LookUpTable.Case.Case_3_2;
                    caseIndex = 4 * (c & 0x7F);
                }
                else
                {
                    pcase = MC33LookUpTable.Case.Case_3_1;
                    caseIndex = 2 * (c & 0x7F);
                }

                break;
            case 4:
                if ((c & 0x0080) != 0) m ^= 0x80;
                if (interior_test((c & 0x7F), 0, v) != 0)
                {
                    pcase = MC33LookUpTable.Case.Case_4_2;
                    caseIndex = 6 * (c & 0x7F);
                }
                else
                {
                    pcase = MC33LookUpTable.Case.Case_4_1;
                    caseIndex = 2 * (c & 0x7F);
                }

                break;
            case 5:
                if ((c & 0x0080) != 0) m ^= 0x80;
                pcase = MC33LookUpTable.Case.Case_5;
                caseIndex = c & 0x7F;
                break;
            case 6:
                if ((c & 0x0080) != 0) m ^= 0x80;
                if (((m != 0 ? cubeIndex : cubeIndex ^ 0xFF) & face_test1((c & 0x7F) % 6, v)) != 0)
                {
                    pcase = MC33LookUpTable.Case.Case_6_2;
                    caseIndex = 5 * (c & 0x7F);
                }
                else if (interior_test((c & 0x7F) / 6, 0, v) != 0)
                {
                    pcase = MC33LookUpTable.Case.Case_6_1_2;
                    caseIndex = 7 * (c & 0x7F);
                }
                else
                {
                    pcase = MC33LookUpTable.Case.Case_6_1_1;
                    caseIndex = 3 * (c & 0x7F);
                }

                break;
            case 7:
                if ((c & 0x0080) != 0) m ^= 0x80;
                switch (face_tests(face, cubeIndex, (m != 0 ? 1 : -1), v))
                {
                    case -3:
                        pcase = MC33LookUpTable.Case.Case_7_1;
                        caseIndex = 3 * (c & 0x7F);
                        break;
                    case -1:
                        if (face[4] + face[5] == 1)
                        {
                            pcase = MC33LookUpTable.Case.Case_7_2_1;
                            caseIndex = 5 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = (face[(33825 >> ((c & 0x7F) << 1)) & 3] == 1
                                ? MC33LookUpTable.Case.Case_7_2_3
                                : MC33LookUpTable.Case.Case_7_2_2);
                            caseIndex = 5 * (c & 0x7F);
                        }

                        break;
                    case 1:
                        if (face[4] + face[5] == -1)
                        {
                            pcase = MC33LookUpTable.Case.Case_7_3_3;
                            caseIndex = 9 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = (face[(33825 >> ((c & 0x7F) << 1)) & 3] == 1
                                ? MC33LookUpTable.Case.Case_7_3_2
                                : MC33LookUpTable.Case.Case_7_3_1);
                            caseIndex = 9 * (c & 0x7F);
                        }

                        break;
                    case 3:
                        if (interior_test((c & 0x7F) >> 1, 0, v) != 0)
                        {
                            pcase = MC33LookUpTable.Case.Case_7_4_2;
                            caseIndex = 9 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = MC33LookUpTable.Case.Case_7_4_1;
                            caseIndex = 5 * (c & 0x7F);
                        }

                        break;
                }

                break;
            case 8:
                pcase = MC33LookUpTable.Case.Case_8;
                caseIndex = (c & 0x7F);
                break;
            case 9:
                pcase = MC33LookUpTable.Case.Case_9;
                caseIndex = (c & 0x7F);
                break;
            case 10:
                switch (face_tests(face, cubeIndex, (m != 0 ? 1 : -1), v))
                {
                    case -2:
                    {
                        var a = interior_test(0, 0, v) != 0;
                        var b = interior_test((c & 0x01) != 0 ? 1 : 3, 0, v) != 0;
                        if ((c & 0x7F) != 0 ? (a || b) : interior_test(0, 0, v) != 0)
                        {
                            pcase = MC33LookUpTable.Case.Case_10_1_2_1;
                            caseIndex = 8 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = MC33LookUpTable.Case.Case_10_1_1_1;
                            caseIndex = 4 * (c & 0x7F);
                        }
                    }
                        break;
                    case 2:
                    {
                        var a = interior_test(2, 0, v) != 0;
                        var b = interior_test((c & 0x01) != 0 ? 3 : 1, 0, v) != 0;
                        if ((c & 0x7F) != 0 ? (a || b) : interior_test(1, 0, v) != 0)
                        {
                            pcase = MC33LookUpTable.Case.Case_10_1_2_2;
                            caseIndex = 8 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = MC33LookUpTable.Case.Case_10_1_1_2;
                            caseIndex = 4 * (c & 0x7F);
                        }
                    }
                        break;
                    case 0:
                        pcase = (face[4 >> ((c & 0x7F) << 1)] == 1
                            ? MC33LookUpTable.Case.Case_10_2_2
                            : MC33LookUpTable.Case.Case_10_2_1);
                        caseIndex = 8 * (c & 0x7F);
                        break;
                }

                break;
            case 11:
                pcase = MC33LookUpTable.Case.Case_11;
                caseIndex = (c & 0x7F);
                break;
            case 12:
                switch (face_tests(face, cubeIndex, (m != 0 ? 1 : -1), v))
                {
                    case -2:
                        if (interior_test((int)MC33LookUpTable._12_test_index[0, (int)(c & 0x7F)], 0, v) != 0)
                        {
                            pcase = MC33LookUpTable.Case.Case_12_1_2_1;
                            caseIndex = 8 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = MC33LookUpTable.Case.Case_12_1_1_1;
                            caseIndex = 4 * (c & 0x7F);
                        }

                        break;
                    case 2:
                        if (interior_test((int)MC33LookUpTable._12_test_index[1, (int)(c & 0x7F)], 0, v) != 0)
                        {
                            pcase = MC33LookUpTable.Case.Case_12_1_2_2;
                            caseIndex = 8 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = MC33LookUpTable.Case.Case_12_1_1_2;
                            caseIndex = 4 * (c & 0x7F);
                        }

                        break;
                    case 0:
                        pcase = (face[(int)MC33LookUpTable._12_test_index[2, (int)(c & 0x7F)]] == 1
                            ? MC33LookUpTable.Case.Case_12_2_2
                            : MC33LookUpTable.Case.Case_12_2_1);
                        caseIndex = 8 * (c & 0x7F);
                        break;
                }

                break;
            case 13:
            {
                // Case 13 は face_tests の結果（-6..6）をさらに分類して pcase/caseIndex を決定する
                int ct = face_tests(face, cubeIndex, (m != 0 ? 1 : -1), v);

                switch (math.abs(ct))
                {
                    case 6:
                        // 13.1
                        pcase = MC33LookUpTable.Case.Case_13_1;
                        caseIndex = 4 * ((ct > 0) ? 1 : 0);
                        break;

                    case 4:
                    {
                        // 13.2
                        // 元コード: c >>= 2; i=0; while(f[i]!=-c)++i; caseIndex = 6*(3*c+3+i); i=1;
                        int cc = ct >> 2; // -1 or +1
                        int which = 0;
                        while (which < 6 && face[which] != -cc) ++which;

                        pcase = MC33LookUpTable.Case.Case_13_2;
                        caseIndex = 6 * (3 * cc + 3 + which);

                        // 元実装と同じく、この分岐では以降の while(i!=0) を確実に回すため i を 1 にする
                        cubeIndex = 1;
                        break;
                    }

                    case 2:
                    {
                        // 13.3
                        // face[0..4] の符号を 5bit に詰める（元コードの手順をそのまま）
                        int bits =
                            (((((face[0] < 0) ? 1 : 0) << 1) | ((face[1] < 0) ? 1 : 0)) << 1 |
                             ((face[2] < 0) ? 1 : 0)) << 1 |
                            ((face[3] < 0) ? 1 : 0);

                        bits = (bits << 1) | ((face[4] < 0) ? 1 : 0);

                        pcase = MC33LookUpTable.Case.Case_13_3;
                        caseIndex = 10 * (25 - bits + ((((bits > 10) ? 1 : 0) + ((bits > 20) ? 1 : 0)) << 1));
                        break;
                    }

                    case 0:
                    {
                        // 13.4 or 13.5 (1/2)
                        // 元コード: c = (((f[1]<0)?1:0)<<1) | ((f[5]<0)?1:0);
                        //           if(f0*f1*f5==1) -> 13.4 else interior_test(c,1,v) で 13.5 を分岐
                        int cc = (((face[1] < 0) ? 1 : 0) << 1) | ((face[5] < 0) ? 1 : 0);

                        // face 値は -1/0/+1 なので積が 1 なら全て同符号（かつ 0 なし）
                        if (face[0] * face[1] * face[5] == 1)
                        {
                            pcase = MC33LookUpTable.Case.Case_13_4;
                            caseIndex = 12 * cc;
                        }
                        else
                        {
                            int it = interior_test(cc, 1, v);
                            if (it != 0)
                            {
                                // 13.5.2
                                pcase = MC33LookUpTable.Case.Case_13_5_2;
                                caseIndex = 10 * (cc | ((it & 1) << 2));
                            }
                            else
                            {
                                // 13.5.1
                                pcase = MC33LookUpTable.Case.Case_13_5_1;
                                caseIndex = 6 * cc;

                                // 元実装と同じく、以降の while(i!=0) を回すため i=1 を立てる
                                cubeIndex = 1;
                            }
                        }

                        break;
                    }
                }
                break;
            }
            case 14:
                pcase = MC33LookUpTable.Case.Case_14;
                caseIndex = (c & 0x7F);
                break;
        }

        Color col = Color.white;

        // p[0..12] を new せず stackalloc
        Span<int> p = stackalloc int[13];
        for (int pi = 0; pi < p.Length; pi++) p[pi] = -1;

        while (cubeIndex != 0)
        {
            cubeIndex = MC33LookUpTable.LookUp(pcase, caseIndex++);
            int f0 = 0, f1 = 0, f2 = 0;

            for (int k = 0; k < 3; k++)
            {
                int edgeId = cubeIndex & 0x0F;

                if (p[edgeId] < 0)
                {
                    if (edgeId == 12)
                    {
                        // Interior center は元コード同等（勾配平均）
                        float3 center = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
                        float3 sumG =
                            GetGridGradient(x, y, z) + GetGridGradient(x + 1, y, z) +
                            GetGridGradient(x, y + 1, z) + GetGridGradient(x + 1, y + 1, z) +
                            GetGridGradient(x, y, z + 1) + GetGridGradient(x + 1, y, z + 1) +
                            GetGridGradient(x, y + 1, z + 1) + GetGridGradient(x + 1, y + 1, z + 1);

                        float3 n = sumG / 8.0f;
                        p[12] = writer.AddVertex(GridToWorld(center), Normalized(n), col);
                    }
                    else
                    {
                        // 12本のエッジは共通関数で生成
                        p[edgeId] = WriteEdgeVertex(writer, x, y, z, edgeId, 0.0f, v, col);
                    }
                }

                if (k == 0) f0 = p[edgeId];
                else if (k == 1) f1 = p[edgeId];
                else f2 = p[edgeId];

                cubeIndex >>= 4;
            }

            if (f0 != f1 && f0 != f2 && f1 != f2)
            {
#if MC_Normal_neg
                if (m != 0)
#else
                if (m == 0)
#endif
                {
                    // 既存挙動維持：頂点順の入れ替え
                    int tmp = f2;
                    f2 = f0;
                    f0 = tmp;
                }

                writer.AddTriangle(f0, f1, f2);
            }
        }
    }

    private Mesh BuildIsoSurface(float iso, float[] volumes)
    {
        _Volumes = volumes;

        int nx = _MCn.x;
        int ny = _MCn.y;
        int nz = _MCn.z;

        var writer = new CpuSurfaceWriter
        {
            V = new NativeList<float3>(4096, Allocator.Temp),
            N = new NativeList<float3>(4096, Allocator.Temp),
            C = new NativeList<Color>(4096, Allocator.Temp),
            T = new List<int>(4096),
        };

        float[] V = new float[8];

        for (int z = 0; z < nz; z++)
        {
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    float V00 = SampleVolume(x, y, z);
                    float V01 = SampleVolume(x, y + 1, z);
                    float V10 = SampleVolume(x, y, z + 1);
                    float V11 = SampleVolume(x, y + 1, z + 1);
                    V[0] = iso - V00;
                    V[1] = iso - V01;
                    V[2] = iso - V11;
                    V[3] = iso - V10;

                    V00 = SampleVolume(x + 1, y, z);
                    V01 = SampleVolume(x + 1, y + 1, z);
                    V10 = SampleVolume(x + 1, y, z + 1);
                    V11 = SampleVolume(x + 1, y + 1, z + 1);
                    V[4] = iso - V00;
                    V[5] = iso - V01;
                    V[6] = iso - V11;
                    V[7] = iso - V10;

                    int idx =
                        ((V[0] >= 0f ? 1 : 0) << 7) |
                        ((V[1] >= 0f ? 1 : 0) << 6) |
                        ((V[2] >= 0f ? 1 : 0) << 5) |
                        ((V[3] >= 0f ? 1 : 0) << 4) |
                        ((V[4] >= 0f ? 1 : 0) << 3) |
                        ((V[5] >= 0f ? 1 : 0) << 2) |
                        ((V[6] >= 0f ? 1 : 0) << 1) |
                        ((V[7] >= 0f ? 1 : 0) << 0);

                    if (idx != 0 && idx != 0xff)
                    {
                        FindCaseAndEmit(writer, x, y, z, idx, V);
                    }
                }
            }
        }

        var mesh = new Mesh();
        if (writer.T.Count > 0)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(writer.V.AsArray());
            mesh.SetTriangles(writer.T, 0);
            mesh.SetNormals(writer.N.AsArray());
            mesh.SetColors(writer.C.AsArray());
        }

        // NativeList はここでは Dispose しない（元コードと合わせるなら呼び出し側で管理 or using化）
        return mesh;
    }

    public Mesh calculate_isosurface(MC33Grid grd, float iso, float[] volumes)
    {
        init_temp_isosurface(grd);
        PrecalcGradients(volumes);

        var mesh = BuildIsoSurface(iso, volumes);

        if (_gradients.IsCreated)
        {
            _gradients.Dispose();
        }

        return mesh;
    }
}
