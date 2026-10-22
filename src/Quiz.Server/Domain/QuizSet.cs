namespace Quiz.Server.Domain;

public enum QuestionDirection
{
    WordToMeaning,
    MeaningToWord,
}

/// <summary>A seeded vocabulary quiz: questions in order, one time limit for all of them.</summary>
public class QuizSet(string id, string title, int timeLimitSec)
{
    public string Id { get; private set; } = id;
    public string Title { get; private set; } = title;
    public int TimeLimitSec { get; private set; } = timeLimitSec;
    public List<Question> Questions { get; private set; } = [];

    public TimeSpan TimeLimit => TimeSpan.FromSeconds(TimeLimitSec);

    /// <summary>The questions in play order.</summary>
    public IReadOnlyList<Question> Ordered => Questions.OrderBy(q => q.Position).ToList();
}

public class Question(string id, string quizSetId, int position, string prompt, QuestionDirection direction, string correctOptionId)
{
    public Question(string id, string quizSetId, int position, string prompt, QuestionDirection direction, string correctOptionId, List<Option> options)
        : this(id, quizSetId, position, prompt, direction, correctOptionId) => Options = options;

    public string Id { get; private set; } = id;
    public string QuizSetId { get; private set; } = quizSetId;
    public int Position { get; private set; } = position;
    public string Prompt { get; private set; } = prompt;
    public QuestionDirection Direction { get; private set; } = direction;
    public string CorrectOptionId { get; private set; } = correctOptionId;
    public List<Option> Options { get; private set; } = [];

    public bool HasOption(string optionId) => Options.Any(o => o.OptionId == optionId);
}

public class Option(string optionId, string questionId, int position, string text)
{
    public string OptionId { get; private set; } = optionId;
    public string QuestionId { get; private set; } = questionId;
    public int Position { get; private set; } = position;
    public string Text { get; private set; } = text;
}
