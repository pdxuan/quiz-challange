using System.Net;
using Microsoft.Data.Sqlite;
using Quiz.Server.Tests.TestHost;

namespace Quiz.Server.Tests.Data;

public class DatabaseHealthTests
{
    [Fact]
    public async Task Health_WhenTheDatabaseIsReachable_Returns200()
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_WhenTheDatabaseFileCannotBeOpened_Returns503()
    {
        using var factory = new QuizApiFactory();
        var client = factory.CreateClient(); // starts and migrates
        SqliteConnection.ClearAllPools();
        Directory.Move(factory.DataDirectory, factory.DataDirectory + "-gone"); // the folder of the DB file disappears

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
