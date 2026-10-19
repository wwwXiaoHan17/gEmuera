using System.Collections.Concurrent;

namespace gEmuera.AgentBridge
{
    /// <summary>
    /// 解释器线程（EmueraThread）→ Godot 主线程的单向检查点队列。
    /// Enqueue 基于 ConcurrentQueue，解释器线程零阻塞；
    /// 主线程由 CheckpointPipelineComponent._Process 轮询消费后发信号。
    /// 线程红线：解释器线程绝不触碰场景树/信号，只入队字符串。
    /// </summary>
    public static class AgentBridgeQueue
    {
        static readonly ConcurrentQueue<string> pending = new();

        public static void Enqueue(string stateName)
        {
            pending.Enqueue(stateName);
        }

        public static bool TryDequeue(out string stateName)
        {
            return pending.TryDequeue(out stateName);
        }
    }
}
