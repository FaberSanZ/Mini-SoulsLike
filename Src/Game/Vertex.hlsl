struct Vertex
{
    float3 Position;
    float3 Normal;
    uint4 Joints;
    float4 Weights;
};

struct InstanceData
{
    float4x4 World;
    float4 BaseColor;
};

StructuredBuffer<Vertex> Vertices : register(t0);
StructuredBuffer<InstanceData> Instances : register(t1);
StructuredBuffer<float4x4> JointMatrices : register(t2);

cbuffer CameraBuffer : register(b0)
{
    float4x4 View;
    float4x4 Projection;
};

cbuffer PartBuffer : register(b1)
{
    float4 MaterialColor;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float3 Normal : NORMAL;
    float4 Color : COLOR;
};

PixelInput VS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    Vertex vertex = Vertices[vertexId];
    InstanceData instance = Instances[instanceId];

    float4 localPosition = float4(vertex.Position, 1.0f);
    float3 localNormal = vertex.Normal;

    float4 modelPosition =
        mul(localPosition, JointMatrices[vertex.Joints.x]) * vertex.Weights.x +
        mul(localPosition, JointMatrices[vertex.Joints.y]) * vertex.Weights.y +
        mul(localPosition, JointMatrices[vertex.Joints.z]) * vertex.Weights.z +
        mul(localPosition, JointMatrices[vertex.Joints.w]) * vertex.Weights.w;

    float3 modelNormal =
        mul(float4(localNormal, 0.0f), JointMatrices[vertex.Joints.x]).xyz * vertex.Weights.x +
        mul(float4(localNormal, 0.0f), JointMatrices[vertex.Joints.y]).xyz * vertex.Weights.y +
        mul(float4(localNormal, 0.0f), JointMatrices[vertex.Joints.z]).xyz * vertex.Weights.z +
        mul(float4(localNormal, 0.0f), JointMatrices[vertex.Joints.w]).xyz * vertex.Weights.w;

    float4 worldPosition = mul(modelPosition, instance.World);
    float3 worldNormal = normalize(mul(float4(modelNormal, 0.0f), instance.World).xyz);

    PixelInput output;
    output.Position = mul(worldPosition, View);
    output.Position = mul(output.Position, Projection);
    output.Normal = worldNormal;
    output.Color = instance.BaseColor * MaterialColor;
    return output;
}
