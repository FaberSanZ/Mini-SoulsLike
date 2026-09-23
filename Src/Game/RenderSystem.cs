using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace Vultaik;

public struct InstanceData
{
    public Matrix4x4 World;
    public Vector4 BaseColor;
}

public sealed class RenderSystem : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _deviceContext;
    private IDXGIFactory2? _factory;
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11Texture2D? _depthStencilBuffer;
    private ID3D11DepthStencilView? _depthStencilView;
    private ID3D11DepthStencilState? _depthStencilState;
    private ID3D11RasterizerState? _rasterizerState;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _cameraBuffer;
    private ID3D11Buffer? _instanceBuffer;
    private ID3D11ShaderResourceView? _instanceBufferView;
    private ID3D11Buffer? _identityJointBuffer;
    private ID3D11ShaderResourceView? _identityJointView;
    private ID3D11Buffer? _defaultMaterialBuffer;

    private readonly Dictionary<Model, ID3D11Buffer[]> _materialBuffers = new();
    private readonly Dictionary<SkeletonState, SkinGpuState[]> _skeletonBuffers = new();
    private readonly GpuInstanceData[] _singleInstance = new GpuInstanceData[1];
    private readonly Matrix4x4[] _identityJoint = [Matrix4x4.Identity];

    private CameraBuffer _cameraData;
    private ulong _frameId;

    private ID3D11ShaderResourceView? _boundVertexView;
    private ID3D11ShaderResourceView? _boundJointView;
    private ID3D11Buffer? _boundIndexBuffer;
    private ID3D11Buffer? _boundMaterialBuffer;

    private long _entityUploadTicks;
    private long _jointUploadTicks;
    private long _drawSubmissionTicks;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint FrameCount { get; } = 2;

    public uint DrawCalls { get; private set; }
    public uint RenderedInstances { get; private set; }
    public ulong RenderedTriangles { get; private set; }
    public uint JointUploads { get; private set; }
    public uint JointBinds { get; private set; }
    public uint MaterialBinds { get; private set; }

    public double EntityUploadMs => TicksToMs(_entityUploadTicks);
    public double JointUploadMs => TicksToMs(_jointUploadTicks);
    public double GpuUploadMs => EntityUploadMs + JointUploadMs;
    public double DrawSubmissionMs => TicksToMs(_drawSubmissionTicks);

    public ID3D11Device Device => _device!;
    public ID3D11DeviceContext DeviceContext => _deviceContext!;

    private struct CameraBuffer
    {
        public Matrix4x4 View;
        public Matrix4x4 Projection;
    }

    private struct MaterialBuffer
    {
        public Vector4 MaterialColor;
    }

    private struct GpuInstanceData
    {
        public Matrix4x4 World;
        public Vector4 BaseColor;
    }

    private sealed class SkinGpuState : IDisposable
    {
        public ID3D11Buffer? Buffer;
        public ID3D11ShaderResourceView? View;
        public int Capacity;
        public ulong LastUploadFrame = ulong.MaxValue;

        public void Dispose()
        {
            View?.Dispose();
            Buffer?.Dispose();
            View = null;
            Buffer = null;
            Capacity = 0;
        }
    }

    public void Initialize(nint hwnd, uint width = 1440, uint height = 820)
    {
        Width = width;
        Height = height;

        FeatureLevel[] featureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.None, featureLevels, out _device, out _, out _deviceContext);

        _factory = CreateDXGIFactory1<IDXGIFactory2>();

        SwapChainDescription1 swapChainDescription = new()
        {
            Width = Width,
            Height = Height,
            Format = Format.R8G8B8A8_UNorm,
            BufferCount = FrameCount,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = SampleDescription.Default,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.AllowTearing
        };

        _swapChain = _factory.CreateSwapChainForHwnd(_device, hwnd, swapChainDescription, new SwapChainFullscreenDescription { Windowed = true });
        _factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);

        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(_backBuffer);

        CreateRasterizer();
        CreateDepthBuffer();
        CreateShaders();
        CreateCameraBuffer();
        CreateSingleInstanceBuffer();
        CreateIdentityJointBuffer();

        _defaultMaterialBuffer = CreateMaterialBuffer(Vector4.One);

        SetCamera(Matrix4x4.CreateLookAt(new Vector3(0.0f, 5.0f, 15.0f), Vector3.Zero, Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, Width / (float)Height, 0.1f, 1000.0f));
    }

    public void PrepareModel(Model model)
    {
        if (_materialBuffers.ContainsKey(model)) return;

        ID3D11Buffer[] buffers = new ID3D11Buffer[model.Materials.Length];

        for (int i = 0; i < buffers.Length; i++) buffers[i] = CreateMaterialBuffer(model.Materials[i].BaseColor);

        _materialBuffers.Add(model, buffers);
    }

    public void PrepareSkeleton(SkeletonState skeleton)
    {
        if (_skeletonBuffers.ContainsKey(skeleton)) return;

        SkinGpuState[] states = new SkinGpuState[skeleton.JointMatrices.Length];

        for (int skinIndex = 0; skinIndex < states.Length; skinIndex++)
        {
            states[skinIndex] = new SkinGpuState();
            EnsureSkinCapacity(states[skinIndex], skeleton.JointMatrices[skinIndex].Length);
        }

        _skeletonBuffers.Add(skeleton, states);
    }

    public void SetCamera(Matrix4x4 view, Matrix4x4 projection)
    {
        _cameraData.View = Matrix4x4.Transpose(view);
        _cameraData.Projection = Matrix4x4.Transpose(projection);
    }

    public void BeginFrame()
    {
        _frameId++;

        DrawCalls = 0;
        RenderedInstances = 0;
        RenderedTriangles = 0;
        JointUploads = 0;
        JointBinds = 0;
        MaterialBinds = 0;

        _entityUploadTicks = 0;
        _jointUploadTicks = 0;
        _drawSubmissionTicks = 0;

        _boundVertexView = null;
        _boundJointView = null;
        _boundIndexBuffer = null;
        _boundMaterialBuffer = null;

        _deviceContext!.UpdateSubresource(_cameraData, _cameraBuffer!);
        _deviceContext.ClearRenderTargetView(_renderTargetView!, new Color4(0.0f, 0.2f, 0.4f, 1.0f));
        _deviceContext.ClearDepthStencilView(_depthStencilView!, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 1.0f, 0);
        _deviceContext.OMSetRenderTargets(_renderTargetView!, _depthStencilView);
        _deviceContext.OMSetDepthStencilState(_depthStencilState, 1);
        _deviceContext.RSSetState(_rasterizerState);
        _deviceContext.RSSetViewport(new Viewport(Width, Height));
        _deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _deviceContext.VSSetConstantBuffer(0, _cameraBuffer);
        _deviceContext.VSSetShaderResource(1, _instanceBufferView);
        _deviceContext.VSSetShader(_vertexShader);
        _deviceContext.PSSetShader(_pixelShader);
    }

    public void DrawModel(Model model, in InstanceData instance, SkeletonState? skeleton = null)
    {
        PrepareModel(model);

        _singleInstance[0].World = Matrix4x4.Transpose(instance.World);
        _singleInstance[0].BaseColor = instance.BaseColor;

        long start = Stopwatch.GetTimestamp();
        _instanceBuffer!.SetData(_deviceContext!, _singleInstance, MapMode.WriteDiscard);
        _entityUploadTicks += Stopwatch.GetTimestamp() - start;

        foreach (Mesh mesh in model.Meshes)
        {
            foreach (MeshPart part in mesh.Parts)
            {
                if (part.Vertex.View is null || part.Index.Buffer is null) continue;

                int skinIndex = (uint)part.NodeIndex < (uint)model.Nodes.Length ? model.Nodes[part.NodeIndex].SkinIndex : -1;
                ID3D11ShaderResourceView jointView = GetJointView(skeleton, skinIndex);
                ID3D11Buffer materialBuffer = GetMaterialBuffer(model, part.MaterialIndex);

                start = Stopwatch.GetTimestamp();

                if (!ReferenceEquals(_boundVertexView, part.Vertex.View))
                {
                    _deviceContext!.VSSetShaderResource(0, part.Vertex.View);
                    _boundVertexView = part.Vertex.View;
                }

                if (!ReferenceEquals(_boundJointView, jointView))
                {
                    _deviceContext!.VSSetShaderResource(2, jointView);
                    _boundJointView = jointView;
                    JointBinds++;
                }

                if (!ReferenceEquals(_boundMaterialBuffer, materialBuffer))
                {
                    _deviceContext!.VSSetConstantBuffer(1, materialBuffer);
                    _boundMaterialBuffer = materialBuffer;
                    MaterialBinds++;
                }

                if (!ReferenceEquals(_boundIndexBuffer, part.Index.Buffer))
                {
                    _deviceContext!.IASetIndexBuffer(part.Index.Buffer, Format.R32_UInt, 0);
                    _boundIndexBuffer = part.Index.Buffer;
                }

                _deviceContext!.DrawIndexed(part.Index.Count, 0, 0);

                _drawSubmissionTicks += Stopwatch.GetTimestamp() - start;

                DrawCalls++;
                RenderedInstances++;
                RenderedTriangles += part.Index.Count / 3;
            }
        }
    }

    public void Present() => _swapChain!.Present(0, PresentFlags.AllowTearing);

    private ID3D11ShaderResourceView GetJointView(SkeletonState? skeleton, int skinIndex)
    {
        if (skeleton is null || (uint)skinIndex >= (uint)skeleton.JointMatrices.Length) return _identityJointView!;

        PrepareSkeleton(skeleton);

        Matrix4x4[] joints = skeleton.JointMatrices[skinIndex];
        if (joints.Length == 0) return _identityJointView!;

        SkinGpuState state = _skeletonBuffers[skeleton][skinIndex];
        EnsureSkinCapacity(state, joints.Length);

        if (state.LastUploadFrame != _frameId)
        {
            long start = Stopwatch.GetTimestamp();
            state.Buffer!.SetData(_deviceContext!, joints, MapMode.WriteDiscard);
            _jointUploadTicks += Stopwatch.GetTimestamp() - start;

            state.LastUploadFrame = _frameId;
            JointUploads++;
        }

        return state.View!;
    }

    private ID3D11Buffer GetMaterialBuffer(Model model, int materialIndex)
    {
        ID3D11Buffer[] buffers = _materialBuffers[model];
        if ((uint)materialIndex >= (uint)buffers.Length) return _defaultMaterialBuffer!;
        return buffers[materialIndex];
    }

    private ID3D11Buffer CreateMaterialBuffer(Vector4 color)
    {
        MaterialBuffer material = new() { MaterialColor = color };
        BufferDescription description = new() { ByteWidth = (uint)Unsafe.SizeOf<MaterialBuffer>(), Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.None };
        return _device!.CreateBuffer(material, description);
    }

    private void EnsureSkinCapacity(SkinGpuState state, int count)
    {
        count = Math.Max(count, 1);
        if (count <= state.Capacity) return;

        state.View?.Dispose();
        state.Buffer?.Dispose();

        uint stride = (uint)Unsafe.SizeOf<Matrix4x4>();

        state.Buffer = _device!.CreateBuffer(stride * (uint)count, BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.BufferStructured, stride);
        state.View = _device.CreateShaderResourceView(state.Buffer, new ShaderResourceViewDescription(ShaderResourceViewDimension.Buffer, Format.Unknown, 0, (uint)count));
        state.Capacity = count;
        state.LastUploadFrame = ulong.MaxValue;
    }

    private void CreateRasterizer() => _rasterizerState = _device!.CreateRasterizerState(RasterizerDescription.CullNone);

    private void CreateDepthBuffer()
    {
        _depthStencilState = _device!.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.Less));
        _depthStencilBuffer = _device.CreateTexture2D(Format.D24_UNorm_S8_UInt, Width, Height, mipLevels: 1, bindFlags: BindFlags.DepthStencil);
        _depthStencilView = _device.CreateDepthStencilView(_depthStencilBuffer);
    }

    private void CreateShaders()
    {
        ReadOnlyMemory<byte> vertexShaderByteCode = Compiler.CompileFromFile("Vertex.hlsl", "VS", "vs_5_0");
        ReadOnlyMemory<byte> pixelShaderByteCode = Compiler.CompileFromFile("Pixel.hlsl", "PS", "ps_5_0");

        _vertexShader = _device!.CreateVertexShader(vertexShaderByteCode.Span);
        _pixelShader = _device.CreatePixelShader(pixelShaderByteCode.Span);
    }

    private void CreateCameraBuffer()
    {
        BufferDescription description = new() { ByteWidth = (uint)Unsafe.SizeOf<CameraBuffer>(), Usage = ResourceUsage.Default, BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.None };
        _cameraBuffer = _device!.CreateBuffer(_cameraData, description);
    }

    private void CreateSingleInstanceBuffer()
    {
        uint stride = (uint)Unsafe.SizeOf<GpuInstanceData>();

        _instanceBuffer = _device!.CreateBuffer(stride, BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.BufferStructured, stride);
        _instanceBufferView = _device.CreateShaderResourceView(_instanceBuffer, new ShaderResourceViewDescription(ShaderResourceViewDimension.Buffer, Format.Unknown, 0, 1));
    }

    private void CreateIdentityJointBuffer()
    {
        uint stride = (uint)Unsafe.SizeOf<Matrix4x4>();

        _identityJointBuffer = _device!.CreateBuffer(_identityJoint, BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured);
        _identityJointView = _device.CreateShaderResourceView(_identityJointBuffer, new ShaderResourceViewDescription(ShaderResourceViewDimension.Buffer, Format.Unknown, 0, 1));
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public void Dispose()
    {
        _deviceContext?.ClearState();
        _deviceContext?.Flush();

        foreach (ID3D11Buffer[] buffers in _materialBuffers.Values)
        {
            for (int i = 0; i < buffers.Length; i++) buffers[i].Dispose();
        }

        foreach (SkinGpuState[] states in _skeletonBuffers.Values)
        {
            for (int i = 0; i < states.Length; i++) states[i].Dispose();
        }

        _materialBuffers.Clear();
        _skeletonBuffers.Clear();

        _defaultMaterialBuffer?.Dispose();
        _identityJointView?.Dispose();
        _identityJointBuffer?.Dispose();
        _instanceBufferView?.Dispose();
        _instanceBuffer?.Dispose();
        _cameraBuffer?.Dispose();
        _rasterizerState?.Dispose();
        _depthStencilState?.Dispose();
        _depthStencilView?.Dispose();
        _depthStencilBuffer?.Dispose();
        _pixelShader?.Dispose();
        _vertexShader?.Dispose();
        _renderTargetView?.Dispose();
        _backBuffer?.Dispose();
        _swapChain?.Dispose();
        _factory?.Dispose();
        _deviceContext?.Dispose();
        _device?.Dispose();
    }
}
