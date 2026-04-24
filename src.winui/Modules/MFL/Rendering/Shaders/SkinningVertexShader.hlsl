cbuffer CameraBuffer : register(b0)
{
    matrix World;
    matrix View;
    matrix Projection;
};

cbuffer SkinningBuffer : register(b1)
{
    matrix BoneMatrices[128];
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
    float4 skinnedPosition = float4(0.0f, 0.0f, 0.0f, 0.0f);
    float3 skinnedNormal = float3(0.0f, 0.0f, 0.0f);

    [unroll]
    for (int i = 0; i < 4; ++i)
    {
        uint boneIndex = input.Bones[i];
        float weight = input.Weights[i];
        if (weight <= 0.0f)
            continue;

        matrix boneMatrix = BoneMatrices[boneIndex];
        skinnedPosition += mul(float4(input.Position, 1.0f), boneMatrix) * weight;
        skinnedNormal += mul(float4(input.Normal, 0.0f), boneMatrix).xyz * weight;
    }

    VSOutput output;
    float4 worldPosition = mul(skinnedPosition, World);
    output.Position = mul(worldPosition, View);
    output.Position = mul(output.Position, Projection);
    output.WorldPosition = worldPosition.xyz;
    output.Normal = normalize(mul(float4(skinnedNormal, 0.0f), World).xyz);
    output.UV = input.UV;
    output.SectionIndex = input.SectionIndex;
    return output;
}
