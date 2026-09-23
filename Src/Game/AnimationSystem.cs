using System.Numerics;

namespace Vultaik;

public sealed class AnimationState
{
    public uint AnimationIndex;
    public float Time;

    public bool Loop = true;
    public bool Playing;
    public bool Finished;

    public bool Blending;

    public uint SourceAnimationIndex;
    public float SourceTime;
    public bool SourceLoop = true;

    public float BlendTime;
    public float BlendDuration;

    public NodePose[] Pose = [];

    internal NodePose[] SourcePose = [];
    internal NodePose[] TargetPose = [];
}

public sealed class AnimationSystem
{
    public bool Play(Model model, AnimationState animation, string animationName, bool loop = true, float blendDuration = 0.20f, bool restart = false)
    {
        for (int i = 0; i < model.Animations.Length; i++)
        {
            if (!string.Equals(model.Animations[i].Name, animationName, StringComparison.Ordinal))
                continue;

            if (!restart && animation.AnimationIndex == (uint)i && animation.Playing)
                return true;

            uint newAnimationIndex = (uint)i;
            AnimationClip targetClip = model.Animations[(int)newAnimationIndex];

            float normalizedTime = 0.0f;

            if (animation.AnimationIndex < (uint)model.Animations.Length)
            {
                AnimationClip currentClip = model.Animations[(int)animation.AnimationIndex];

                if (animation.Loop && loop && currentClip.Duration > 0.0f)
                    normalizedTime = animation.Time / currentClip.Duration;
            }

            bool canBlend = animation.Pose.Length != 0 && animation.AnimationIndex < (uint)model.Animations.Length && blendDuration > 0.0f;

            if (canBlend)
            {
                animation.SourceAnimationIndex = animation.AnimationIndex;
                animation.SourceTime = animation.Time;
                animation.SourceLoop = animation.Loop;

                animation.Blending = true;
                animation.BlendTime = 0.0f;
                animation.BlendDuration = blendDuration;
            }
            else
            {
                animation.Blending = false;
                animation.BlendTime = 0.0f;
                animation.BlendDuration = 0.0f;
            }

            animation.AnimationIndex = newAnimationIndex;
            animation.Time = loop && targetClip.Duration > 0.0f ? normalizedTime * targetClip.Duration : 0.0f;
            animation.Loop = loop;
            animation.Playing = true;
            animation.Finished = false;

            if (animation.Pose.Length == 0)
            {
                ResetPose(model, ref animation.Pose);
                Evaluate(animation.Pose, targetClip, animation.Time);
            }

            return true;
        }

        return false;
    }

    public void Stop(AnimationState animation)
    {
        animation.Playing = false;
        animation.Blending = false;
    }

    public bool IsPlaying(AnimationState animation) => animation.Playing;
    public bool Finished(AnimationState animation) => animation.Finished;

    public bool IsCurrent(Model model, AnimationState animation, string animationName)
    {
        if (animation.AnimationIndex >= (uint)model.Animations.Length)
            return false;

        return string.Equals(model.Animations[(int)animation.AnimationIndex].Name, animationName, StringComparison.Ordinal);
    }

    public void Update(Model model, AnimationState animation, float deltaTime)
    {
        if (animation.AnimationIndex >= (uint)model.Animations.Length)
            return;

        AnimationClip targetClip = model.Animations[(int)animation.AnimationIndex];

        UpdateTargetTime(animation, targetClip, deltaTime);

        if (!animation.Blending)
        {
            ResetPose(model, ref animation.Pose);
            Evaluate(animation.Pose, targetClip, animation.Time);
            return;
        }

        if (animation.SourceAnimationIndex >= (uint)model.Animations.Length)
        {
            animation.Blending = false;

            ResetPose(model, ref animation.Pose);
            Evaluate(animation.Pose, targetClip, animation.Time);
            return;
        }

        AnimationClip sourceClip = model.Animations[(int)animation.SourceAnimationIndex];

        UpdateSourceTime(animation, sourceClip, deltaTime);

        ResetPose(model, ref animation.SourcePose);
        ResetPose(model, ref animation.TargetPose);

        Evaluate(animation.SourcePose, sourceClip, animation.SourceTime);
        Evaluate(animation.TargetPose, targetClip, animation.Time);

        animation.BlendTime += deltaTime;

        float factor = animation.BlendDuration > 0.0f ? animation.BlendTime / animation.BlendDuration : 1.0f;
        factor = Math.Clamp(factor, 0.0f, 1.0f);

        float smoothFactor = factor * factor * (3.0f - 2.0f * factor);

        BlendPoses(animation.SourcePose, animation.TargetPose, smoothFactor, ref animation.Pose);

        if (factor >= 1.0f)
        {
            animation.Blending = false;
            animation.BlendTime = 0.0f;
            animation.BlendDuration = 0.0f;
        }
    }

