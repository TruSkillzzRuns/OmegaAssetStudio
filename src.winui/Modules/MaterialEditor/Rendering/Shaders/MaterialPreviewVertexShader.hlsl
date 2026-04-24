cbuffer MaterialPreviewConstants : register(b0)
{
    row_major matrix WorldViewProjection;
    row_major matrix World;
}

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float3 Tangent : TANGENT;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    float4 worldPosition = mul(float4(input.Position, 1.0f), World);
    output.Position = mul(worldPosition, WorldViewProjection);
    output.WorldPosition = worldPosition.xyz;
    output.Normal = normalize(mul(float4(input.Normal, 0.0f), World).xyz);
    output.TexCoord = input.TexCoord;
    return output;
}
