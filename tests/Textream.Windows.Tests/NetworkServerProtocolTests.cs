using System.Text.Json;
using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class NetworkServerProtocolTests
{
    [Fact]
    public void BrowserState_SerializesMatchesExpectedJsonSchema()
    {
        var state = new BrowserState(
            words: new List<string> { "Hello", "world" },
            highlightedCharCount: 5,
            totalCharCount: 11,
            audioLevels: new List<double> { 0.1, 0.4, 0.8 },
            isListening: true,
            isDone: false,
            fontColor: "#FFFFFF",
            cueColor: "#FFB340",
            hasNextPage: false,
            isActive: true,
            highlightWords: true,
            lastSpokenText: "Hello"
        );

        string json = JsonSerializer.Serialize(state);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("words").GetArrayLength());
        Assert.Equal("Hello", root.GetProperty("words")[0].GetString());
        Assert.Equal(5, root.GetProperty("highlightedCharCount").GetInt32());
        Assert.Equal(11, root.GetProperty("totalCharCount").GetInt32());
        Assert.True(root.GetProperty("isListening").GetBoolean());
        Assert.Equal("#FFFFFF", root.GetProperty("fontColor").GetString());
        Assert.Equal("#FFB340", root.GetProperty("cueColor").GetString());
        Assert.Equal("Hello", root.GetProperty("lastSpokenText").GetString());
    }

    [Fact]
    public void DirectorCommand_DeserializesCorrectly()
    {
        string jsonSetText = "{\"type\":\"setText\",\"text\":\"New script content\",\"token\":\"abcd1234\"}";
        var cmdSet = JsonSerializer.Deserialize<DirectorCommand>(jsonSetText);
        Assert.NotNull(cmdSet);
        Assert.Equal("setText", cmdSet.type);
        Assert.Equal("New script content", cmdSet.text);
        Assert.Equal("abcd1234", cmdSet.token);

        string jsonUpdate = "{\"type\":\"updateText\",\"text\":\"Revised notes\",\"readCharCount\":24,\"token\":\"abcd1234\"}";
        var cmdUpdate = JsonSerializer.Deserialize<DirectorCommand>(jsonUpdate);
        Assert.NotNull(cmdUpdate);
        Assert.Equal("updateText", cmdUpdate.type);
        Assert.Equal("Revised notes", cmdUpdate.text);
        Assert.Equal(24, cmdUpdate.readCharCount);

        string jsonStop = "{\"type\":\"stop\",\"token\":\"abcd1234\"}";
        var cmdStop = JsonSerializer.Deserialize<DirectorCommand>(jsonStop);
        Assert.NotNull(cmdStop);
        Assert.Equal("stop", cmdStop.type);
    }
}
