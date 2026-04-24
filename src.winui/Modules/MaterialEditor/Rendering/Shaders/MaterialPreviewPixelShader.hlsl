Texture2D BaseTexture : register(t0);
SamplerState BaseSampler : register(s0);

cbuffer MaterialPreviewParameters : register(b1)
{
    float3 LightDirection;
    float LightIntensity;
    float3 LightColor;
    float AmbientLight;
    float3 DiffuseColor;
    float SpecularPower;
    float3 SpecularColor;
    float SpecMult;
    float3 FillLightColor;
    float SpecMultLq;
}

struct PSInput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
};

float4 main(PSInput input) : SV_TARGET
{
    float3 normal = normalize(input.Normal);
    float3 lightDir = normalize(-LightDirection);
    float ndotl = saturate(dot(normal, lightDir));
    float3 baseColor = DiffuseColor;
    float4 texColor = BaseTexture.Sample(BaseSampler, input.TexCoord);
    float3 lit = (baseColor * texColor.rgb * (AmbientLight + ndotl * LightIntensity)) + (FillLightColor * 0.1f);
    return float4(saturate(lit), texColor.a);
}
