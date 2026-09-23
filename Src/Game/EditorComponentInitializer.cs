using System;
using System.Numerics;

namespace Vultaik;

public static class EditorComponentInitializer
{
    public static void Initialize(World world, Entity entity, Type type)
    {
        if (type == typeof(TransformComponent))
        {
            ref TransformComponent component = ref world.Get<TransformComponent>(entity);
            component.Position = Vector3.Zero;
            component.Rotation = Quaternion.Identity;
            component.Scale = Vector3.One;
            return;
        }

        if (type == typeof(ModelComponent))
        {
            ref ModelComponent component = ref world.Get<ModelComponent>(entity);
            component.Model = null;
            component.AssetPath = null;
            component.Color = Vector4.One;
            return;
        }

        if (type == typeof(CameraComponent))
        {
            ref CameraComponent component = ref world.Get<CameraComponent>(entity);
            component.FieldOfView = 60.0f;
            component.NearPlane = 0.1f;
            component.FarPlane = 1000.0f;
            return;
        }

        if (type == typeof(RigidBodyComponent))
        {
            ref RigidBodyComponent component = ref world.Get<RigidBodyComponent>(entity);
            component.Type = BodyType.Dynamic;
            component.LinearVelocity = Vector3.Zero;
            component.LinearAcceleration = Vector3.Zero;
            return;
        }

        if (type == typeof(PlayerComponent))
        {
            ref PlayerComponent component = ref world.Get<PlayerComponent>(entity);
            component.State = PlayerState.None;
            component.RollDirection = Vector3.UnitZ;
            component.PreviousAttack = false;
            component.PreviousRoll = false;
            component.PreviousDeath = false;
            return;
        }

        if (type == typeof(BossComponent))
        {
            ref BossComponent component = ref world.Get<BossComponent>(entity);
            component.State = BossState.None;
            component.AttackCooldown = 0.0f;
        }
    }
}
