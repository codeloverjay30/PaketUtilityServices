using Commands.Infrastructure;
using CommonModels;

namespace PaketUtilityServices.Infrastructure.Utils;

/// <summary>
/// Defines operations for converting Central Package Management configuration to Paket
/// and executing Paket installation.
/// </summary>
public interface IPaketConversionService
{
    /// <summary>
    /// Converts Central Package Management configuration to Paket configuration.
    /// </summary>
    /// <param name="rootPath">The solution root directory.</param>
    /// <returns>The conversion execution status.</returns>
    StatusJsonModels ConvertCpmToPaket(string rootPath);

    /// <summary>
    /// Executes Paket installation in the specified solution directory.
    /// </summary>
    /// <param name="rootPath">The solution root directory.</param>
    /// <returns>The command execution result.</returns>
    Task<CommandExecutionResult> RunPaketInstallAsync(string rootPath);
}