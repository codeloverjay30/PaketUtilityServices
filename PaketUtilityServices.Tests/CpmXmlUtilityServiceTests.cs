using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using PaketUtilityServices.Infrastructure.Services;

namespace PaketUtilityServices.Tests;

public class CpmXmlUtilityServiceTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly CpmXmlUtilityService _sut;

    public CpmXmlUtilityServiceTests()
    {
        _sut = new CpmXmlUtilityService(_fileSystem);
    }

    [Fact]
    public void Constructor_WhenFileSystemIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new CpmXmlUtilityService(null!);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*fileSystem*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ParseCpmElements_WhenFilePathIsBlank_ShouldThrowArgumentException(string filePath)
    {
        // Act
        Action act = () => _sut.ParseCpmElements(filePath, "PackageVersion");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*File path cannot be null.*")
            .Which.ParamName.Should().Be("filePath");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ParseCpmElements_WhenTagNameIsBlank_ShouldThrowArgumentException(string tagName)
    {
        // Arrange
        var filePath = @"D:\workspace\utility_packages\Directory.Packages.props";
        _fileSystem.AddFile(filePath, new MockFileData("<Project />"));

        // Act
        Action act = () => _sut.ParseCpmElements(filePath, tagName);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*Tag name cannot be null.*")
            .Which.ParamName.Should().Be("tagName");
    }

    [Fact]
    public void ParseCpmElements_WhenFileDoesNotExist_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var missingPath = @"D:\workspace\utility_packages\NonExistent.props";

        // Act
        Action act = () => _sut.ParseCpmElements(missingPath, "PackageVersion");

        // Assert
        act.Should()
            .Throw<FileNotFoundException>()
            .WithMessage("*Target configuration was missing.*");
    }

    [Fact]
    public void ParseCpmElements_WhenIncludeAndVersionAttributesExist_ShouldReturnPackage()
    {
        // Arrange
        var filePath = @"D:\workspace\utility_packages\Directory.Packages.props";
        var xml =
            """
            <Project>
              <ItemGroup>
                <PackageVersion Include="FluentAssertions" Version="8.6.0" />
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(filePath, new MockFileData(xml));

        // Act
        var result = _sut.ParseCpmElements(filePath, "PackageVersion");

        // Assert
        result.Should().ContainSingle();
        result[0].Id.Should().Be("FluentAssertions");
        result[0].Version.Should().Be("8.6.0");
    }

    [Fact]
    public void ParseCpmElements_WhenUpdateAttributeAndVersionElementExist_ShouldReturnPackage()
    {
        // Arrange
        var filePath = @"D:\workspace\utility_packages\Directory.Packages.props";
        var xml =
            """
            <Project>
              <ItemGroup>
                <PackageVersion Update="System.Text.Json">
                  <Version>10.0.0</Version>
                </PackageVersion>
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(filePath, new MockFileData(xml));

        // Act
        var result = _sut.ParseCpmElements(filePath, "PackageVersion");

        // Assert
        result.Should().ContainSingle();
        result[0].Id.Should().Be("System.Text.Json");
        result[0].Version.Should().Be("10.0.0");
    }

    [Fact]
    public void ParseCpmElements_WhenDefaultNamespaceExists_ShouldParseMatchingElements()
    {
        // Arrange
        var filePath = @"D:\workspace\utility_packages\Directory.Packages.props";
        var xml =
            """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <PackageVersion Include="Moq" Version="4.20.72" />
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(filePath, new MockFileData(xml));

        // Act
        var result = _sut.ParseCpmElements(filePath, "PackageVersion");

        // Assert
        result.Should().ContainSingle();
        result[0].Id.Should().Be("Moq");
        result[0].Version.Should().Be("4.20.72");
    }

    [Fact]
    public void ParseCpmElements_WhenEntriesAreIncomplete_ShouldExcludeInvalidPackages()
    {
        // Arrange
        var filePath = @"D:\workspace\utility_packages\Directory.Packages.props";
        var xml =
            """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Valid.Package" Version="1.2.3" />
                <PackageVersion Include="Missing.Version" />
                <PackageVersion Version="9.9.9" />
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(filePath, new MockFileData(xml));

        // Act
        var result = _sut.ParseCpmElements(filePath, "PackageVersion");

        // Assert
        result.Should().ContainSingle();
        result[0].Id.Should().Be("Valid.Package");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void RemovePackageReferencesVersion_WhenPathIsBlank_ShouldThrowArgumentException(string path)
    {
        // Act
        Action act = () => _sut.RemovePackageReferencesVersion(path);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*Path cannot be empty.*")
            .Which.ParamName.Should().Be("projectFilePath");
    }

    [Fact]
    public void RemovePackageReferencesVersion_WhenFileDoesNotExist_ShouldReturnFalse()
    {
        // Act
        var result = _sut.RemovePackageReferencesVersion(
            @"D:\workspace\utility_packages\Missing.csproj");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void RemovePackageReferencesVersion_WhenNoVersionAttributeExists_ShouldReturnFalseAndPreserveFile()
    {
        // Arrange
        var path = @"D:\workspace\utility_packages\Project.csproj";
        var xml =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Moq" />
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(path, new MockFileData(xml));

        // Act
        var result = _sut.RemovePackageReferencesVersion(path);

        // Assert
        result.Should().BeFalse();
        _fileSystem.File.ReadAllText(path).Should().Be(xml);
    }

    [Fact]
    public void RemovePackageReferencesVersion_WhenVersionAttributesExist_ShouldRemoveOnlyThoseAttributes()
    {
        // Arrange
        var path = @"D:\workspace\utility_packages\Project.csproj";
        var xml =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FluentAssertions" Version="8.6.0" PrivateAssets="all" />
                <PackageReference Include="Moq" Version="4.20.72" />
                <ProjectReference Include="..\Other\Other.csproj" />
              </ItemGroup>
            </Project>
            """;

        _fileSystem.AddFile(path, new MockFileData(xml));

        // Act
        var result = _sut.RemovePackageReferencesVersion(path);

        // Assert
        result.Should().BeTrue();

        var updated = _fileSystem.File.ReadAllText(path);
        updated.Should()
            .Contain("Include=\"FluentAssertions\"")
            .And.Contain("PrivateAssets=\"all\"")
            .And.Contain("Include=\"Moq\"")
            .And.Contain("ProjectReference")
            .And.NotContain("Version=\"8.6.0\"")
            .And.NotContain("Version=\"4.20.72\"");
    }
}
