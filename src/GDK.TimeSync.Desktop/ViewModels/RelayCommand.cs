using System.Windows.Input;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Func<bool> canExecute;

    public RelayCommand(Action execute, Func<bool> canExecute)
        : this(_ => execute(), canExecute) { }

    public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute ?? (() => true);
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter) => execute(parameter);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
