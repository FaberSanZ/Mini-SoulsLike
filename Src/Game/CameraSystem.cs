using System;
using System.Numerics;
using Vultaik;

namespace Vultaik;

public sealed class CameraSystem
{
    public bool TryGetMatrices(World world, float aspectRatio, out Matrix4x4 view, out Matrix4x4 projection)
    {
        foreach (Entity entity in world.Query<TransformComponent, CameraComponent, PrimaryCameraComponent>())
        {
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
            ref CameraComponent camera = ref world.Get<CameraComponent>(entity);

            Vector3 forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
            Vector3 up = Vector3.Transform(Vector3.UnitY, transform.Rotation);

            view = CreateLookToLH(transform.Position, forward, up);
            projection = CreatePerspectiveFovLH(camera.FieldOfView * MathF.PI / 180.0f, aspectRatio, camera.NearPlane, camera.FarPlane);
            return true;
        }

        view = Matrix4x4.Identity;
        projection = Matrix4x4.Identity;
        return false;
    }

    public Matrix4x4 GetViewProjection(World world, float aspectRatio)
    {
        TryGetMatrices(world, aspectRatio, out Matrix4x4 view, out Matrix4x4 projection);
        return view * projection;
    }

    private static Matrix4x4 CreateLookToLH(Vector3 position, Vector3 direction, Vector3 up)
    {
        Vector3 z = Vector3.Normalize(direction);
        Vector3 x = Vector3.Normalize(Vector3.Cross(up, z));
        Vector3 y = Vector3.Cross(z, x);

        return new Matrix4x4(
            x.X, y.X, z.X, 0.0f,
            x.Y, y.Y, z.Y, 0.0f,
            x.Z, y.Z, z.Z, 0.0f,
            -Vector3.Dot(x, position), -Vector3.Dot(y, position), -Vector3.Dot(z, position), 1.0f);
    }

    private static Matrix4x4 CreatePerspectiveFovLH(float fieldOfView, float aspectRatio, float nearPlane, float farPlane)
    {
        float yScale = 1.0f / MathF.Tan(fieldOfView * 0.5f);
        float xScale = yScale / aspectRatio;
        float range = farPlane / (farPlane - nearPlane);

        return new Matrix4x4(
            xScale, 0.0f, 0.0f, 0.0f,
            0.0f, yScale, 0.0f, 0.0f,
            0.0f, 0.0f, range, 1.0f,
            0.0f, 0.0f, -nearPlane * range, 0.0f);
    }
}
