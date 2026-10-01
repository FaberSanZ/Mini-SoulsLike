using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Vultaik;


public sealed class PhysicsSystem : IDisposable
{
    private readonly BufferPool _bufferPool = new();
    private readonly Dictionary<Entity, BodyHandle> _bodies = new();
    private readonly Dictionary<Entity, StaticHandle> _statics = new();

    private ThreadDispatcher? _threadDispatcher;
    private Simulation? _simulation;

    public Vector3 Gravity = new(0.0f, -9.81f, 0.0f);

    public Simulation Simulation => _simulation ?? throw new InvalidOperationException("PhysicsSystem is not initialized.");

    public void Initialize()
    {
        _threadDispatcher = new ThreadDispatcher(Math.Max(1, Environment.ProcessorCount - 1));
        CreateSimulation();
    }

    public void Rebuild(World world)
    {
        ResetSimulation();

        foreach (Entity entity in world.Query<TransformComponent, CapsuleColliderComponent>())
            RegisterCapsule(world, entity);

        foreach (Entity entity in world.Query<TransformComponent, BoxColliderComponent>())
        {
            if (_bodies.ContainsKey(entity) || _statics.ContainsKey(entity))
                continue;

            RegisterBox(world, entity);
        }
    }

    public void Clear()
    {
        ResetSimulation();
    }

    public void Update(World world, float deltaTime)
    {
        if (_simulation is null)
            return;

        SyncKinematics(world);

        _simulation.Timestep(deltaTime, _threadDispatcher);

        SyncDynamics(world);
    }

    private void RegisterCapsule(World world, Entity entity)
    {
        ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
        ref CapsuleColliderComponent collider = ref world.Get<CapsuleColliderComponent>(entity);

        Capsule shape = new(collider.Radius, collider.Length);
        TypedIndex shapeIndex = Simulation.Shapes.Add(shape);

        RegisterShape(world, entity, in transform, collider.Offset, shapeIndex, shape.ComputeInertia);
    }

    private void RegisterBox(World world, Entity entity)
    {
        ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
        ref BoxColliderComponent collider = ref world.Get<BoxColliderComponent>(entity);

        Box shape = new(collider.Size.X, collider.Size.Y, collider.Size.Z);
        TypedIndex shapeIndex = Simulation.Shapes.Add(shape);

        RegisterShape(world, entity, in transform, collider.Offset, shapeIndex, shape.ComputeInertia);
    }

