using System.IO.Abstractions;
using CliUtilityServices;
using Commands.Infrastructure;
using CommonModels;
using PaketUtilityServices.Core.Interfaces;
using PaketUtilityServices.Infrastructure.Services;

namespace PaketUtilityServices.Infrastructure.Utils;

/// <summary>
/// Coordinates conversion from Central Package Management configuration to Paket configuration.
/// </summary>
public sealed class PaketConversionService : IPaketConversionService
{
    private const string DirectoryPackagesPropsFileName = "Directory.Packages.props";
    private const string ConvertedBackupSuffix = ".converted_bak";
    private const string PaketCommand = "paket";
    private const string InstallArgument = "install";

    private readonly IFileSystem _fileSystem;
    private readonly ICliCommandExecutor _commandExecutor;
    private readonly ICpmXmlUtilityService _cpmXmlUtilityService;
    private readonly IDependenciesUtilityService _dependenciesUtilityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaketConversionService"/> class.
    /// </summary>
    /// <param name="fileSystem">The abstracted file system.</param>
    /// <param name="commandExecutor">The CLI command executor.</param>
    /// <param name="cpmXmlUtilityService">The CPM XML utility service.</param>
    /// <param name="dependenciesUtilityService">The Paket dependencies generator.</param>
    public PaketConversionService(
        IFileSystem fileSystem,
        ICliCommandExecutor commandExecutor,
        ICpmXmlUtilityService cpmXmlUtilityService,
        IDependenciesUtilityService dependenciesUtilityService)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(commandExecutor);
        ArgumentNullException.ThrowIfNull(cpmXmlUtilityService);
        ArgumentNullException.ThrowIfNull(dependenciesUtilityService);

        _fileSystem = fileSystem;
        _commandExecutor = commandExecutor;
        _cpmXmlUtilityService = cpmXmlUtilityService;
        _dependenciesUtilityService = dependenciesUtilityService;
    }

    /// <summary>
    /// Converts Central Package Management configuration to Paket configuration
    /// while protecting all mutated source files with rollback scopes.
    /// </summary>
    /// <param name="solutionRoot">The absolute solution root directory.</param>
    /// <returns>The conversion execution status.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="solutionRoot"/> is empty.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when the solution directory does not exist.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the central package configuration is missing.
    /// </exception>
    public StatusJsonModels ConvertCpmToPaket(string solutionRoot)
    {
        ValidateSolutionRoot(solutionRoot);

        var cpmPropsPath = _fileSystem.Path.Combine(
            solutionRoot,
            DirectoryPackagesPropsFileName);

        if (!_fileSystem.File.Exists(cpmPropsPath))
        {
            throw new InvalidOperationException(
                $"Critical CPM configuration file was missing at expected location: {cpmPropsPath}");
        }

        var packages = _cpmXmlUtilityService.ParseCpmElements(
            cpmPropsPath,
            "PackageVersion");

        if (packages.Count == 0)
        {
            return CreateFailureStatus(
                $"No package information could be extracted from CPM configuration at {cpmPropsPath}. Aborting conversion.");
        }

        return ExecuteConversionTransaction(
            solutionRoot,
            cpmPropsPath);
    }

    /// <summary>
    /// Executes Paket installation using the abstracted CLI command executor.
    /// </summary>
    /// <param name="rootPath">The solution root directory.</param>
    /// <returns>The command execution result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Paket exits with a non-zero exit code.
    /// </exception>
    public async Task<CommandExecutionResult> RunPaketInstallAsync(
        string rootPath
    )
    {
        ValidateSolutionRoot(rootPath);

        var result = await _commandExecutor.ExecuteInShellAsync(
            PaketCommand,
            [InstallArgument])
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Paket installation failed with exit code {result.ExitCode}. Error: {result.StandardError}");
        }

        return result;
    }

    /// <summary>
    /// Executes all conversion mutations within a coordinated rollback boundary.
    /// </summary>
    private StatusJsonModels ExecuteConversionTransaction(
        string solutionRoot,
        string cpmPropsPath)
    {
        var projectFiles = _fileSystem.Directory.GetFiles(
            solutionRoot,
            "*.csproj",
            SearchOption.AllDirectories);

        var transactions = new Stack<IFileTransactionScope>();

        try
        {
            foreach (var projectFile in projectFiles)
            {
                var transaction = new FileTransactionScope(
                    _fileSystem,
                    projectFile);

                transactions.Push(transaction);

                _cpmXmlUtilityService.RemovePackageReferencesVersion(
                    projectFile);
            }

            var cpmTransaction = new FileTransactionScope(
                _fileSystem,
                cpmPropsPath);

            transactions.Push(cpmTransaction);

            var backupCpmPath = $"{cpmPropsPath}{ConvertedBackupSuffix}";

            _fileSystem.File.Move(
                cpmPropsPath,
                backupCpmPath,
                overwrite: true);

            // IMPORTANT:
            // Generate dependencies BEFORE committing source mutations.
            _dependenciesUtilityService.WritePaketDependenciesLayout(
                solutionRoot,
                backupCpmPath);

            CommitTransactions(transactions);

            return CreateSuccessStatus(projectFiles.Length + 1);
        }
        catch
        {
            RollbackTransactions(transactions);
            throw;
        }
    }

    /// <summary>
    /// Commits and disposes all active transactions.
    /// </summary>
    private static void CommitTransactions(
        Stack<IFileTransactionScope> transactions)
    {
        while (transactions.TryPop(out var transaction))
        {
            transaction.Commit();
            transaction.Dispose();
        }
    }

    /// <summary>
    /// Rolls back all uncommitted transactions without hiding the primary exception.
    /// </summary>
    private static void RollbackTransactions(
        Stack<IFileTransactionScope> transactions)
    {
        while (transactions.TryPop(out var transaction))
        {
            transaction.Dispose();
        }
    }

    /// <summary>
    /// Validates the target solution root directory.
    /// </summary>
    private void ValidateSolutionRoot(string solutionRoot)
    {
        if (string.IsNullOrWhiteSpace(solutionRoot))
        {
            throw new ArgumentException(
                "Solution root path cannot be empty.",
                nameof(solutionRoot));
        }

        if (!_fileSystem.Directory.Exists(solutionRoot))
        {
            throw new DirectoryNotFoundException(
                $"The specified solution root directory was not found: {solutionRoot}");
        }
    }

    /// <summary>
    /// Creates a successful conversion status collection.
    /// </summary>
    private static StatusJsonModels CreateSuccessStatus(int mutatedFileCount)
    {
        var statuses = new StatusJsonModels();

        statuses.StatusList.Add(
            new StatusJsonModel
            {
                IsSuccess = true,
                Result = $"Successfully converted CPM configuration and committed {mutatedFileCount} protected file mutations.",
                ErrorMessage = string.Empty,
                OverallErrorMessage = string.Empty
            });

        return statuses;
    }

    /// <summary>
    /// Creates a failed conversion status collection.
    /// </summary>
    private static StatusJsonModels CreateFailureStatus(string message)
    {
        var statuses = new StatusJsonModels();

        statuses.StatusList.Add(
            new StatusJsonModel
            {
                IsSuccess = false,
                Result = message,
                ErrorMessage = message,
                OverallErrorMessage = message
            });

        return statuses;
    }
}