using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Quiz.Server.Tests.TestHost;

/// <summary>Keeps every log entry with the scopes that were active, so tests can check what was logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<LogEntry> Entries => _entries.ToList();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyList<object?> Scopes)
    {
        /// <summary>The value of a named scope property, e.g. "quizId".</summary>
        public object? Scope(string key) =>
            Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(s => s).FirstOrDefault(kv => kv.Key == key).Value;
    }

    private sealed class Logger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<object?>();
            provider._scopes.ForEachScope((scope, list) => list.Add(scope), scopes);
            provider._entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception, scopes));
        }
    }
}
