using System;
using System.IO;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Services
{
	public class SessionLogger
	{
		private readonly string _sessionId;

		private string _logFilePath;

		private readonly object _lock = new object();

		private bool _logFileInitialized = false;

		public SessionLogger()
		{
			_sessionId = $"TeklaModelAssistant_Session_{DateTime.Now:yyyyMMdd_HHmmss}";
		}

		private void EnsureLogFileInitialized()
		{
			if (_logFileInitialized)
			{
				return;
			}
			lock (_lock)
			{
				if (_logFileInitialized)
				{
					return;
				}
				string modelPath = GetModelPath();
				string logsFolder = Path.Combine(modelPath, "logs");
				try
				{
					if (!Directory.Exists(logsFolder))
					{
						Directory.CreateDirectory(logsFolder);
					}
				}
				catch
				{
					logsFolder = Path.Combine(Path.GetTempPath(), "TeklaModelAssistant_Logs");
					Directory.CreateDirectory(logsFolder);
				}
				_logFilePath = Path.Combine(logsFolder, _sessionId + ".log");
				File.WriteAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SESSION START\n\n");
				_logFileInitialized = true;
			}
		}

		private string GetModelPath()
		{
			try
			{
				Model model = new Model();
				ModelInfo modelInfo = model.GetInfo();
				if (modelInfo != null && !string.IsNullOrEmpty(modelInfo.ModelPath))
				{
					return modelInfo.ModelPath;
				}
			}
			catch
			{
			}
			return Path.GetTempPath();
		}

		public void LogToolCall(string toolName, string parametersJson, string resultJson, TimeSpan duration, bool success)
		{
			try
			{
				EnsureLogFileInitialized();
				lock (_lock)
				{
					File.AppendAllText(_logFilePath, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] Tool: {1} | {2:F0}ms | {3}\n", DateTime.Now, toolName, duration.TotalMilliseconds, success ? "SUCCESS" : "FAILED") + "    Params: " + (string.IsNullOrEmpty(parametersJson) ? "{}" : parametersJson) + "\n    Result: " + resultJson + "\n\n");
				}
			}
			catch
			{
			}
		}

		public void LogError(string context, string errorMessage, string stackTrace = null)
		{
			try
			{
				EnsureLogFileInitialized();
				lock (_lock)
				{
					File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR - {context}: {errorMessage}\n" + ((stackTrace != null) ? ("    StackTrace:\n" + stackTrace + "\n\n") : "\n"));
				}
			}
			catch
			{
			}
		}
	}
}
