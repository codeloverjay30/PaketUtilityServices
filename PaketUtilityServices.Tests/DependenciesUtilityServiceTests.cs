using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Moq;
using PaketUtilityServices.Core.Models;
using PaketUtilityServices.Infrastructure.Services;
using PaketUtilityServices.Infrastructure.Utils;
using Xunit;

namespace PaketUtilityServices.Tests;

public class DependenciesUtilityServiceTests
{
    private readonly MockFileSystem _fileSystem;
    private readonly Mock<ICpmXmlUtilityService> _mockXmlService;
    private readonly DependenciesUtilityService _sut;
    private const string DummyRoot = @"D:\workspace\utility_packages";

    public DependenciesUtilityServiceTests()
    {
        _fileSystem = new MockFileSystem();
        _mockXmlService = new Mock<ICpmXmlUtilityService>(MockBehavior.Strict); // Strictly defensive mock approach
        _sut = new DependenciesUtilityService(_fileSystem, _mockXmlService.Object);
        _fileSystem.Directory.CreateDirectory(DummyRoot);
    }

    [Fact]
    public void ParseGlobalPackages_WhenFilePathIsEmpty_ShouldThrowArgumentExceptionVerifiedByFluentAssertions()
    {
        // Arrange
        var emptyPath = "  ";

        // Act
        Action act = () => _sut.ParseGlobalPackages(emptyPath);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage("*File path cannot be null or empty.*");
    }

    [Fact]
    public void WritePaketDependenciesLayout_WhenSourceFileIsMissing_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var missingCpmPath = Path.Combine(DummyRoot, "Missing.props");

        // Act
        Action act = () => _sut.WritePaketDependenciesLayout(DummyRoot, missingCpmPath);

        // Assert
        act.Should().Throw<FileNotFoundException>()
           .WithMessage("*The specified CPM configuration source was not found.*");
    }

    [Fact]
    public void WritePaketDependenciesLayout_WhenInvoked_ShouldParseBothGlobalAndStandardPackagesSeparatelyAndOutputValidDsl()
    {
        // Arrange
        var cpmPath = Path.Combine(DummyRoot, "Directory.Packages.props");
        _fileSystem.AddFile(cpmPath, new MockFileData("<Project />")); // Dummy file boundary match

        var mockGlobals = new List<PackageInfo>
        {
            new() { Id = "Microsoft.SourceLink.GitHub", Version = "8.0.0" }
        };

        var mockStandards = new List<PackageInfo>
        {
            new() { Id = "Newtonsoft.Json", Version = "13.0.3" },
            new() { Id = "Newtonsoft.Json", Version = "13.0.3" } // Duplicate simulation hazard
        };

        // Strict Setup to prevent recursive side-effects
        _mockXmlService.Setup(x => x.ParseCpmElements(cpmPath, "GlobalPackageReference")).Returns(mockGlobals);
        _mockXmlService.Setup(x => x.ParseCpmElements(cpmPath, "PackageVersion")).Returns(mockStandards);

        var expectedDslPath = Path.Combine(DummyRoot, "paket.dependencies");

        // Act
        _sut.WritePaketDependenciesLayout(DummyRoot, cpmPath);

        // Assert
        _fileSystem.File.Exists(expectedDslPath).Should().BeTrue();
        
        var generatedLines = _fileSystem.File.ReadAllLines(expectedDslPath);
        
        // Asserting utilizing FluentAssertions chains strictly
        generatedLines.Should().Contain(line => line.Contains("source https://api.nuget.org/v3/index.json"))
                     .And.Contain(line => line.Contains("nuget Microsoft.SourceLink.GitHub 8.0.0 // Global Reference"))
                     .And.Contain(line => line.Contains("nuget Newtonsoft.Json 13.0.3"));

        // Deduplication proof check: Newtonsoft.Json must only exist once inside the output array
        generatedLines.Where(l => l.Contains("nuget Newtonsoft.Json")).Should().HaveCount(1);
        
        _mockXmlService.VerifyAll();
    }
}