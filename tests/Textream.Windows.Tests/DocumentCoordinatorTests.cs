using System.IO;
using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class DocumentCoordinatorTests : IDisposable
{
    private readonly string _tempDocFile;

    public DocumentCoordinatorTests()
    {
        _tempDocFile = Path.Combine(Path.GetTempPath(), $"textream_doc_{Guid.NewGuid():N}.textream");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDocFile))
        {
            try { File.Delete(_tempDocFile); } catch { }
        }
    }

    [Fact]
    public void NewDocument_HasOnePageWithDefaultText()
    {
        var coordinator = new TextreamCoordinator();

        Assert.Single(coordinator.Pages);
        Assert.Contains("Welcome to Textream!", coordinator.CurrentPageText);
        Assert.False(coordinator.HasUnsavedChanges);
        Assert.Equal(0, coordinator.CurrentPageIndex);
        Assert.Null(coordinator.CurrentFilePath);
        Assert.Equal("Untitled", coordinator.DocumentTitle);
    }

    [Fact]
    public void AddPage_AppendsNewPageAndUpdatesIndex()
    {
        var coordinator = new TextreamCoordinator();
        coordinator.AddPage();

        Assert.Equal(2, coordinator.Pages.Count);
        Assert.Equal(1, coordinator.CurrentPageIndex);
        Assert.Equal(string.Empty, coordinator.CurrentPageText);
        Assert.True(coordinator.HasUnsavedChanges);
    }

    [Fact]
    public void DeletePage_RemovesTargetPageAndMaintainsValidSelection()
    {
        var coordinator = new TextreamCoordinator();
        coordinator.AddPage();
        coordinator.CurrentPageText = "Page 2 content";
        coordinator.AddPage();
        coordinator.CurrentPageText = "Page 3 content";

        Assert.Equal(3, coordinator.Pages.Count);
        Assert.Equal(2, coordinator.CurrentPageIndex);

        // Delete page at index 1
        coordinator.DeletePage(1);

        Assert.Equal(2, coordinator.Pages.Count);
        Assert.Equal(1, coordinator.CurrentPageIndex);
        Assert.Equal("Page 3 content", coordinator.CurrentPageText);

        // Cannot delete when only 1 page remains
        coordinator.DeletePage(0);
        Assert.Single(coordinator.Pages);
        coordinator.DeletePage(0);
        Assert.Single(coordinator.Pages);
    }

    [Fact]
    public void HasNextPage_DetectsUpcomingNonEmptyPages()
    {
        var coordinator = new TextreamCoordinator();
        coordinator.Pages.Clear();
        coordinator.Pages.Add("Page 1");
        coordinator.Pages.Add("   ");
        coordinator.Pages.Add("Page 3");
        coordinator.CurrentPageIndex = 0;

        Assert.True(coordinator.HasNextPage);

        coordinator.CurrentPageIndex = 2;
        Assert.False(coordinator.HasNextPage);
    }

    [Fact]
    public async Task SaveAndOpen_PreservesPagesAcrossSessions()
    {
        var coordinator = new TextreamCoordinator();
        coordinator.Pages.Clear();
        coordinator.Pages.Add("First slide script");
        coordinator.Pages.Add("Second slide script with [smile] cue");
        coordinator.Pages.Add("Final conclusion");

        await coordinator.SaveFileAsync(_tempDocFile);

        Assert.False(coordinator.HasUnsavedChanges);
        Assert.Equal(_tempDocFile, coordinator.CurrentFilePath);
        Assert.Equal(Path.GetFileNameWithoutExtension(_tempDocFile), coordinator.DocumentTitle);

        var readerCoordinator = new TextreamCoordinator();
        await readerCoordinator.OpenFileAsync(_tempDocFile);

        Assert.Equal(3, readerCoordinator.Pages.Count);
        Assert.Equal("First slide script", readerCoordinator.Pages[0]);
        Assert.Equal("Second slide script with [smile] cue", readerCoordinator.Pages[1]);
        Assert.Equal("Final conclusion", readerCoordinator.Pages[2]);
        Assert.False(readerCoordinator.HasUnsavedChanges);
    }

    [Fact]
    public async Task OpenFileAsync_WithCorruptedJson_ThrowsInvalidOperationException()
    {
        var corruptedFile = Path.Combine(Path.GetTempPath(), $"corrupted_{Guid.NewGuid():N}.textream");
        try
        {
            await File.WriteAllTextAsync(corruptedFile, "{ this is not a valid json array: 123 }");
            var coordinator = new TextreamCoordinator();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.OpenFileAsync(corruptedFile));
            Assert.Contains("valid Textream document", ex.Message);
        }
        finally
        {
            if (File.Exists(corruptedFile)) File.Delete(corruptedFile);
        }
    }

    [Fact]
    public void NextPage_And_PreviousPage_NavigateCorrectly()
    {
        var coordinator = new TextreamCoordinator();
        coordinator.Pages.Clear();
        coordinator.Pages.Add("Page 1");
        coordinator.Pages.Add("Page 2");
        coordinator.CurrentPageIndex = 0;

        Assert.True(coordinator.NextPage());
        Assert.Equal(1, coordinator.CurrentPageIndex);
        Assert.False(coordinator.NextPage()); // Reached end

        Assert.True(coordinator.PreviousPage());
        Assert.Equal(0, coordinator.CurrentPageIndex);
        Assert.False(coordinator.PreviousPage()); // Reached start
    }
}
