using System.Collections.Concurrent;
using NovelWriter.Core.Exceptions;
using Serilog;

namespace NovelWriter.Engine.Llm;

/// <summary>
/// 多模型降级策略。
/// 降级链: deepseek-v4-pro(1) → qwen-max(2) → moonshot-v1-128k(3)。
/// 连续 3 次失败 → 熔断 30s → 自动切换下一优先级。
/// </summary>
public class LlmDegradationPolicy
{
    private readonly (string Model, int Priority)[] _chain;
    private readonly ConcurrentDictionary<string, CircuitState> _circuitStates = new();
    private readonly ConcurrentDictionary<string, int> _failureCounts = new();
    private readonly ConcurrentDictionary<string, DateTime> _circuitOpenTime = new();

    private const int MaxFailures = 3;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(30);

    public LlmDegradationPolicy()
    {
        _chain =
        [
            ("deepseek-v4-pro", 1),
            ("qwen-max", 2),
            ("moonshot-v1-128k", 3)
        ];
    }

    /// <summary>
    /// 获取当前可用的最高优先级模型。
    /// </summary>
    public string GetActiveModel()
    {
        foreach (var (model, _) in _chain.OrderBy(m => m.Priority))
        {
            var state = GetCircuitState(model);
            if (state != CircuitState.Open) return model;
        }

        // 所有模型熔断，检查是否有已恢复的
        foreach (var (model, _) in _chain.OrderBy(m => m.Priority))
        {
            if (IsHalfOpen(model)) return model;
        }

        throw new LlmUnavailableException("All LLM models are currently unavailable");
    }

    /// <summary>
    /// 报告调用失败。连续 3 次失败后熔断。
    /// </summary>
    public void ReportFailure(string model)
    {
        var count = _failureCounts.AddOrUpdate(model, 1, (_, v) => v + 1);
        Log.Warning("LLM failure reported: Model={Model}, ConsecutiveFailures={Count}", model, count);

        if (count >= MaxFailures)
        {
            _circuitStates[model] = CircuitState.Open;
            _circuitOpenTime[model] = DateTime.UtcNow;
            Log.Warning("Circuit OPEN for model {Model}, will retry after {Duration}s",
                model, CircuitOpenDuration.TotalSeconds);
        }
    }

    /// <summary>
    /// 报告调用成功，重置熔断状态。
    /// </summary>
    public void ReportSuccess(string model)
    {
        _failureCounts.TryRemove(model, out _);
        _circuitStates[model] = CircuitState.Closed;
        _circuitOpenTime.TryRemove(model, out _);
        Log.Debug("Circuit CLOSED for model {Model}", model);
    }

    private CircuitState GetCircuitState(string model)
    {
        if (!_circuitStates.TryGetValue(model, out var state)) return CircuitState.Closed;
        if (state == CircuitState.Open && IsHalfOpen(model)) return CircuitState.HalfOpen;
        return state;
    }

    private bool IsHalfOpen(string model)
    {
        if (!_circuitOpenTime.TryGetValue(model, out var openTime)) return false;
        return DateTime.UtcNow - openTime >= CircuitOpenDuration;
    }

    private enum CircuitState { Closed, Open, HalfOpen }
}
