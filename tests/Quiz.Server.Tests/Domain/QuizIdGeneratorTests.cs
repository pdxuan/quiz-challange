using Quiz.Server.Domain;

namespace Quiz.Server.Tests.Domain;

public class QuizIdGeneratorTests
{
    [Fact]
    public void New_IsSixCharsFromTheAllowedAlphabet()
    {
        for (var i = 0; i < 500; i++)
        {
            var id = QuizIdGenerator.New();

            Assert.Equal(6, id.Length);
            Assert.All(id, c => Assert.Contains(c, QuizIdGenerator.Alphabet));
        }
    }

    [Fact]
    public void Alphabet_HasNoLookAlikes()
    {
        Assert.Equal(32, QuizIdGenerator.Alphabet.Length);
        Assert.DoesNotContain('0', QuizIdGenerator.Alphabet);
        Assert.DoesNotContain('O', QuizIdGenerator.Alphabet);
        Assert.DoesNotContain('1', QuizIdGenerator.Alphabet);
        Assert.DoesNotContain('I', QuizIdGenerator.Alphabet);
    }

    [Theory]
    [InlineData("abc234", "ABC234")]
    [InlineData(" Abc234 ", "ABC234")]
    public void Normalize_UppercasesAndTrims(string input, string expected)
    {
        Assert.Equal(expected, QuizIdGenerator.Normalize(input));
    }

    [Theory]
    [InlineData("ABC234", true)]
    [InlineData("ABC23", false)]
    [InlineData("ABC2340", false)]
    [InlineData("ABCO34", false)] // O is not in the alphabet
    [InlineData("", false)]
    public void IsValid_ChecksLengthAndAlphabet(string id, bool expected)
    {
        Assert.Equal(expected, QuizIdGenerator.IsValid(id));
    }
}
