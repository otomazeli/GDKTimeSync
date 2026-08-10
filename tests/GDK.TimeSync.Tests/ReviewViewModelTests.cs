using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class ReviewViewModelTests
{
    [Fact]
    public void PostAll_IsUnavailableBeforeDeliveryWorkflowExists()
    {
        var review = new ReviewViewModel();

        Assert.False(review.CanPostAll);
        Assert.False(review.PostAllCommand.CanExecute(null));
    }
}
