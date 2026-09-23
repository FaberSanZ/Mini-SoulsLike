using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;

namespace Vultaik;

[Flags]
public enum EditorAction
{
    None = 0,
    PlayStarted = 1 << 0,
    PlayStopped = 1 << 1,
    SceneLoaded = 1 << 2,
    SceneChanged = 1 << 3
}

public sealed class EditorSystem
{
    private Entity _selectedEntity = Entity.Null;
    private string _scenePath = "TestScene.json";
    private string _hierarchySearch = string.Empty;
    private string _componentSearch = string.Empty;
    private string _newEntityName = "Entity";
    private string _newSceneName = "Scene";
    private string? _playSnapshot;
    private bool _dirtyBeforePlay;
    private bool _dirty;
    private bool _isPlaying;
    private string _status = "Ready";

    public Entity SelectedEntity => _selectedEntity;
    public bool IsPlaying => _isPlaying;
    public bool IsDirty => _dirty;

    public EditorAction Draw(SceneSystem scenes, ComponentRegistry components, SceneSerializer serializer, AssetSystem assets)
    {
        EditorAction action = EditorAction.None;

        if (_selectedEntity != Entity.Null)
        {
            Scene? active = scenes.ActiveScene;
            if (active is null || !active.World.IsAlive(_selectedEntity)) _selectedEntity = Entity.Null;
        }

        ImGui.SetNextWindowSize(new Vector2(1200.0f, 720.0f), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(_dirty ? "Kairo Editor *" : "Kairo Editor", ImGuiWindowFlags.MenuBar))
        {
            ImGui.End();
            return action;
        }

        action |= DrawMenuBar(scenes, serializer, assets);
        action |= DrawToolbar(scenes, serializer, assets);

        if (ImGui.BeginTable("EditorLayout", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Scenes", ImGuiTableColumnFlags.WidthFixed, 190.0f);
            ImGui.TableSetupColumn("Hierarchy", ImGuiTableColumnFlags.WidthFixed, 280.0f);
            ImGui.TableSetupColumn("Inspector", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextColumn();
            action |= DrawScenes(scenes);

            ImGui.TableNextColumn();
            DrawHierarchy(scenes.ActiveScene?.World, components, assets);

            ImGui.TableNextColumn();
            DrawInspector(scenes.ActiveScene?.World, components, assets);

            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.TextDisabled($"{(_isPlaying ? "PLAY" : "EDIT")} | {_status}");

        ImGui.End();
        return action;
    }

    private EditorAction DrawMenuBar(SceneSystem scenes, SceneSerializer serializer, AssetSystem assets)
    {
        EditorAction action = EditorAction.None;

        if (!ImGui.BeginMenuBar()) return action;

        if (ImGui.BeginMenu("File"))
        {
            bool canUseFile = !_isPlaying && scenes.ActiveScene is not null;

            if (ImGui.MenuItem("Save Scene", "Ctrl+S", false, canUseFile)) SaveActiveScene(scenes, serializer);
            if (ImGui.MenuItem("Load Scene", "Ctrl+O", false, canUseFile)) action |= LoadActiveScene(scenes, serializer, assets);

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Play"))
        {
            if (ImGui.MenuItem("Play", null, false, !_isPlaying && scenes.ActiveScene is not null)) action |= StartPlay(scenes, serializer);
            if (ImGui.MenuItem("Stop", null, false, _isPlaying && scenes.ActiveScene is not null)) action |= StopPlay(scenes, serializer, assets);
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
        return action;
    }

    private EditorAction DrawToolbar(SceneSystem scenes, SceneSerializer serializer, AssetSystem assets)
    {
        EditorAction action = EditorAction.None;

        if (!_isPlaying)
        {
            if (ImGui.Button("Play")) action |= StartPlay(scenes, serializer);
        }
        else
        {
            if (ImGui.Button("Stop")) action |= StopPlay(scenes, serializer, assets);
        }

        ImGui.SameLine();

        if (ImGui.Button("Save") && !_isPlaying) SaveActiveScene(scenes, serializer);

        ImGui.SameLine();

        if (ImGui.Button("Load") && !_isPlaying) action |= LoadActiveScene(scenes, serializer, assets);

        ImGui.SameLine();

        if (ImGui.Button("Rebuild Runtime") && scenes.ActiveScene is not null) TryRebuildRuntime(scenes.ActiveScene.World, assets);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(280.0f);
        ImGui.InputText("Scene File", ref _scenePath, 512);

        return action;
    }

    private EditorAction DrawScenes(SceneSystem scenes)
    {
        EditorAction action = EditorAction.None;

        ImGui.Text("Scenes");
        ImGui.Separator();

        if (!_isPlaying)
        {
            ImGui.SetNextItemWidth(-1.0f);
            ImGui.InputText("##NewScene", ref _newSceneName, 128);

            if (ImGui.Button("Create Scene") && !string.IsNullOrWhiteSpace(_newSceneName))
            {
                Scene scene = scenes.CreateScene(_newSceneName.Trim());
                scenes.SetActiveScene(scene.Name);
                _selectedEntity = Entity.Null;
                _scenePath = $"{scene.Name}.json";
                _dirty = true;
                _status = $"Created scene '{scene.Name}'.";
                action |= EditorAction.SceneChanged;
            }
        }

        ImGui.Separator();

        string? destroyScene = null;

        foreach (Scene scene in scenes.Scenes)
        {
            bool selected = ReferenceEquals(scene, scenes.ActiveScene);

            ImGui.PushID(scene.Name);

            if (ImGui.Selectable($"{scene.Name} ({scene.World.Count})", selected) && !_isPlaying)
            {
                scenes.SetActiveScene(scene.Name);
                _selectedEntity = Entity.Null;
                _status = $"Active scene: {scene.Name}";
                action |= EditorAction.SceneChanged;
            }

            if (!_isPlaying && ImGui.BeginPopupContextItem("SceneContext"))
            {
                if (ImGui.MenuItem("Delete Scene")) destroyScene = scene.Name;
                ImGui.EndPopup();
            }

            ImGui.PopID();
        }

        if (destroyScene is not null)
        {
            scenes.DestroyScene(destroyScene);
            _selectedEntity = Entity.Null;
            _dirty = true;
            _status = $"Deleted scene '{destroyScene}'.";
            action |= EditorAction.SceneChanged;
        }

        return action;
    }

    private void DrawHierarchy(World? world, ComponentRegistry components, AssetSystem assets)
    {
        ImGui.Text("Hierarchy");
        ImGui.Separator();

        if (world is null)
        {
            ImGui.TextDisabled("No active scene.");
            return;
        }

        ImGui.SetNextItemWidth(-1.0f);
        ImGui.InputText("##HierarchySearch", ref _hierarchySearch, 128);

        if (ImGui.Button("Create"))
        {
            _selectedEntity = EntityFactory.Create(world, string.IsNullOrWhiteSpace(_newEntityName) ? "Entity" : _newEntityName.Trim());
            _dirty = true;
            _status = "Entity created.";
        }

        ImGui.SameLine();

        bool hasSelection = _selectedEntity != Entity.Null && world.IsAlive(_selectedEntity);

        if (ImGui.Button("Duplicate") && hasSelection) DuplicateEntity(world, components, assets);
        ImGui.SameLine();

        if (ImGui.Button("Delete") && hasSelection) DeleteSelected(world);

        ImGui.SetNextItemWidth(-1.0f);
        ImGui.InputText("Name##NewEntity", ref _newEntityName, 128);
        ImGui.Separator();

        Entity deleteEntity = Entity.Null;
        Entity duplicateEntity = Entity.Null;

        foreach (Entity entity in world.Entities)
        {
            string name = GetEntityName(world, entity);

            if (!string.IsNullOrWhiteSpace(_hierarchySearch) && name.IndexOf(_hierarchySearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

            bool selected = entity == _selectedEntity;

            ImGui.PushID(entity.Id);

            if (ImGui.Selectable(name, selected)) _selectedEntity = entity;

            if (ImGui.BeginPopupContextItem("EntityContext"))
            {
                if (ImGui.MenuItem("Duplicate")) duplicateEntity = entity;
                if (ImGui.MenuItem("Delete")) deleteEntity = entity;
                ImGui.EndPopup();
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Entity {entity.Id}:{entity.Version}");
            ImGui.PopID();
        }

        if (duplicateEntity != Entity.Null)
        {
            _selectedEntity = duplicateEntity;
            DuplicateEntity(world, components, assets);
        }

        if (deleteEntity != Entity.Null)
        {
            _selectedEntity = deleteEntity;
            DeleteSelected(world);
        }
    }

    private void DrawInspector(World? world, ComponentRegistry components, AssetSystem assets)
    {
        ImGui.Text("Inspector");
        ImGui.Separator();

        if (world is null || _selectedEntity == Entity.Null || !world.IsAlive(_selectedEntity))
        {
            ImGui.TextDisabled("Select an entity.");
            return;
        }

        ImGui.TextDisabled($"Entity {_selectedEntity.Id}:{_selectedEntity.Version}");

        ComponentDescriptor? removeComponent = null;

        foreach (ComponentDescriptor component in components.Components)
        {
            if (!component.Has(world, _selectedEntity)) continue;

            ImGui.PushID(component.Name);

            bool open = ImGui.CollapsingHeader(component.Name, ImGuiTreeNodeFlags.DefaultOpen);

            if (!IsCoreComponent(component.Type))
            {
                ImGui.SameLine();

                if (ImGui.SmallButton("Remove")) removeComponent = component;
            }

            if (open)
            {
                if (component.Members.Count == 0)
                {
                    ImGui.TextDisabled("Tag / runtime component.");
                }
                else
                {
                    object boxed = component.GetBoxed(world, _selectedEntity);
                    bool changed = false;
                    bool rebuildRuntime = false;

                    foreach (ComponentMemberDescriptor member in component.Members)
                    {
                        object? value = member.GetValue(boxed);
                        bool assetPath = component.Type == typeof(ModelComponent) && member.Field.Name == nameof(ModelComponent.AssetPath);
                        bool memberChanged = DrawMember(member.DisplayName, member.ValueType, value, out object? edited);
                        bool editingFinished = assetPath && ImGui.IsItemDeactivatedAfterEdit();

                        if (memberChanged)
                        {
                            member.SetValue(boxed, edited);
                            changed = true;
                        }

                        if (editingFinished) rebuildRuntime = true;
                    }

                    if (changed)
                    {
                        component.SetBoxed(world, _selectedEntity, boxed);
                        _dirty = true;
                    }

                    if (rebuildRuntime) TryRebuildRuntime(world, assets);
                }
            }

            ImGui.PopID();
        }

        if (removeComponent is not null)
        {
            removeComponent.Remove(world, _selectedEntity);
            TryRebuildRuntime(world, assets);
            _dirty = true;
            _status = $"Removed {removeComponent.Name}.";
        }

        ImGui.Separator();

        if (ImGui.Button("Add Component")) ImGui.OpenPopup("AddComponentPopup");

        DrawAddComponentPopup(world, components, assets);
    }

    private void DrawAddComponentPopup(World world, ComponentRegistry components, AssetSystem assets)
    {
        if (!ImGui.BeginPopup("AddComponentPopup")) return;

        ImGui.SetNextItemWidth(300.0f);
        ImGui.InputText("Search", ref _componentSearch, 128);
        ImGui.Separator();

        string? currentCategory = null;

        foreach (ComponentDescriptor component in components.Components)
        {
            if (component.Has(world, _selectedEntity)) continue;
            if (!string.IsNullOrWhiteSpace(_componentSearch) && component.Name.IndexOf(_componentSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (!string.Equals(currentCategory, component.Category, StringComparison.Ordinal))
            {
                currentCategory = component.Category;
                ImGui.TextDisabled(currentCategory);
            }

            if (!ImGui.Selectable(component.Name)) continue;

            component.Add(world, _selectedEntity);
            EditorComponentInitializer.Initialize(world, _selectedEntity, component.Type);
            TryRebuildRuntime(world, assets);

            _dirty = true;
            _status = $"Added {component.Name}.";
            ImGui.CloseCurrentPopup();
            break;
        }

        ImGui.EndPopup();
    }

    private void DuplicateEntity(World world, ComponentRegistry components, AssetSystem assets)
    {
        if (_selectedEntity == Entity.Null || !world.IsAlive(_selectedEntity)) return;

        Entity source = _selectedEntity;
        Entity target = EntityFactory.Create(world, $"{GetEntityName(world, source)} Copy");

        foreach (ComponentDescriptor component in components.Components)
        {
            if (component.Type == typeof(IDComponent) || component.Type == typeof(NameComponent) || component.Type == typeof(TransformComponent)) continue;
            if (!component.Has(world, source)) continue;

            component.Add(world, target);

            if (component.Members.Count == 0) continue;

            object sourceBoxed = component.GetBoxed(world, source);
            object targetBoxed = component.GetBoxed(world, target);

            foreach (ComponentMemberDescriptor member in component.Members) member.SetValue(targetBoxed, member.GetValue(sourceBoxed));

            component.SetBoxed(world, target, targetBoxed);
        }

        if (world.Has<TransformComponent>(source))
        {
            ref TransformComponent sourceTransform = ref world.Get<TransformComponent>(source);
            ref TransformComponent targetTransform = ref world.Get<TransformComponent>(target);
            targetTransform = sourceTransform;
        }

        TryRebuildRuntime(world, assets);

        _selectedEntity = target;
        _dirty = true;
        _status = "Entity duplicated.";
    }

    private void DeleteSelected(World world)
    {
        if (_selectedEntity == Entity.Null || !world.IsAlive(_selectedEntity)) return;

        world.Destroy(_selectedEntity);
        _selectedEntity = Entity.Null;
        _dirty = true;
        _status = "Entity deleted.";
    }

    private EditorAction StartPlay(SceneSystem scenes, SceneSerializer serializer)
    {
        Scene? scene = scenes.ActiveScene;
        if (scene is null || _isPlaying) return EditorAction.None;

        _playSnapshot = serializer.Serialize(scene);
        _dirtyBeforePlay = _dirty;
        _isPlaying = true;
        _status = "Play mode started.";
        return EditorAction.PlayStarted;
    }

    private EditorAction StopPlay(SceneSystem scenes, SceneSerializer serializer, AssetSystem assets)
    {
        Scene? scene = scenes.ActiveScene;
        if (scene is null || !_isPlaying) return EditorAction.None;

        if (_playSnapshot is not null && serializer.Deserialize(scene, _playSnapshot)) TryRebuildRuntime(scene.World, assets);

        _selectedEntity = Entity.Null;
        _dirty = _dirtyBeforePlay;
        _playSnapshot = null;
        _isPlaying = false;
        _status = "Play mode stopped. Scene restored.";
        return EditorAction.PlayStopped;
    }

    private void SaveActiveScene(SceneSystem scenes, SceneSerializer serializer)
    {
        Scene? scene = scenes.ActiveScene;

        if (scene is null)
        {
            _status = "No active scene.";
            return;
        }

        if (serializer.Save(scene, _scenePath))
        {
            _dirty = false;
            _status = $"Saved {_scenePath}.";
        }
        else
        {
            _status = $"Failed to save {_scenePath}.";
        }
    }

    private EditorAction LoadActiveScene(SceneSystem scenes, SceneSerializer serializer, AssetSystem assets)
    {
        Scene? scene = scenes.ActiveScene;

        if (scene is null)
        {
            _status = "No active scene.";
            return EditorAction.None;
        }

        if (!serializer.Load(scene, _scenePath))
        {
            _status = $"Failed to load {_scenePath}.";
            return EditorAction.None;
        }

        TryRebuildRuntime(scene.World, assets);
        _selectedEntity = Entity.Null;
        _dirty = false;
        _status = $"Loaded {_scenePath}.";
        return EditorAction.SceneLoaded;
    }

    private bool TryRebuildRuntime(World world, AssetSystem assets)
    {
        try
        {
            SceneRuntime.Rebuild(world, assets);
            _status = "Runtime rebuilt.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _status = $"Runtime rebuild failed: {exception.Message}";
            return false;
        }
    }

    private static bool IsCoreComponent(Type type) => type == typeof(IDComponent) || type == typeof(NameComponent) || type == typeof(TransformComponent);

    private static string GetEntityName(World world, Entity entity)
    {
        if (!world.Has<NameComponent>(entity)) return $"Entity {entity.Id}";

        ref NameComponent name = ref world.Get<NameComponent>(entity);
        return string.IsNullOrWhiteSpace(name.Name) ? $"Entity {entity.Id}" : name.Name;
    }

    private static bool DrawMember(string label, Type type, object? value, out object? edited)
    {
        edited = value;

        if (type == typeof(float))
        {
            float v = value is float current ? current : 0.0f;
            if (!ImGui.DragFloat(label, ref v, 0.05f)) return false;
            edited = v;
            return true;
        }

        if (type == typeof(int))
        {
            int v = value is int current ? current : 0;
            if (!ImGui.DragInt(label, ref v)) return false;
            edited = v;
            return true;
        }

        if (type == typeof(bool))
        {
            bool v = value is bool current && current;
            if (!ImGui.Checkbox(label, ref v)) return false;
            edited = v;
            return true;
        }

        if (type == typeof(string))
        {
            string v = value as string ?? string.Empty;
            if (!ImGui.InputText(label, ref v, 512)) return false;
            edited = v;
            return true;
        }

        if (type == typeof(Vector2))
        {
            Vector2 v = value is Vector2 current ? current : Vector2.Zero;
            if (!ImGui.DragFloat2(label, ref v, 0.05f)) return false;
            edited = v;
            return true;
        }

        if (type == typeof(Vector3))
        {
            Vector3 v = value is Vector3 current ? current : Vector3.Zero;
            if (!ImGui.DragFloat3(label, ref v, 0.05f)) return false;
            edited = v;
            return true;
        }

        if (type == typeof(Vector4))
        {
            Vector4 v = value is Vector4 current ? current : Vector4.Zero;
            bool changed = label.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0 ? ImGui.ColorEdit4(label, ref v) : ImGui.DragFloat4(label, ref v, 0.05f);
            if (!changed) return false;
            edited = v;
            return true;
        }

        if (type == typeof(Quaternion))
        {
            Quaternion q = value is Quaternion current ? current : Quaternion.Identity;
            Vector4 v = new(q.X, q.Y, q.Z, q.W);

            if (!ImGui.DragFloat4(label, ref v, 0.01f)) return false;

            Quaternion result = new(v.X, v.Y, v.Z, v.W);
            edited = result.LengthSquared() > 0.000001f ? Quaternion.Normalize(result) : Quaternion.Identity;
            return true;
        }

        if (type.IsEnum)
        {
            Array values = Enum.GetValues(type);
            string preview = value?.ToString() ?? values.GetValue(0)?.ToString() ?? string.Empty;
            bool changed = false;

            if (ImGui.BeginCombo(label, preview))
            {
                foreach (object item in values)
                {
                    bool selected = Equals(item, value);

                    if (ImGui.Selectable(item.ToString() ?? string.Empty, selected))
                    {
                        edited = item;
                        changed = true;
                    }

                    if (selected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            return changed;
        }

        if (type == typeof(ulong))
        {
            ImGui.Text($"{label}: {(value is ulong v ? v : 0UL)}");
            return false;
        }

        ImGui.TextDisabled($"{label}: {value}");
        return false;
    }
}

public sealed class EditorCameraSystem
{
    private Vector3 _position = new(-8.0f, 4.0f, -8.0f);
    private float _yaw = MathF.PI * 0.25f;
    private float _pitch = -0.25f;
    private float _moveSpeed = 8.0f;
    private float _fastSpeed = 20.0f;
    private float _slowSpeed = 3.0f;
    private float _lookSensitivity = 0.0025f;
    private bool _active;

    public Vector3 Position => _position;
    public float Yaw => _yaw;
    public float Pitch => _pitch;
    public bool Active => _active;

    public void Reset(World world)
    {
        Vector3 target = Vector3.Zero;
        bool found = false;

        foreach (Entity entity in world.Query<PlayerComponent, TransformComponent>())
        {
            ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
            target = transform.Position + new Vector3(0.0f, 2.0f, 0.0f);
            found = true;
            break;
        }

        if (!found)
        {
            foreach (Entity entity in world.Query<ModelComponent, TransformComponent>())
            {
                ref TransformComponent transform = ref world.Get<TransformComponent>(entity);
                target = transform.Position;
                found = true;
                break;
            }
        }

        if (!found) target = Vector3.Zero;

        _position = target + new Vector3(-8.0f, 4.5f, -8.0f);

        Vector3 direction = Vector3.Normalize(target - _position);
        _yaw = MathF.Atan2(direction.X, direction.Z);
        _pitch = Math.Clamp(MathF.Asin(direction.Y), -1.2f, 1.2f);
        _active = false;

        GameInput.SetMouseMode(MouseMode.Absolute);
    }

    public void Update(float deltaTime)
    {
        bool rightMouseDown = GameInput.IsMouseButtonDown(MouseButton.Right);

        if (!rightMouseDown)
        {
            if (_active) GameInput.SetMouseMode(MouseMode.Absolute);
            _active = false;
            return;
        }

        if (!ImGui.GetIO().WantTextInput)
        {
            _active = true;
            GameInput.SetMouseMode(MouseMode.Relative);

            _yaw -= GameInput.MouseDeltaX * _lookSensitivity;
            _pitch += GameInput.MouseDeltaY * _lookSensitivity;
            _pitch = Math.Clamp(_pitch, -1.5f, 1.5f);

            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0.0f);
            Vector3 right = Vector3.Transform(Vector3.UnitX, rotation);
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, rotation);
            Vector3 flatForward = new(forward.X, 0.0f, forward.Z);

            if (flatForward.LengthSquared() > 0.000001f) flatForward = Vector3.Normalize(flatForward);
            else flatForward = Vector3.UnitZ;

            Vector3 movement = Vector3.Zero;

            if (GameInput.IsKeyDown(KeyCode.W)) movement += flatForward;
            if (GameInput.IsKeyDown(KeyCode.S)) movement -= flatForward;
            if (GameInput.IsKeyDown(KeyCode.D)) movement -= right;
            if (GameInput.IsKeyDown(KeyCode.A)) movement += right;
            if (GameInput.IsKeyDown(KeyCode.E)) movement += Vector3.UnitY;
            if (GameInput.IsKeyDown(KeyCode.Q)) movement -= Vector3.UnitY;

            if (movement.LengthSquared() > 0.000001f) movement = Vector3.Normalize(movement);

            float speed = _moveSpeed;
            if (GameInput.IsKeyDown(KeyCode.Shift)) speed = _fastSpeed;
            if (GameInput.IsKeyDown(KeyCode.Control)) speed = _slowSpeed;

            _position += movement * speed * deltaTime;
        }
    }

    public bool TryGetMatrices(float aspectRatio, out Matrix4x4 view, out Matrix4x4 projection)
    {
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0.0f);
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, rotation);
        Vector3 target = _position + forward;

        view = Matrix4x4.CreateLookAt(_position, target, Vector3.UnitY);
        projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.0f, aspectRatio, 0.1f, 1000.0f);
        return true;
    }
}
