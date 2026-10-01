using System.Numerics;

namespace Vultaik;

public struct DebugLine
{
    public Vector3 A;
    public Vector3 B;
    public Vector4 Color;

    public DebugLine(Vector3 a, Vector3 b, Vector4 color)
    {
        A = a;
        B = b;
        Color = color;
    }
}

public sealed class PhysicsDebugSystem
{
    private readonly List<DebugLine> _lines = new(1024);

    public bool Enabled = true;

    public void Draw(World world, RenderSystem renderer)
    {
        if (!Enabled)
            return;

        _lines.Clear();

        DrawBoxes(world);
        DrawCapsules(world);

        renderer.DrawDebugLines(_lines);
    }

    private void DrawBoxes(World world)
    {
        foreach (Entity entity in world.Query<TransformComponent, BoxColliderComponent>())
        {
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
            ref BoxColliderComponent collider = ref world.Get<BoxColliderComponent>(entity);

            DrawBox(
                transform.Position + Vector3.Transform(collider.Offset, transform.Rotation),
                transform.Rotation,
                collider.Size,
                new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
        }
    }

    private void DrawCapsules(World world)
    {
        foreach (Entity entity in world.Query<TransformComponent, CapsuleColliderComponent>())
        {
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
            ref CapsuleColliderComponent collider = ref world.Get<CapsuleColliderComponent>(entity);

            DrawCapsule(
                transform.Position + Vector3.Transform(collider.Offset, transform.Rotation),
                transform.Rotation,
                collider.Radius,
                collider.Length,
                new Vector4(1.0f, 0.8f, 0.1f, 1.0f));
        }
    }

    private void DrawBox(Vector3 center, Quaternion rotation, Vector3 size, Vector4 color)
    {
        Vector3 h = size * 0.5f;

        Span<Vector3> corners = stackalloc Vector3[8];

        corners[0] = new Vector3(-h.X, -h.Y, -h.Z);
        corners[1] = new Vector3(h.X, -h.Y, -h.Z);
        corners[2] = new Vector3(h.X, -h.Y, h.Z);
        corners[3] = new Vector3(-h.X, -h.Y, h.Z);

        corners[4] = new Vector3(-h.X, h.Y, -h.Z);
        corners[5] = new Vector3(h.X, h.Y, -h.Z);
        corners[6] = new Vector3(h.X, h.Y, h.Z);
        corners[7] = new Vector3(-h.X, h.Y, h.Z);

        for (int i = 0; i < corners.Length; i++)
            corners[i] = center + Vector3.Transform(corners[i], rotation);

        AddLine(corners[0], corners[1], color);
        AddLine(corners[1], corners[2], color);
        AddLine(corners[2], corners[3], color);
        AddLine(corners[3], corners[0], color);

        AddLine(corners[4], corners[5], color);
        AddLine(corners[5], corners[6], color);
        AddLine(corners[6], corners[7], color);
        AddLine(corners[7], corners[4], color);

        AddLine(corners[0], corners[4], color);
        AddLine(corners[1], corners[5], color);
        AddLine(corners[2], corners[6], color);
        AddLine(corners[3], corners[7], color);
    }

    private void DrawCapsule(Vector3 center, Quaternion rotation, float radius, float length, Vector4 color)
    {
        const int segments = 16;

        float halfLength = length * 0.5f;

        Vector3 top = new(0.0f, halfLength, 0.0f);
        Vector3 bottom = new(0.0f, -halfLength, 0.0f);

        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathF.Tau / segments;
            float a1 = (i + 1) * MathF.Tau / segments;

            Vector3 ring0 = new(MathF.Cos(a0) * radius, 0.0f, MathF.Sin(a0) * radius);
            Vector3 ring1 = new(MathF.Cos(a1) * radius, 0.0f, MathF.Sin(a1) * radius);

            AddLocalLine(center, rotation, top + ring0, top + ring1, color);
            AddLocalLine(center, rotation, bottom + ring0, bottom + ring1, color);
        }

        AddLocalLine(center, rotation, new Vector3(radius, halfLength, 0), new Vector3(radius, -halfLength, 0), color);
        AddLocalLine(center, rotation, new Vector3(-radius, halfLength, 0), new Vector3(-radius, -halfLength, 0), color);

        AddLocalLine(center, rotation, new Vector3(0, halfLength, radius), new Vector3(0, -halfLength, radius), color);
        AddLocalLine(center, rotation, new Vector3(0, halfLength, -radius), new Vector3(0, -halfLength, -radius), color);

        DrawCapsuleArc(center, rotation, halfLength, radius, true, true, color);
        DrawCapsuleArc(center, rotation, halfLength, radius, false, true, color);
        DrawCapsuleArc(center, rotation, halfLength, radius, true, false, color);
        DrawCapsuleArc(center, rotation, halfLength, radius, false, false, color);
    }

    private void DrawCapsuleArc(Vector3 center, Quaternion rotation, float halfLength, float radius, bool top, bool xAxis, Vector4 color)
    {
        const int segments = 8;

        float baseY = top ? halfLength : -halfLength;

        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathF.PI / segments;
            float a1 = (i + 1) * MathF.PI / segments;

            float y0 = MathF.Sin(a0) * radius;
            float y1 = MathF.Sin(a1) * radius;

            if (!top)
            {
                y0 = -y0;
                y1 = -y1;
            }

            Vector3 p0;
            Vector3 p1;

            if (xAxis)
            {
                p0 = new Vector3(MathF.Cos(a0) * radius, baseY + y0, 0.0f);
                p1 = new Vector3(MathF.Cos(a1) * radius, baseY + y1, 0.0f);
            }
            else
            {
                p0 = new Vector3(0.0f, baseY + y0, MathF.Cos(a0) * radius);
                p1 = new Vector3(0.0f, baseY + y1, MathF.Cos(a1) * radius);
            }

            AddLocalLine(center, rotation, p0, p1, color);
        }
    }

    private void AddLocalLine(Vector3 center, Quaternion rotation, Vector3 a, Vector3 b, Vector4 color)
    {
        AddLine(
            center + Vector3.Transform(a, rotation),
            center + Vector3.Transform(b, rotation),
            color);
    }

    private void AddLine(Vector3 a, Vector3 b, Vector4 color)
    {
        _lines.Add(new DebugLine(a, b, color));
    }
}