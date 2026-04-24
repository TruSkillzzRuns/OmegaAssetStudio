struct PSInput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : NORMAL;
    float2 UV : TEXCOORD1;
    uint SectionIndex : TEXCOORD2;
};

float4 main(PSInput input) : SV_TARGET
{
    float3 lightDirection = normalize(float3(0.35f, 0.45f, 0.85f));
    float3 normal = normalize(input.Normal);
    float diffuse = saturate(dot(normal, lightDirection));
    float ambient = 0.32f;
    float3 baseColor = float3(0.82f, 0.82f, 0.84f);
    float3 litColor = baseColor * (ambient + (diffuse * 0.68f));
    return float4(litColor, 1.0f);
}
