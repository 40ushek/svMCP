using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TeklaModelAssistant.WebBridge.Services
{
	public class LocalHttpServer : IDisposable
	{
		private HttpListener listener;

		private Thread listenerThread;

		private readonly string webRoot;

		private bool isRunning;

		public int Port { get; private set; }

		public string BaseUrl => $"http://localhost:{Port}";

		public LocalHttpServer(string webRootPath)
		{
			webRoot = webRootPath;
		}

		public void Start()
		{
			Port = FindAvailablePort();
			listener = new HttpListener();
			listener.Prefixes.Add($"http://localhost:{Port}/");
			listener.Start();
			isRunning = true;
			listenerThread = new Thread(Listen)
			{
				IsBackground = true
			};
			listenerThread.Start();
		}

		private void Listen()
		{
			while (isRunning)
			{
				try
				{
					HttpListenerContext context = listener.GetContext();
					ThreadPool.QueueUserWorkItem(delegate
					{
						HandleRequest(context);
					});
				}
				catch (HttpListenerException)
				{
					if (isRunning)
					{
						throw;
					}
				}
			}
		}

		private void HandleRequest(HttpListenerContext context)
		{
			try
			{
				string path = context.Request.Url.LocalPath.TrimStart('/');
				if (string.IsNullOrEmpty(path))
				{
					path = "index.html";
				}
				string filePath = Path.Combine(webRoot, path);
				if (File.Exists(filePath))
				{
					byte[] buffer = File.ReadAllBytes(filePath);
					context.Response.ContentType = GetContentType(path);
					context.Response.ContentLength64 = buffer.Length;
					context.Response.OutputStream.Write(buffer, 0, buffer.Length);
				}
				else
				{
					context.Response.StatusCode = 404;
				}
			}
			catch (Exception)
			{
				context.Response.StatusCode = 500;
			}
			finally
			{
				context.Response.OutputStream.Close();
			}
		}

		private static string GetContentType(string path)
		{
			string ext = Path.GetExtension(path).ToLowerInvariant();
			if (1 == 0)
			{
			}
			string result;
			switch (ext)
			{
			case ".html":
				result = "text/html";
				break;
			case ".js":
				result = "application/javascript";
				break;
			case ".css":
				result = "text/css";
				break;
			case ".json":
				result = "application/json";
				break;
			case ".png":
				result = "image/png";
				break;
			case ".jpg":
			case ".jpeg":
				result = "image/jpeg";
				break;
			case ".gif":
				result = "image/gif";
				break;
			case ".svg":
				result = "image/svg+xml";
				break;
			case ".webp":
				result = "image/webp";
				break;
			default:
				result = "application/octet-stream";
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}

		private static int FindAvailablePort()
		{
			TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		public void Dispose()
		{
			isRunning = false;
			listener?.Stop();
			listener?.Close();
		}
	}
}
