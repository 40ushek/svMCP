using System;
using Fusion;

namespace TeklaModelAssistant.WebBridge
{
	public interface IBrowserManager : IDisposable
	{
		ViewModel BrowserViewModel { get; }

		void InitializeBrowser();
	}
}
