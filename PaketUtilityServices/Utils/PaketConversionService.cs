using System.CodeDom.Compiler;
using System.IO.Abstractions;
using System.Text;
using System.Xml.Linq;
using CliUtilityServices;
using CliWrap.Buffered;
using CommonModels;
using PaketUtilityServices.Core.Interfaces;
using PaketUtilityServices.Infrastructure.Services;

namespace PaketUtilityServices.Infrastructure.Utils;

public class PaketConversionService : IPaketConversionService
{
    private const string PaketDependenciesFile = "paket.dependencies";
    private const string Packet = "paket";
    private const string Install = "install";

    private readonly IFileSystem _fileSystem;
    private readonly ICliCommandExecutor _commandExecutor;
    private readonly ICpmXmlUtilityService _cpmXmlUtilityService;
    private readonly IDependenciesUtilityService _dependenciesUtilityService;

    public PaketConversionService(
        IFileSystem fileSystem,
        ICliCommandExecutor commandExecutor,
        ICpmXmlUtilityService cpmXmlUtilityService,
        IDependenciesUtilityService dependenciesUtilityService
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem, nameof(fileSystem));
        ArgumentNullException.ThrowIfNull(commandExecutor, nameof(commandExecutor));
        ArgumentNullException.ThrowIfNull(cpmXmlUtilityService, nameof(cpmXmlUtilityService));
        ArgumentNullException.ThrowIfNull(dependenciesUtilityService, nameof(dependenciesUtilityService));

        _fileSystem = fileSystem;
        _commandExecutor = commandExecutor;
        _cpmXmlUtilityService = cpmXmlUtilityService;
        _dependenciesUtilityService = dependenciesUtilityService;
    }

    /// <summary>
    /// Converts a solution from CPM to Paket model safely using multi-file transaction guards.
    /// </summary>
    /// <param name="solutionRoot">The absolute root directory path of the target MonoRepo solution.</param>
    /// <exception cref="DirectoryNotFoundException">Thrown when the specified solution root does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when critical CPM source configuration files are missing.</exception>
    public StatusJsonModels ConvertCpmToPaket(string solutionRoot)
    {
        var statusJsonModels = new StatusJsonModels();
        
        if (string.IsNullOrWhiteSpace(solutionRoot)) throw new ArgumentException("Solution root path cannot be empty.", nameof(solutionRoot));
        if (!_fileSystem.Directory.Exists(solutionRoot)) throw new DirectoryNotFoundException($"The specified solution root directory was not found: {solutionRoot}");

        var cpmPropsPath = _fileSystem.Path.Combine(solutionRoot, "Directory.Packages.props");
        if (!_fileSystem.File.Exists(cpmPropsPath))
        {
            throw new InvalidOperationException($"Critical CPM configuration file was missing at expected location: {cpmPropsPath}");
        }

        // 1. Parse and extract all package information from CPM safely
        var packages = _cpmXmlUtilityService.ParseCpmElements(cpmPropsPath, "PackageVersion");
        if (packages.Count == 0)
        {
            var message = $"No package information could be extracted from CPM configuration at {cpmPropsPath}. Aborting conversion.";
            statusJsonModels.StatusList.Add(new StatusJsonModel
            {
                IsSuccess = false,
                Result = message,
                OverallErrorMessage = message,
                ErrorMessage = message,
            });

            return statusJsonModels;
        }
        // 2. Discover all project files (*.csproj) that need version attribute pruning
        var projectFiles = _fileSystem.Directory.GetFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories);
        var transactionStack = new Stack<IFileTransactionScope>();

        try
        {
            // 3. Open transaction scopes for all project files to guard against mid-process crash corruptions
            foreach (var projFile in projectFiles)
            {
                var tx = new FileTransactionScope(_fileSystem, projFile);
                transactionStack.Push(tx);

                // Modify XML content in memory and save under transaction protection
                _cpmXmlUtilityService.RemovePackageReferencesVersion(projFile);
            }

            // 4. Protect the central CPM props file before renaming it to avoid compilation collision
            var cpmTx = new FileTransactionScope(_fileSystem, cpmPropsPath);
            transactionStack.Push(cpmTx);

            // Defensively rename CPM configuration to completely disable it for MSBuild Engine
            var backupCpmPath = cpmPropsPath + ".converted_bak";
            _fileSystem.File.Move(cpmPropsPath, backupCpmPath, overwrite: true);

            // 5. Commit all file mutations simultaneously once every phase succeeded flawlessly
            while (transactionStack.Count > 0)
            {
                var tx = transactionStack.Pop();
                tx.Commit();
                tx.Dispose();

                var message = $"Successfully committed changes to file";
                statusJsonModels.StatusList.Add(new StatusJsonModel
                {
                    IsSuccess = true,
                    Result = message,
                    OverallErrorMessage = string.Empty,
                    ErrorMessage = string.Empty,
                });
            }

            _dependenciesUtilityService.WritePaketDependenciesLayout(solutionRoot, backupCpmPath);

            return statusJsonModels;
        }
        catch (Exception)
        {
            // Automatic Rollback: If any error occurs, disposing uncommitted scopes will restore all files to original state
            while (transactionStack.Count > 0)
            {
                transactionStack.Pop().Dispose();
            }
            throw;
        }
    }

    public async Task<BufferedCommandResult> RunPaketInstallAsync(string rootPath)
    {
        var arguments = $"{Packet} {Install} \"{rootPath}\"";
        var workingDirectory = rootPath;
        var commandInputFactory = new CommandLineInputFactory();

        var tempCommandInput = commandInputFactory.CreateShellInput(
            arguments,
            workingDirectory
        );

        var commandInput = tempCommandInput with {
            InputEncoding = Encoding.UTF8,
            OutputEncoding = Encoding.UTF8
        };  

        var result = await _commandRunner.ExecuteAsync(commandInput);
        
        if (result.ExitCode != 0)
        {
            throw new Exception($"Failure to execute {commandInput.FileName} with arguments {commandInput.Arguments}. The error message:{result.StandardError}");
        }
        //Console.WriteLine(result.StandardOutput);
        return result;
    }
}