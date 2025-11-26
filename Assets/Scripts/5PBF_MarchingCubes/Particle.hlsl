struct Particle
{
    float3 position;
    float radiusScale;  // 微妙にサイズを変えてパーティクル同士が重なるのを防ぐ
    float3 velocity;
    float life;
    float4 color;
};
