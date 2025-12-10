using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class MC33PrepareCS
{
    private int _MC_DVE = 1 << 12; // 分割する単位

    private struct surface
    {
        public int flag;
        public int nV;
        public List<int> T;
        public NativeList<float3> V;
        public NativeList<float3> N;
        public NativeList<Color> C;
    }

    private surface _MC_S;

    private float3 _MC_O;
    private float3 _MC_D;
    private int3 _MCn;

    private float[] _F;

    private void init_temp_isosurface(MC33Grid grd)
    {
        _MCn = grd.N;
        _MC_O = grd.r0;
        _MC_D = grd.d;
        // キャッシュ配列の初期化は削除
    }

    private float GetCellValue(int x, int y, int z)
    {
        var nx = _MCn.x + 1;
        var ny = _MCn.y + 1;
        return _F[x + y * nx + z * nx * ny];
    }

    // グリッド座標における勾配(法線)を中心差分で計算する
    private float3 GetGridGradient(int x, int y, int z)
    {
        float3 n = float3.zero;

        // X軸
        if (x == 0)
            n.x = GetCellValue(0, y, z) - GetCellValue(1, y, z);
        else if (x == _MCn.x)
            n.x = GetCellValue(x - 1, y, z) - GetCellValue(x, y, z);
        else
            n.x = 0.5f * (GetCellValue(x - 1, y, z) - GetCellValue(x + 1, y, z));

        // Y軸
        if (y == 0)
            n.y = GetCellValue(x, 0, z) - GetCellValue(x, 1, z);
        else if (y == _MCn.y)
            n.y = GetCellValue(x, y - 1, z) - GetCellValue(x, y, z);
        else
            n.y = 0.5f * (GetCellValue(x, y - 1, z) - GetCellValue(x, y + 1, z));

        // Z軸
        if (z == 0)
            n.z = GetCellValue(x, y, 0) - GetCellValue(x, y, 1);
        else if (z == _MCn.z)
            n.z = GetCellValue(x, y, z - 1) - GetCellValue(x, y, z);
        else
            n.z = 0.5f * (GetCellValue(x, y, z - 1) - GetCellValue(x, y, z + 1));

        return n;
    }

    /*
    This function return a vector with all six test face results (face[6]).
    */
    private int face_tests(int[] face, int ind, int sw, float[] v)
    {
        if ((ind & 0x80) != 0) //vertex 0
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

        if ((ind & 0x02) != 0) //vertex 6
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

    /* Faster function for the face test */
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

    /* Interior test function. */
    private int interior_test(int i, int flagtplane, float[] v)
    {
        var At = v[4] - v[0];
        var Bt = v[5] - v[1];
        var Ct = v[6] - v[2];
        var Dt = v[7] - v[3];
        var t = At * Ct - Bt * Dt; //the "a" value.

        if ((i & 0x01) != 0) //false for i = 0 and 2, and true for i = 1 and 3
        {
            if (t <= 0.0f) return 0;
        }
        else
        {
            if (t >= 0.0f) return 0;
        }

        t = 0.5f * (v[3] * Bt + v[1] * Dt - v[2] * At - v[0] * Ct) / t; //t = -b/2a
        if (t <= 0.0f || t >= 1.0f)
            return 0;

        At = v[0] + At * t;
        Bt = v[1] + Bt * t;
        Ct = v[2] + Ct * t;
        Dt = v[3] + Dt * t;

        if ((i & 0x01) != 0)
        {
            if (At * Ct < Bt * Dt && Mathf.Approximately(Mathf.Sign(Bt), Mathf.Sign(Dt)))
                return (Mathf.Approximately(Mathf.Sign(Bt), Mathf.Sign(v[i]))) ? 1 : 0 + flagtplane;
        }
        else
        {
            if (At * Ct > Bt * Dt && Mathf.Approximately(Mathf.Sign(At), Mathf.Sign(Ct)))
                return (Mathf.Approximately(Mathf.Sign(At), Mathf.Sign(v[i]))) ? 1 : 0 + flagtplane;
        }

        return 0;
    }

    private int store_point_normal(float3 r, float3 n, Color c)
    {
        // 法線nを正規化する
#if MC_Normal_neg
		n = -math.normalize(n);
#else
        n = math.normalize(n);
#endif

        _MC_S.V.Add(r * _MC_D + _MC_O);
        _MC_S.N.Add(n);
        _MC_S.C.Add(c);
        _MC_S.nV++;
        return (_MC_S.nV - 1);
    }

    private void store_triangle(int3 t)
    {
        _MC_S.T.Add(t.x);
        _MC_S.T.Add(t.y);
        _MC_S.T.Add(t.z);
    }

    // グリッド点上の頂点を登録する
    private int surfint(int x, int y, int z, float3 r, float3 n)
    {
        r.x = x;
        r.y = y;
        r.z = z;
        // 共通メソッドを使って勾配計算
        n = GetGridGradient(x, y, z);
        return store_point_normal(r, n, Color.green);
    }

    /******************************************************************
    This function find the MC33 case...
    キャッシュ依存を削除し、すべての頂点を独立計算するバージョン。
    法線はGetGridGradientを用いた補間で求める。
    */
    private void find_case(int x, int y, int z, int i, float[] v)
    {
        var pcase = MC33LookUpTable.Case.Case_0;
        var caseIndex = 0;

        float t;
        var r = new float3();
        var n = new float3();
        int k, m, c;
        var f = new int[6];
        int[] p = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };

        m = i & 0x80;
        c = MC33LookUpTable.Case_Index[(m != 0) ? (i ^ 0xff) : i];
        switch (c >> 8) //find the MC33 case
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
                if (((m != 0 ? i : i ^ 0xFF) & face_test1((c & 0x7F) >> 1, v)) != 0)
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
                if (((m != 0 ? i : i ^ 0xFF) & face_test1((c & 0x7F) % 6, v)) != 0)
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
                switch (face_tests(f, i, (m != 0 ? 1 : -1), v))
                {
                    case -3:
                        pcase = MC33LookUpTable.Case.Case_7_1;
                        caseIndex = 3 * (c & 0x7F);
                        break;
                    case -1:
                        if (f[4] + f[5] == 1)
                        {
                            pcase = MC33LookUpTable.Case.Case_7_2_1;
                            caseIndex = 5 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = (f[(33825 >> ((c & 0x7F) << 1)) & 3] == 1
                                ? MC33LookUpTable.Case.Case_7_2_3
                                : MC33LookUpTable.Case.Case_7_2_2);
                            caseIndex = 5 * (c & 0x7F);
                        }

                        break;
                    case 1:
                        if (f[4] + f[5] == -1)
                        {
                            pcase = MC33LookUpTable.Case.Case_7_3_3;
                            caseIndex = 9 * (c & 0x7F);
                        }
                        else
                        {
                            pcase = (f[(33825 >> ((c & 0x7F) << 1)) & 3] == 1
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
                switch (face_tests(f, i, (m != 0 ? 1 : -1), v))
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
                        pcase = (f[4 >> ((c & 0x7F) << 1)] == 1
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
                switch (face_tests(f, i, (m != 0 ? 1 : -1), v))
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
                        pcase = (f[(int)MC33LookUpTable._12_test_index[2, (int)(c & 0x7F)]] == 1
                            ? MC33LookUpTable.Case.Case_12_2_2
                            : MC33LookUpTable.Case.Case_12_2_1);
                        caseIndex = 8 * (c & 0x7F);
                        break;
                }

                break;
            case 13:
                c = face_tests(f, i, (m != 0 ? 1 : -1), v);
                switch (Mathf.Abs(c))
                {
                    case 6:
                        pcase = MC33LookUpTable.Case.Case_13_1;
                        caseIndex = 4 * ((c > 0) ? 1 : 0);
                        break;
                    case 4:
                        c >>= 2;
                        i = 0;
                        while (f[i] != -c)
                            ++i;
                        pcase = MC33LookUpTable.Case.Case_13_2;
                        caseIndex = 6 * (3 * c + 3 + i);
                        i = 1;
                        break;
                    case 2:
                        c = (((((((((f[0] < 0) ? 1 : 0) << 1) | ((f[1] < 0) ? 1 : 0)) << 1) |
                                ((f[2] < 0) ? 1 : 0)) << 1) |
                              ((f[3] < 0) ? 1 : 0)) << 1) | ((f[4] < 0) ? 1 : 0);
                        pcase = MC33LookUpTable.Case.Case_13_3;
                        caseIndex = 10 * (25 - c + (((c > 10 ? 1 : 0) + (c > 20 ? 1 : 0)) << 1));
                        break;
                    case 0:
                        c = (((f[1] < 0) ? 1 : 0) << 1) | ((f[5] < 0) ? 1 : 0);
                        if (f[0] * f[1] * f[5] == 1)
                        {
                            pcase = MC33LookUpTable.Case.Case_13_4;
                            caseIndex = 12 * c;
                        }
                        else
                        {
                            i = interior_test(c, 1, v);
                            if (i != 0)
                            {
                                pcase = MC33LookUpTable.Case.Case_13_5_2;
                                caseIndex = 10 * (c | ((i & 1) << 2));
                            }
                            else
                            {
                                pcase = MC33LookUpTable.Case.Case_13_5_1;
                                caseIndex = 6 * c;
                                i = 1;
                            }
                        }

                        break;
                }

                break;
            case 14:
                pcase = MC33LookUpTable.Case.Case_14;
                caseIndex = (c & 0x7F);
                break;
        }

        var col = Color.white;
        while (i != 0)
        {
            i = MC33LookUpTable.LookUp(pcase, caseIndex++);
            for (k = 0; k < 3; k++)
            {
                c = i & 0x0F;
                if (p[c] < 0)
                {
                    float3 g0, g1;
                    switch (c)
                    {
                        case 0: // Edge 0-1 (Y axis)
                            if (v[0] == 0.0f) p[0] = surfint(x, y, z, r, n);
                            else if (v[1] == 0.0f) p[0] = surfint(x, y + 1, z, r, n);
                            else
                            {
                                t = v[0] / (v[0] - v[1]);
                                r.x = x;
                                r.z = z;
                                r.y = y + t;
                                g0 = GetGridGradient(x, y, z);
                                g1 = GetGridGradient(x, y + 1, z);
                                n = math.lerp(g0, g1, t);
                                p[0] = store_point_normal(r, n, col);
                            }
                            break;
                        case 1: // Edge 1-2 (Z axis)
                            if (v[1] == 0.0f) p[1] = surfint(x, y + 1, z, r, n);
                            else if (v[2] == 0.0f) p[1] = surfint(x, y + 1, z + 1, r, n);
                            else
                            {
                                t = v[1] / (v[1] - v[2]);
                                r.x = x;
                                r.y = y + 1;
                                r.z = z + t;
                                g0 = GetGridGradient(x, y + 1, z);
                                g1 = GetGridGradient(x, y + 1, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[1] = store_point_normal(r, n, col);
                            }
                            break;
                        case 2: // Edge 3-2 (Y axis) from (0,0,1) to (0,1,1) -> v[3] to v[2]
                            if (v[3] == 0.0f) p[2] = surfint(x, y, z + 1, r, n);
                            else if (v[2] == 0.0f) p[2] = surfint(x, y + 1, z + 1, r, n);
                            else
                            {
                                t = v[3] / (v[3] - v[2]);
                                r.x = x;
                                r.z = z + 1;
                                r.y = y + t;
                                g0 = GetGridGradient(x, y, z + 1);
                                g1 = GetGridGradient(x, y + 1, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[2] = store_point_normal(r, n, col);
                            }
                            break;
                        case 3: // Edge 0-3 (Z axis)
                            if (v[0] == 0.0f) p[3] = surfint(x, y, z, r, n);
                            else if (v[3] == 0.0f) p[3] = surfint(x, y, z + 1, r, n);
                            else
                            {
                                t = v[0] / (v[0] - v[3]);
                                r.x = x;
                                r.y = y;
                                r.z = z + t;
                                g0 = GetGridGradient(x, y, z);
                                g1 = GetGridGradient(x, y, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[3] = store_point_normal(r, n, col);
                            }
                            break;
                        case 4: // Edge 4-5 (Y axis)
                            if (v[4] == 0.0f) p[4] = surfint(x + 1, y, z, r, n);
                            else if (v[5] == 0.0f) p[4] = surfint(x + 1, y + 1, z, r, n);
                            else
                            {
                                t = v[4] / (v[4] - v[5]);
                                r.x = x + 1;
                                r.z = z;
                                r.y = y + t;
                                g0 = GetGridGradient(x + 1, y, z);
                                g1 = GetGridGradient(x + 1, y + 1, z);
                                n = math.lerp(g0, g1, t);
                                p[4] = store_point_normal(r, n, col);
                            }
                            break;
                        case 5: // Edge 5-6 (Z axis)
                            if (v[5] == 0.0f) p[5] = surfint(x + 1, y + 1, z, r, n);
                            else if (v[6] == 0.0f) p[5] = surfint(x + 1, y + 1, z + 1, r, n);
                            else
                            {
                                t = v[5] / (v[5] - v[6]);
                                r.x = x + 1;
                                r.y = y + 1;
                                r.z = z + t;
                                g0 = GetGridGradient(x + 1, y + 1, z);
                                g1 = GetGridGradient(x + 1, y + 1, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[5] = store_point_normal(r, n, col);
                            }
                            break;
                        case 6: // Edge 7-6 (Y axis)
                            if (v[7] == 0.0f) p[6] = surfint(x + 1, y, z + 1, r, n);
                            else if (v[6] == 0.0f) p[6] = surfint(x + 1, y + 1, z + 1, r, n);
                            else
                            {
                                t = v[7] / (v[7] - v[6]);
                                r.x = x + 1;
                                r.z = z + 1;
                                r.y = y + t;
                                g0 = GetGridGradient(x + 1, y, z + 1);
                                g1 = GetGridGradient(x + 1, y + 1, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[6] = store_point_normal(r, n, col);
                            }
                            break;
                        case 7: // Edge 4-7 (Z axis)
                            if (v[4] == 0.0f) p[7] = surfint(x + 1, y, z, r, n);
                            else if (v[7] == 0.0f) p[7] = surfint(x + 1, y, z + 1, r, n);
                            else
                            {
                                t = v[4] / (v[4] - v[7]);
                                r.x = x + 1;
                                r.y = y;
                                r.z = z + t;
                                g0 = GetGridGradient(x + 1, y, z);
                                g1 = GetGridGradient(x + 1, y, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[7] = store_point_normal(r, n, col);
                            }
                            break;
                        case 8: // Edge 0-4 (X axis)
                            if (v[0] == 0.0f) p[8] = surfint(x, y, z, r, n);
                            else if (v[4] == 0.0f) p[8] = surfint(x + 1, y, z, r, n);
                            else
                            {
                                t = v[0] / (v[0] - v[4]);
                                r.y = y;
                                r.z = z;
                                r.x = x + t;
                                g0 = GetGridGradient(x, y, z);
                                g1 = GetGridGradient(x + 1, y, z);
                                n = math.lerp(g0, g1, t);
                                p[8] = store_point_normal(r, n, col);
                            }
                            break;
                        case 9: // Edge 1-5 (X axis)
                            if (v[1] == 0.0f) p[9] = surfint(x, y + 1, z, r, n);
                            else if (v[5] == 0.0f) p[9] = surfint(x + 1, y + 1, z, r, n);
                            else
                            {
                                t = v[1] / (v[1] - v[5]);
                                r.y = y + 1;
                                r.z = z;
                                r.x = x + t;
                                g0 = GetGridGradient(x, y + 1, z);
                                g1 = GetGridGradient(x + 1, y + 1, z);
                                n = math.lerp(g0, g1, t);
                                p[9] = store_point_normal(r, n, col);
                            }
                            break;
                        case 10: // Edge 2-6 (X axis)
                            if (v[2] == 0.0f) p[10] = surfint(x, y + 1, z + 1, r, n);
                            else if (v[6] == 0.0f) p[10] = surfint(x + 1, y + 1, z + 1, r, n);
                            else
                            {
                                t = v[2] / (v[2] - v[6]);
                                r.y = y + 1;
                                r.z = z + 1;
                                r.x = x + t;
                                g0 = GetGridGradient(x, y + 1, z + 1);
                                g1 = GetGridGradient(x + 1, y + 1, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[10] = store_point_normal(r, n, col);
                            }
                            break;
                        case 11: // Edge 3-7 (X axis)
                            if (v[3] == 0.0f) p[11] = surfint(x, y, z + 1, r, n);
                            else if (v[7] == 0.0f) p[11] = surfint(x + 1, y, z + 1, r, n);
                            else
                            {
                                t = v[3] / (v[3] - v[7]);
                                r.y = y;
                                r.z = z + 1;
                                r.x = x + t;
                                g0 = GetGridGradient(x, y, z + 1);
                                g1 = GetGridGradient(x + 1, y, z + 1);
                                n = math.lerp(g0, g1, t);
                                p[11] = store_point_normal(r, n, col);
                            }
                            break;
                        case 12: // Interior center
                            r.x = x + 0.5f;
                            r.y = y + 0.5f;
                            r.z = z + 0.5f;
                            var sumG = GetGridGradient(x, y, z) + GetGridGradient(x + 1, y, z)
                                                                + GetGridGradient(x, y + 1, z) +
                                                                GetGridGradient(x + 1, y + 1, z)
                                                                + GetGridGradient(x, y, z + 1) +
                                                                GetGridGradient(x + 1, y, z + 1)
                                                                + GetGridGradient(x, y + 1, z + 1) +
                                                                GetGridGradient(x + 1, y + 1, z + 1);
                            n = sumG / 8.0f;
                            p[12] = store_point_normal(r, n, col);
                            break;
                    }
                }

                f[k] = p[c];
                i >>= 4;
            }

            if (f[0] != f[1] && f[0] != f[2] && f[1] != f[2])
            {
#if MC_Normal_neg
				if (m != 0)
#else
                if (m == 0)
#endif
                {
                    f[2] = f[0];
                    f[0] = p[c];
                }

                store_triangle(new int3(f[0], f[1], f[2]));
            }
        }
    }

    private Mesh calc_isosurface(float iso, float[] cells)
    {
        _F = cells;
        int x, y, z;
        var nx = _MCn.x;
        var ny = _MCn.y;
        var nz = _MCn.z;
        int i;

        var V = new float[8];
        float V00, V10, V01, V11;

        _MC_S = new surface();
        _MC_S.V = new NativeList<float3>(4096, Allocator.Temp);
        _MC_S.N = new NativeList<float3>(4096, Allocator.Temp);
        _MC_S.C = new NativeList<Color>(4096, Allocator.Temp);
        _MC_S.T = new List<int>();
        _MC_S.nV = 0;

        for (z = 0; z < nz; z++)
        {
            for (y = 0; y < ny; y++)
            {
                for (x = 0; x < nx; x++)
                {
                    V00 = GetCellValue(x, y, z);
                    V01 = GetCellValue(x, y + 1, z);
                    V10 = GetCellValue(x, y, z + 1);
                    V11 = GetCellValue(x, y + 1, z + 1);
                    V[0] = iso - V00;
                    V[1] = iso - V01;
                    V[2] = iso - V11;
                    V[3] = iso - V10;

                    V00 = GetCellValue(x + 1, y, z);
                    V01 = GetCellValue(x + 1, y + 1, z);
                    V10 = GetCellValue(x + 1, y, z + 1);
                    V11 = GetCellValue(x + 1, y + 1, z + 1);
                    V[4] = iso - V00;
                    V[5] = iso - V01;
                    V[6] = iso - V11;
                    V[7] = iso - V10;

                    i = ((V[0] >= 0f ? 1 : 0) << 7) |
                        ((V[1] >= 0f ? 1 : 0) << 6) |
                        ((V[2] >= 0f ? 1 : 0) << 5) |
                        ((V[3] >= 0f ? 1 : 0) << 4) |
                        ((V[4] >= 0f ? 1 : 0) << 3) |
                        ((V[5] >= 0f ? 1 : 0) << 2) |
                        ((V[6] >= 0f ? 1 : 0) << 1) |
                        ((V[7] >= 0f ? 1 : 0) << 0);

                    if (i != 0 && i != 0xff)
                    {
                        find_case(x, y, z, i, V);
                    }
                }
            }
        }

        var mesh = new Mesh();
        if (_MC_S.T.Count > 0)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_MC_S.V.AsArray());
            mesh.SetTriangles(_MC_S.T, 0);
            mesh.SetNormals(_MC_S.N.AsArray());
            mesh.SetColors(_MC_S.C.AsArray());
        }

        return mesh;
    }

    public Mesh calculate_isosurface(MC33Grid grd, float iso, float[] cells)
    {
        init_temp_isosurface(grd);
        return calc_isosurface(iso, cells);
    }
}
