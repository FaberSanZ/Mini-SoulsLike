using System.Numerics;

namespace Vultaik;

public sealed class PhysicsSystem
{
    private Vector3 _gravity = new(0.0f, -9.81f * 0.1f, 0.0f);

    public Vector3 Gravity
    {
        get => _gravity;
        set => _gravity = value;
    }

    public void Initialize()
    {
    }

    public void Update(World world, float deltaTime)
    {
        foreach (Entity entity in world.Query<RigidBodyComponent, TransformComponent>())
        {
            ref RigidBodyComponent body = ref world.Get<RigidBodyComponent>(entity);
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);

            if (body.Type == BodyType.Dynamic)
            {
                Vector3 acceleration = body.LinearAcceleration + _gravity;
                body.LinearVelocity += acceleration * deltaTime;
            }

            if (body.Type != BodyType.Static) transform.Position += body.LinearVelocity * deltaTime;
        }
    }
}
