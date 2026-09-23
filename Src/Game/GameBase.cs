using System;
using System.Numerics;
using Vultaik;

namespace Vultaik;

public abstract class GameBase : IDisposable
{
    private readonly GameWindow _window = new();
    private readonly GameTime _gameTime = new();
    private readonly RenderSystem _renderer = new();
    private readonly CameraSystem _camera = new();
    private readonly EditorCameraSystem _editorCamera = new();
    private readonly AnimationSystem _animations = new();
    private readonly SkeletonSystem _skeletons = new();
    private readonly PhysicsSystem _physics = new();
    private readonly SceneSystem _scenes = new();
    private readonly ComponentRegistry _components = new();
    private readonly EditorSystem _editor = new();

    private AssetSystem? _assets;
    private ImGuiController? _imgui;
    private SceneSerializer? _serializer;
    private bool _initialized;
    private bool _disposed;

    protected GameWindow Window => _window;
    protected GameTime Time => _gameTime;
    protected RenderSystem Renderer => _renderer;
    protected CameraSystem Camera => _camera;
    protected EditorCameraSystem EditorCamera => _editorCamera;
    protected AnimationSystem Animations => _animations;
    protected SkeletonSystem Skeletons => _skeletons;
    protected PhysicsSystem Physics => _physics;
    protected SceneSystem Scenes => _scenes;
    protected ComponentRegistry Components => _components;
    protected EditorSystem Editor => _editor;
    protected SceneSerializer Serializer => _serializer!;
    protected AssetSystem Assets => _assets!;
    protected ImGuiController ImGui => _imgui!;

    protected virtual string Title => "Kairo";
    protected virtual uint Width => 1640;
    protected virtual uint Height => 820;
    protected virtual bool Resizable => true;

    public void Run()
    {
        if (_initialized) throw new InvalidOperationException("GameBase.Run() can only be called once.");

        InitializeEngine();

        Scene mainScene = _scenes.CreateScene("Main");
        _scenes.SetActiveScene("Main");

        OnInitialize(mainScene.World);
        _editorCamera.Reset(mainScene.World);
        _gameTime.Reset();

        try
        {
            while (_window.IsRunning)
            {
                _window.PumpMessages();

                if (!_window.IsRunning) break;
                if (_window.IsMinimized) continue;

                _gameTime.Update();

                Scene? activeScene = _scenes.ActiveScene;

                if (activeScene is not null)
                {
                    World world = activeScene.World;
                    float deltaTime = _gameTime.DeltaTime;

                    if (_editor.IsPlaying)
                    {
                        OnUpdate(world, deltaTime);
                        UpdateAnimations(world, deltaTime);
                        _physics.Update(world, deltaTime);
                    }
                    else
                    {
                        _editorCamera.Update(deltaTime);
                    }

                    UpdateSkeletons(world);
                }

                _imgui!.BeginFrame(_window.ClientWidth, _window.ClientHeight);

                EditorAction editorAction = _editor.Draw(_scenes, _components, _serializer!, _assets!);
                activeScene = _scenes.ActiveScene;

                if (activeScene is not null)
                {
                    World world = activeScene.World;

                    if ((editorAction & EditorAction.PlayStarted) != 0) OnPlay(world);

                    if ((editorAction & EditorAction.PlayStopped) != 0)
                    {
                        OnStop(world);
                        _editorCamera.Reset(world);
                    }

                    if ((editorAction & EditorAction.SceneLoaded) != 0)
                    {
                        OnSceneLoaded(world);
                        _editorCamera.Reset(world);
                    }

                    if ((editorAction & EditorAction.SceneChanged) != 0)
                    {
                        OnSceneChanged(world);
                        _editorCamera.Reset(world);
                    }

                    OnDrawImGui(world);
                    UpdateCamera(world);
                }

                _renderer.BeginFrame();

                if (activeScene is not null) Render(activeScene.World);

                _imgui.Render();
                _renderer.Present();
            }

            Scene? scene = _scenes.ActiveScene;
            if (scene is not null) OnDestroy(scene.World);
        }
        finally
        {
            Dispose();
        }
    }