    private void RegisterShape(World world, Entity entity, in TransformComponent transform, Vector3 offset, TypedIndex shapeIndex, Func<float, BodyInertia> computeInertia)
    {
        RigidPose pose = CreatePose(in transform, offset);

        if (!world.Has<RigidBodyComponent>(entity))
        {
            _statics.Add(entity, Simulation.Statics.Add(new StaticDescription(pose.Position, pose.Orientation, shapeIndex)));
            return;
        }

        ref RigidBodyComponent rigidbody = ref world.Get<RigidBodyComponent>(entity);

        if (rigidbody.Type == BodyType.Static)
        {
            _statics.Add(entity, Simulation.Statics.Add(new StaticDescription(pose.Position, pose.Orientation, shapeIndex)));
            return;
        }

        BodyHandle handle;

        if (rigidbody.Type == BodyType.Kinematic)
        {
            CollidableDescription collidable = new(shapeIndex, 0.1f);
            BodyActivityDescription activity = new(0.01f);

            handle = Simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, collidable, activity));
        }
        else
        {
            float mass = MathF.Max(rigidbody.Mass, 0.001f);
            BodyInertia inertia = computeInertia(mass);

            if (world.Has<PlayerComponent>(entity))
                inertia.InverseInertiaTensor = default;

            handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(pose, inertia, shapeIndex, 0.01f));
        }

        BodyReference body = Simulation.Bodies[handle];
        body.Velocity.Linear = rigidbody.LinearVelocity;
        body.Velocity.Angular = rigidbody.AngularVelocity;

        _bodies.Add(entity, handle);
    }

    private void SyncKinematics(World world)
    {
        foreach ((Entity entity, BodyHandle handle) in _bodies)
        {
            if (!world.IsAlive(entity))
                continue;

            ref RigidBodyComponent rigidbody = ref world.Get<RigidBodyComponent>(entity);

            if (rigidbody.Type != BodyType.Kinematic)
                continue;

            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);

            Vector3 offset = GetColliderOffset(world, entity);
            RigidPose pose = CreatePose(in transform, offset);

            BodyReference body = Simulation.Bodies[handle];

            body.Pose.Position = pose.Position;
            body.Pose.Orientation = pose.Orientation;
            body.Velocity.Linear = rigidbody.LinearVelocity;
            body.Velocity.Angular = rigidbody.AngularVelocity;
        }
    }

    private void SyncDynamics(World world)
    {
        foreach ((Entity entity, BodyHandle handle) in _bodies)
        {
            if (!world.IsAlive(entity))
                continue;

            ref RigidBodyComponent rigidbody = ref world.Get<RigidBodyComponent>(entity);

            if (rigidbody.Type != BodyType.Dynamic)
                continue;

            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
            BodyReference body = Simulation.Bodies[handle];

            transform.Rotation = body.Pose.Orientation;

            Vector3 offset = GetColliderOffset(world, entity);
            transform.Position = body.Pose.Position - Vector3.Transform(offset, transform.Rotation);

            rigidbody.LinearVelocity = body.Velocity.Linear;
            rigidbody.AngularVelocity = body.Velocity.Angular;
        }
    }

    private static RigidPose CreatePose(in TransformComponent transform, Vector3 offset)
    {
        Vector3 worldOffset = Vector3.Transform(offset, transform.Rotation);
        return new RigidPose(transform.Position + worldOffset, transform.Rotation);
    }

    private static Vector3 GetColliderOffset(World world, Entity entity)
    {
        if (world.Has<CapsuleColliderComponent>(entity))
            return world.Get<CapsuleColliderComponent>(entity).Offset;

        if (world.Has<BoxColliderComponent>(entity))
            return world.Get<BoxColliderComponent>(entity).Offset;

        return Vector3.Zero;
    }

    public void SetLinearVelocity(Entity entity, Vector3 velocity)
    {
        if (!_bodies.TryGetValue(entity, out BodyHandle handle))
            return;

        BodyReference body = Simulation.Bodies[handle];
        body.Awake = true;
        body.Velocity.Linear = velocity;
    }

    public void SetHorizontalVelocity(Entity entity, Vector3 velocity)
    {
        if (!_bodies.TryGetValue(entity, out BodyHandle handle))
            return;

        BodyReference body = Simulation.Bodies[handle];
        body.Awake = true;

        Vector3 current = body.Velocity.Linear;
        current.X = velocity.X;
        current.Z = velocity.Z;

        body.Velocity.Linear = current;
    }

    public void SetOrientation(Entity entity, Quaternion orientation)
    {
        if (!_bodies.TryGetValue(entity, out BodyHandle handle))
            return;

        BodyReference body = Simulation.Bodies[handle];
        body.Awake = true;
        body.Pose.Orientation = orientation;
    }

    private void CreateSimulation()
    {
        _simulation = Simulation.Create(
            _bufferPool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(Gravity),
            new SolveDescription(8, 1)
        );
    }

    private void ResetSimulation()
    {
        _simulation?.Dispose();

        _bodies.Clear();
        _statics.Clear();

        CreateSimulation();
    }

    public void Dispose()
    {
        _simulation?.Dispose();
        _threadDispatcher?.Dispose();
        _bufferPool.Clear();

        _simulation = null;
        _threadDispatcher = null;

        _bodies.Clear();
        _statics.Clear();
    }
}



public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public void Initialize(Simulation simulation)
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial.FrictionCoefficient = 1.0f;
        pairMaterial.MaximumRecoveryVelocity = 2.0f;
        pairMaterial.SpringSettings = new SpringSettings(30.0f, 1.0f);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
    {
        return true;
    }

    public void Dispose()
    {
    }
}

public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    private Vector3Wide _gravityWideDt;

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public PoseIntegratorCallbacks(Vector3 gravity)
    {
        Gravity = gravity;
        _gravityWideDt = default;
    }

    public void Initialize(Simulation simulation)
    {
    }

    public void PrepareForIntegration(float dt)
    {
        _gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
    }

    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear += _gravityWideDt;
    }
}