using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

// A ComboBox bound with SelectedValue/SelectedValuePath writes null back to its source when it cannot
// resolve the value against its ItemsSource -- which happens whenever a cell is realised before the
// project list is there. Autosave then persisted that null, so every load quietly wiped the row's
// TogglProjectId while leaving the name intact. Delivery posts by id, so the work went to Toggl with
// no project at all.
public sealed class TogglProjectSelectionTests
{
    private static readonly TogglProject Cgm = new(77, "CGM");
    private static readonly TogglProject Other = new(88, "Other");

    [Fact]
    public void ClearingTheSelectionWithNoOptionsLoadedDoesNotWipeTheStoredProject()
    {
        var item = new PlannedWorkItemViewModel(togglProject: "CGM", togglProjectId: 77);

        item.SelectedTogglProject = null;

        Assert.Equal(77, item.TogglProjectId);
        Assert.Equal("CGM", item.TogglProject);
    }

    [Fact]
    public void ClearingTheSelectionWithOptionsLoadedIsARealUserActionAndClearsTheProject()
    {
        var item = new PlannedWorkItemViewModel(togglProject: "CGM", togglProjectId: 77);
        item.SetTogglProjectOptions([Cgm, Other]);

        item.SelectedTogglProject = null;

        Assert.Null(item.TogglProjectId);
        Assert.Equal("", item.TogglProject);
    }

    [Fact]
    public void ChoosingAProjectSetsBothTheIdAndTheName()
    {
        var item = new PlannedWorkItemViewModel();
        item.SetTogglProjectOptions([Cgm, Other]);

        item.SelectedTogglProject = Other;

        Assert.Equal(88, item.TogglProjectId);
        Assert.Equal("Other", item.TogglProject);
    }

    [Fact]
    public void TheSelectionResolvesFromTheStoredIdOnceOptionsArrive()
    {
        var item = new PlannedWorkItemViewModel(togglProject: "CGM", togglProjectId: 77);

        Assert.Null(item.SelectedTogglProject);

        item.SetTogglProjectOptions([Cgm, Other]);

        Assert.Same(Cgm, item.SelectedTogglProject);
    }

    // Repairs rows already wiped by the old binding: the name survived, so the id can be recovered.
    [Fact]
    public void ARowWhoseIdWasWipedRecoversItFromItsStoredName()
    {
        var item = new PlannedWorkItemViewModel(togglProject: "CGM", togglProjectId: null);

        item.SetTogglProjectOptions([Cgm, Other]);

        Assert.Equal(77, item.TogglProjectId);
        Assert.Same(Cgm, item.SelectedTogglProject);
    }

    [Fact]
    public void ANameThatMatchesNoProjectIsLeftAloneRatherThanGuessed()
    {
        var item = new PlannedWorkItemViewModel(togglProject: "a project this workspace does not have", togglProjectId: null);

        item.SetTogglProjectOptions([Cgm, Other]);

        Assert.Null(item.TogglProjectId);
        Assert.Equal("a project this workspace does not have", item.TogglProject);
    }
}
