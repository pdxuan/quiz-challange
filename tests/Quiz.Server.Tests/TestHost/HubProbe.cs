using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Quiz.Server.Tests.TestHost;

/// <summary>
/// A real SignalR client on the test server (long polling through the in-memory handler),
/// logged in with the auth cookie. Every server event is kept, in order, so a test can wait for
/// the next one by name or check that none arrived.
/// </summary>
public sealed class HubProbe : IAsyncDisposable
{
    public static readonly string[] EventNames =
        ["PlayerJoined", "PlayerOnlineChanged", "QuestionStarted", "AnswersCount", "QuestionClosed", "QuizFinished"];

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Channel<JsonElement>> _events = new();

    private HubProbe(HubConnection connection)
    {
        Connection = connection;
        foreach (var name in EventNames)
        {
            var channel = Channel.CreateUnbounded<JsonElement>();
            _events[name] = channel;
            connection.On<JsonElement>(name, payload => channel.Writer.TryWrite(payload));
        }
    }

    public HubConnection Connection { get; }

    /// <summary>Connects as <paramref name="username"/> (logged in first), or anonymously when null.</summary>
    public static async Task<HubProbe> ConnectAsync(QuizApiFactory factory, string? username)
    {
        string? cookie = null;
        if (username is not null)
        {
            var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username, password = TestData.Password });
            login.EnsureSuccessStatusCode();
            cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        }

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/quiz"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (cookie is not null)
                {
                    options.Headers["Cookie"] = cookie;
                }
            })
            .Build();
        var probe = new HubProbe(connection);
        await connection.StartAsync();
        return probe;
    }

    public Task<JsonElement> InvokeAsync(string method, params object?[] args) =>
        Connection.InvokeCoreAsync<JsonElement>(method, args);

    /// <summary>The hub error code a call failed with (the client sees "...HubException: Code").</summary>
    public async Task<string> ErrorOfAsync(string method, params object?[] args)
    {
        var error = await Assert.ThrowsAsync<HubException>(() => InvokeAsync(method, args));
        const string marker = "HubException: ";
        var at = error.Message.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, error.Message);
        return error.Message[(at + marker.Length)..];
    }

    /// <summary>The next event with that name (fails after 10 s).</summary>
    public async Task<JsonElement> NextAsync(string eventName)
    {
        using var timeout = new CancellationTokenSource(Wait);
        try
        {
            return await _events[eventName].Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException($"No {eventName} event within {Wait.TotalSeconds} s");
        }
    }

    /// <summary>The next event with that name that matches, skipping the others.</summary>
    public async Task<JsonElement> NextAsync(string eventName, Func<JsonElement, bool> match)
    {
        while (true)
        {
            var next = await NextAsync(eventName);
            if (match(next))
            {
                return next;
            }
        }
    }

    /// <summary>True if an event with that name arrives within <paramref name="window"/>.</summary>
    public async Task<bool> ReceivesAsync(string eventName, TimeSpan window)
    {
        using var timeout = new CancellationTokenSource(window);
        try
        {
            await _events[eventName].Reader.ReadAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
}
