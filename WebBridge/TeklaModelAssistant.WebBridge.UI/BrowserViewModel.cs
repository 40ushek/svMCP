#define DEBUG
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Fusion;
using Tekla.Structures.WebBrowser;
using TeklaModelAssistant.WebBridge.Services;

namespace TeklaModelAssistant.WebBridge.UI
{
	public class BrowserViewModel : ViewModel, IDisposable
	{
		private IBrowser browserWrapper;

		private AgenticIntegrator integrator;

		private LocalHttpServer httpServer;

		private string webRootPath;

		private bool disposedValue;

		public FrameworkElement Browser => browserWrapper?.Browser;

		public BrowserViewModel()
		{
			try
			{
				Debug.WriteLine("=== Starting Tekla Model Assistant ===");
				ExtractWebUI();
				Debug.WriteLine("UI extracted to: " + webRootPath);
				httpServer = new LocalHttpServer(webRootPath);
				httpServer.Start();
				Debug.WriteLine("HTTP server started at: " + httpServer.BaseUrl);
				browserWrapper = WebBrowserFactory.CreateWebBrowserInstance(httpServer.BaseUrl, httpServer.Port, true, false);
				if (browserWrapper == null)
				{
					throw new Exception("WebBrowserFactory returned null");
				}
				Debug.WriteLine("Browser created, configuring...");
				browserWrapper.BindPopUpHandler();
				browserWrapper.DisableDefaultErrorPage();
				integrator = new AgenticIntegrator(this);
				browserWrapper.RegisterJsObject("integrator", integrator, true);
				Debug.WriteLine("=== Initialization complete ===");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"=== INITIALIZATION FAILED: {ex} ===");
				IHost host = ((ViewModel)this).Host;
				if (host != null)
				{
					IHostDiagnostics diagnostics = host.Diagnostics;
					if (diagnostics != null)
					{
						diagnostics.Warning("Failed to initialize Tekla Model Assistant: {0}", new object[1] { ex.ToString() });
					}
				}
			}
		}

		protected override void Initialize()
		{
			((ViewModel)this).Initialize();
		}

		private void ExtractWebUI()
		{
			try
			{
				string tempDir = Path.Combine(Path.GetTempPath(), "TeklaModelingAssistant", "WebUI");
				if (Directory.Exists(tempDir))
				{
					try
					{
						Directory.Delete(tempDir, true);
					}
					catch
					{
					}
				}
				Directory.CreateDirectory(tempDir);
				Assembly assembly = Assembly.GetExecutingAssembly();
				using (Stream stream = assembly.GetManifestResourceStream("WebUI.index.html"))
				{
					if (stream == null)
					{
						throw new Exception("WebUI.index.html resource not found");
					}
					string indexPath = Path.Combine(tempDir, "index.html");
					using (FileStream fileStream = File.Create(indexPath))
					{
						stream.CopyTo(fileStream);
					}
				}
				webRootPath = tempDir;
			}
			catch (Exception ex)
			{
				throw new Exception("Failed to extract UI: " + ex.Message, ex);
			}
		}

		[CommandHandler]
		public void Reload()
		{
			browserWrapper?.Reload();
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposedValue)
			{
				return;
			}
			if (disposing)
			{
				httpServer?.Dispose();
				browserWrapper?.Dispose();
				try
				{
					if (!string.IsNullOrEmpty(webRootPath) && Directory.Exists(webRootPath))
					{
						Directory.Delete(webRootPath, true);
					}
				}
				catch
				{
				}
			}
			disposedValue = true;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}
}
