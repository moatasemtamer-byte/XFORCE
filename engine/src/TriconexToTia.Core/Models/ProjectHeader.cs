namespace TriconexToTia.Core.Models;

/// <summary>Values from the PLCopen <c>fileHeader</c> element.</summary>
public sealed class ProjectHeader
{
    public string? CompanyName { get; set; }

    public string? ProductName { get; set; }

    public string? ProductVersion { get; set; }

    public string? ProductRelease { get; set; }

    public string? SchemaVersion { get; set; }

    public string? PreviousProductVersion { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>ts: extensions all use this namespace.</summary>
    public const string VendorNamespace = "http://www.triconex.com/controller/03/2008";

    /// <summary>The PLCopen namespace Triconex exports.</summary>
    public const string PlcOpenNamespace = "http://www.plcopen.org/xml/tc6.xsd";

    public bool HasAny => ProductName is not null || CompanyName is not null || ContentDescription is not null;

    /// <summary>Free-text project description, possibly multi-line.</summary>
    public string? ContentDescription { get; set; }
}