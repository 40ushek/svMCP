using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace TeklaModelAssistant.WebBridge.UI
{
	public partial class BrowserView : UserControl, IComponentConnector
	{
		public BrowserView()
		{
			InitializeComponent();
			base.DataContextChanged += delegate(object s, DependencyPropertyChangedEventArgs e)
			{
				if (e.NewValue is BrowserViewModel browserViewModel)
				{
					BrowserHost.Content = browserViewModel.Browser;
				}
			};
		}
	}
}
