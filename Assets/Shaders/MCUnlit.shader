// MCUnlit.shader - renders vertices from _TriVerts as triangles
Shader "Hidden/MCUnlit"
{
    Properties{
        _Color("Color", Color) = (0.4,0.7,1,1)
    }
    SubShader{
        Tags{"RenderType"="Opaque"}
        Pass{
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            StructuredBuffer<float3> _TriVerts;
            float4 _Color;

            float4x4 _LocalToWorld;
            float4x4 _WorldToClip;

            struct v2f { float4 pos:SV_POSITION; };

            v2f vert(uint vid:SV_VertexID){
                v2f o;
                float3 p = _TriVerts[vid];
                float4 wp = mul(_LocalToWorld, float4(p,1));
                o.pos = mul(_WorldToClip, wp);
                return o;
            }

            fixed4 frag(v2f i):SV_Target{
                return _Color;
            }
            ENDHLSL
        }
    }
}
