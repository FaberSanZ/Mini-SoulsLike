struct DebugVertex
{
    float3 Position;
    float4 Color;
};

StructuredBuffer<DebugVertex> Vertices : register(t3);

cbuffer CameraBuffer : register(b0)
{
    float4x4 View;
    float4x4 Projection;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
};

PixelInput VS(uint vertexId : SV_VertexID)
{
    DebugVertex vertex = Vertices[vertexId];

    PixelInput output;
    output.Position = mul(float4(vertex.Position, 1.0f), View);
    output.Position = mul(output.Position, Projection);
    output.Color = vertex.Color;
    return output;
}