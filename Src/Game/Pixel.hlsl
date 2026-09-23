struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 Normal : TEXCOORD0;
    float4 Color : COLOR0;
};

float4 PS(PixelInput input) : SV_Target
{
    float3 normal = normalize(input.Normal);
    float3 lightDirection = normalize(float3(-0.35f, 0.75f, -0.55f));

    float diffuse = saturate(dot(normal, lightDirection));
    float lighting = 0.2f + diffuse * 0.8f;

    return float4(input.Color.rgb * lighting, input.Color.a);
}