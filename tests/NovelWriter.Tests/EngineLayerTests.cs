using NovelWriter.Engine.ContextWindow;
using NovelWriter.Engine.Llm;

namespace NovelWriter.Tests;

public class LlmAdapterTests
{
    [Fact]
    public void DeepSeekAdapter_Properties_Correct()
    {
        using var http = new HttpClient();
        var adapter = new DeepSeekAdapter(http, "test-key");

        Assert.Equal("deepseek-v4-pro", adapter.ModelName);
        Assert.Equal(1_000_000, adapter.MaxContextTokens);
        Assert.Equal(8_192, adapter.RecommendedOutputTokens);
    }

    [Fact]
    public void QwenAdapter_Properties_Correct()
    {
        using var http = new HttpClient();
        var adapter = new QwenAdapter(http, "test-key", "qwen-plus");

        Assert.Equal("qwen-plus", adapter.ModelName);
        Assert.Equal(131_072, adapter.MaxContextTokens);
    }

    [Fact]
    public void QwenAdapter_MaxModel_Properties_Correct()
    {
        using var http = new HttpClient();
        var adapter = new QwenAdapter(http, "test-key", "qwen-max");

        Assert.Equal("qwen-max", adapter.ModelName);
        Assert.Equal(32_768, adapter.MaxContextTokens);
    }

    [Fact]
    public void KimiAdapter_Properties_Correct()
    {
        using var http = new HttpClient();
        var adapter = new KimiAdapter(http, "test-key", "moonshot-v1-128k");

        Assert.Equal("moonshot-v1-128k", adapter.ModelName);
        Assert.Equal(131_072, adapter.MaxContextTokens);
    }

    [Fact]
    public void KimiAdapter_8k_Properties_Correct()
    {
        using var http = new HttpClient();
        var adapter = new KimiAdapter(http, "test-key", "moonshot-v1-8k");

        Assert.Equal(8_192, adapter.MaxContextTokens);
    }

    [Fact]
    public void LlmAdapterBase_NullApiKey_Throws()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new DeepSeekAdapter(http, ""));
    }
}

public class LlmDegradationPolicyTests
{
    [Fact]
    public void GetActiveModel_Default_ReturnsDeepSeek()
    {
        var policy = new LlmDegradationPolicy();
        Assert.Equal("deepseek-v4-pro", policy.GetActiveModel());
    }

    [Fact]
    public void ReportFailure_After3Failures_CircuitOpens()
    {
        var policy = new LlmDegradationPolicy();

        policy.ReportFailure("deepseek-v4-pro");
        policy.ReportFailure("deepseek-v4-pro");
        Assert.Equal("deepseek-v4-pro", policy.GetActiveModel());

        policy.ReportFailure("deepseek-v4-pro");
        Assert.Equal("qwen-max", policy.GetActiveModel());
    }

    [Fact]
    public void ReportSuccess_ResetsCircuit()
    {
        var policy = new LlmDegradationPolicy();

        policy.ReportFailure("deepseek-v4-pro");
        policy.ReportFailure("deepseek-v4-pro");
        policy.ReportSuccess("deepseek-v4-pro");

        Assert.Equal("deepseek-v4-pro", policy.GetActiveModel());
    }
}

public class LlmResponseParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsObject()
    {
        var json = """{"name": "test", "value": 42}""";
        var result = LlmResponseParser.Parse<TestDto>(json);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("test", result.Value.Name);
        Assert.Equal(42, result.Value.Value);
    }

    [Fact]
    public void Parse_JsonInCodeBlock_ExtractsCorrectly()
    {
        var output = "Here is the result:\n```json\n{\"name\": \"hello\", \"value\": 99}\n```\nDone.";
        var result = LlmResponseParser.Parse<TestDto>(output);

        Assert.True(result.Success);
        Assert.Equal("hello", result.Value!.Name);
    }

    [Fact]
    public void Parse_NoJson_ReturnsFailure()
    {
        var result = LlmResponseParser.Parse<TestDto>("No JSON here at all");
        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_TruncatedJson_BestEffort()
    {
        var truncated = """{"name": "test", "value": 42}""";
        var result = LlmResponseParser.Parse<TestDto>(truncated);
        Assert.True(result.Success);

        var reallyTruncated = """{"name": "test", "value": 42""";
        var result2 = LlmResponseParser.Parse<TestDto>(reallyTruncated);
        Assert.NotNull(result2);
    }

    private record TestDto(string Name, int Value);
}

public class TokenCounterTests
{
    [Fact]
    public void Estimate_ChineseText_ReturnsApproximateTokens()
    {
        var counter = new TokenCounter();
        var text = "这是一段中文测试文本，用于验证token计数器的估算功能是否正常工作。";
        var tokens = counter.Estimate(text, "test-model");

        Assert.True(tokens > 0);
        Assert.True(tokens < text.Length);
    }

    [Fact]
    public void Estimate_EmptyText_ReturnsZero()
    {
        var counter = new TokenCounter();
        Assert.Equal(0, counter.Estimate("", "test-model"));
    }

    [Fact]
    public void IsWithinBudget_Within85Percent_ReturnsTrue()
    {
        var counter = new TokenCounter();
        Assert.True(counter.IsWithinBudget(84, 100));
    }

    [Fact]
    public void IsWithinBudget_Over85Percent_ReturnsFalse()
    {
        var counter = new TokenCounter();
        Assert.False(counter.IsWithinBudget(86, 100));
    }
}

public class SystemPromptBuilderTests
{
    [Fact]
    public void BuildWritingSystemPrompt_AllSections_IncludesAll()
    {
        var prompt = SystemPromptBuilder.BuildWritingSystemPrompt(
            l3CharacterSection: "人物边界",
            l3WorldSettingSection: "世界观",
            l2Section: "卷记忆",
            l1Section: "摘要",
            toneDirective: "基调",
            styleDirective: "风格",
            writingInstructions: "指令");

        Assert.Contains("人物边界", prompt);
        Assert.Contains("世界观", prompt);
        Assert.Contains("卷记忆", prompt);
        Assert.Contains("摘要", prompt);
        Assert.Contains("基调", prompt);
        Assert.Contains("风格", prompt);
        Assert.Contains("指令", prompt);
    }

    [Fact]
    public void BuildWritingSystemPrompt_EmptySections_OmitsThem()
    {
        var prompt = SystemPromptBuilder.BuildWritingSystemPrompt(
            l3CharacterSection: "人物",
            l3WorldSettingSection: "",
            l2Section: "",
            l1Section: "",
            toneDirective: "",
            styleDirective: "",
            writingInstructions: "");

        Assert.Contains("人物", prompt);
        Assert.DoesNotContain("世界观规则", prompt);
    }

    [Fact]
    public void BuildSummaryExtractionPrompt_ContainsJsonFormat()
    {
        var prompt = SystemPromptBuilder.BuildSummaryExtractionPrompt();
        Assert.Contains("summary", prompt);
        Assert.Contains("key_events", prompt);
    }

    [Fact]
    public void BuildStructuralDetectionPrompt_ContainsAllTasks()
    {
        var prompt = SystemPromptBuilder.BuildStructuralDetectionPrompt();
        Assert.Contains("foreshadowing_resolutions", prompt);
        Assert.Contains("new_foreshadowings", prompt);
        Assert.Contains("arc_updates", prompt);
    }
}
