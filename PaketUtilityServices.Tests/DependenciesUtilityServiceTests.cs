using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Moq;
using PaketUtilityServices.Core.Models;
using PaketUtilityServices.Infrastructure.Services;
using PaketUtilityServices.Infrastructure.Utils;

namespace PaketUtilityServices.Tests;

public class DependenciesUtilityServiceTests
{
    private const string RootPath = @"D:\workspace\utility_packages";

    private readonly MockFileSystem _fileSystem;
    private readonly Mock<ICpmXmlUtilityService> _xmlService;
    private readonly DependenciesUtilityService _sut;

    public DependenciesUtilityServiceTests()
    {
        _fileSystem = new MockFileSystem();
        _fileSystem.Directory.CreateDirectory(RootPath);

        _xmlService = new Mock<ICpmXmlUtilityService>(MockBehavior.Strict);

        _sut = new DependenciesUtilityService(
            _fileSystem,
            _xmlService.Object);
    }

    [Fact]
    public void Constructor_WhenFileSystemIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new DependenciesUtilityService(null!);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*fileSystem*");
    }

    [Fact]
    public void ParseGlobalPackages_WhenFilePathIsBlank_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _sut.ParseGlobalPackages(" ");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*File path cannot be null or empty.*")
            .Which.ParamName.Should().Be("filePath");
    }

    [Fact]
    public void ParseGlobalPackages_WhenPathIsValid_ShouldDelegateUsingGlobalPackageReferenceTag()
    {
        // Arrange
        var path = _fileSystem.Path.Combine(RootPath, "Directory.Packages.props");
        var expected = new List<PackageInfo>
        {
            new() { Id = "Microsoft.SourceLink.GitHub", Version = "8.0.0" }
        };

        _xmlService
            .Setup(service => service.ParseCpmElements(path, "GlobalPackageReference"))
            .Returns(expected);

        // Act
        var result = _sut.ParseGlobalPackages(path);

        // Assert
        result.Should().BeSameAs(expected);
        _xmlService.VerifyAll();
    }

    [Fact]
    public void ParseDirectoryPackagesProps_WhenFilePathIsBlank_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _sut.ParseDirectoryPackagesProps("\t");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*File path cannot be null or empty.*")
            .Which.ParamName.Should().Be("filePath");
    }

    [Fact]
    public void ParseDirectoryPackagesProps_WhenPathIsValid_ShouldDelegateUsingPackageVersionTag()
    {
        // Arrange
        var path = _fileSystem.Path.Combine(RootPath, "Directory.Packages.props");
        var expected = new List<PackageInfo>
        {
            new() { Id = "FluentAssertions", Version = "8.6.0" }
        };

        _xmlService
            .Setup(service => service.ParseCpmElements(path, "PackageVersion"))
            .Returns(expected);

        // Act
        var result = _sut.ParseDirectoryPackagesProps(path);

        // Assert
        result.Should().BeSameAs(expected);
        _xmlService.VerifyAll();
    }

    [Fact]
    public void WritePaketDependenciesLayout_WhenSolutionRootIsBlank_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _sut.WritePaketDependenciesLayout(
            " ",
            @"D:\workspace\utility_packages\Directory.Packages.props");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*Solution root path cannot be empty.*")
            .Which.ParamName.Should().Be("solutionRoot");
    }

    [Fact]
    public void WritePaketDependenciesLayout_WhenCpmPathIsBlank_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _sut.WritePaketDependenciesLayout(RootPath, " ");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*CPM properties file path cannot be empty.*")
            .Which.ParamName.Should().Be("cpmPropsFilePath");
    }

    [Fact]
    public void WritePaketDependenciesLayout_WhenSourceFileDoesNotExist_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var missingPath = _fileSystem.Path.Combine(RootPath, "Missing.props");

        // Act
        Action act = () => _sut.WritePaketDependenciesLayout(
            RootPath,
            missingPath);

        // Assert
        act.Should()
            .Throw<FileNotFoundException>()
            .WithMessage("*The specified CPM configuration source was not found.*");
    }

    [Fact]
    public void WritePaketDependenciesLayout_WhenPackagesContainDuplicatesAndInvalidEntries_ShouldWriteSanitizedDsl()
    {
        // Arrange
        var cpmPath = _fileSystem.Path.Combine(
            RootPath,
            "Directory.Packages.props");

        _fileSystem.AddFile(cpmPath, new MockFileData("<Project />"));

        var globalPackages = new List<PackageInfo>
        {
            new() { Id = " Microsoft.SourceLink.GitHub ", Version = " 8.0.0 " },
            new() { Id = "microsoft.sourcelink.github", Version = "9.0.0" },
            new() { Id = " ", Version = "1.0.0" }
        };

        var standardPackages = new List<PackageInfo>
        {
            new() { Id = " Newtonsoft.Json ", Version = " 13.0.3 " },
            new() { Id = "newtonsoft.json", Version = "99.0.0" },
            new() { Id = "Missing.Version", Version = " " }
        };

        _xmlService
            .Setup(service => service.ParseCpmElements(
                cpmPath,
                "GlobalPackageReference"))
            .Returns(globalPackages);

        _xmlService
            .Setup(service => service.ParseCpmElements(
                cpmPath,
                "PackageVersion"))
            .Returns(standardPackages);

        var outputPath = _fileSystem.Path.Combine(
            RootPath,
            "paket.dependencies");

        // Act
        _sut.WritePaketDependenciesLayout(RootPath, cpmPath);

        // Assert
        _fileSystem.File.Exists(outputPath).Should().BeTrue();

        var lines = _fileSystem.File.ReadAllLines(outputPath);

        lines.Should()
            .Contain("source https://api.nuget.org/v3/index.json")
            .And.Contain("storage: none")
            .And.Contain("strategy: min")
            .And.Contain("nuget Microsoft.SourceLink.GitHub 8.0.0 // Global Reference")
            .And.Contain("nuget Newtonsoft.Json 13.0.3");

        lines.Count(line =>
                line.Contains(
                    "Microsoft.SourceLink.GitHub",
                    StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(1);

        lines.Count(line =>
                line.Contains(
                    "Newtonsoft.Json",
                    StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(1);

        lines.Should()
            .NotContain(line => line.Contains("99.0.0", StringComparison.Ordinal))
            .And.NotContain(line => line.Contains("Missing.Version", StringComparison.Ordinal));

        _xmlService.VerifyAll();
    }

    [Fact]
    public void StripVersionFromProjects_WhenProjectsExist_ShouldProcessEveryProjectRecursively()
    {
        // Arrange
        var firstProject = _fileSystem.Path.Combine(
            RootPath,
            "src",
            "A",
            "A.csproj");

        var secondProject = _fileSystem.Path.Combine(
            RootPath,
            "modules",
            "B",
            "B.csproj");

        _fileSystem.AddFile(firstProject, new MockFileData("<Project />"));
        _fileSystem.AddFile(secondProject, new MockFileData("<Project />"));

        _xmlService
            .Setup(service => service.RemovePackageReferencesVersion(firstProject))
            .Returns(true);

        _xmlService
            .Setup(service => service.RemovePackageReferencesVersion(secondProject))
            .Returns(false);

        // Act
        _sut.StripVersionFromProjects(RootPath);

        // Assert
        _xmlService.Verify(
            service => service.RemovePackageReferencesVersion(firstProject),
            Times.Once);

        _xmlService.Verify(
            service => service.RemovePackageReferencesVersion(secondProject),
            Times.Once);

        _xmlService.VerifyNoOtherCalls();
    }
}
