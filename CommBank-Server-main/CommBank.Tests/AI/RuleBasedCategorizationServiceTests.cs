using CommBank.AI.Models;
using CommBank.AI.Services;
using CommBank.Models;
using Microsoft.Extensions.Options;

namespace CommBank.Tests.AI;

public class RuleBasedCategorizationServiceTests
{
    private static RuleBasedCategorizationService NewService(AiOptions? options = null) =>
        new(Options.Create(options ?? new AiOptions()));

    [Fact]
    public async Task GroceryDescription_SuggestsGroceriesTagAndAutoApplies()
    {
        var tags = new List<Tag>
        {
            new() { Id = "t1", Name = "Groceries" },
            new() { Id = "t2", Name = "Dining" }
        };
        var transaction = new Transaction
        {
            Id = "X",
            Description = "WOOLWORTHS GROCERY purchase",
            TransactionType = TransactionType.Debit
        };

        CategorizationResult result = await NewService().CategorizeAsync(transaction, tags);

        Assert.NotNull(result.Best);
        Assert.Equal("t1", result.Best!.TagId);
        Assert.True(result.AutoApplied);
        Assert.True(result.Best.Confidence >= 0.55d);
    }

    [Fact]
    public async Task NoCandidateTags_ReturnsNoSuggestions()
    {
        var transaction = new Transaction
        {
            Id = "Y",
            Description = "unmatched description",
            TransactionType = TransactionType.Debit
        };

        CategorizationResult result = await NewService().CategorizeAsync(transaction, new List<Tag>());

        Assert.Empty(result.Suggestions);
        Assert.False(result.AutoApplied);
    }
}
