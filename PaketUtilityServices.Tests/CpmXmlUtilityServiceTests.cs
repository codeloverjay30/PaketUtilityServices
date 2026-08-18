using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using PaketUtilityServices.Infrastructure.Services;
using Xunit;

namespace PaketUtilityServices.Tests;

public class CpmXmlUtilityServiceTests
{
    private readonly MockFileSystem _fileSystem;
    private readonly CpmXmlUtilityService _sut;

    public CpmXmlUtilityServiceTests()
    {
        _fileSystem = new MockFileSystem();
        _sut = new CpmXmlUtilityService(_fileSystem);
    }

    [Fact]
    public void ParseCpmElements_WhenFileDoesNotExist_ShouldThrowFileNotFoundExceptionWithExplicitMessage()
    {
        // Arrange
        var missingPath = @"D:\workspace\utility_packages\NonExistent.props";

        // Act
        Action act = () => _sut.ParseCpmElements(missingPath, "PackageVersion");

        // Assert
        act.Should().Throw<FileNotFoundException>()
           .WithMessage("*Target configuration was missing.*");
    }

    [Fact]
    public void RemovePackageReferencesVersion_WhenXmlStructureIsModified_ShouldPersistChangesTransactionally()
    {
        // Arrange
        var targetProjectPath = @"D:\workspace\utility_packages\src\Project.csproj";
        var xmlContent = 
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;
        
        _fileSystem.AddFile(targetProjectPath, new MockFileData(xmlContent));

        // Act
        var result = _sut.RemovePackageReferencesVersion(targetProjectPath);

        // Assert
        result.Should().BeTrue();
        var updatedContent = _fileSystem.File.ReadAllText(targetProjectPath);
        updatedContent.Should().Contain("Include=\"Newtonsoft.Json\"")
                      .And.NotContain("Version=\"13.0.3\"");
    }
}