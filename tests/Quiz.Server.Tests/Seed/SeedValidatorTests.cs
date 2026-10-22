using Quiz.Server.Domain;
using Quiz.Server.Seed;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Seed;

public class SeedValidatorTests
{
    private static Question Q(string id, int position, string prompt, string correct, params string[] texts) =>
        Q(id, position, prompt, QuestionDirection.WordToMeaning, correct, texts);

    private static Question Q(string id, int position, string prompt, QuestionDirection direction, string correct, params string[] texts) =>
        new(id, "set", position, prompt, direction, correct,
            texts.Select((t, n) => new Option(((char)('a' + n)).ToString(), id, n, t)).ToList());

    private static QuizSet Set(int timeLimitSec, params Question[] questions)
    {
        var set = new QuizSet("set", "A set", timeLimitSec);
        set.Questions.AddRange(questions);
        return set;
    }

    [Fact]
    public void Validate_AWellFormedSet_HasNoErrors()
    {
        Assert.Empty(SeedValidator.Validate(TestData.BuildQuizSet(questions: 5)));
    }

    [Fact]
    public void Validate_AQuestionWithThreeOptions_IsRejected()
    {
        var set = Set(15, Q("q1", 0, "apple", "a", "fruit", "car", "house"));

        var error = Assert.Single(SeedValidator.Validate(set));

        Assert.Contains("q1", error);
        Assert.Contains("exactly 4 options", error);
    }

    [Fact]
    public void Validate_AQuestionWithFiveOptions_IsRejected()
    {
        var set = Set(15, Q("q1", 0, "apple", "a", "fruit", "car", "house", "tree", "dog"));

        Assert.Contains("exactly 4 options", Assert.Single(SeedValidator.Validate(set)));
    }

    [Fact]
    public void Validate_DuplicateOptionTexts_AreRejectedIgnoringCaseAndSpaces()
    {
        var set = Set(15, Q("q1", 0, "apple", "a", "fruit", "car", " Fruit ", "house"));

        var error = Assert.Single(SeedValidator.Validate(set));

        Assert.Contains("q1", error);
        Assert.Contains("unique option texts", error);
    }

    [Fact]
    public void Validate_DuplicateOptionIds_AreRejected()
    {
        var question = new Question("q1", "set", 0, "apple", QuestionDirection.WordToMeaning, "a",
            [new("a", "q1", 0, "fruit"), new("b", "q1", 1, "car"), new("b", "q1", 2, "house"), new("d", "q1", 3, "tree")]);

        Assert.Contains("unique option ids", Assert.Single(SeedValidator.Validate(Set(15, question))));
    }

    [Fact]
    public void Validate_AnUnknownCorrectOptionId_IsRejected()
    {
        var set = Set(15, Q("q1", 0, "apple", "z", "fruit", "car", "house", "tree"));

        var error = Assert.Single(SeedValidator.Validate(set));

        Assert.Contains("q1", error);
        Assert.Contains("correct option 'z'", error);
    }

    [Fact]
    public void Validate_ASetWithoutQuestions_IsRejected()
    {
        Assert.Contains("no questions", Assert.Single(SeedValidator.Validate(Set(15))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_ANonPositiveTimeLimit_IsRejected(int timeLimitSec)
    {
        var set = Set(timeLimitSec, Q("q1", 0, "apple", "a", "fruit", "car", "house", "tree"));

        Assert.Contains("time limit", Assert.Single(SeedValidator.Validate(set)));
    }

    [Fact]
    public void Validate_DuplicateQuestionIds_AreRejected()
    {
        var set = Set(15,
            Q("q1", 0, "apple", "a", "fruit", "car", "house", "tree"),
            Q("q1", 1, "dog", "a", "animal", "car", "house", "tree"));

        Assert.Contains("question id 'q1'", Assert.Single(SeedValidator.Validate(set)));
    }

    [Fact]
    public void Validate_DuplicatePositions_AreRejected()
    {
        var set = Set(15,
            Q("q1", 0, "apple", "a", "fruit", "car", "house", "tree"),
            Q("q2", 0, "dog", "a", "animal", "car", "house", "tree"));

        Assert.Contains("position 0", Assert.Single(SeedValidator.Validate(set)));
    }

    [Fact]
    public void Validate_TheSameWordInBothDirections_IsRejected()
    {
        // "apple" is the prompt of q1 and the correct answer of q2 (meaning -> word).
        var set = Set(15,
            Q("q1", 0, "apple", "a", "a round fruit", "a vehicle", "a building", "a plant"),
            Q("q2", 1, "a round fruit", QuestionDirection.MeaningToWord, "b", "car", "Apple", "house", "tree"));

        var error = Assert.Single(SeedValidator.Validate(set));

        Assert.Contains("q2", error);
        Assert.Contains("'apple'", error);
    }

    [Fact]
    public void Validate_AnEmptyPromptOrOptionText_IsRejected()
    {
        var set = Set(15, Q("q1", 0, " ", "a", "fruit", "car", "", "tree"));

        var errors = SeedValidator.Validate(set);

        Assert.Contains(errors, e => e.Contains("prompt"));
        Assert.Contains(errors, e => e.Contains("empty option text"));
    }
}
