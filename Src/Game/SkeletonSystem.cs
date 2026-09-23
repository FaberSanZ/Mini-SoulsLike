using System.Collections.Generic;
using System.Numerics;

namespace Vultaik;

public sealed class SkeletonState
{
    private Model? _model;

    public Matrix4x4[] GlobalTransforms { get; private set; } = [];
    public Matrix4x4[][] JointMatrices { get; private set; } = [];

    public SkeletonState() { }
    public SkeletonState(Model model) => Ensure(model);

    internal void Ensure(Model model)
    {
        if (ReferenceEquals(_model, model) && GlobalTransforms.Length == model.Nodes.Length && JointMatrices.Length == model.Skins.Length) return;

        _model = model;
        GlobalTransforms = new Matrix4x4[model.Nodes.Length];
        JointMatrices = new Matrix4x4[model.Skins.Length][];

        for (int skinIndex = 0; skinIndex < model.Skins.Length; skinIndex++) JointMatrices[skinIndex] = new Matrix4x4[model.Skins[skinIndex].Joints.Length];
    }

    public Matrix4x4[]? GetJointMatrices(int skinIndex)
    {
        if ((uint)skinIndex >= (uint)JointMatrices.Length) return null;
        return JointMatrices[skinIndex];
    }
}

public sealed class SkeletonSystem
{
    private readonly Dictionary<Model, SkeletonDefinition> _definitions = new();

    public void Update(Model model, NodePose[]? pose, SkeletonState skeleton)
    {
        skeleton.Ensure(model);

        SkeletonDefinition definition = GetDefinition(model);
        Matrix4x4[] globals = skeleton.GlobalTransforms;

        for (int i = 0; i < definition.EvaluationOrder.Length; i++)
        {
            int nodeIndex = definition.EvaluationOrder[i];
            Node node = model.Nodes[nodeIndex];
            NodePose localPose = pose is not null && (uint)nodeIndex < (uint)pose.Length ? pose[nodeIndex] : GetBasePose(node);
            Matrix4x4 local = Matrix4x4.CreateScale(localPose.Scale) * Matrix4x4.CreateFromQuaternion(localPose.Rotation) * Matrix4x4.CreateTranslation(localPose.Translation);
            globals[nodeIndex] = node.Parent >= 0 && node.Parent < globals.Length ? local * globals[node.Parent] : local;
        }

        for (int skinIndex = 0; skinIndex < model.Skins.Length; skinIndex++)
        {
            Skin skin = model.Skins[skinIndex];
            Matrix4x4[] joints = skeleton.JointMatrices[skinIndex];

            for (int jointIndex = 0; jointIndex < joints.Length; jointIndex++)
            {
                uint jointNodeIndex = skin.Joints[jointIndex];

                if (jointNodeIndex >= (uint)globals.Length || jointIndex >= skin.InverseBindMatrices.Length)
                {
                    joints[jointIndex] = Matrix4x4.Identity;
                    continue;
                }

                joints[jointIndex] = Matrix4x4.Transpose(skin.InverseBindMatrices[jointIndex] * globals[jointNodeIndex]);
            }
        }
    }

    private SkeletonDefinition GetDefinition(Model model)
    {
        if (_definitions.TryGetValue(model, out SkeletonDefinition? definition)) return definition;

        definition = new SkeletonDefinition(BuildEvaluationOrder(model));
        _definitions.Add(model, definition);
        return definition;
    }

    private static int[] BuildEvaluationOrder(Model model)
    {
        int[] order = new int[model.Nodes.Length];
        byte[] state = new byte[model.Nodes.Length];
        int write = 0;

        for (int nodeIndex = 0; nodeIndex < model.Nodes.Length; nodeIndex++) VisitNode(model, nodeIndex, state, order, ref write);
        return order;
    }

    private static void VisitNode(Model model, int nodeIndex, byte[] state, int[] order, ref int write)
    {
        if ((uint)nodeIndex >= (uint)model.Nodes.Length || state[nodeIndex] == 2) return;

        if (state[nodeIndex] == 1)
        {
            state[nodeIndex] = 2;
            order[write++] = nodeIndex;
            return;
        }

        state[nodeIndex] = 1;

        int parent = model.Nodes[nodeIndex].Parent;
        if (parent >= 0 && parent < model.Nodes.Length && parent != nodeIndex) VisitNode(model, parent, state, order, ref write);

        if (state[nodeIndex] == 2) return;

        state[nodeIndex] = 2;
        order[write++] = nodeIndex;
    }

    private static NodePose GetBasePose(Node node) => new() { Translation = node.BaseTranslation, Rotation = node.BaseRotation, Scale = node.BaseScale };

    private sealed class SkeletonDefinition
    {
        public readonly int[] EvaluationOrder;
        public SkeletonDefinition(int[] evaluationOrder) => EvaluationOrder = evaluationOrder;
    }
}
