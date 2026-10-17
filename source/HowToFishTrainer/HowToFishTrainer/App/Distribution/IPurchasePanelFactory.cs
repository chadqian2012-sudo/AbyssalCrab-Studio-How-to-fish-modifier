using System;
using System.Windows;

namespace HowToFishTrainer.App.Distribution;

internal interface IPurchasePanelFactory
{
	FrameworkElement? Create(Action<string, bool> report);
}
