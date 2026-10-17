using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HowToFishTrainer.App.Distribution;

internal sealed class PurchasePanelFactory : IPurchasePanelFactory
{
	private const string PurchaseUrl = "https://www.goofish.com/item?id=1077254294720";

	public FrameworkElement Create(Action<string, bool> report)
	{
		return null;
	}
}
