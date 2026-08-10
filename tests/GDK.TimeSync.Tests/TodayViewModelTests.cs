using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class TodayViewModelTests
{
    [Fact]
    public void AddItemCommand_AddsEditableItemAndUpdatesPlannedSeconds()
    {
        var today = new TodayViewModel();

        today.AddItemCommand.Execute(null);

        var item = Assert.Single(today.Items);
        Assert.True(item.IsEditable);
        Assert.Equal(0, today.PlannedSeconds);
    }

    [Fact]
    public void RemoveItemCommand_RemovesItemAndUpdatesPlannedSeconds()
    {
        var today = new TodayViewModel();
        var item = new PlannedWorkItemViewModel("Work", "CGMFRAVII-1", "Description", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");
        today.Items.Add(item);

        today.RemoveItemCommand.Execute(item);

        Assert.Empty(today.Items);
        Assert.Equal(0, today.PlannedSeconds);
    }

    [Fact]
    public void AddTemplateCommand_AddsEditableItemToToday()
    {
        var today = new TodayViewModel();
        var template = new RecurringTaskTemplateViewModel(
            "Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");

        today.AddTemplateCommand.Execute(template);

        var item = Assert.Single(today.Items);
        Assert.Equal("CGMFRAVII-2767", item.JiraIssueKey);
        Assert.True(item.IsEditable);
        Assert.Equal(1800, today.PlannedSeconds);
    }

    [Fact]
    public void ChangingDuration_UpdatesPlannedSeconds()
    {
        var today = new TodayViewModel();
        var item = new PlannedWorkItemViewModel("Work", "CGMFRAVII-1", "Description", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");
        today.Items.Add(item);

        item.Duration = TimeSpan.FromMinutes(45);

        Assert.Equal(2700, today.PlannedSeconds);
    }
}
