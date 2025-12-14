#ifndef MC33_VERTEX_UTILITY
#define MC33_VERTEX_UTILITY
#include "../MC33CS.hlsl"

StructuredBuffer<MCVertex> _Vertices; // xyz: position, w: packed normal

void MC33Vertex_float(in uint vertexId, out float3 position, out float3 normal)
{
#ifndef SHADERGRAPH_PREVIEW
    MCVertex v = _Vertices[vertexId];

    position = v.position.xyz;

    uint packed = asuint(v.position.w);
    normal = UnpackOct16(packed);
#else
    position = float3(0,0,0);
    normal = float3(0,1,0);
#endif
}
#endif
