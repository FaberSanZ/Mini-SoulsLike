using System;
using System.Numerics;

namespace Vultaik;

public sealed class Game : GameBase
{
    private float _walkSpeed = 3.0f;
    private float _runSpeed = 15.0f;
    private float _rollSpeed = 8.0f;
    private float _playerRotationSpeed = 8.0f;

    private float _bossMoveSpeed = 2.2f;
    private float _bossRotationSpeed = 5.0f;
    private float _bossDetectionRange = 12.0f;
    private float _bossAttackRange = 2.3f;

    private float _cameraYaw = MathF.PI * 0.5f;
    private float _cameraPitch = -0.22f;
    private float _cameraDistance = 10.0f;
    private float _cameraTargetHeight = 5.4f;
    private float _cameraSensitivity = 0.0025f;

    protected override string Title => "Kairo";

    protected override void OnInitialize(World world)
    {
        Scene scene = Scenes.ActiveScene ?? throw new InvalidOperationException("No active scene.");

        if (!Serializer.Load(scene, "TestScene.json")) throw new InvalidOperationException("Failed to load TestScene.json.");

        SceneRuntime.Rebuild(world, Assets);
        ResetCamera(world);
    }

    protected override void OnSceneLoaded(World world)
    {
        ResetCamera(world);
    }

    protected override void OnSceneChanged(World world)
    {
        ResetCamera(world);
    }

    protected override void OnPlay(World world)
    {
        foreach (Entity entity in world.Query<PlayerComponent>())
        {
            ref PlayerComponent player = ref world.Get<PlayerComponent>(entity);
            player.State = PlayerState.None;
            player.RollDirection = Vector3.UnitZ;
            player.PreviousAttack = false;
            player.PreviousRoll = false;
            player.PreviousDeath = false;
            SetPlayerState(world, entity, ref player, PlayerState.Idle);
        }

        foreach (Entity entity in world.Query<BossComponent>())
        {
            ref BossComponent boss = ref world.Get<BossComponent>(entity);
            boss.State = BossState.None;
            boss.AttackCooldown = 0.0f;
            SetBossState(world, entity, ref boss, BossState.Idle);
        }

        ResetCamera(world);
    }

    protected override void OnUpdate(World world, float deltaTime)
    {
        UpdateCameraInput();
        UpdatePlayer(world, deltaTime);
        UpdateBoss(world, deltaTime);
        UpdateThirdPersonCamera(world);
    }

    protected override void OnStop(World world)
    {
        GameInput.SetMouseMode(MouseMode.Absolute);
    }

    protected override void OnDestroy(World world)
    {
        GameInput.SetMouseMode(MouseMode.Absolute);
    }

    private void SetPlayerState(World world, Entity entity, ref PlayerComponent player, PlayerState state)
    {
        if (player.State == state) return;

        player.State = state;

        ref ModelComponent model = ref world.Get<ModelComponent>(entity);
        ref AnimationComponent animation = ref world.Get<AnimationComponent>(entity);

        if (model.Model is null || animation.State is null) return;

        switch (state)
        {
            case PlayerState.Idle: Animations.Play(model.Model, animation.State, "Idle", true, 0.25f); break;
            case PlayerState.Walk: Animations.Play(model.Model, animation.State, "Walking", true, 0.25f); break;
            case PlayerState.Run: Animations.Play(model.Model, animation.State, "Run", true, 0.20f); break;
            case PlayerState.Roll: Animations.Play(model.Model, animation.State, "Roll", false, 0.08f); break;
            case PlayerState.Attack: Animations.Play(model.Model, animation.State, "swordAttackJump", false, 0.10f); break;
            case PlayerState.Death: Animations.Play(model.Model, animation.State, "Death", false, 0.20f); break;
        }
    }

    private void SetBossState(World world, Entity entity, ref BossComponent boss, BossState state)
    {
        if (boss.State == state) return;

        boss.State = state;

        ref ModelComponent model = ref world.Get<ModelComponent>(entity);
        ref AnimationComponent animation = ref world.Get<AnimationComponent>(entity);

        if (model.Model is null || animation.State is null) return;

        switch (state)
        {
            case BossState.Idle: Animations.Play(model.Model, animation.State, "SpiderArmature|Spider_Idle", true, 0.18f); break;
            case BossState.Chase: Animations.Play(model.Model, animation.State, "SpiderArmature|Spider_Walk", true, 0.12f); break;
            case BossState.Attack: Animations.Play(model.Model, animation.State, "SpiderArmature|Spider_Attack", false, 0.08f); break;
            case BossState.Death: Animations.Play(model.Model, animation.State, "SpiderArmature|Spider_Death", false, 0.20f); break;
        }
    }

