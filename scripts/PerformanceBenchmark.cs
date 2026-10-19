using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using MinorShift.Emuera;

/// <summary>
/// 性能基准测试工具
/// 提供自动化测试场景，量化优化收益
/// </summary>
public partial class PerformanceBenchmark : Node
{
    public class BenchmarkResult
    {
        public string ScenarioName;
        public double TotalTimeMs;
        public double AvgFrameTimeMs;
        public double MaxFrameTimeMs;
        public int FrameCount;
        public double FPS => FrameCount / (TotalTimeMs / 1000.0);
        public long PeakMemoryBytes;
        public int TotalDisplayLines;
        public int TotalButtons;

        public override string ToString()
        {
            return $"[{ScenarioName}] " +
                   $"Total: {TotalTimeMs:F0}ms, " +
                   $"Avg: {AvgFrameTimeMs:F2}ms/frame, " +
                   $"Max: {MaxFrameTimeMs:F2}ms, " +
                   $"FPS: {FPS:F1}, " +
                   $"Memory: {PeakMemoryBytes / 1024 / 1024}MB, " +
                   $"Lines: {TotalDisplayLines}, " +
                   $"Buttons: {TotalButtons}";
        }
    }

    List<BenchmarkResult> results = new List<BenchmarkResult>();
    Stopwatch stopwatch = new Stopwatch();
    bool running = false;

    public override void _Ready()
    {
        SetProcess(false);
    }

    /// <summary>
    /// 运行所有基准测试
    /// </summary>
    public async void RunAllBenchmarks()
    {
        if (running)
        {
            GenericUtils.Error("Benchmark: 拒绝重复运行 → 已在运行中");
            return;
        }

        running = true;
        results.Clear();

        GenericUtils.PerfTrace("PERF.BENCH.START", () => "gEmuera 性能基准测试开始");

        // 场景 1: 大量文本输出
        await RunBenchmark("大量文本输出", CreateLargeTextScript(1000));

        // 场景 2: 频繁按钮渲染
        await RunBenchmark("频繁按钮渲染", CreateButtonsScript(100));

        // 场景 3: 混合文本和按钮
        await RunBenchmark("混合文本和按钮", CreateMixedScript(500, 50));

        // 输出汇总报告
        PrintSummary();

        running = false;
    }

    /// <summary>
    /// 运行单个基准测试
    /// </summary>
    async System.Threading.Tasks.Task RunBenchmark(string name, string erbScript)
    {
        GenericUtils.PerfTrace("PERF.BENCH.SCENARIO", () => $"场景开始: {name}（脚本 {erbScript.Length} 字符）");

        // 清理环境
        ClearConsole();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // 强制 GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long startMemory = GC.GetTotalMemory(false);

        // 准备测试
        var console = GlobalStatic.Console;
        if (console == null)
        {
            GenericUtils.Error($"Benchmark: 场景跳过 → Console 未初始化（{name}）");
            return;
        }

        // 执行测试
        stopwatch.Restart();
        int frameCount = 0;
        double totalFrameTime = 0;
        double maxFrameTime = 0;

        // 模拟执行 ERB 脚本
        // 注意：这里简化为直接调用 Console API
        ExecuteTestScript(console, erbScript);

        // 等待渲染完成并收集帧数据
        for (int i = 0; i < 60; i++) // 采样 60 帧
        {
            double frameStart = Time.GetTicksMsec();

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            double frameTime = Time.GetTicksMsec() - frameStart;
            totalFrameTime += frameTime;
            maxFrameTime = Math.Max(maxFrameTime, frameTime);
            frameCount++;
        }

        stopwatch.Stop();

        long endMemory = GC.GetTotalMemory(false);

        // 收集结果
        var result = new BenchmarkResult
        {
            ScenarioName = name,
            TotalTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            AvgFrameTimeMs = totalFrameTime / frameCount,
            MaxFrameTimeMs = maxFrameTime,
            FrameCount = frameCount,
            PeakMemoryBytes = endMemory - startMemory,
            TotalDisplayLines = console?.GetDisplayLinesCount() ?? 0,
            TotalButtons = CountButtons(console)
        };

        results.Add(result);

        GenericUtils.PerfTrace("PERF.BENCH.RESULT", () => result.ToString());
    }

