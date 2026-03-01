using System.Collections.Generic;

namespace TeklaMcpServer.Api.Selection;

public class SelectionResult
{
    public bool Success { get; set; }

    public IList<int> Ids { get; set; } = new List<int>();

    public string? EffectiveSelectionId { get; set; }

    public int Total { get; set; }

    public bool HasMore { get; set; }

    public string? Message { get; set; }

    public string? Data { get; set; }
}
