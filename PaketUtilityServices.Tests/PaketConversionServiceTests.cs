using System.IO.Abstractions.TestingHelpers;
using CliUtilityServices;
using Commands.Infrastructure;
using FluentAssertions;
using NSubstitute;
using PaketUtilityServices.Core.Models;
using PaketUtilityServices.Infrastructure.Services;
using PaketUtilityServices.Infrastructure.Utils;

namespace PaketUtilityServices.Tests;

public class PaketConversionServiceTests
{
    private const string RootPath = @"D:\workspace\utility_packages";

    private readonly MockFileSystem _fileSystem;
    private readonly ICliCommandExecutor _commandExecutor;
    private readonly ICpmXmlUtilityService _xmlService;
    private readonly IDependenciesUtilityService _dependenciesService;
    private readonly PaketConversionService _sut;

    public PaketConversionServiceTests()
    {
        _fileSystem = new MockFileSystem();
        _fileSystem.Directory.CreateDirectory(RootPath);

        _commandExecutor = Substitute.For<ICliCommandExecutor>();
        _xmlService = Substitute.For<ICpmXmlUtilityService>();
        _dependenciesService = Substitute.For<IDependenciesUtilityService>();

        _sut = new PaketConversionService(
            _fileSystem,
            _commandExecutor,
            _xmlService,
            _dependenciesService);
    }

