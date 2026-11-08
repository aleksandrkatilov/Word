using System.Windows.Input;

namespace Pero.Services;

/// <summary>Команда-обёртка для кнопок ленты: RibbonButton движка надёжно выполняет Command, а не событие Click.</summary>
public sealed class ActionCommand(string name, Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        Log.Info("UI command: " + name);
        action();
    }
}
