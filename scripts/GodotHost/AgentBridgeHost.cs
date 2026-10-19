using Godot;

namespace gEmuera.AgentBridge
{
    /// <summary>
    /// AgentBridge 组合容器：唯一进树的宿主节点，子组件各挂脚本、只发信号向上，
    /// Host 编排订阅。组件不互相直连，便于诊断面板/测试场景单独复用。
    /// </summary>
    public sealed partial class AgentBridgeHost : Node
    {
        public CheckpointPipelineComponent Pipeline { get; private set; }

        public override void _Ready()
        {
            Pipeline = new CheckpointPipelineComponent
            {
                Name = "CheckpointPipeline"
            };
            AddChild(Pipeline);
        }
    }
}
