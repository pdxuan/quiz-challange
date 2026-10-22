namespace Quiz.Server.Features.QuizSets;

/// <summary>Lists the quiz sets without their answers (filled in S6).</summary>
public static class QuizSetsFeature
{
    public static IServiceCollection AddQuizSetsFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapQuizSetsFeature(this IEndpointRouteBuilder app) => app;
}
