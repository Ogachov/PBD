using Unity.Mathematics;
using UnityEngine;

public struct MC33Grid
{
    // オリジナルにはここに3次元のfloat配列Fがあるが、ComputeBufferで渡すため省略
    public int3 N;  // 各次元ごとのセル数（頂点数は次元ごとに+1存在する）
    public float3 L;    // 各次元ごとのグリッドサイズ（ソースを読む必要はあるがたぶん未使用）
    public float3 r0;   // グリッドの原点位置
    public float3 d;    // 各次元ごとの隣接点間の距離
}