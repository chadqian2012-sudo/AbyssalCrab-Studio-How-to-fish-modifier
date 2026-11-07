using System;
using System.Windows.Input;

namespace HowToFishTrainer.App;

public sealed class RelayCommand : ICommand
{
	private readonly Action<object?> _action;
	public RelayCommand(Action action) { _action = _ => action(); }
	public RelayCommand(Action<object?> action) { _action = action; }
	public event EventHandler? CanExecuteChanged
	{
		add
		{
		}
		remove
		{
		}
	}

	public bool CanExecute(object? parameter)
	{
		return true;
	}

	public void Execute(object? parameter)
	{
		_action(parameter);
	}
}