    [Fact]
    public void Constructor_WhenFileSystemIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new PaketConversionService(
            null!,
            _commandExecutor,
            _xmlService,
            _dependenciesService);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*fileSystem*");
    }

    [Fact]
    public void Constructor_WhenCommandExecutorIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new PaketConversionService(
            _fileSystem,
            null!,
            _xmlService,
            _dependenciesService);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*commandExecutor*");
    }

    [Fact]
    public void Constructor_WhenXmlServiceIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new PaketConversionService(
            _fileSystem,
            _commandExecutor,
            null!,
            _dependenciesService);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*cpmXmlUtilityService*");
    }

    [Fact]
    public void Constructor_WhenDependenciesServiceIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new PaketConversionService(
            _fileSystem,
            _commandExecutor,
            _xmlService,
            null!);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*dependenciesUtilityService*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ConvertCpmToPaket_WhenSolutionRootIsBlank_ShouldThrowArgumentException(string rootPath)
    {
        // Act
        Action act = () => _sut.ConvertCpmToPaket(rootPath);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*Solution root path cannot be empty.*")
            .Which.ParamName.Should().Be("solutionRoot");
    }

    [Fact]
    public void ConvertCpmToPaket_WhenSolutionRootDoesNotExist_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var missingRoot = @"D:\workspace\missing";

        // Act
        Action act = () => _sut.ConvertCpmToPaket(missingRoot);

        // Assert
        act.Should()
            .Throw<DirectoryNotFoundException>()
            .WithMessage($"*{missingRoot}*");
    }

    [Fact]
    public void ConvertCpmToPaket_WhenDirectoryPackagesPropsIsMissing_ShouldThrowInvalidOperationException()
    {
        // Act
        Action act = () => _sut.ConvertCpmToPaket(RootPath);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Critical CPM configuration file was missing*Directory.Packages.props*");
    }

    [Fact]
    public void ConvertCpmToPaket_WhenNoPackagesAreParsed_ShouldReturnFailureWithoutMutatingFiles()
    {
        // Arrange
        var cpmPath = AddCpmProps();

        _xmlService
            .ParseCpmElements(cpmPath, "PackageVersion")
            .Returns([]);

        // Act
        var result = _sut.ConvertCpmToPaket(RootPath);

        // Assert
        result.IsAllFailure.Should().BeTrue();
        result.StatusList.Should().ContainSingle();

        result.StatusList[0].ErrorMessage.Should()
            .Contain("No package information could be extracted");

        _fileSystem.File.Exists(cpmPath).Should().BeTrue();
        _fileSystem.File.Exists($"{cpmPath}.converted_bak").Should().BeFalse();

        _dependenciesService.DidNotReceiveWithAnyArgs()
            .WritePaketDependenciesLayout(default!, default!);
    }

    [Fact]
    public void ConvertCpmToPaket_WhenConversionSucceeds_ShouldRenameCpmFileGenerateDependenciesAndReturnSuccess()
    {
        // Arrange
        var cpmPath = AddCpmProps();
        var projectPath = AddProject("src", "ProjectA", "ProjectA.csproj");

        _xmlService
            .ParseCpmElements(cpmPath, "PackageVersion")
            .Returns(
            [
                new PackageInfo
                {
                    Id = "FluentAssertions",
                    Version = "8.6.0"
                }
            ]);

        _xmlService
            .RemovePackageReferencesVersion(projectPath)
            .Returns(true);

        // Act
        var result = _sut.ConvertCpmToPaket(RootPath);

        // Assert
        result.IsAllSuccess.Should().BeTrue();
        result.StatusList.Should().ContainSingle();
        result.StatusList[0].Result.Should()
            .Contain("committed 2 protected file mutations");

        _fileSystem.File.Exists(cpmPath).Should().BeFalse();
        _fileSystem.File.Exists($"{cpmPath}.converted_bak").Should().BeTrue();

        _dependenciesService.Received(1)
            .WritePaketDependenciesLayout(
                RootPath,
                $"{cpmPath}.converted_bak");

        _xmlService.Received(1)
            .RemovePackageReferencesVersion(projectPath);

        GetTransactionBackups().Should().BeEmpty();
    }

    [Fact]
    public void ConvertCpmToPaket_WhenDependenciesGenerationFails_ShouldRollbackProjectAndCpmFiles()
    {
        // Arrange
        var cpmPath = AddCpmProps();

        var projectPath = AddProject(
            "src",
            "ProjectA",
            "ProjectA.csproj");

        var originalProjectContent = _fileSystem.File.ReadAllText(projectPath);
        var originalCpmContent = _fileSystem.File.ReadAllText(cpmPath);

        _xmlService
            .ParseCpmElements(cpmPath, "PackageVersion")
            .Returns(
            [
                new PackageInfo
                {
                    Id = "FluentAssertions",
                    Version = "8.6.0"
                }
            ]);

        _xmlService
            .RemovePackageReferencesVersion(projectPath)
            .Returns(callInfo =>
            {
                var path = callInfo.Arg<string>();
                _fileSystem.File.WriteAllText(
                    path,
                    "<Project>mutated-before-failure</Project>");

                return true;
            });

        _dependenciesService
            .When(service => service.WritePaketDependenciesLayout(
                RootPath,
                $"{cpmPath}.converted_bak"))
            .Do(_ => throw new IOException("Simulated paket.dependencies write failure."));

        // Act
        Action act = () => _sut.ConvertCpmToPaket(RootPath);

        // Assert
        act.Should()
            .Throw<IOException>()
            .WithMessage("*Simulated paket.dependencies write failure.*");

        _fileSystem.File.Exists(cpmPath).Should().BeTrue();
        _fileSystem.File.ReadAllText(cpmPath).Should().Be(originalCpmContent);

        _fileSystem.File.Exists(projectPath).Should().BeTrue();
        _fileSystem.File.ReadAllText(projectPath).Should().Be(originalProjectContent);

        GetTransactionBackups().Should().BeEmpty();
    }

    [Fact]
    public async Task RunPaketInstallAsync_WhenRootPathIsMissing_ShouldThrowDirectoryNotFoundExceptionWithoutExecutingCommand()
    {
        // Arrange
        var missingRoot = @"D:\workspace\missing";

        // Act
        Func<Task> act = () => _sut.RunPaketInstallAsync(missingRoot);

        // Assert
        await act.Should()
            .ThrowAsync<DirectoryNotFoundException>()
            .WithMessage($"*{missingRoot}*");

        await _commandExecutor.DidNotReceiveWithAnyArgs()
            .ExecuteInShellAsync(default!, default!);
    }

    [Fact]
    public async Task RunPaketInstallAsync_WhenCommandSucceeds_ShouldReturnCommandExecutionResult()
    {
        // Arrange
        var expected = new CommandExecutionResult(
            StandardOutput: "Paket install succeeded.",
            StandardError: string.Empty,
            ExitCode: 0,
            RunTime: TimeSpan.FromMilliseconds(125));

        _commandExecutor
            .ExecuteInShellAsync(
                "paket",
                Arg.Is<IEnumerable<string>>(arguments =>
                    arguments.SequenceEqual(new[] { "install" })))
            .Returns(Task.FromResult(expected));

        // Act
        var result = await _sut.RunPaketInstallAsync(RootPath);

        // Assert
        result.Should().Be(expected);

        await _commandExecutor.Received(1)
            .ExecuteInShellAsync(
                "paket",
                Arg.Is<IEnumerable<string>>(arguments =>
                    arguments.SequenceEqual(new[] { "install" })));
    }

    [Fact]
    public async Task RunPaketInstallAsync_WhenCommandReturnsNonZeroExitCode_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var failed = new CommandExecutionResult(
            StandardOutput: string.Empty,
            StandardError: "Cannot resolve package dependency 'Serilog'.",
            ExitCode: 17,
            RunTime: TimeSpan.FromMilliseconds(50));

        _commandExecutor
            .ExecuteInShellAsync(
                "paket",
                Arg.Any<IEnumerable<string>>())
            .Returns(Task.FromResult(failed));

        // Act
        Func<Task> act = () => _sut.RunPaketInstallAsync(RootPath);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "*Paket installation failed with exit code 17*" +
                "Cannot resolve package dependency 'Serilog'.*");
    }

    private string AddCpmProps()
    {
        var path = _fileSystem.Path.Combine(
            RootPath,
            "Directory.Packages.props");

        var xml =
            """
            <Project>
              <ItemGroup>
                <PackageVersion Include="FluentAssertions" Version="8.6.0" />
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(path, new MockFileData(xml));
        return path;
    }

    private string AddProject(params string[] relativeSegments)
    {
        var segments = new[] { RootPath }
            .Concat(relativeSegments)
            .ToArray();

        var path = _fileSystem.Path.Combine(segments);

        var xml =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FluentAssertions" Version="8.6.0" />
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(path, new MockFileData(xml));
        return path;
    }

    private string[] GetTransactionBackups()
    {
        return _fileSystem.AllFiles
            .Where(path =>
                path.Contains(".bak_", StringComparison.Ordinal))
            .ToArray();
    }
}