    private void UpdatePlayer(World world, float deltaTime)
    {
        foreach (Entity entity in world.Query<PlayerComponent, TransformComponent>())
        {
            ref PlayerComponent player = ref world.Get<PlayerComponent>(entity);
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);

            bool attackDown = GameInput.IsMouseButtonDown(MouseButton.Left);
            bool rollDown = GameInput.IsKeyDown(KeyCode.Space);
            bool deathDown = GameInput.IsKeyDown(KeyCode.T);

            bool attackPressed = attackDown && !player.PreviousAttack;
            bool rollPressed = rollDown && !player.PreviousRoll;
            bool deathPressed = deathDown && !player.PreviousDeath;

            player.PreviousAttack = attackDown;
            player.PreviousRoll = rollDown;
            player.PreviousDeath = deathDown;

            if (deathPressed)
            {
                SetPlayerState(world, entity, ref player, PlayerState.Death);
                continue;
            }

            if (player.State == PlayerState.Death) continue;

            if (player.State == PlayerState.Roll)
            {
                if (!AnimationFinished(world, entity))
                {
                    transform.Position.X += player.RollDirection.X * _rollSpeed * deltaTime;
                    transform.Position.Z += player.RollDirection.Z * _rollSpeed * deltaTime;
                    continue;
                }

                SetPlayerState(world, entity, ref player, PlayerState.Idle);
            }

            if (player.State == PlayerState.Attack)
            {
                if (!AnimationFinished(world, entity)) continue;
                SetPlayerState(world, entity, ref player, PlayerState.Idle);
            }

            Vector3 movementDirection = GetMovementDirection();
            bool moving = movementDirection.X != 0.0f || movementDirection.Z != 0.0f;

            if (attackPressed)
            {
                SetPlayerState(world, entity, ref player, PlayerState.Attack);
                continue;
            }

            if (rollPressed)
            {
                if (moving) player.RollDirection = movementDirection;
                else
                {
                    Vector3 forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
                    forward.Y = 0.0f;
                    player.RollDirection = NormalizeDirection(forward);
                }

                FaceDirection(ref transform, player.RollDirection, deltaTime, 14.0f);
                SetPlayerState(world, entity, ref player, PlayerState.Roll);
                continue;
            }

            if (!moving)
            {
                SetPlayerState(world, entity, ref player, PlayerState.Idle);
                continue;
            }

            bool running = GameInput.IsKeyDown(KeyCode.Shift);
            float speed = running ? _runSpeed : _walkSpeed;

            transform.Position.X += movementDirection.X * speed * deltaTime;
            transform.Position.Z += movementDirection.Z * speed * deltaTime;

            FaceDirection(ref transform, movementDirection, deltaTime, _playerRotationSpeed);
            SetPlayerState(world, entity, ref player, running ? PlayerState.Run : PlayerState.Walk);
        }
    }

    private void UpdateBoss(World world, float deltaTime)
    {
        Entity playerEntity = Entity.Null;

        foreach (Entity entity in world.Query<PlayerComponent, TransformComponent>())
        {
            playerEntity = entity;
            break;
        }

        if (playerEntity == Entity.Null) return;

        ref TransformComponent playerTransform = ref world.Get<TransformComponent>(playerEntity);

        foreach (Entity entity in world.Query<BossComponent, TransformComponent>())
        {
            ref BossComponent boss = ref world.Get<BossComponent>(entity);
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);

            if (boss.State == BossState.Death) continue;

            if (boss.AttackCooldown > 0.0f) boss.AttackCooldown -= deltaTime;

            if (boss.State == BossState.Attack)
            {
                if (!AnimationFinished(world, entity)) continue;
                boss.AttackCooldown = 0.8f;
                SetBossState(world, entity, ref boss, BossState.Idle);
            }

            Vector3 direction = new(playerTransform.Position.X - transform.Position.X, 0.0f, playerTransform.Position.Z - transform.Position.Z);
            float distance = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);

            if (distance > _bossDetectionRange)
            {
                SetBossState(world, entity, ref boss, BossState.Idle);
                continue;
            }

            if (distance > 0.0001f)
            {
                direction /= distance;
                FaceDirection(ref transform, direction, deltaTime, _bossRotationSpeed);
            }

            if (distance <= _bossAttackRange)
            {
                SetBossState(world, entity, ref boss, boss.AttackCooldown <= 0.0f ? BossState.Attack : BossState.Idle);
                continue;
            }

            transform.Position.X += direction.X * _bossMoveSpeed * deltaTime;
            transform.Position.Z += direction.Z * _bossMoveSpeed * deltaTime;

            SetBossState(world, entity, ref boss, BossState.Chase);
        }
    }

    private Vector3 GetMovementDirection()
    {
        float inputX = 0.0f;
        float inputZ = 0.0f;

        if (GameInput.IsKeyDown(KeyCode.W)) inputZ += 1.0f;
        if (GameInput.IsKeyDown(KeyCode.S)) inputZ -= 1.0f;
        if (GameInput.IsKeyDown(KeyCode.D)) inputX += 1.0f;
        if (GameInput.IsKeyDown(KeyCode.A)) inputX -= 1.0f;

        if (inputX == 0.0f && inputZ == 0.0f) return Vector3.Zero;

        Quaternion yawRotation = Quaternion.CreateFromYawPitchRoll(_cameraYaw, 0.0f, 0.0f);
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, yawRotation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, yawRotation);
        Vector3 direction = Vector3.Normalize(forward * inputZ + right * inputX);

        direction.Y = 0.0f;
        return direction;
    }

    private static void FaceDirection(ref TransformComponent transform, Vector3 direction, float deltaTime, float rotationSpeed)
    {
        if (direction.X == 0.0f && direction.Z == 0.0f) return;

        float yaw = MathF.Atan2(direction.X, direction.Z);
        Quaternion targetRotation = Quaternion.CreateFromYawPitchRoll(yaw, 0.0f, 0.0f);
        float factor = 1.0f - MathF.Exp(-rotationSpeed * deltaTime);

        transform.Rotation = Quaternion.Normalize(Quaternion.Slerp(transform.Rotation, targetRotation, factor));
    }

    private static Vector3 NormalizeDirection(Vector3 direction)
    {
        float lengthSquared = direction.X * direction.X + direction.Z * direction.Z;
        if (lengthSquared <= 0.0f) return direction;

        float inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
        direction.X *= inverseLength;
        direction.Z *= inverseLength;
        return direction;
    }

    private void ResetCamera(World world)
    {
        _cameraYaw = MathF.PI * 0.5f;
        _cameraPitch = -0.22f;
        UpdateThirdPersonCamera(world);
    }

    private void UpdateCameraInput()
    {
        if (GameInput.IsMouseButtonDown(MouseButton.Right))
        {
            GameInput.SetMouseMode(MouseMode.Relative);
            _cameraYaw += GameInput.MouseDeltaX * _cameraSensitivity;
            _cameraPitch += GameInput.MouseDeltaY * _cameraSensitivity;
            _cameraPitch = Math.Clamp(_cameraPitch, -0.90f, 0.35f);
        }
        else
        {
            GameInput.SetMouseMode(MouseMode.Absolute);
        }
    }

    private void UpdateThirdPersonCamera(World world)
    {
        Entity playerEntity = Entity.Null;
        Entity cameraEntity = Entity.Null;

        foreach (Entity entity in world.Query<PlayerComponent, TransformComponent>())
        {
            playerEntity = entity;
            break;
        }

        foreach (Entity entity in world.Query<PrimaryCameraComponent, TransformComponent>())
        {
            cameraEntity = entity;
            break;
        }

        if (playerEntity == Entity.Null || cameraEntity == Entity.Null) return;

        ref TransformComponent playerTransform = ref world.Get<TransformComponent>(playerEntity);
        ref TransformComponent cameraTransform = ref world.Get<TransformComponent>(cameraEntity);

        Vector3 target = playerTransform.Position + new Vector3(0.0f, _cameraTargetHeight, 0.0f);
        Quaternion cameraRotation = Quaternion.CreateFromYawPitchRoll(_cameraYaw, _cameraPitch, 0.0f);
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, cameraRotation);

        cameraTransform.Position = target - forward * _cameraDistance;
        cameraTransform.Rotation = cameraRotation;
    }

    private bool AnimationFinished(World world, Entity entity)
    {
        if (!world.Has<AnimationComponent>(entity)) return true;

        ref AnimationComponent animation = ref world.Get<AnimationComponent>(entity);
        return animation.State is null || Animations.Finished(animation.State);
    }
}
