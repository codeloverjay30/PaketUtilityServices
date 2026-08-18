using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Threading.Tasks;
using CliUtilityServices;
using CliWrap.Buffered;
using FluentAssertions;
using NSubstitute;
using PaketUtilityServices;
using PaketUtilityServices.Infrastructure.Utils;
using Xunit;

namespace PaketUtilityServices.Tests
{
    public class PaketConversionServiceTests
    {
        private readonly MockFileSystem _mockFileSystem
        ;
        private readonly ICommandLineRunner _mockCommandRunner;
        private readonly IDependenciesUtilityService _mockDependenciesService;
        private readonly PaketConversionService _paketConversionService;
        private const string RootPath = @"D:\workspace\utility_packages";

        public PaketConversionServiceTests()
        {
            _mockFileSystem = new MockFileSystem();
            _mockCommandRunner = Substitute.For<ICommandLineRunner>();
            _mockDependenciesService = Substitute.For<IDependenciesUtilityService>();

            _paketConversionService = new PaketConversionService(
                _mockFileSystem,
                _mockCommandRunner,
                cpmXmlUtilityService: null, // Default initialized inside implementation constructor
                dependenciesUtilityService: _mockDependenciesService
            );
        }

        /// <summary>
        /// Validates that <see cref="PaketConversionService.ConvertCpmToPaket"/> correctly tracks 
        /// the directory dependencies layout and successfully writes the target configuration onto disk.
        /// </summary>
        [Fact]
        public void ConvertCpmToPaket_ShouldCoordinateLayoutAndWriteFileToDisk()
        {
            // Arrange
            // 1. Establish the workspace perimeter with an architecturally compliant CPM XML schema
            var rootProps = _mockFileSystem.Path.Combine(RootPath, "Directory.Packages.props");
            
            // DEFENSIVE FIX: Provide at least one valid PackageVersion element to allow ParseCpmElements to return packages.
            var validCpmXmlContent = 
                """
                <Project>
                    <ItemGroup>
                        <PackageVersion Include="FluentAssertions" Version="6.12.0" />
                    </ItemGroup>
                </Project>
                """;
            _mockFileSystem.AddFile(rootProps, new MockFileData(validCpmXmlContent));

            var expectedFile = _mockFileSystem.Path.Combine(RootPath, "paket.dependencies");

            var isCallbackInvoked = false; // Flag to confirm that the callback was invoked
            _mockDependenciesService.When(x => x.WritePaketDependenciesLayout(Arg.Any<string>(), Arg.Any<string>()))
                .Do(callback =>
                {
                    isCallbackInvoked = true; // Flag to confirm invocation
                    // Defensive Action: Simulate the outcome of text streaming directly onto our isolated filesystem
                    _mockFileSystem.AddFile(expectedFile, new MockFileData("mocked_line\r\n"));
                });

            // Act
            var statusJsonModels = _paketConversionService.ConvertCpmToPaket(RootPath);

            // Assert
            // Clear, absolute alignment with the virtual file system environment state via FluentAssertions
            statusJsonModels.StatusList.Should().HaveCountGreaterThan(0);
            statusJsonModels.IsAllSuccess.Should().BeTrue();
            _mockFileSystem.FileExists(expectedFile).Should().BeTrue(); 
            isCallbackInvoked.Should().BeTrue();
        }

        [Fact]
        public async Task RunPaketInstallAsync_WhenCliExecutionSucceeds_ShouldReturnSuccessfully()
        {
            // Arrange
            var mockSuccessResult = new BufferedCommandResult(
                exitCode: 0,
                startTime: DateTimeOffset.UtcNow,
                exitTime: DateTimeOffset.UtcNow,
                standardOutput: "Paket installation completed successfully.",
                standardError: string.Empty
            );

            _mockCommandRunner.ExecuteAsync(Arg.Any<CommandLineInput>())
                .Returns(Task.FromResult(mockSuccessResult));

            // Act & Assert
            Func<Task> action = async () => await _paketConversionService.RunPaketInstallAsync(RootPath);
            await action.Should().NotThrowAsync();

            // Verify specific command constraints and required UTF-8 serialization mapping definitions
            await _mockCommandRunner.Received(1).ExecuteAsync(Arg.Is<CommandLineInput>(input =>
                input.Arguments.Contains("paket") &&
                input.Arguments.Contains("install") &&
                input.WorkingDirectory == RootPath &&
                input.OutputEncoding == Encoding.UTF8
            ));
        }

        [Fact]
        public async Task RunPaketInstallAsync_WhenCliExecutionFails_ShouldThrowDetailedException()
        {
            // Arrange
            var technicalError = "Error: Cannot merge transient packages constraints.";
            var mockFailedResult = new BufferedCommandResult(
                exitCode: -1,
                startTime: DateTimeOffset.UtcNow,
                exitTime: DateTimeOffset.UtcNow,
                standardOutput: string.Empty,
                standardError: technicalError
            );

            _mockCommandRunner.ExecuteAsync(Arg.Any<CommandLineInput>())
                .Returns(Task.FromResult(mockFailedResult));

            // Act & Assert
            Func<Task> action = async () => await _paketConversionService.RunPaketInstallAsync(RootPath);
            
            var thrownException = await action.Should().ThrowAsync<Exception>();
            thrownException.And.Message.Should().Contain("Failure to execute");
            thrownException.And.Message.Should().Contain(technicalError);
        }
    }
}