    private static void UpdateTargetTime(AnimationState animation, AnimationClip clip, float deltaTime)
    {
        if (!animation.Playing || clip.Duration <= 0.0f)
            return;

        animation.Time += deltaTime;

        if (animation.Loop)
        {
            animation.Time %= clip.Duration;
            return;
        }

        if (animation.Time >= clip.Duration)
        {
            animation.Time = clip.Duration;
            animation.Playing = false;
            animation.Finished = true;
        }
    }

    private static void UpdateSourceTime(AnimationState animation, AnimationClip clip, float deltaTime)
    {
        if (clip.Duration <= 0.0f)
            return;

        animation.SourceTime += deltaTime;

        if (animation.SourceLoop)
        {
            animation.SourceTime %= clip.Duration;
            return;
        }

        animation.SourceTime = Math.Min(animation.SourceTime, clip.Duration);
    }

    private static void ResetPose(Model model, ref NodePose[] pose)
    {
        if (pose.Length != model.Nodes.Length)
            pose = new NodePose[model.Nodes.Length];

        for (int i = 0; i < model.Nodes.Length; i++)
        {
            pose[i].Translation = model.Nodes[i].BaseTranslation;
            pose[i].Rotation = model.Nodes[i].BaseRotation;
            pose[i].Scale = model.Nodes[i].BaseScale;
        }
    }

    private static void Evaluate(NodePose[] pose, AnimationClip clip, float time)
    {
        foreach (AnimationChannel channel in clip.Channels)
        {
            if (channel.NodeIndex >= (uint)pose.Length || channel.SamplerIndex >= (uint)clip.Samplers.Length)
                continue;

            AnimationSampler sampler = clip.Samplers[(int)channel.SamplerIndex];

            if (sampler.Times.Length == 0 || sampler.Values.Length == 0)
                continue;

            ref NodePose node = ref pose[(int)channel.NodeIndex];
            Vector4 value = Sample(sampler, channel.Path, time);

            switch (channel.Path)
            {
                case AnimationPath.Translation:
                    node.Translation = new Vector3(value.X, value.Y, value.Z);
                    break;

                case AnimationPath.Rotation:
                    node.Rotation = Quaternion.Normalize(new Quaternion(value.X, value.Y, value.Z, value.W));
                    break;

                case AnimationPath.Scale:
                    node.Scale = new Vector3(value.X, value.Y, value.Z);
                    break;
            }
        }
    }

    private static void BlendPoses(NodePose[] source, NodePose[] target, float factor, ref NodePose[] result)
    {
        if (result.Length != target.Length)
            result = new NodePose[target.Length];

        for (int i = 0; i < target.Length; i++)
        {
            result[i].Translation = Vector3.Lerp(source[i].Translation, target[i].Translation, factor);
            result[i].Rotation = Quaternion.Normalize(Quaternion.Slerp(source[i].Rotation, target[i].Rotation, factor));
            result[i].Scale = Vector3.Lerp(source[i].Scale, target[i].Scale, factor);
        }
    }

    private static Vector4 Sample(AnimationSampler sampler, AnimationPath path, float time)
    {
        if (sampler.Times.Length == 1 || time <= sampler.Times[0])
            return sampler.Values[0];

        int lastIndex = sampler.Times.Length - 1;

        if (time >= sampler.Times[lastIndex])
            return sampler.Values[lastIndex];

        int low = 0;
        int high = sampler.Times.Length;

        while (low < high)
        {
            int mid = low + ((high - low) >> 1);

            if (sampler.Times[mid] <= time)
                low = mid + 1;
            else
                high = mid;
        }

        int nextIndex = low;
        int previousIndex = nextIndex - 1;

        if (sampler.Interpolation == AnimationInterpolation.Step)
            return sampler.Values[previousIndex];

        float previousTime = sampler.Times[previousIndex];
        float nextTime = sampler.Times[nextIndex];
        float factor = (time - previousTime) / (nextTime - previousTime);

        if (path == AnimationPath.Rotation)
        {
            Quaternion previous = new(
                sampler.Values[previousIndex].X,
                sampler.Values[previousIndex].Y,
                sampler.Values[previousIndex].Z,
                sampler.Values[previousIndex].W
            );

            Quaternion next = new(
                sampler.Values[nextIndex].X,
                sampler.Values[nextIndex].Y,
                sampler.Values[nextIndex].Z,
                sampler.Values[nextIndex].W
            );

            Quaternion value = Quaternion.Normalize(Quaternion.Slerp(previous, next, factor));
            return new Vector4(value.X, value.Y, value.Z, value.W);
        }

        return Vector4.Lerp(sampler.Values[previousIndex], sampler.Values[nextIndex], factor);
    }
}
