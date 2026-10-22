using System.Runtime.CompilerServices;

// Points and online state change only through Session; tests may set them directly to
// build ranking cases.
[assembly: InternalsVisibleTo("Quiz.Server.Tests")]
