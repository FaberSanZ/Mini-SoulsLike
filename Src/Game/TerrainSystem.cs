
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Vultaik;

public sealed class TerrainMesh : IDisposable
{
    public Resource Vertex = new();
    public Resource Index = new();

    public void Dispose()
    {
        Vertex.Dispose();
        Index.Dispose();
    }
}

public sealed class TerrainSystem : IDisposable
{
    private ID3D11Device? _device;

    public TerrainMesh? Mesh { get; private set; }

    public void Initialize(ID3D11Device device)
    {
        _device = device;
    }

    public void Generate(float size, int vertexCount)
    {
        Mesh?.Dispose();

        Vertex[] vertices = GenerateVertices(size, vertexCount);
        uint[] indices = GenerateIndices(vertexCount);

        Mesh = new TerrainMesh
        {
            Vertex = CreateVertexResource(vertices),
            Index = CreateIndexResource(indices)
        };
    }

    private static Vertex[] GenerateVertices(float size, int vertexCount)
    {
        Vertex[] vertices = new Vertex[vertexCount * vertexCount];

        for (int z = 0; z < vertexCount; z++)
        {
            for (int x = 0; x < vertexCount; x++)
            {
                int index = z * vertexCount + x;

                float percentX = x / (float)(vertexCount - 1);
                float percentZ = z / (float)(vertexCount - 1);

                vertices[index] = new Vertex
                {
                    Position = new Vector3(
                        percentX * size - size * 0.5f,
                        0.0f,
                        percentZ * size - size * 0.5f),

                    Normal = Vector3.UnitY,
                    Joints = default,
                    Weights = new Vector4(1.0f, 0.0f, 0.0f, 0.0f)
                };
            }
        }

        return vertices;
    }

    private static uint[] GenerateIndices(int vertexCount)
    {
        int quadCount = (vertexCount - 1) * (vertexCount - 1);
        uint[] indices = new uint[quadCount * 6];

        int index = 0;

        for (int z = 0; z < vertexCount - 1; z++)
        {
            for (int x = 0; x < vertexCount - 1; x++)
            {
                uint topLeft = (uint)(z * vertexCount + x);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((z + 1) * vertexCount + x);
                uint bottomRight = bottomLeft + 1;

                indices[index++] = topLeft;
                indices[index++] = bottomLeft;
                indices[index++] = topRight;

                indices[index++] = topRight;
                indices[index++] = bottomLeft;
                indices[index++] = bottomRight;
            }
        }

        return indices;
    }

    private Resource CreateVertexResource(Vertex[] vertices)
    {
        ID3D11Buffer buffer = _device!.CreateBuffer(
            vertices,
            BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured);

        ID3D11ShaderResourceView view = _device.CreateShaderResourceView(
            buffer,
            new ShaderResourceViewDescription(
                ShaderResourceViewDimension.Buffer,
                Format.Unknown,
                0,
                (uint)vertices.Length));

        return new Resource
        {
            Buffer = buffer,
            View = view,
            Stride = (uint)Unsafe.SizeOf<Vertex>(),
            Count = (uint)vertices.Length
        };
    }

    private Resource CreateIndexResource(uint[] indices)
    {
        ID3D11Buffer buffer = _device!.CreateBuffer(indices, BindFlags.IndexBuffer);

        return new Resource
        {
            Buffer = buffer,
            Stride = sizeof(uint),
            Count = (uint)indices.Length
        };
    }

    public void Dispose()
    {
        Mesh?.Dispose();
        Mesh = null;
        _device = null;
    }
}