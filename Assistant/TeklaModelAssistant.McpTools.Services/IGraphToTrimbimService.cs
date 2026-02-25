using System.Threading.Tasks;

namespace TeklaModelAssistant.McpTools.Services
{
	public interface IGraphToTrimbimService
	{
		Task<string> ConvertGraphToTrimbimAsync(string graphJson);
	}
}
