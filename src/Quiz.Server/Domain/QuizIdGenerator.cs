using System.Security.Cryptography;

namespace Quiz.Server.Domain;

public static class QuizIdGenerator
{
    /// <summary>A-Z and 2-9 without the look-alikes 0/O and 1/I.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int Length = 6;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Quiz IDs are case-insensitive on input.</summary>
    public static string Normalize(string input) => input.Trim().ToUpperInvariant();

    public static bool IsValid(string quizId) => quizId.Length == Length && quizId.All(Alphabet.Contains);
}
