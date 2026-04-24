cbuffer CameraBuffer : register(b0)
{
    matrix World;
    matrix View;
    matrix Projection;
};

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float3 Tangent : TANGENT;
    float3 Bitangent : BINORMAL;
    float2 UV : TEXCOORD0;
    uint4 Bones : BLENDINDICES0;
    float4 Weights : BLENDWEIGHT0;
    uint SectionIndex : TEXCOORD1;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : NORMAL;
    float2 UV : TEXCOORD1;
    uint SectionIndex : TEXCOORD2;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    float4 worldPosition = mul(float4(input.Position, 1.0f), World);
    output.Position = mul(worldPosition, View);
    output.Position = mul(output.Position, Projection);
    output.WorldPosition = worldPosition.xyz;
    output.Normal = normalize(mul(float4(input.Normal, 0.0f), World).xyz);
    output.UV = input.UV;
    output.SectionIndex = input.SectionIndex;
    return output;
}
