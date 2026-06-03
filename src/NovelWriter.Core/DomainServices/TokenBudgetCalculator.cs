using NovelWriter.Core.ValueObjects;

namespace NovelWriter.Core.DomainServices;

public static class TokenBudgetCalculator
{
    public static TokenBudget Calculate(int modelMaxTokens, int systemPromptTokens, int l1Tokens, int l2Tokens, int l3Tokens, int userMessageTokens)
    {
        var reserved = (int)(modelMaxTokens * 0.05); // 5% safety margin
        return new TokenBudget(
            Total: modelMaxTokens,
            SystemPrompt: systemPromptTokens,
            L1Context: l1Tokens,
            L2Context: l2Tokens,
            L3Context: l3Tokens,
            UserMessage: userMessageTokens,
            Reserved: reserved
        );
    }

    public static bool IsWithinBudget(TokenBudget budget) => budget.Remaining >= 0;
}
