using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HowToFishTrainer.App;

public sealed class FunEventCardViewModel : INotifyPropertyChanged
{
	private readonly Func<FunEventCardViewModel, Task> _execute;

	private bool _available;

	private bool _busy;

	private string _reason = "等待读取游戏资源";

	public FunEventDefinition Definition { get; }

	public string Id => Definition.Id;

	public string Title => Definition.Title;

	public string Description => Definition.Description;

	public string RiskLabel => Definition.RiskLabel;

	public int RequestedCount => Definition.RequestedCount;

	public bool Available
	{
		get
		{
			return _available;
		}
		internal set
		{
			if (Set(ref _available, value, "Available"))
			{
				OnPropertyChanged("CanExecute");
			}
		}
	}

	public bool Busy
	{
		get
		{
			return _busy;
		}
		internal set
		{
			if (Set(ref _busy, value, "Busy"))
			{
				OnPropertyChanged("CanExecute");
			}
		}
	}

	public string Reason
	{
		get
		{
			return _reason;
		}
		internal set
		{
			Set(ref _reason, value, "Reason");
		}
	}

	public bool CanExecute
	{
		get
		{
			if (Available)
			{
				return !Busy;
			}
			return false;
		}
	}

	public ICommand ExecuteCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	internal FunEventCardViewModel(FunEventDefinition definition, Func<FunEventCardViewModel, Task> execute)
	{
		Definition = definition;
		_execute = execute;
		ExecuteCommand = new RelayCommand(() =>
		{
			ExecuteAsync();
		});
	}

	private Task ExecuteAsync()
	{
		if (!CanExecute)
		{
			return Task.CompletedTask;
		}
		return _execute(this);
	}

	private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		return true;
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
