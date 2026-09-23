using System.Numerics;
using Vultaik;

namespace Vultaik;



[Component("ID", "Core")]
public struct IDComponent
{
    [DataMember, Display("ID", "Core", 0)]
    public ulong Id;
}

[Component("Name", "Core")]
public struct NameComponent
{
    [DataMember, Display("Name", "Core", 0)]
    public string? Name;
}

[Component("Transform", "Core")]
public struct TransformComponent
{
    [DataMember, Display("Position", "Transform", 0)]
    public Vector3 Position;

    [DataMember, Display("Rotation", "Transform", 1)]
    public Quaternion Rotation;

    [DataMember, Display("Scale", "Transform", 2)]
    public Vector3 Scale;
}

[Component("Model", "Rendering")]
public struct ModelComponent
{
    public Model? Model;

    [DataMember, Display("Asset Path", "Model", 0)]
    public string? AssetPath;

    [DataMember, Display("Color", "Model", 1)]
    public Vector4 Color;
}

[Component("Camera", "Rendering")]
public struct CameraComponent
{
    [DataMember, Display("Field Of View", "Camera", 0)]
    public float FieldOfView;

    [DataMember, Display("Near Plane", "Camera", 1)]
    public float NearPlane;

    [DataMember, Display("Far Plane", "Camera", 2)]
    public float FarPlane;
}

[Component("Primary Camera", "Rendering")]
public struct PrimaryCameraComponent
{
}

public enum BodyType
{
    Static,
    Kinematic,
    Dynamic
}

[Component("Rigid Body", "Physics")]
public struct RigidBodyComponent
{
    [DataMember, Display("Body Type", "Physics", 0)]
    public BodyType Type;

    [DataMember, Display("Linear Velocity", "Physics", 1)]
    public Vector3 LinearVelocity;

    [DataMember, Display("Linear Acceleration", "Physics", 2)]
    public Vector3 LinearAcceleration;
}

[Component("Animation", "Animation")]
public struct AnimationComponent
{
    public AnimationState? State;
}

[Component("Skeleton", "Animation")]
public struct SkeletonComponent
{
    public SkeletonState? State;
}


// game


public enum PlayerState
{
    None,
    Idle,
    Walk,
    Run,
    Roll,
    Attack,
    Death
}

public enum BossState
{
    None,
    Idle,
    Chase,
    Attack,
    Death
}

[Component("Player", "Gameplay")]
public struct PlayerComponent
{
    public PlayerState State;
    public Vector3 RollDirection;
    public bool PreviousAttack;
    public bool PreviousRoll;
    public bool PreviousDeath;
}

[Component("Boss", "Gameplay")]
public struct BossComponent
{
    public BossState State;
    public float AttackCooldown;
}
