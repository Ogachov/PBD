#if !defined(MC33CS_HLSL)
#define MC33CS_HLSL

// #define USE_OCT_ENCODED_NORMAL

struct MCVertex
{
    // xyz:position, w:octahederal encoded normal(R16G16)
    float4 position;
    float4 normal;
    float4 color;
};

// float4のカラーをRGBA8のuintにパック/アンパックする関数
uint PackRGBA8(float4 c)
{
    c = saturate(c);
    uint4 b = (uint4)round(c * 255.0);
    return (b.x) | (b.y << 8) | (b.z << 16) | (b.w << 24);
}

float4 UnpackRGBA8(uint p)
{
    float4 b = float4(
        (p      ) & 255,
        (p >>  8) & 255,
        (p >> 16) & 255,
        (p >> 24) & 255
    );
    return b / 255.0;
}

// Octahedral encoding: unit normal -> 2D
float2 OctEncode(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z) + 1e-8);

    float2 e = n.xy;
    if (n.z < 0.0)
        e = (1.0 - abs(e.yx)) * float2(e.x >= 0 ? 1 : -1, e.y >= 0 ? 1 : -1);

    return e; // [-1,1]
}

float3 OctDecode(float2 e)
{
    float3 n = float3(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = (1.0 - abs(n.yx)) * (float2(n.x >= 0 ? 1 : -1, n.y >= 0 ? 1 : -1));
    return normalize(n);
}

uint PackOct16(float3 n)
{
    float2 e = OctEncode(normalize(n));
    // [-1,1] -> [0,1] -> [0..65535]
    uint2 u = (uint2)round(saturate(e * 0.5 + 0.5) * 65535.0);
    return (u.x & 0xFFFFu) | (u.y << 16);
}

float3 UnpackOct16(uint p)
{
    uint2 u = uint2(p & 0xFFFFu, p >> 16);
    float2 e = (float2(u) / 65535.0) * 2.0 - 1.0;
    return OctDecode(e);
}

// encode sample
// void AppendVertex(float3 worldPos, float3 worldNormal)
// {
//     uint packed = PackOct16(worldNormal);
//
//     MCVertex v;
//     v.position = float4(worldPos, asfloat(packed)); // ←ここ
//     _Vertices.Append(v);
// }

// decode sample
// Varyings vert(uint id : SV_VertexID)
// {
//     MCVertex v = _Vertices[id];
//
//     uint packed = asuint(v.position.w);
//     float3 nWS = UnpackOct16(packed);
//
//     float4 posWS = float4(v.position.xyz, 1.0); // ←ここ重要（wを捨てて1にする）
//     float4 posCS = mul(UNITY_MATRIX_VP, posWS);
//
//     // ... 出力 ...
// }
#endif
