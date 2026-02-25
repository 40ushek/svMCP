using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Fusion;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using TeklaModelAssistant.McpTools.Models;
using TeklaModelAssistant.McpTools.Services;
using TeklaModelAssistant.WebBridge.Extensions;
using TeklaModelAssistant.WebBridge.UI;

namespace TeklaModelAssistant.WebBridge
{
	[ComVisible(true)]
	public class AgenticIntegrator
	{
		private readonly BrowserViewModel viewModel;

		private IServiceProvider _serviceProvider;

		private SessionLogger _sessionLogger;

		public AgenticIntegrator(BrowserViewModel viewModel)
		{
			this.viewModel = viewModel ?? throw new ArgumentNullException("viewModel");
			IServiceCollection services = new ServiceCollection();
			_serviceProvider = services.InitializeServices();
			_sessionLogger = new SessionLogger();
		}

		public async Task<string> ExecuteTool(string toolName, string parametersJson, bool limitResponse = true)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				ToolExecutionResult result = await ExecuteStaticToolMethod(toolName, parametersJson);
				string resultJson = JsonConvert.SerializeObject(result);
				stopwatch.Stop();
				_sessionLogger?.LogToolCall(toolName, parametersJson, resultJson, stopwatch.Elapsed, result.Success);
				if (limitResponse && resultJson.Length > 8000)
				{
					BrowserViewModel browserViewModel = viewModel;
					if (browserViewModel != null)
					{
						IHost host = ((ViewModel)browserViewModel).Host;
						if (host != null)
						{
							IHostDiagnostics diagnostics = host.Diagnostics;
							if (diagnostics != null)
							{
								diagnostics.Warning("Tool result exceeds 8000 character limit.", Array.Empty<object>());
							}
						}
					}
					return "Tool result exceeds 8000 character limit, showing truncated result:\n" + resultJson.Substring(0, 7500);
				}
				return resultJson;
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				stopwatch.Stop();
				BrowserViewModel browserViewModel2 = viewModel;
				if (browserViewModel2 != null)
				{
					IHost host2 = ((ViewModel)browserViewModel2).Host;
					if (host2 != null)
					{
						IHostDiagnostics diagnostics2 = host2.Diagnostics;
						if (diagnostics2 != null)
						{
							diagnostics2.Warning("Tool execution failed: {0}", new object[1] { ex2.ToString() });
						}
					}
				}
				ToolExecutionResult errorResult = ToolExecutionResult.CreateErrorResult("Tool execution failed: " + ex2.Message, ex2.ToString());
				string resultJson2 = JsonConvert.SerializeObject(errorResult);
				_sessionLogger?.LogToolCall(toolName, parametersJson, resultJson2, stopwatch.Elapsed, false);
				_sessionLogger?.LogError("Tool Execution", ex2.Message, ex2.StackTrace);
				return resultJson2;
			}
		}

		private MethodInfo FindToolMethod(string methodName)
		{
			Assembly toolsAssembly = typeof(ToolExecutionResult).Assembly;
			IEnumerable<Type> toolClasses = from t in toolsAssembly.GetTypes()
				where t.Namespace == "TeklaModelAssistant.McpTools.Tools" && t.IsClass
				select t;
			foreach (Type toolClass in toolClasses)
			{
				MethodInfo method = toolClass.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
				if (method != null && (method.ReturnType.Name.Contains("ToolExecutionResult") || method.ReturnType.GetGenericArguments().Any((Type t) => t.Name.Contains("ToolExecutionResult"))))
				{
					return method;
				}
			}
			return null;
		}

		private async Task<ToolExecutionResult> ExecuteStaticToolMethod(string methodName, string parametersJson)
		{
			try
			{
				MethodInfo method = FindToolMethod(methodName);
				if (method == null)
				{
					return ToolExecutionResult.CreateErrorResult("Tool '" + methodName + "' not found");
				}
				Dictionary<string, object> parameters = (string.IsNullOrWhiteSpace(parametersJson) ? new Dictionary<string, object>() : JsonConvert.DeserializeObject<Dictionary<string, object>>(parametersJson));
				ParameterInfo[] methodParams = method.GetParameters();
				object[] args = new object[methodParams.Length];
				for (int i = 0; i < methodParams.Length; i++)
				{
					ParameterInfo param = methodParams[i];
					object service = _serviceProvider.GetService(param.ParameterType);
					object value;
					if (service != null)
					{
						args[i] = service;
					}
					else if (parameters.TryGetValue(param.Name, out value))
					{
						Type targetType = Nullable.GetUnderlyingType(param.ParameterType) ?? param.ParameterType;
						if (value == null || (value is string str && string.IsNullOrWhiteSpace(str) && targetType != typeof(string)))
						{
							args[i] = null;
						}
						else
						{
							args[i] = ((targetType == typeof(string)) ? value.ToString() : ((targetType == typeof(int)) ? ((object)Convert.ToInt32(value)) : ((targetType == typeof(double)) ? ((object)Convert.ToDouble(value)) : Convert.ChangeType(value, targetType))));
						}
					}
					else if (param.HasDefaultValue)
					{
						args[i] = param.DefaultValue;
					}
					else if (param.ParameterType.IsValueType && Nullable.GetUnderlyingType(param.ParameterType) == null)
					{
						args[i] = Activator.CreateInstance(param.ParameterType);
					}
					else
					{
						args[i] = null;
						value = null;
					}
				}
				object result = method.Invoke(null, args);
				if (result is Task task)
				{
					await task;
					return task.GetType().GetProperty("Result")?.GetValue(task) as ToolExecutionResult;
				}
				return result as ToolExecutionResult;
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				return ToolExecutionResult.CreateErrorResult("Execution error: " + ex2.Message, ex2.ToString());
			}
		}

		public Task<string> GetToolSchemas()
		{
			try
			{
				List<object> toolList = new List<object>();
				Assembly toolsAssembly = typeof(ToolExecutionResult).Assembly;
				List<Type> toolClasses = (from t in toolsAssembly.GetTypes()
					where t.Namespace == "TeklaModelAssistant.McpTools.Tools" && t.IsClass && !t.IsAbstract && t.IsPublic && t.Name.Contains("Tool")
					select t).ToList();
				foreach (Type toolClass in toolClasses)
				{
					IEnumerable<MethodInfo> methods = from m in toolClass.GetMethods(BindingFlags.Static | BindingFlags.Public)
						where m.ReturnType.Name.Contains("ToolExecutionResult") || (m.ReturnType.IsGenericType && m.ReturnType.GetGenericArguments().Any((Type t) => t.Name.Contains("ToolExecutionResult")))
						select m;
					foreach (MethodInfo method in methods)
					{
						DescriptionAttribute methodDesc = method.GetCustomAttribute<DescriptionAttribute>();
						DescriptionAttribute classDesc = toolClass.GetCustomAttribute<DescriptionAttribute>();
						string description = methodDesc?.Description ?? classDesc?.Description ?? (method.Name + " tool");
						ParameterInfo[] parameters = method.GetParameters();
						Dictionary<string, object> paramProps = new Dictionary<string, object>();
						List<string> requiredParams = new List<string>();
						ParameterInfo[] array = parameters;
						foreach (ParameterInfo param in array)
						{
							if (!param.ParameterType.IsInterface)
							{
								DescriptionAttribute paramDesc = param.GetCustomAttribute<DescriptionAttribute>();
								paramProps[param.Name] = new
								{
									type = GetJsonType(param.ParameterType),
									description = (paramDesc?.Description ?? param.Name)
								};
								if (!param.IsOptional && !param.HasDefaultValue)
								{
									requiredParams.Add(param.Name);
								}
							}
						}
						toolList.Add(new
						{
							name = method.Name,
							description = description,
							parameters = new
							{
								type = "object",
								properties = paramProps,
								required = requiredParams.ToArray()
							}
						});
					}
				}
				return Task.FromResult(JsonConvert.SerializeObject(toolList, Formatting.Indented));
			}
			catch (Exception ex)
			{
				BrowserViewModel browserViewModel = viewModel;
				if (browserViewModel != null)
				{
					IHost host = ((ViewModel)browserViewModel).Host;
					if (host != null)
					{
						IHostDiagnostics diagnostics = host.Diagnostics;
						if (diagnostics != null)
						{
							diagnostics.Warning("GetToolSchemas failed: {0}", new object[1] { ex.Message });
						}
					}
				}
				return Task.FromResult("[]");
			}
		}

		private static string GetJsonType(Type type)
		{
			Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
			if (underlyingType == typeof(string))
			{
				return "string";
			}
			if (underlyingType == typeof(int) || underlyingType == typeof(double) || underlyingType == typeof(float) || underlyingType == typeof(long))
			{
				return "number";
			}
			if (underlyingType == typeof(bool))
			{
				return "boolean";
			}
			return "string";
		}
	}
}
