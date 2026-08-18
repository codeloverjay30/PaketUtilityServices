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
    /// Safely purges inline Version attributes from MSBuild itemgroups utilizing transactional rollbacks on unexpected crashes.
    /// </summary>
    /// <param name="projectFilePath">The destination project file path.</param>
    public bool RemovePackageReferencesVersion(
        string projectFilePath
    )
    {
        if (string.IsNullOrWhiteSpace(projectFilePath)) throw new ArgumentException("Path cannot be empty.", nameof(projectFilePath));
        if (!_fileSystem.File.Exists(projectFilePath)) return false;

        XDocument doc;
        using (var readStream = _fileSystem.File.OpenRead(projectFilePath))
        {
            doc = XDocument.Load(readStream);
        }

        if (doc.Root == null) return false;

        var targets = doc.Descendants().Where(x => x.Name.LocalName == "PackageReference").ToList();
        var isModified = false;

        foreach (var element in targets)
        {
            var attr = element.Attribute("Version");
            if (attr != null)
            {
                attr.Remove();
                isModified = true;
            }
        }

        if (isModified)
        {
            using var tx = new Utils.FileTransactionScope(_fileSystem, projectFilePath);
            using var writeStream = _fileSystem.File.OpenWrite(projectFilePath);
            writeStream.SetLength(0); // Clean slate write
            doc.Save(writeStream);
            tx.Commit(); // Commit only if execution reaches here smoothly
        }

        return isModified;
    }
}