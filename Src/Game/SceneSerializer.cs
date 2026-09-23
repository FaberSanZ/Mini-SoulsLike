using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vultaik;

public sealed class SceneSerializer
{
    private readonly ComponentRegistry _components;
    private readonly JsonSerializerOptions _options;

    public SceneSerializer(ComponentRegistry components)
    {
        _components = components;
        _options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true, PropertyNameCaseInsensitive = false };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public string Serialize(Scene scene)
    {
        using MemoryStream stream = new();

        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Scene", scene.Name);
            writer.WriteStartArray("Entities");

            foreach (Entity entity in scene.World.Entities)
            {
                writer.WriteStartObject();
                writer.WriteStartObject("Components");

                foreach (ComponentDescriptor component in _components.Components)
                {
                    if (!component.Has(scene.World, entity)) continue;

                    writer.WriteStartObject(component.Name);

                    if (component.Members.Count > 0)
                    {
                        object boxed = component.GetBoxed(scene.World, entity);

                        foreach (ComponentMemberDescriptor member in component.Members)
                        {
                            writer.WritePropertyName(member.Name);
                            JsonSerializer.Serialize(writer, member.GetValue(boxed), member.ValueType, _options);
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public bool Deserialize(Scene scene, string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("Entities", out JsonElement entities) || entities.ValueKind != JsonValueKind.Array) return false;

            scene.World.Clear();

            foreach (JsonElement entityNode in entities.EnumerateArray())
            {
                if (!entityNode.TryGetProperty("Components", out JsonElement componentsNode) || componentsNode.ValueKind != JsonValueKind.Object) continue;

                Entity entity = scene.World.Create();

                foreach (JsonProperty componentNode in componentsNode.EnumerateObject())
                {
                    if (!_components.TryGet(componentNode.Name, out ComponentDescriptor descriptor)) continue;
                    if (componentNode.Value.ValueKind != JsonValueKind.Object) continue;

                    descriptor.Add(scene.World, entity);

                    if (descriptor.Members.Count == 0) continue;

                    object boxed = descriptor.GetBoxed(scene.World, entity);

                    foreach (ComponentMemberDescriptor member in descriptor.Members)
                    {
                        if (!componentNode.Value.TryGetProperty(member.Name, out JsonElement valueNode)) continue;

                        object? value = JsonSerializer.Deserialize(valueNode.GetRawText(), member.ValueType, _options);
                        member.SetValue(boxed, value);
                    }

                    descriptor.SetBoxed(scene.World, entity, boxed);
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            scene.World.Clear();
            return false;
        }
    }

    public bool Save(Scene scene, string filePath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(filePath, Serialize(scene), Encoding.UTF8);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    public bool Load(Scene scene, string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            return Deserialize(scene, File.ReadAllText(filePath, Encoding.UTF8));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}


public static class SceneRuntime
{
    public static void Rebuild(World world, AssetSystem assets)
    {
        foreach (Entity entity in world.Query<ModelComponent>())
        {
            ref ModelComponent model = ref world.Get<ModelComponent>(entity);

            if (string.IsNullOrWhiteSpace(model.AssetPath)) continue;

            model.Model = assets.LoadModel(model.AssetPath);

            if (world.Has<AnimationComponent>(entity))
            {
                ref AnimationComponent animation = ref world.Get<AnimationComponent>(entity);
                animation.State = new AnimationState();
            }

            if (model.Model is not null && world.Has<SkeletonComponent>(entity))
            {
                ref SkeletonComponent skeleton = ref world.Get<SkeletonComponent>(entity);
                skeleton.State = new SkeletonState(model.Model);
            }
        }
    }
}
