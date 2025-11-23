Shader "Unlit/PointDrawGeomMove"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Size ("Size", Range(0.01,0.3)) = 0.01
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        
//        Cull Front

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            
            #include "UnityCG.cginc"

            struct ParticleData
            {
                float3 vertex;
                float3 velocity;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                // float4 color : COLOR;
            };

            StructuredBuffer<ParticleData> _PositionBuffer;
            
            half4 _Color;
            half _Size;
            float4x4 _ObjectToWorld;

            v2f vert (uint id : SV_VertexID)
            {
                v2f o;
                ParticleData p = _PositionBuffer[id];
                o.vertex = mul(_ObjectToWorld, float4(p.vertex, 1.0));
                // o.vertex = float4(vData.vertex, 1.0);
                return o;
            }
            
            // 中心座標を受け取り、四角形の頂点を生成するジオメトリシェーダ
            [maxvertexcount(4)]
            void geom(point v2f input[1], inout TriangleStream<v2f> triStream)
            {
                float4 center = input[0].vertex;
                float size = _Size; // 四角形のサイズ
                // float size = 0.05; // 四角形のサイズ

                float4x4 invView = unity_MatrixInvV;
                invView._m03_m13_m23 = float3(0, 0, 0); // 平行移動成分をゼロにする
                v2f vertex;

                // centerに基づいて四角形の4頂点を生成してObject空間からクリップ空間へ変換
                // vertex.vertex = UnityObjectToClipPos(center + float4(-size, -size, 0, 0));
                // triStream.Append(vertex);
                // vertex.vertex = UnityObjectToClipPos(center + float4(-size, size, 0, 0));
                // triStream.Append(vertex);
                // vertex.vertex = UnityObjectToClipPos(center + float4(size, -size, 0, 0));
                // triStream.Append(vertex);
                // vertex.vertex = UnityObjectToClipPos(center + float4(size, size, 0, 0));
                // triStream.Append(vertex);
                //UNITY_MATRIX_VP
                vertex.vertex = mul(UNITY_MATRIX_VP, center + mul(invView,float4(-size, -size, 0, 0)));
                triStream.Append(vertex);
                vertex.vertex = mul(UNITY_MATRIX_VP, center + mul(invView,float4(-size, size, 0, 0)));
                triStream.Append(vertex);
                vertex.vertex = mul(UNITY_MATRIX_VP, center + mul(invView,float4(size, -size, 0, 0)));
                triStream.Append(vertex);
                vertex.vertex = mul(UNITY_MATRIX_VP, center + mul(invView,float4(size, size, 0, 0)));
                triStream.Append(vertex);
                // vertex.vertex = UnityObjectToClipPos(center + mul(invView,float4(-size, -size, 0, 0)));
                // triStream.Append(vertex);
                // vertex.vertex = UnityObjectToClipPos(center + mul(invView,float4(-size, size, 0, 0)));
                // triStream.Append(vertex);
                // vertex.vertex = UnityObjectToClipPos(center + mul(invView,float4(size, -size, 0, 0)));
                // triStream.Append(vertex);
                // vertex.vertex = UnityObjectToClipPos(center + mul(invView,float4(size, size, 0, 0)));
                // triStream.Append(vertex);

                triStream.RestartStrip();
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 col = _Color;
                return col;
            }
            ENDCG
        }
    }
}
