#ifndef MC33_VERTEX_UTILITY
#define MC33_VERTEX_UTILITY
#include "../MC33CS.hlsl"

StructuredBuffer<MCVertex> _Vertices; // xyz: position, w: packed normal

void MC33Vertex_float(in uint vertexId, out float3 position, out float3 normal, out float4 color)
{
#ifndef SHADERGRAPH_PREVIEW
    MCVertex v = _Vertices[vertexId];

    position = v.position.xyz;

#ifdef USE_OCT_ENCODED_NORMAL
    uint packed = asuint(v.position.w);
    normal = UnpackOct16(packed);
#else    
    normal = v.normal.xyz;
#endif
    color = v.color;
    
#else
    position = float3(0,0,0);
    normal = float3(0,1,0);
    color = float4(1,1,1,1);
#endif
}
#endif
