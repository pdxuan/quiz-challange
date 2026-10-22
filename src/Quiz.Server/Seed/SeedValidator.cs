// AI-assisted: Claude Code (Claude Opus 5.5), slice S6; reviewed by a reviewer agent and a human; verified by Seed/SeedValidatorTests, mutations (docs/ai-collab/s6.md).
using Quiz.Server.Domain;

namespace Quiz.Server.Seed;

/// <summary>
/// Checks a quiz set before it is stored. The schema cannot hold these rules (the correct option
/// has no foreign key), so the seeder refuses any set with an error.
/// </summary>
public static class SeedValidator
{
    public const int OptionsPerQuestion = 4;

    /// <summary>Every rule the set breaks, one message each; empty when the set is valid.</summary>
    public static IReadOnlyList<string> Validate(QuizSet set)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(set.Id) || string.IsNullOrWhiteSpace(set.Title))
        {
            errors.Add($"Quiz set '{set.Id}': needs an id and a title.");
        }
        if (set.TimeLimitSec <= 0)
        {
            errors.Add($"Quiz set '{set.Id}': the time limit must be positive, is {set.TimeLimitSec}.");
        }
        if (set.Questions.Count == 0)
        {
            errors.Add($"Quiz set '{set.Id}': has no questions.");
        }

        errors.AddRange(Duplicates(set.Questions.Select(q => q.Id), StringComparer.Ordinal)
            .Select(id => $"Quiz set '{set.Id}': question id '{id}' is used more than once."));
        errors.AddRange(Duplicates(set.Questions.Select(q => q.Position), EqualityComparer<int>.Default)
            .Select(position => $"Quiz set '{set.Id}': position {position} is used more than once."));

        var words = new Dictionary<string, Question>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in set.Questions.OrderBy(q => q.Position))
        {
            errors.AddRange(ValidateQuestion(question));

            // At most one direction per word: the word is the prompt (word -> meaning) or the correct option (meaning -> word).
            var word = question.Direction == QuestionDirection.WordToMeaning
                ? question.Prompt
                : question.Options.FirstOrDefault(o => o.OptionId == question.CorrectOptionId)?.Text;
            if (!string.IsNullOrWhiteSpace(word) && !words.TryAdd(word.Trim(), question))
            {
                errors.Add($"Question {question.Id}: the word '{word.Trim().ToLowerInvariant()}' is already asked by question {words[word.Trim()].Id}.");
            }
        }
        return errors;
    }

    private static IEnumerable<string> ValidateQuestion(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.Prompt))
        {
            yield return $"Question {question.Id}: the prompt is empty.";
        }
        if (question.Options.Count != OptionsPerQuestion)
        {
            yield return $"Question {question.Id}: needs exactly {OptionsPerQuestion} options, has {question.Options.Count}.";
        }
        if (question.Options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
        {
            yield return $"Question {question.Id}: has an empty option text.";
        }
        if (Duplicates(question.Options.Select(o => o.OptionId), StringComparer.Ordinal).Any())
        {
            yield return $"Question {question.Id}: needs unique option ids.";
        }
        var texts = question.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).Select(o => o.Text.Trim());
        if (Duplicates(texts, StringComparer.OrdinalIgnoreCase).Any())
        {
            yield return $"Question {question.Id}: needs unique option texts.";
        }
        if (!question.HasOption(question.CorrectOptionId))
        {
            yield return $"Question {question.Id}: the correct option '{question.CorrectOptionId}' is not one of its options.";
        }
    }

    private static IEnumerable<T> Duplicates<T>(IEnumerable<T> values, IEqualityComparer<T> comparer) =>
        values.GroupBy(v => v, comparer).Where(g => g.Count() > 1).Select(g => g.Key);
}