    void ExecuteTestScript(MinorShift.Emuera.GameView.EmueraConsole console, string script)
    {
        // 解析简单的测试脚本格式
        var lines = script.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("PRINTL "))
            {
                string text = trimmed.Substring(7);
                console.Print(text);
                console.NewLine();
            }
            else if (trimmed.StartsWith("PRINTBUTTON "))
            {
                string[] parts = trimmed.Substring(12).Split(',');
                if (parts.Length >= 2)
                {
                    string text = parts[0].Trim();
                    string value = parts[1].Trim();
                    console.PrintButton(text, value);
                }
            }
        }

        console.RefreshStrings(false);
    }

    int CountButtons(MinorShift.Emuera.GameView.EmueraConsole console)
    {
        if (console == null)
            return 0;

        int buttonCount = 0;
        int lineCount = console.GetDisplayLinesCount();

        for (int i = 0; i < lineCount; i++)
        {
            var line = console.GetDisplayLinesForuEmuera(i);
            if (line?.Buttons != null)
                buttonCount += line.Buttons.Length; // 修正：使用 Length 而非 Count
        }

        return buttonCount;
    }

    void ClearConsole()
    {
        var console = GlobalStatic.Console;
        if (console != null)
        {
            // 清空控制台（如果有公开接口）
            // console.Clear(); // 假设有此方法
        }
    }

    void PrintSummary()
    {
        double totalTime = 0;
        double totalAvgFrame = 0;

        foreach (var result in results)
        {
            totalTime += result.TotalTimeMs;
            totalAvgFrame += result.AvgFrameTimeMs;
        }

        double avgFrame = results.Count > 0 ? totalAvgFrame / results.Count : 0;
        GenericUtils.PerfTrace("PERF.BENCH.SUMMARY",
            () => $"基准测试汇总: 场景数={results.Count} 总耗时={totalTime:F0}ms 平均帧时间={avgFrame:F2}ms");
    }

    // ====== 测试脚本生成器 ======

    string CreateLargeTextScript(int lineCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("; 场景 1: 大量文本输出");
        sb.AppendLine("; 测试 DisplayLineList 和渲染性能");

        for (int i = 0; i < lineCount; i++)
        {
            sb.AppendLine($"PRINTL 这是第 {i} 行文本，用于测试大量文本输出的性能表现");
        }

        return sb.ToString();
    }

    string CreateButtonsScript(int buttonCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("; 场景 2: 频繁按钮渲染");
        sb.AppendLine("; 测试按钮渲染和 hit rect 计算");

        for (int i = 0; i < buttonCount; i++)
        {
            sb.AppendLine($"PRINTBUTTON [选项{i}], {i}");
            sb.AppendLine("PRINTL ");
        }

        return sb.ToString();
    }

    string CreateMixedScript(int textLines, int buttons)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("; 场景 3: 混合文本和按钮");
        sb.AppendLine("; 测试真实游戏场景");

        for (int i = 0; i < textLines; i++)
        {
            if (i % 10 == 0 && buttons > 0)
            {
                sb.AppendLine($"PRINTBUTTON [选项{i / 10}], {i / 10}");
                buttons--;
            }
            else
            {
                sb.AppendLine($"PRINTL 文本行 {i}：这是一段描述性文本，模拟游戏对话或场景描述");
            }
        }

        return sb.ToString();
    }

    // ====== 公开 API ======

    /// <summary>
    /// 导出基准测试结果为 CSV
    /// </summary>
    public void ExportResults(string filePath)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("场景名称,总耗时(ms),平均帧时间(ms),最大帧时间(ms),帧数,FPS,内存增长(MB),文本行数,按钮数");

        foreach (var result in results)
        {
            csv.AppendLine($"{result.ScenarioName}," +
                          $"{result.TotalTimeMs:F2}," +
                          $"{result.AvgFrameTimeMs:F2}," +
                          $"{result.MaxFrameTimeMs:F2}," +
                          $"{result.FrameCount}," +
                          $"{result.FPS:F2}," +
                          $"{result.PeakMemoryBytes / 1024.0 / 1024.0:F2}," +
                          $"{result.TotalDisplayLines}," +
                          $"{result.TotalButtons}");
        }

        try
        {
            System.IO.File.WriteAllText(filePath, csv.ToString());
            GenericUtils.PerfTrace("PERF.BENCH.EXPORT", () => $"基准测试结果已导出: {filePath}");
        }
        catch (Exception ex)
        {
            GenericUtils.Error($"Benchmark: 导出失败 → {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取最近的测试结果
    /// </summary>
    public List<BenchmarkResult> GetResults()
    {
        return new List<BenchmarkResult>(results);
    }
}
