namespace PaketUtilityServices.Core.Models;

/// <summary>
/// Represents parsed NuGet package identity and version metadata.
/// </summary>
public class PackageInfo
{
    /// <summary>
    /// Gets or sets the unique package identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exact package version string.
    /// </summary>
    public string Version { get; set; } = string.Empty;
}