using System.Text;
using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;
using Microsoft.Extensions.Options;

namespace CommBank.AI.Services;

/// <summary>
/// Transparent keyword/feature categorizer that maps a transaction onto the tenant's existing tag
/// taxonomy. Deterministic and explainable (every suggestion carries the matched terms), and a clean
/// seam for a future trained classifier behind <see cref="ICategorizationService"/>. The user-correction
/// path is the feedback loop: a corrected tag is simply the ground-truth label for later training.
/// </summary>
public sealed class RuleBasedCategorizationService : ICategorizationService
{
    private const string ModelIdentifier = "commbank.categorize.rules";

    private static readonly IReadOnlyDictionary<string, string[]> Lexicon =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Groceries"]     = new[] { "grocery", "groceries", "supermarket", "woolworths", "coles", "aldi", "iga", "market" },
            ["Dining"]        = new[] { "restaurant", "cafe", "coffee", "mcdonald", "kfc", "uber eats", "ubereats", "doordash", "dining", "bistro", "pub" },
            ["Transport"]     = new[] { "uber", "lyft", "taxi", "fuel", "petrol", "bp", "shell", "caltex", "parking", "opal", "myki", "train", "bus" },
            ["Utilities"]     = new[] { "electricity", "energy", "water bill", "internet", "broadband", "telstra", "optus", "vodafone", "utility" },
            ["Rent"]          = new[] { "rent", "landlord", "lease", "real estate" },
            ["Mortgage"]      = new[] { "mortgage", "home loan", "loan repayment" },
            ["Entertainment"] = new[] { "netflix", "spotify", "disney", "cinema", "movie", "steam", "playstation", "xbox", "concert" },
            ["Shopping"]      = new[] { "amazon", "ebay", "kmart", "target", "myer", "retail", "clothing" },
            ["Health"]        = new[] { "pharmacy", "chemist", "medical", "doctor", "dental", "hospital", "gym", "fitness" },
            ["Income"]        = new[] { "salary", "payroll", "wage", "dividend", "interest", "refund", "reimbursement" },
            ["Fees"]          = new[] { "fee", "atm", "overdraft", "surcharge" },
            ["Transfer"]      = new[] { "transfer", "bpay", "payid", "osko", "remittance" }
        };

    private readonly AiOptions _options;

    public RuleBasedCategorizationService(IOptions<AiOptions> options) => _options = options.Value;

    public Task<CategorizationResult> CategorizeAsync(
        Transaction transaction,
        IReadOnlyList<Tag> candidateTags,
        CancellationToken cancellationToken = default)
    {
        CategorizationOptions opt = _options.Categorization;
        string description = (transaction.Description ?? string.Empty).ToLowerInvariant();
        HashSet<string> tokens = Tokenize(description);

        var suggestions = new List<TagSuggestion>();

        foreach (KeyValuePair<string, string[]> entry in Lexicon)
        {
            string category = entry.Key;
            var matched = new List<string>();
            double matchScore = 0d;

            foreach (string keyword in entry.Value)
            {
                if (keyword.Contains(' '))
                {
                    if (description.Contains(keyword))
                    {
                        matched.Add(keyword);
                        matchScore += 1.0d;
                    }
                }
                else if (tokens.Contains(keyword))
                {
                    matched.Add(keyword);
                    matchScore += 1.0d;
                }
                else if (description.Contains(keyword))
                {
                    matched.Add(keyword);
                    matchScore += 0.5d;
                }
            }

            if (matchScore <= 0d)
            {
                continue;
            }

            Tag? tag = ResolveTag(category, candidateTags);
            if (tag?.Id is null)
            {
                continue;
            }

            double confidence = Math.Min(0.95d, 0.55d + (0.13d * matchScore));
            suggestions.Add(new TagSuggestion(
                tag.Id,
                tag.Name ?? category,
                Math.Round(confidence, 3),
                $"Matched '{category}' on: {string.Join(", ", matched)}"));
        }

        if (suggestions.Count == 0)
        {
            string? fallback = transaction.TransactionType switch
            {
                TransactionType.Transfer => "Transfer",
                TransactionType.Credit => "Income",
                _ => null
            };

            if (fallback is not null)
            {
                Tag? tag = ResolveTag(fallback, candidateTags);
                if (tag?.Id is not null)
                {
                    suggestions.Add(new TagSuggestion(
                        tag.Id,
                        tag.Name ?? fallback,
                        0.50d,
                        $"Inferred from transaction type {transaction.TransactionType}."));
                }
            }
        }

        List<TagSuggestion> ordered = suggestions
            .GroupBy(s => s.TagId)
            .Select(g => g.OrderByDescending(s => s.Confidence).First())
            .OrderByDescending(s => s.Confidence)
            .Take(Math.Max(1, opt.MaxSuggestions))
            .ToList();

        TagSuggestion? best = ordered.Count > 0 ? ordered[0] : null;
        bool autoApplied = best is not null && best.Confidence >= opt.MinConfidence;

        return Task.FromResult(new CategorizationResult
        {
            TransactionId = transaction.Id,
            Suggestions = ordered,
            AutoApplied = autoApplied,
            ModelVersion = opt.ModelVersion
        });
    }

    private static Tag? ResolveTag(string category, IReadOnlyList<Tag>? candidates)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        Tag? exact = candidates.FirstOrDefault(t =>
            t.Id is not null && string.Equals(t.Name, category, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return candidates.FirstOrDefault(t =>
            t.Id is not null && t.Name is not null &&
            (t.Name.Contains(category, StringComparison.OrdinalIgnoreCase) ||
             category.Contains(t.Name, StringComparison.OrdinalIgnoreCase)));
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new StringBuilder();

        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
