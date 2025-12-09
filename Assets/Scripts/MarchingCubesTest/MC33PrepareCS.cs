using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class MC33PrepareCS
{
    // 大きすぎる頂点配列を受け入れられないレンダラー用にある程度のサイズに区切って保存するための定数
    private int _MC_N = 12; // 
    private int _MC_DVE = 1 << 12;  // 分割する単位
    private int _MC_A = (1<<12) - 1;    // マスク用定数

    /*_MCnT and _MCnV are the value of the first dimension of arrays T and V of the
    structure surface. They are used in store_point_normal and store_triangle.
    functions*/
    //_MCnT と _MCnV は、構造体 surface の配列 T と V の1次元目の値です。
    // これらは store_point_normal と store_triangle 関数で使用されます。
    private int _MCnT;
    private int _MCnV;
    
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
    
    // temporary structures that store the indices of triangle vertices:
    // 計算用の一時変数
    // これらが int なのはなぜなのか
    private int[][] _Ox;	//edges 8 (only read) and 9		８と９はXY平面上にY+方向に並んでいるエッジ		_Oxは_Nxより原点寄り
    private int[][] _Nx;	//edges 10 (only write) and 11	10と11はXY平面上にY+方向に並んでいるエッジ
    private int[][] _Oy;	//edges 0 (only read) and 4		０と４はXY平面上にX＋方向に並んでいるエッジ	_Oyは_Nyより原点寄り
    private int[][] _Ny;	//edges 2 and 6 (only write)	２と６はXY平面上にX＋方向に並んでいるエッジ
    private int[] _OL2;		//edges 3 (only read) and 7		３と７はXZ平面上にX＋方向に並んでいるエッジ	_OLは_NLより原点寄り
    private int[] _NL2;		//edges 1 and 5 (only write)	１と５はXZ平面上にX＋方向に並んでいるエッジ

    private float[] _F;

    private int _debugParam;

    // アクセス個所を炙り出すため一次変数の出し入れを関数化する
    private int GetOL(int x)
    {
	    return _OL2[x];
    }

    private int SetOL(int x, int value)
    {
	    _OL2[x] = value;
	    return value;
    }

    private int GetNL(int x)
    {
	    return _NL2[x];
    }

    private int SetNL(int x, int value)
    {
	    _NL2[x] = value;
	    return value;
    }
    
    private void init_temp_isosurface(MC33Grid grd)
    {
        _MCn = grd.N;
        _MC_O = grd.r0;
        _MC_D = grd.d;
        
        _OL2 = new int[_MCn.x + 1];
        _NL2 = new int[_MCn.x + 1];
        _Oy = new int[_MCn.y][];
        _Ny = new int[_MCn.y][];
        _Ox = new int[_MCn.y + 1][];
        _Nx = new int[_MCn.y + 1][];

        for (var i = 0; i < _MCn.y; i++)
        {
            _Ox[i] = new int[_MCn.x];
            _Nx[i] = new int[_MCn.x];
            _Oy[i] = new int[_MCn.x + 1];
            _Ny[i] = new int[_MCn.x + 1];
        }
        _Ox[_MCn.y] = new int[_MCn.x];
        _Nx[_MCn.y] = new int[_MCn.x];
    }
    
    private float GetCellValue(int x, int y, int z)
    {
        var nx = _MCn.x + 1;
        var ny = _MCn.y + 1;
        return _F[x + y * nx + z * nx * ny];
    }

/******************************************************************
Vertices:           Faces:
    3 __________2        ___________
   /|          /|      /|          /|
  / |         / |     / |   2     / |
7/__________6/  |    /  |     4  /  |
|   |       |   |   |�����������| 1 |     z
|   0_______|___1   | 3 |_______|___|     |
|  /        |  /    |  /  5     |  /      |____y
| /         | /     | /     0   | /      /
4/__________5/      |/__________|/      x


This function return a vector with all six test face results (face[6]). Each
result value is 1 if the positive face vertices are joined, -1 if the negative
vertices are joined, and 0 (unchanged) if the test must no be applied. The
return value of this function is the the sum of all six results.
この関数は、6つのテスト面の結果（face[6]）すべてを格納したベクトルを返します。
それぞれの結果値は、正の面頂点が結合されている場合は1、負の面頂点が結合されている場合は-1、
テストを適用しない場合は0（変更なし）となります。
この関数の戻り値は、6つの結果の合計です。
*/
	private int face_tests(int[] face, int ind, int sw, float[] v)
	{
		if((ind&0x80) != 0)//vertex 0
		{
			face[0] = ((ind&0xCC) == 0x84? (v[0]*v[5] < v[1]*v[4]? -sw: sw): 0);//0x84 = 10000100, vertices 0 and 5
			face[3] = ((ind&0x99) == 0x81? (v[0]*v[7] < v[3]*v[4]? -sw: sw): 0);//0x81 = 10000001, vertices 0 and 7
			face[4] = ((ind&0xF0) == 0xA0? (v[0]*v[2] < v[1]*v[3]? -sw: sw): 0);//0xA0 = 10100000, vertices 0 and 2
		}
		else
		{
			face[0] = ((ind&0xCC) == 0x48? (v[0]*v[5] < v[1]*v[4]? sw: -sw): 0);//0x48 = 01001000, vertices 1 and 4
			face[3] = ((ind&0x99) == 0x18? (v[0]*v[7] < v[3]*v[4]? sw: -sw): 0);//0x18 = 00011000, vertices 3 and 4
			face[4] = ((ind&0xF0) == 0x50? (v[0]*v[2] < v[1]*v[3]? sw: -sw): 0);//0x50 = 01010000, vertices 1 and 3
		}
		if((ind&0x02) != 0)//vertex 6
		{
			face[1] = ((ind&0x66) == 0x42? (v[1]*v[6] < v[2]*v[5]? -sw: sw): 0);//0x42 = 01000010, vertices 1 and 6
			face[2] = ((ind&0x33) == 0x12? (v[3]*v[6] < v[2]*v[7]? -sw: sw): 0);//0x12 = 00010010, vertices 3 and 6
			face[5] = ((ind&0x0F) == 0x0A? (v[4]*v[6] < v[5]*v[7]? -sw: sw): 0);//0x0A = 00001010, vertices 4 and 6
		}
		else
		{
			face[1] = ((ind&0x66) == 0x24? (v[1]*v[6] < v[2]*v[5]? sw: -sw): 0);//0x24 = 00100100, vertices 2 and 5
			face[2] = ((ind&0x33) == 0x21? (v[3]*v[6] < v[2]*v[7]? sw: -sw): 0);//0x21 = 00100001, vertices 2 and 7
			face[5] = ((ind&0x0F) == 0x05? (v[4]*v[6] < v[5]*v[7]? sw: -sw): 0);//0x05 = 00000101, vertices 5 and 7
		}
		return face[0] + face[1] + face[2] + face[3] + face[4] + face[5];
	}
    
    
/* Faster function for the face test, the test is applied to only one face
(int face). This function is only used for the cases 3 and 6 of MC33*/
/* 面テストの高速化関数。テストは1つの面(int face)のみに適用されます。この関数はMC33のケース3と6でのみ使用されます*/
	private int face_test1(int face, float[] v)
	{
		switch (face)
		{
			case 0:
				return (v[0] * v[5] < v[1] * v[4] ? 0x48 : 0x84);
			case 1:
				return (v[1] * v[6] < v[2] * v[5] ? 0x24 : 0x42);
			case 2:
				return (v[3] * v[6] < v[2] * v[7] ? 0x21 : 0x12);
			case 3:
				return (v[0] * v[7] < v[3] * v[4] ? 0x18 : 0x81);
			case 4:
				return (v[0] * v[2] < v[1] * v[3] ? 0x50 : 0xA0);
			case 5:
				return (v[4] * v[6] < v[5] * v[7] ? 0x05 : 0x0A);
		}

		return 0;
	}

	private bool signbf(float f)
	{
		return f < 0.0f;
	}

/******************************************************************
Interior test function. If the test is positive, the function returns a value
different fom 0. The integer i must be 0 to test if the vertices 0 and 6 are
joined. 1 for vertices 1 and 7, 2 for vertices 2 and 4, and 3 for 3 and 5.
For case 13, the integer flagtplane must be 1, and the function returns 2 if
one of the vertices 0, 1, 2 or 3 is joined to the center point of the cube
(case 13.5.2), returns 1 if one of the vertices 4, 5, 6 or 7 is joined to the
center point of the cube (case 13.5.2 too), and it returns 0 if the vertices
are no joined (case 13.5.1)
内部テスト関数。テストが肯定的であれば、関数は0とは異なる値を返します。
頂点0と6が結合されているかどうかをテストするには、整数iは0でなければなりません。
頂点1と7が結合されている場合は1、頂点2と4が結合されている場合は2、頂点3と5が結合されている場合は3です。
ケース13の場合、整数flagtplaneは1でなければなりません。
関数は、頂点0、1、2、または3のいずれかが立方体の中心点に結合されている場合は2を返し（ケース13.5.2）、
頂点4、5、6、または7のいずれかが立方体の中心点に結合されている場合は1を返し（ケース13.5.2も同様）、
頂点が結合されていない場合は0を返します（ケース13.5.1）。
*/
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

	// rとnはオリジナルではポインタ渡しだが、C#ではstructの値渡しにした。
	// オリジナルでもr,nの中身は戻り先で使ってないように見えるから要らなくないか？
	private int surfint(int x, int y, int z, float3 r, float3 n)
	{
		// グリッド境界部分の頂点と法線を求める
		r.x = x;
		r.y = y;
		r.z = z;
		if (x == 0)
			n.x = GetCellValue(0, y, z) - GetCellValue(1, y, z);
		else if (x == _MCn.x)
			n.x = GetCellValue(x - 1, y, z) - GetCellValue(x, y, z);
		else
			n.x = 0.5f * (GetCellValue(x - 1, y, z) - GetCellValue(x + 1, y, z));

		if (y == 0)
			n.y = GetCellValue(x, 0, z) - GetCellValue(x, 1, z);
		else if (y == _MCn.y)
			n.y = GetCellValue(x, y - 1, z) - GetCellValue(x, y, z);
		else
			n.y = 0.5f * (GetCellValue(x, y - 1, z) - GetCellValue(x, y + 1, z));

		if (z == 0)
			n.z = GetCellValue(x, y, 0) - GetCellValue(x, y, 1);
		else if (z == _MCn.z)
			n.z = GetCellValue(x, y, z - 1) - GetCellValue(x, y, z);
		else
			n.z = 0.5f * (GetCellValue(x, y, z - 1) - GetCellValue(x, y, z + 1));

		return store_point_normal(r, n, Color.green);
	}

/******************************************************************
This function find the MC33 case (using the index i, and the face and interior
tests). The correct triangle pattern is obtained from the arrays contained in
the MC33_LookUpTable.h file. The necessary vertices (intersection points) are
also calculated here (using trilinear interpolation).
この関数は、MC33 ケースを検索します（インデックス i と面および内部テストを使用）。
正しい三角形パターンは、MC33_LookUpTable.h ファイルに含まれる配列から取得されます。
必要な頂点（交点）もここで計算されます（三線補間を使用）。

       _____2_____
     /|          /|
   11 |<-3     10 |
   /____6_____ /  1     z
  |   |       |   |     |
  |   |_____0_|___|     |____y
  7  /        5  /     /
  | 8         | 9     x
  |/____4_____|/

The temporary matrices: _Ox, _Oy, _Nx and _Ny, and vectors: _OL and _NL are filled
and used here.
一時行列_Ox、_Oy、_Nx、_Nyとベクトル_OL、_NLが
入力され、ここで使用されます。
*/
	private void find_case(int x, int y, int z, int i, float[] v)
	{
		// const unsigned short int *pcase;
		var pcase = MC33LookUpTable.Case.Case_0;
		var caseIndex = 0;

		float t;
		var r = new float3();
		var n = new float3();
		int k, m, c;
		var f = new int[6];	//この値は別のグリッドに持ち越されない。最終的に[0][1][2]に頂点インデックスが入る
		int[] p = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };

		m = i & 0x80;
		c = MC33LookUpTable.Case_Index[(m != 0) ? (i ^ 0xff) : i];
		switch (c >> 8) //find the MC33 case
		{
			case 1: //********************************************
				if ((c & 0x0080) != 0) m ^= 0x80;
				pcase = MC33LookUpTable.Case.Case_1;
				caseIndex = c & 0x7F;
				break;
			case 2: //********************************************
				if ((c & 0x0080) != 0) m ^= 0x80;
				pcase = MC33LookUpTable.Case.Case_2;
				caseIndex = c & 0x7F;
				break;
			case 3: //********************************************
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
			case 4: //********************************************
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
			case 5: //********************************************
				if ((c & 0x0080) != 0) m ^= 0x80;
				pcase = MC33LookUpTable.Case.Case_5;
				caseIndex = c & 0x7F;
				break;
			case 6: //********************************************
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
			case 7: //********************************************
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
			case 8: //********************************************
				pcase = MC33LookUpTable.Case.Case_8;
				caseIndex = (c & 0x7F);
				break;
			case 9: //********************************************
				pcase = MC33LookUpTable.Case.Case_9;
				caseIndex = (c & 0x7F);
				break;
			case 10: //********************************************
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
			case 11: //********************************************
				pcase = MC33LookUpTable.Case.Case_11;
				caseIndex = (c & 0x7F);
				break;
			case 12: //********************************************
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
			case 13: //********************************************
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
			case 14: //********************************************
				pcase = MC33LookUpTable.Case.Case_14;
				caseIndex = (c & 0x7F);
				break;
		}

		// int p[]が使われるのはここ以下
		while (i != 0)
		{
			i = MC33LookUpTable.LookUp(pcase, caseIndex++);
			for (k = 0; k < 3; k++)
			{
				c = i & 0x0F;
				if (p[c] < 0)
				{
					var col = c == _debugParam ? Color.red : Color.white;
					switch (c)
					{
						case 0:
							if (z != 0 || x != 0)
							{
								p[0] = _Oy[y][x];
							}
							else
							{
								if (v[0] == 0.0f)
								{
									p[0] = surfint(0, y, 0, r, n);
									if (signbf(v[3])) p[3] = p[0];
									if (signbf(v[4])) p[8] = p[0];
								}
								else if (v[1] == 0.0f)
								{
									p[0] = surfint(0, y + 1, 0, r, n);
									// if(signbf(v[2])) _NL[0] = p[1] = p[0];
									if (signbf(v[2])) SetNL(0, p[1] = p[0]);
									if (signbf(v[5])) _Ox[y + 1][0] = p[9] = p[0];
								}
								else
								{
									t = v[0] / (v[0] - v[1]);
									r.x = r.z = 0.0f;
									r.y = y + t;
									n.x = (v[4] - v[0]) * (1.0f - t) + (v[5] - v[1]) * t;
									n.y = v[1] - v[0];
									n.z = (v[3] - v[0]) * (1.0f - t) + (v[2] - v[1]) * t;
									// p[0] = store_point_normal(r,n);
									p[0] = store_point_normal(r, n, col);
								}
							}

							break;
						case 1:
							if (x != 0)
							{
								p[1] = GetNL(x);
							}
							else
							{
								if (v[1] == 0.0f)
								{
									SetNL(0, p[1] = surfint(0, y + 1, z, r, n));
									if (signbf(v[0])) p[0] = p[1];
									if (signbf(v[5]))
									{
										p[9] = p[1];
										if (z == 0) _Ox[y + 1][0] = p[9];
									}
								}
								else if (v[2] == 0.0f)
								{
									SetNL(0, p[1] = surfint(0, y + 1, z + 1, r, n));
									if (signbf(v[3])) _Ny[y][0] = p[2] = p[1];
									if (signbf(v[6])) _Nx[y + 1][0] = p[10] = p[1];
								}
								else
								{
									t = v[1] / (v[1] - v[2]);
									r.x = 0.0f;
									r.y = y + 1;
									r.z = z + t;
									n.x = (v[5] - v[1]) * (1.0f - t) + (v[6] - v[2]) * t;
									n.y = (y + 1 < _MCn.y
										? 0.5f * ((GetCellValue(0, y, z) - GetCellValue(0, y + 2, z)) * (1.0f - t) +
										          (GetCellValue(0, y, z + 1) - GetCellValue(0, y + 2, z + 1)) * t)
										: (v[1] - v[0]) * (1.0f - t) + (v[2] - v[3]) * t);
									n.z = v[2] - v[1];
									SetNL(0, p[1] = store_point_normal(r, n, col));
								}
							}

							break;
						case 2:
							if (x != 0)
							{
								p[2] = _Ny[y][x];
							}
							else
							{
								if (v[3] == 0.0f)
								{
									_Ny[y][0] = p[2] = surfint(0, y, z + 1, r, n);
									if (signbf(v[0])) p[3] = p[2];
									if (signbf(v[7]))
									{
										p[11] = p[2];
										if (y == 0) _Nx[0][0] = p[11];
									}
								}
								else if (v[2] == 0.0f)
								{
									_Ny[y][0] = p[2] = surfint(0, y + 1, z + 1, r, n);
									if (signbf(v[1])) SetNL(0, p[1] = p[2]);
									if (signbf(v[6])) _Nx[y + 1][0] = p[10] = p[2];
								}
								else
								{
									t = v[3] / (v[3] - v[2]);
									r.x = 0.0f;
									r.z = z + 1;
									r.y = y + t;
									n.x = (v[7] - v[3]) * (1.0f - t) + (v[6] - v[2]) * t;
									n.y = v[2] - v[3];
									n.z = (z + 1 < _MCn.z
										? 0.5f * ((GetCellValue(0, y, z) - GetCellValue(0, y, z + 2)) * (1.0f - t)
										          + (GetCellValue(0, y + 1, z) - GetCellValue(0, y + 1, z + 2)) * t)
										: (v[3] - v[0]) * (1.0f - t) + (v[2] - v[1]) * t);
									_Ny[y][0] = p[2] = store_point_normal(r, n, col);
								}
							}

							break;
						case 3:
							if (y != 0 || x != 0)
							{
								p[3] = GetOL(x);
							}
							else
							{
								if (v[0] == 0.0f)
								{
									p[3] = surfint(0, 0, z, r, n);
									if (signbf(v[1])) p[0] = p[3];
									if (signbf(v[4])) p[8] = p[3];
								}
								else if (v[3] == 0.0f)
								{
									p[3] = surfint(0, 0, z + 1, r, n);
									if (signbf(v[2])) _Ny[0][0] = p[2] = p[3];
									if (signbf(v[7])) _Nx[0][0] = p[11] = p[3];
								}
								else
								{
									t = v[0] / (v[0] - v[3]);
									r.x = r.y = 0.0f;
									r.z = z + t;
									n.x = (v[4] - v[0]) * (1.0f - t) + (v[7] - v[3]) * t;
									n.y = (v[1] - v[0]) * (1.0f - t) + (v[2] - v[3]) * t;
									n.z = v[3] - v[0];
									p[3] = store_point_normal(r, n, col);
								}
							}

							break;
						case 4:
							if (z != 0)
							{
								p[4] = _Oy[y][x + 1];
							}
							else
							{
								if (v[4] == 0.0f)
								{
									_Oy[y][x + 1] = p[4] = surfint(x + 1, y, 0, r, n);
									if (signbf(v[7])) p[7] = p[4];
									if (signbf(v[0])) p[8] = p[4];
									if (y == 0)
										SetOL(x + 1, p[7]);
								}
								else if (v[5] == 0.0f)
								{
									_Oy[y][x + 1] = p[4] = surfint(x + 1, y + 1, 0, r, n);
									if (signbf(v[6])) SetNL(x + 1, p[5] = p[4]);
									if (signbf(v[1])) _Ox[y + 1][x] = p[9] = p[4];
								}
								else
								{
									t = v[4] / (v[4] - v[5]);
									r.x = x + 1;
									r.z = 0.0f;
									r.y = y + t;
									n.x = (x + 1 < _MCn.x
										? 0.5f * ((GetCellValue(x, y, 0) - GetCellValue(x + 2, y, 0)) * (1.0f - t)
										          + (GetCellValue(x, y + 1, 0) - GetCellValue(x + 2, y + 1, 0)) * t)
										: (v[4] - v[0]) * (1.0f - t) + (v[5] - v[1]) * t);
									n.y = v[5] - v[4];
									n.z = (v[7] - v[4]) * (1.0f - t) + (v[6] - v[5]) * t;
									_Oy[y][x + 1] = p[4] = store_point_normal(r, n, col);
								}
							}

							break;
						case 5:
							if (v[5] == 0.0f)
							{
								if (signbf(v[4]))
								{
									if (z != 0)
									{
										SetNL(x + 1, p[5] = p[4] = _Oy[y][x + 1]);
										if (signbf(v[1])) p[9] = p[5];
									}
									else
									{
										SetNL(x + 1, p[5] = _Oy[y][x + 1] = p[4] = surfint(x + 1, y + 1, 0, r, n));
										if (signbf(v[1])) _Ox[y + 1][x] = p[9] = p[5];
									}
								}
								else if (signbf(v[1]))
								{
									if (z != 0)
										SetNL(x + 1, p[5] = p[9] = _Ox[y + 1][x]);
									else
										SetNL(x + 1, p[5] = _Ox[y + 1][x] = p[9] = surfint(x + 1, y + 1, 0, r, n));
								}
								else
									SetNL(x + 1, p[5] = surfint(x + 1, y + 1, z, r, n));
							}
							else if (v[6] == 0.0f)
							{
								SetNL(x + 1, p[5] = surfint(x + 1, y + 1, z + 1, r, n));
								if (signbf(v[2])) _Nx[y + 1][x] = p[10] = p[5];
								if (signbf(v[7])) _Ny[y][x + 1] = p[6] = p[5];
							}
							else
							{
								t = v[5] / (v[5] - v[6]);
								r.x = x + 1;
								r.y = y + 1;
								r.z = z + t;
								n.x = (x + 1 < _MCn.x
									? 0.5f * ((GetCellValue(x, y + 1, z) - GetCellValue(x + 2, y + 1, z)) * (1.0f - t)
									          + (GetCellValue(x, y + 1, z + 1) - GetCellValue(x + 2, y + 1, z + 1)) * t)
									: (v[5] - v[1]) * (1.0f - t) + (v[6] - v[2]) * t);
								n.y = (y + 1 < _MCn.y
									? 0.5f * ((GetCellValue(x + 1, y, z) - GetCellValue(x + 1, y + 2, z)) * (1.0f - t)
									          + (GetCellValue(x + 1, y, z + 1) - GetCellValue(x + 1, y + 2, z + 1)) * t)
									: (v[5] - v[4]) * (1.0f - t) + (v[6] - v[7]) * t);
								n.z = v[6] - v[5];
								SetNL(x + 1, p[5] = store_point_normal(r, n, col));
							}

							break;
						case 6:
							if (v[7] == 0.0f)
							{
								if (signbf(v[3]))
								{
									if (y != 0)
									{
										_Ny[y][x + 1] = p[6] = p[11] = _Nx[y][x];
										if (signbf(v[4])) p[7] = p[6];
									}
									else
									{
										_Ny[y][x + 1] = p[6] = _Nx[0][x] = p[11] = surfint(x + 1, 0, z + 1, r, n);
										if (signbf(v[4])) SetOL(x + 1, p[7] = p[6]);
									}
								}
								else if (signbf(v[4]))
								{
									if (y != 0)
										_Ny[y][x + 1] = p[6] = p[7] = GetOL(x + 1);
									else
										_Ny[y][x + 1] = p[6] = SetOL(x + 1, p[7] = surfint(x + 1, 0, z + 1, r, n));
								}
								else
									_Ny[y][x + 1] = p[6] = surfint(x + 1, y, z + 1, r, n);
							}
							else if (v[6] == 0.0f)
							{
								_Ny[y][x + 1] = p[6] = surfint(x + 1, y + 1, z + 1, r, n);
								if (signbf(v[5])) SetNL(x + 1, p[5] = p[6]);
								if (signbf(v[2])) _Nx[y + 1][x] = p[10] = p[6];
							}
							else
							{
								t = v[7] / (v[7] - v[6]);
								r.x = x + 1;
								r.z = z + 1;
								r.y = y + t;
								n.x = (x + 1 < _MCn.x
									? 0.5f * ((GetCellValue(x, y, z + 1) - GetCellValue(x + 2, y, z + 1)) * (1.0f - t)
									          + (GetCellValue(x, y + 1, z + 1) - GetCellValue(x + 2, y + 1, z + 1)) * t)
									: (v[7] - v[3]) * (1.0f - t) + (v[6] - v[2]) * t);
								n.y = v[6] - v[7];
								n.z = (z + 1 < _MCn.z
									? 0.5f * ((GetCellValue(x + 1, y, z) - GetCellValue(x + 1, y, z + 2)) * (1.0f - t)
									          + (GetCellValue(x + 1, y + 1, z) - GetCellValue(x + 1, y + 1, z + 2)) * t)
									: (v[7] - v[4]) * (1.0f - t) + (v[6] - v[5]) * t);
								_Ny[y][x + 1] = p[6] = store_point_normal(r, n, col);
							}

							break;
						case 7:
							if (y != 0)
							{
								p[7] = GetOL(x + 1);
							}
							else
							{
								if (v[4] == 0.0f)
								{
									SetOL(x + 1, p[7] = surfint(x + 1, 0, z, r, n));
									if (signbf(v[0])) p[8] = p[7];
									if (signbf(v[5]))
									{
										p[4] = p[7];
										if (z == 0)
											_Oy[0][x + 1] = p[4];
									}
								}
								else if (v[7] == 0.0f)
								{
									SetOL(x + 1, p[7] = surfint(x + 1, 0, z + 1, r, n));
									if (signbf(v[6])) _Ny[0][x + 1] = p[6] = p[7];
									if (signbf(v[3])) _Nx[0][x] = p[11] = p[7];
								}
								else
								{
									t = v[4] / (v[4] - v[7]);
									r.x = x + 1;
									r.y = 0.0f;
									r.z = z + t;
									n.x = (x + 1 < _MCn.x
										? 0.5f * ((GetCellValue(x, 0, z) - GetCellValue(x + 2, 0, z)) * (1.0f - t)
										          + (GetCellValue(x, 0, z + 1) - GetCellValue(x + 2, 0, z + 1)) * t)
										: (v[4] - v[0]) * (1.0f - t) + (v[7] - v[3]) * t);
									n.y = (v[5] - v[4]) * (1.0f - t) + (v[6] - v[7]) * t;
									n.z = v[7] - v[4];
									SetOL(x + 1, p[7] = store_point_normal(r, n, col));
								}
							}

							break;
						case 8:
							if (z != 0 || y != 0)
							{
								p[8] = _Ox[y][x];
							}
							else
							{
								if (v[0] == 0.0f)
								{
									p[8] = surfint(x, 0, 0, r, n);
									if (signbf(v[1])) p[0] = p[8];
									if (signbf(v[3])) p[3] = p[8];
								}
								else if (v[4] == 0.0f)
								{
									p[8] = surfint(x + 1, 0, 0, r, n);
									if (signbf(v[5]))
										_Oy[0][x + 1] = p[4] = p[8];
									if (signbf(v[7]))
										SetOL(x + 1, p[7] = p[8]);
								}
								else
								{
									t = v[0] / (v[0] - v[4]);
									r.y = r.z = 0.0f;
									r.x = x + t;
									n.x = v[4] - v[0];
									n.y = (v[1] - v[0]) * (1.0f - t) + (v[5] - v[4]) * t;
									n.z = (v[3] - v[0]) * (1.0f - t) + (v[7] - v[4]) * t;
									p[8] = store_point_normal(r, n, col);
								}
							}

							break;
						case 9:
							if (z != 0)
							{
								p[9] = _Ox[y + 1][x];
							}
							else
							{
								if (v[1] == 0.0f)
								{
									_Ox[y + 1][x] = p[9] = surfint(x, y + 1, 0, r, n);
									if (signbf(v[2]))
									{
										p[1] = p[9];
										if (x == 0) SetNL(0, p[1]);
									}

									if (signbf(v[0])) p[0] = p[9];
								}
								else if (v[5] == 0.0f)
								{
									_Ox[y + 1][x] = p[9] = surfint(x + 1, y + 1, 0, r, n);
									if (signbf(v[6])) SetNL(x + 1, p[5] = p[9]);
									if (signbf(v[4])) _Oy[y][x + 1] = p[4] = p[9];
								}
								else
								{
									t = v[1] / (v[1] - v[5]);
									r.y = y + 1;
									r.z = 0.0f;
									r.x = x + t;
									n.x = v[5] - v[1];
									n.y = (y + 1 < _MCn.y
										? 0.5f * ((GetCellValue(x, y, 0) - GetCellValue(x, y + 2, 0)) * (1.0f - t)
										          + (GetCellValue(x + 1, y, 0) - GetCellValue(x + 1, y + 2, 0)) * t)
										: (v[1] - v[0]) * (1.0f - t) + (v[5] - v[4]) * t);
									n.z = (v[2] - v[1]) * (1.0f - t) + (v[6] - v[5]) * t;
									_Ox[y + 1][x] = p[9] = store_point_normal(r, n, col);
								}
							}

							break;
						case 10:
							if (v[2] == 0.0f)
							{
								if (signbf(v[1]))
								{
									if (x != 0)
									{
										_Nx[y + 1][x] = p[10] = p[1] = GetNL(x);
										if (signbf(v[3])) p[2] = p[10];
									}
									else
									{
										_Nx[y + 1][0] = p[10] = SetNL(0, p[1] = surfint(0, y + 1, z + 1, r, n));
										if (signbf(v[3])) _Ny[y][0] = p[2] = p[10];
									}
								}
								else if (signbf(v[3]))
								{
									if (x != 0)
										_Nx[y + 1][x] = p[10] = p[2] = _Ny[y][x];
									else
										_Nx[y + 1][0] = p[10] = _Ny[y][0] = p[2] = surfint(0, y + 1, z + 1, r, n);
								}
								else
									_Nx[y + 1][x] = p[10] = surfint(x, y + 1, z + 1, r, n);
							}
							else if (v[6] == 0.0f)
							{
								_Nx[y + 1][x] = p[10] = surfint(x + 1, y + 1, z + 1, r, n);
								if (signbf(v[5])) SetNL(x + 1, p[5] = p[10]);
								if (signbf(v[7])) _Ny[y][x + 1] = p[6] = p[10];
							}
							else
							{
								t = v[2] / (v[2] - v[6]);
								r.y = y + 1;
								r.z = z + 1;
								r.x = x + t;
								n.x = v[6] - v[2];
								n.y = (y + 1 < _MCn.y
									? 0.5f * ((GetCellValue(x, y, z + 1) - GetCellValue(x, y + 2, z + 1)) * (1.0f - t)
									          + (GetCellValue(x + 1, y, z + 1) - GetCellValue(x + 1, y + 2, z + 1)) * t)
									: (v[2] - v[3]) * (1.0f - t) + (v[6] - v[7]) * t);
								n.z = (z + 1 < _MCn.z
									? 0.5f * ((GetCellValue(x, y + 1, z) - GetCellValue(x, y + 1, z + 2)) * (1.0f - t)
									          + (GetCellValue(x + 1, y + 1, z) - GetCellValue(x + 1, y + 1, z + 2)) * t)
									: (v[2] - v[1]) * (1.0f - t) + (v[6] - v[5]) * t);
								_Nx[y + 1][x] = p[10] = store_point_normal(r, n, col);
							}

							break;
						case 11:
							if (y != 0)
							{
								p[11] = _Nx[y][x];
							}
							else
							{
								if (v[3] == 0.0f)
								{
									_Nx[0][x] = p[11] = surfint(x, 0, z + 1, r, n);
									if (signbf(v[0])) p[3] = p[11];
									if (signbf(v[2]))
									{
										p[2] = p[11];
										if (x == 0)
											_Ny[0][0] = p[2];
									}
								}
								else if (v[7] == 0.0f)
								{
									_Nx[0][x] = p[11] = surfint(x + 1, 0, z + 1, r, n);
									if (signbf(v[4])) SetOL(x + 1, p[7] = p[11]);
									if (signbf(v[6])) _Ny[0][x + 1] = p[6] = p[11];
								}
								else
								{
									t = v[3] / (v[3] - v[7]);
									r.y = 0.0f;
									r.z = z + 1;
									r.x = x + t;
									n.x = v[7] - v[3];
									n.y = (v[2] - v[3]) * (1.0f - t) + (v[6] - v[7]) * t;
									n.z = (z + 1 < _MCn.z
										? 0.5f * ((GetCellValue(x, 0, z) - GetCellValue(x, 0, z + 2)) * (1.0f - t)
										          + (GetCellValue(x + 1, 0, z) - GetCellValue(x + 1, 0, z + 2)) * t)
										: (v[3] - v[0]) * (1.0f - t) + (v[7] - v[4]) * t);
									_Nx[0][x] = p[11] = store_point_normal(r, n, col);
								}
							}

							break;
						case 12:
							r.x = x + 0.5f;
							r.y = y + 0.5f;
							r.z = z + 0.5f;
							n.x = v[4] + v[5] + v[6] + v[7] - v[0] - v[1] - v[2] - v[3];
							n.y = v[1] + v[2] + v[5] + v[6] - v[0] - v[3] - v[4] - v[7];
							n.z = v[2] + v[3] + v[6] + v[7] - v[0] - v[1] - v[4] - v[5];
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
	                V[0] = iso - V00;	// V[4]から値を入れる
	                V[1] = iso - V01;
	                V[2] = iso - V11;
	                V[3] = iso - V10;
	                //the eight least significant bits of i correspond to vertex indices. (x...x01234567)
	                //If the bit is 1 then the vertex value is greater than zero.
	                //i の下位8ビットは頂点インデックスに対応します。(x...x01234567)
	                //ビットが1の場合、頂点の値は0より大きいです。
	                i = ((V[0] >= 0f ? 1 : 0) << 3) |
	                    ((V[1] >= 0f ? 1 : 0) << 2) |
	                    ((V[2] >= 0f ? 1 : 0) << 1) |
	                    ((V[3] >= 0f ? 1 : 0) << 0);
	                
                    V00 = GetCellValue(x + 1, y, z);
                    V01 = GetCellValue(x + 1, y + 1, z);
                    V10 = GetCellValue(x + 1, y, z + 1);
                    V11 = GetCellValue(x + 1, y + 1, z + 1);
                    V[4] = iso - V00;	// V[0]から値を入れる
                    V[5] = iso - V01;
                    V[6] = iso - V11;
                    V[7] = iso - V10;
                    
                    i = (i << 4) & 0xFF; // shift left 4 bits and keep only the last 8 bits
                    i |= ((V[4] >= 0f ? 1 : 0) << 3) |
                         ((V[5] >= 0f ? 1 : 0) << 2) |
                         ((V[6] >= 0f ? 1 : 0) << 1) |
                         ((V[7] >= 0f ? 1 : 0) << 0);

                    if (i != 0 && i != 0xff)
                    {
                        find_case(x,y,z,i,V);
                    }
                }

                (_OL2, _NL2) = (_NL2, _OL2);
            }
            (_Ox, _Nx) = (_Nx, _Ox);
            (_Oy, _Ny) = (_Ny, _Oy);
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
    
    public Mesh calculate_isosurface(MC33Grid grd, float iso, float[] cells, int debugParam)
    {
	    _debugParam = debugParam;
	    
        init_temp_isosurface(grd);
        return calc_isosurface(iso, cells);
    }
    
}
