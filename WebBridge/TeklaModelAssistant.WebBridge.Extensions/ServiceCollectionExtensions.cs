using System;
using Microsoft.Extensions.DependencyInjection;
using TeklaModelAssistant.McpTools.Managers;
using TeklaModelAssistant.McpTools.Providers;
using TeklaModelAssistant.McpTools.Services;
using Trimble.Connect.Client;

namespace TeklaModelAssistant.WebBridge.Extensions
{
	public static class ServiceCollectionExtensions
	{
		public static IServiceProvider InitializeServices(this IServiceCollection serviceCollection)
		{
			if (serviceCollection == null)
			{
				throw new ArgumentNullException("serviceCollection");
			}
			serviceCollection.AddSingleton<ISelectionCacheManager, SelectionCacheManager>();
			serviceCollection.AddSingleton<ICredentialsProvider, TsIdentityManagerTokenProvider>();
			serviceCollection.AddSingleton<ITrimbleConnectService, TrimbleConnectService>();
			serviceCollection.AddSingleton<IExt2D3DService, Ext2D3DService>();
			serviceCollection.AddSingleton<IGraphToTrimbimService, GraphToTrimbimService>();
			return serviceCollection.BuildServiceProvider();
		}
	}
}