    protected Entity CreateEntity(World world, string name = "Entity") => EntityFactory.Create(world, name);

    protected abstract void OnInitialize(World world);
    protected abstract void OnUpdate(World world, float deltaTime);
    protected virtual void OnPlay(World world) { }
    protected virtual void OnStop(World world) { }
    protected virtual void OnSceneLoaded(World world) { }
    protected virtual void OnSceneChanged(World world) { }
    protected virtual void OnDrawImGui(World world) { }
    protected virtual void OnDestroy(World world) { }

    private void InitializeEngine()
    {
        _window.Initialize(new GameWindow.Config { Title = Title, Width = Width, Height = Height, Resizable = Resizable });
        _renderer.Initialize(_window.Handle, _window.ClientWidth, _window.ClientHeight);

        _assets = new AssetSystem(_renderer.Device);
        _imgui = new ImGuiController(_renderer.Device, _renderer.DeviceContext);

        _components.Discover(typeof(World).Assembly, GetType().Assembly);
        _serializer = new SceneSerializer(_components);
        _physics.Initialize();

        _window.MessageReceived += _imgui.ProcessMessage;
        _initialized = true;
    }

    private void UpdateAnimations(World world, float deltaTime)
    {
        foreach (Entity entity in world.Query<ModelComponent, AnimationComponent>())
        {
            ref ModelComponent model = ref world.Get<ModelComponent>(entity);
            ref AnimationComponent animation = ref world.Get<AnimationComponent>(entity);

            if (model.Model is null || animation.State is null) continue;
            _animations.Update(model.Model, animation.State, deltaTime);
        }
    }

    private void UpdateSkeletons(World world)
    {
        foreach (Entity entity in world.Query<ModelComponent, AnimationComponent, SkeletonComponent>())
        {
            ref ModelComponent model = ref world.Get<ModelComponent>(entity);
            ref AnimationComponent animation = ref world.Get<AnimationComponent>(entity);
            ref SkeletonComponent skeleton = ref world.Get<SkeletonComponent>(entity);

            if (model.Model is null || animation.State is null || skeleton.State is null) continue;
            _skeletons.Update(model.Model, animation.State.Pose, skeleton.State);
        }
    }

    private void UpdateCamera(World world)
    {
        if (_window.ClientHeight == 0) return;

        float aspectRatio = _window.ClientWidth / (float)_window.ClientHeight;

        if (_editor.IsPlaying)
        {
            if (_camera.TryGetMatrices(world, aspectRatio, out Matrix4x4 playView, out Matrix4x4 playProjection)) _renderer.SetCamera(playView, playProjection);
            return;
        }

        if (_editorCamera.TryGetMatrices(aspectRatio, out Matrix4x4 editView, out Matrix4x4 editProjection)) _renderer.SetCamera(editView, editProjection);
    }

    private void Render(World world)
    {
        foreach (Entity entity in world.Query<TransformComponent, ModelComponent>())
        {
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
            ref ModelComponent model = ref world.Get<ModelComponent>(entity);

            if (model.Model is null) continue;

            InstanceData instance = new()
            {
                World = Matrix4x4.CreateScale(transform.Scale) * Matrix4x4.CreateFromQuaternion(transform.Rotation) * Matrix4x4.CreateTranslation(transform.Position),
                BaseColor = model.Color
            };

            SkeletonState? skeletonState = null;

            if (world.Has<SkeletonComponent>(entity))
            {
                ref SkeletonComponent skeleton = ref world.Get<SkeletonComponent>(entity);
                skeletonState = skeleton.State;
            }

            _renderer.DrawModel(model.Model, in instance, skeletonState);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        if (_imgui is not null) _window.MessageReceived -= _imgui.ProcessMessage;

        _imgui?.Dispose();
        _assets?.Dispose();
        _renderer.Dispose();
        _scenes.Clear();
        _window.Dispose();

        _serializer = null;
        _imgui = null;
        _assets = null;

        GC.SuppressFinalize(this);
    }
}
