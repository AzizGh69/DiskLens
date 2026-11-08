using System;
using System.Windows.Input;

namespace DiskLens.Mvvm;

/// <summary>Minimal ICommand that re-queries CanExecute through WPF's CommandManager.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public static RelayCommand From(Action execute, Func<bool>? canExecute = null) =>
        new(_ => execute(), canExecute is null ? null : _ => canExecute());

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}
