using System;
using System.Windows;
using Fusion;
using TeklaModelAssistant.WebBridge.UI;

namespace TeklaModelAssistant.WebBridge
{
	public class BrowserManager : IBrowserManager, IDisposable
	{
		private BrowserViewModel browserViewModel;

		public ViewModel BrowserViewModel => (ViewModel)(object)browserViewModel;

		public FrameworkElement BrowserElement => browserViewModel?.Browser;

		public void InitializeBrowser()
		{
			if (browserViewModel == null)
			{
				browserViewModel = new BrowserViewModel();
			}
		}

		public void Dispose()
		{
			((IDisposable)browserViewModel)?.Dispose();
			browserViewModel = null;
		}
	}
}
