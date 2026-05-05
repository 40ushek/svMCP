namespace TeklaMcpServer.Api.Drawing;

public sealed class DrawingInfo
{
    public string? Guid { get; set; }

    public string? Name { get; set; }

    public string? Mark { get; set; }

    public string? Title1 { get; set; }

    public string? Title2 { get; set; }

    public string? Title3 { get; set; }

    public string? Type { get; set; }

    public string? DrawingType { get; set; }

    public string? Status { get; set; }

    public int? SourceModelObjectId { get; set; }

    public string? SourceModelObjectGuid { get; set; }

    public string? SourceModelObjectKind { get; set; }

    public bool IsLocked { get; set; }

    public bool IsIssued { get; set; }

    public bool IsIssuedButModified { get; set; }

    public bool IsFrozen { get; set; }

    public bool IsReadyForIssue { get; set; }

    public string? IsLockedBy { get; set; }

    public string? IsReadyForIssueBy { get; set; }

    public string? CreationDate { get; set; }

    public string? ModificationDate { get; set; }

    public string? IssuingDate { get; set; }

    public string? OutputDate { get; set; }
}
