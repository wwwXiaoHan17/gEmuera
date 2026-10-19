using Godot;

namespace gEmuera.AgentBridge
{
    /// <summary>
    /// 检查点流水线组件：轮询 AgentBridgeQueue（解释器线程入队的引擎状态转移），
    /// 在主线程发 checkpoint_reached 信号 + 诊断日志。
    /// 组合原则：本组件只负责「队列 → 信号」流转，LLM 批处理等动作由 Host/后续组件订阅实现，
    /// 组件间零直连，可单独摘到测试场景复用。
    /// </summary>
    public sealed partial class CheckpointPipelineComponent : Node
    {
        [Signal]
        public delegate void CheckpointReachedEventHandler(string stateName);

        public override void _Process(double delta)
        {
            while (AgentBridgeQueue.TryDequeue(out string stateName))
            {
                GenericUtils.Info($"AGENT_CHECKPOINT state={stateName}");
                EmitSignal(SignalName.CheckpointReached, stateName);

                // 主检查点：预取批处理放后台线程，主线程零阻塞（godot-master 红线：主线程禁网络 IO）。
                // 缓存填充前 CALLSHARP AI_PREFETCH_GET 未命中即降级（离线兜底不变量）。
                if (stateName == "Shop_Begin")
                {
                    _ = System.Threading.Tasks.Task.Run(() =>
                        global::MinorShift.Emuera.Runtime.AgentBridge.AgentLlmMethods.OnPrimaryCheckpoint());
                }
            }
        }
    }
}
