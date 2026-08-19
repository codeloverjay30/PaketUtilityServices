using System.IO.Abstractions;
using System.Xml.Linq;
using PaketUtilityServices.Core.Models;

namespace PaketUtilityServices.Infrastructure.Services;

/// <summary>
/// Enterprise-grade implementation for reading, querying, and updating project XML patterns defensively.
/// </summary>
public class CpmXmlUtilityService: ICpmXmlUtilityService
{
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpmXmlUtilityService"/> class.
    /// </summary>
    /// <param name="fileSystem">The abstracted file system system dependency.</param>
    public CpmXmlUtilityService(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem, nameof(fileSystem));
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Extracts structured package references matching specific tags safely, accounting for varying XML namespace specifications.
    /// </summary>
    /// <param name="filePath">Target path to parse.</param>
    /// <param name="tagName">XML target tag matching token.</param>
    public List<PackageInfo> ParseCpmElements(string filePath, string tagName)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be null.", nameof(filePath));
        if (string.IsNullOrWhiteSpace(tagName)) throw new ArgumentException("Tag name cannot be null.", nameof(tagName));
        if (!_fileSystem.File.Exists(filePath)) throw new FileNotFoundException("Target configuration was missing.", filePath);

        using var stream = _fileSystem.File.OpenRead(filePath);
        var doc = XDocument.Load(stream);
        if (doc.Root == null) return [];

        var ns = doc.Root.GetDefaultNamespace();

        return doc.Descendants()
            .Where(x => x.Name.LocalName == tagName && (ns == XNamespace.None || x.Name.Namespace == ns))
            .Select(x => new PackageInfo
            {
                Id = x.Attribute("Include")?.Value ?? x.Attribute("Update")?.Value ?? string.Empty,
                Version = x.Attribute("Version")?.Value ?? x.Element(ns + "Version")?.Value ?? string.Empty
            })
            .Where(x => !string.IsNullOrEmpty(x.Id) && !string.IsNullOrEmpty(x.Version))
            .ToList();
    }

    /// <summary>
    /// Removes inline version attributes from package references in the specified project file.
    /// </summary>
    /// <param name="projectFilePath">The target project file path.</param>
    /// <returns>
    /// <see langword="true"/> when at least one version attribute was removed;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool RemovePackageReferencesVersion(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            throw new ArgumentException(
                "Path cannot be empty.",
                nameof(projectFilePath));
        }

        if (!_fileSystem.File.Exists(projectFilePath))
        {
            return false;
        }

        XDocument document;

        using (var readStream = _fileSystem.File.OpenRead(projectFilePath))
        {
            document = XDocument.Load(readStream);
        }

        if (document.Root is null)
        {
            return false;
        }

        var isModified = false;

        foreach (var packageReference in document
            .Descendants()
            .Where(static element =>
                element.Name.LocalName == "PackageReference"))
        {
            var versionAttribute = packageReference.Attribute("Version");

            if (versionAttribute is null)
            {
                continue;
            }

            versionAttribute.Remove();
            isModified = true;
        }

        if (!isModified)
        {
            return false;
        }

        using var writeStream = _fileSystem.File.Open(
            projectFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        document.Save(writeStream);

        return true;
    }
}