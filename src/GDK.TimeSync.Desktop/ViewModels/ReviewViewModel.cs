namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class ReviewViewModel
{
    public ReviewViewModel() => PostAllCommand = new RelayCommand(() => { }, () => false);

    public bool CanPostAll => false;
    public string PostAllExplanation => "Post all is disabled until the delivery workflow is available. No external systems can be contacted from this milestone.";
    public RelayCommand PostAllCommand { get; }
}
