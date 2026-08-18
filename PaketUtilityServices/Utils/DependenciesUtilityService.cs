using System.CodeDom.Compiler;
using System.IO.Abstractions;
using System.Text;
using PaketUtilityServices.Core.Models;
using PaketUtilityServices.Infrastructure.Services;

namespace PaketUtilityServices.Infrastructure.Utils;

/// <summary>
/// Service responsible for orchestrating and generating Paket DSL configurations from Central Package Management (CPM) source definitions.
/// </summary>
public class DependenciesUtilityService : IDependenciesUtilityService
{
    private static readonly DependencyStrategy _dependencyStrategy = new();
    private const string _paketDependenciesFile = "paket.dependencies";
    private const string _packageVersionTag = "PackageVersion";
    private const string _globalPackageVersionTag = "GlobalPackageReference";
    private string _defaultNuGetSource => _dependencyStrategy.Source;
    private string _defaultStorage => _dependencyStrategy.Storage;
    private string _defaultStategyLevel => _dependencyStrategy.StrategyLevel;

    private readonly IFileSystem _fileSystem;
    private readonly ICpmXmlUtilityService _cpmXmlUtilityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DependenciesUtilityService"/> class.
    /// </summary>
    /// <param name="fileSystem">The abstracted file system dependency.</param>
    /// <param name="cpmXmlUtilityService">The XML utility service dependency used to parse MSBuild layouts.</param>
    /// <exception cref="ArgumentNullException">Thrown when fileSystem is null.</exception>
    public DependenciesUtilityService(
        IFileSystem fileSystem,
        ICpmXmlUtilityService? cpmXmlUtilityService = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem, nameof(fileSystem));
        _fileSystem = fileSystem;
        _cpmXmlUtilityService = cpmXmlUtilityService ?? new CpmXmlUtilityService(_fileSystem);
    }

    /// <summary>
    /// Defensively parses and extracts global package definitions matching the 'GlobalPackageReference' syntax pattern.
    /// </summary>
    /// <param name="filePath">The absolute path to the target props file.</param>
    /// <returns>A collection of verified global package identities and versions.</returns>
    public List<PackageInfo> ParseGlobalPackages(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        return _cpmXmlUtilityService.ParseCpmElements(filePath, _globalPackageVersionTag);
    }

    /// <summary>
    /// Defensively parses and extracts standard package definitions matching the 'PackageVersion' syntax pattern.
    /// </summary>
    /// <param name="filePath">The absolute path to the target props file.</param>
    /// <returns>A collection of verified standard package identities and versions.</returns>
    public List<PackageInfo> ParseDirectoryPackagesProps(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        return _cpmXmlUtilityService.ParseCpmElements(filePath, _packageVersionTag);
    }

    /// <summary>
    /// Composes and serializes the structured Paket dependencies layout securely onto the physical or virtual filesystem layer.
    /// </summary>
    /// <param name="solutionRoot">The base output root path where the DSL script should be generated.</param>
    /// <param name="cpmPropsFilePath">The reference source file path to parse metadata out of.</param>
    /// <exception cref="ArgumentException">Thrown when paths are missing or malformed.</exception>
    public void WritePaketDependenciesLayout(string solutionRoot, string cpmPropsFilePath)
    {
        if (string.IsNullOrWhiteSpace(solutionRoot)) throw new ArgumentException("Solution root path cannot be empty.", nameof(solutionRoot));
        if (string.IsNullOrWhiteSpace(cpmPropsFilePath)) throw new ArgumentException("CPM properties file path cannot be empty.", nameof(cpmPropsFilePath));
        if (!_fileSystem.File.Exists(cpmPropsFilePath)) throw new FileNotFoundException("The specified CPM configuration source was not found.", cpmPropsFilePath);

        // Step 1 & 2: Extract data into clean POCO representations safely before initializing I/O write operations
        var globalPackages = ParseGlobalPackages(cpmPropsFilePath);
        var standardPackages = ParseDirectoryPackagesProps(cpmPropsFilePath);

        var targetOutputPath = _fileSystem.Path.Combine(solutionRoot, _paketDependenciesFile);
        
        using var memoryStream = new MemoryStream();
        using (var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
        using (var indentedWriter = new IndentedTextWriter(streamWriter, "  "))
        {
            // Step 1: Write foundational header infrastructure details
            indentedWriter.WriteLine($"source {_defaultNuGetSource}");
            indentedWriter.WriteLine($"storage: {_defaultStorage}");
            indentedWriter.WriteLine($"strategy: {_defaultStategyLevel}");

            // Step 2: Handle global level packages (Deduplicated defensively)
            var cleanGlobals = SanitizeAndDeduplicate(globalPackages);
            if (cleanGlobals.Count > 0)
            {
                foreach (var globalPkg in cleanGlobals)
                {
                    // Global packages are often declared wrapped with transient properties or top-level constraints depending on DSL choices
                    indentedWriter.WriteLine($"nuget {globalPkg.Id} {globalPkg.Version} // Global Reference");
                }
                indentedWriter.WriteLine();
            }

            // Step 3: Handle standard level packages (Deduplicated defensively)
            var cleanStandards = SanitizeAndDeduplicate(standardPackages);
            foreach (var standardPkg in cleanStandards)
            {
                indentedWriter.WriteLine($"nuget {standardPkg.Id} {standardPkg.Version}");
            }

            indentedWriter.Flush();
        }

        // Atomic File Write operation to prevent runtime partial corruption side-effects
        memoryStream.Position = 0;
        using var fileStream = _fileSystem.File.Open(targetOutputPath, FileMode.Create, FileAccess.Write);
        memoryStream.CopyTo(fileStream);
    }

    /// <summary>
    /// Helper method providing architectural defensive guards against whitespace bugs, null injections, and identical key duplication hazards.
    /// </summary>
    private static List<PackageInfo> SanitizeAndDeduplicate(List<PackageInfo> rawPackages)
    {
        if (rawPackages == null || rawPackages.Count == 0) return [];

        return rawPackages
            .Where(p => !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.Version))
            .GroupBy(p => p.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new PackageInfo
            {
                Id = g.Key,
                Version = g.First().Version.Trim()
            })
            .ToList();
    }

    /// <summary>
    /// Strip version tag for all projects under directory <paramref name="rootPath"/>
    /// </summary>
    /// <param name="rootPath">root path</param>
    public void StripVersionFromProjects(string rootPath)
    {
        var projectFiles = _fileSystem.Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);

        foreach (var proj in projectFiles)
        {
            _cpmXmlUtilityService.RemovePackageReferencesVersion(proj);
        }
    }
}