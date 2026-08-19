using System.IO.Abstractions.TestingHelpers;
using CliUtilityServices;
using Commands.Infrastructure;
using FluentAssertions;
using NSubstitute;
using PaketUtilityServices.Infrastructure.Services;
using PaketUtilityServices.Infrastructure.Utils;

namespace PaketUtilityServices.Tests;

public class PaketConversionServiceExecutionTests
{
    private const string RootPath = @"D:\workspace\utility_packages";

    private readonly MockFileSystem _fileSystem;
    private readonly ICliCommandExecutor _mockCommandExecutor;
    private readonly ICpmXmlUtilityService _mockCpmXmlUtilityService;
    private readonly IDependenciesUtilityService _mockDependenciesUtilityService;
    private readonly PaketConversionService _service;

    public PaketConversionServiceExecutionTests()
    {
        _fileSystem = new MockFileSystem();
        _fileSystem.Directory.CreateDirectory(RootPath);

        _mockCommandExecutor = Substitute.For<ICliCommandExecutor>();
        _mockCpmXmlUtilityService = Substitute.For<ICpmXmlUtilityService>();
        _mockDependenciesUtilityService = Substitute.For<IDependenciesUtilityService>();

        _service = new PaketConversionService(
            _fileSystem,
            _mockCommandExecutor,
            _mockCpmXmlUtilityService,
            _mockDependenciesUtilityService);
    }

    [Fact]
    public async Task RunPaketInstallAsync_WhenCommandSucceeds_ShouldExecuteWithoutThrowing()
    {
        // Arrange
        var expectedResult = new CommandExecutionResult(
            StandardOutput: "paket install completed successfully.",
            StandardError: string.Empty,
            ExitCode: 0,
            RunTime: TimeSpan.FromMilliseconds(100));

        _mockCommandExecutor
            .ExecuteInShellAsync(
                "paket",
                Arg.Is<IEnumerable<string>>(arguments =>
                    arguments.SequenceEqual(new[] { "install" })))
            .Returns(Task.FromResult(expectedResult));

        // Act
        Func<Task> act = async () =>
            await _service.RunPaketInstallAsync(RootPath);

        // Assert
        await act.Should().NotThrowAsync();

        await _mockCommandExecutor
            .Received(1)
            .ExecuteInShellAsync(
                "paket",
                Arg.Is<IEnumerable<string>>(arguments =>
                    arguments.SequenceEqual(new[] { "install" })));
    }

    [Fact]
    public async Task RunPaketInstallAsync_WhenCommandFails_ShouldThrowInvalidOperationExceptionWithErrorMessage()
    {
        // Arrange
        const string expectedErrorMessage =
            "Error: Cannot resolve package dependency 'Serilog'.";

        var failedResult = new CommandExecutionResult(
            StandardOutput: string.Empty,
            StandardError: expectedErrorMessage,
            ExitCode: 1,
            RunTime: TimeSpan.FromMilliseconds(100));

        _mockCommandExecutor
            .ExecuteInShellAsync(
                "paket",
                Arg.Is<IEnumerable<string>>(arguments =>
                    arguments.SequenceEqual(new[] { "install" })))
            .Returns(Task.FromResult(failedResult));

        // Act
        Func<Task> act = async () =>
            await _service.RunPaketInstallAsync(RootPath);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"*Paket installation failed with exit code 1*{expectedErrorMessage}*");

        await _mockCommandExecutor
            .Received(1)
            .ExecuteInShellAsync(
                "paket",
                Arg.Is<IEnumerable<string>>(arguments =>
                    arguments.SequenceEqual(new[] { "install" })));
    }
}
