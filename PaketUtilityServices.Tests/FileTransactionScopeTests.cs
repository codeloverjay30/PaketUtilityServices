using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using PaketUtilityServices.Infrastructure.Utils;

namespace PaketUtilityServices.Tests;

public class FileTransactionScopeTests
{
    private const string RootPath = @"D:\workspace\utility_packages";

    private readonly MockFileSystem _fileSystem = new();

    public FileTransactionScopeTests()
    {
        _fileSystem.Directory.CreateDirectory(RootPath);
    }

    [Fact]
    public void Constructor_WhenFileSystemIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        Action act = () => new FileTransactionScope(
            null!,
            @"D:\workspace\utility_packages\Project.csproj");

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*fileSystem*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_WhenFilePathIsBlank_ShouldThrowArgumentException(string filePath)
    {
        // Act
        Action act = () => new FileTransactionScope(_fileSystem, filePath);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*Target path cannot be empty.*")
            .Which.ParamName.Should().Be("filePath");
    }

    [Fact]
    public void Dispose_WhenNotCommitted_ShouldRestoreOriginalContent()
    {
        // Arrange
        var path = _fileSystem.Path.Combine(RootPath, "Project.csproj");
        const string original = "<Project>original</Project>";
        const string mutated = "<Project>mutated</Project>";

        _fileSystem.AddFile(path, new MockFileData(original));

        var sut = new FileTransactionScope(_fileSystem, path);
        _fileSystem.File.WriteAllText(path, mutated);

        // Act
        sut.Dispose();

        // Assert
        sut.IsDisposed.Should().BeTrue();
        sut.IsCommitted.Should().BeFalse();
        _fileSystem.File.ReadAllText(path).Should().Be(original);
        GetBackupFiles(path).Should().BeEmpty();
    }

    [Fact]
    public void Dispose_WhenCommitted_ShouldPreserveModifiedContentAndDeleteBackup()
    {
        // Arrange
        var path = _fileSystem.Path.Combine(RootPath, "Project.csproj");
        const string original = "<Project>original</Project>";
        const string mutated = "<Project>mutated</Project>";

        _fileSystem.AddFile(path, new MockFileData(original));

        var sut = new FileTransactionScope(_fileSystem, path);
        _fileSystem.File.WriteAllText(path, mutated);

        // Act
        sut.Commit();
        sut.Dispose();

        // Assert
        sut.IsCommitted.Should().BeTrue();
        sut.IsDisposed.Should().BeTrue();
        _fileSystem.File.ReadAllText(path).Should().Be(mutated);
        GetBackupFiles(path).Should().BeEmpty();
    }

    [Fact]
    public void Dispose_WhenCalledMoreThanOnce_ShouldBeIdempotent()
    {
        // Arrange
        var path = _fileSystem.Path.Combine(RootPath, "Project.csproj");
        _fileSystem.AddFile(path, new MockFileData("original"));

        var sut = new FileTransactionScope(_fileSystem, path);

        // Act
        Action act = () =>
        {
            sut.Dispose();
            sut.Dispose();
        };

        // Assert
        act.Should().NotThrow();
        sut.IsDisposed.Should().BeTrue();
        GetBackupFiles(path).Should().BeEmpty();
    }

    [Fact]
    public void Commit_WhenScopeIsDisposed_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var path = _fileSystem.Path.Combine(RootPath, "Project.csproj");
        _fileSystem.AddFile(path, new MockFileData("original"));

        var sut = new FileTransactionScope(_fileSystem, path);
        sut.Dispose();

        // Act
        Action act = sut.Commit;

        // Assert
        act.Should()
            .Throw<ObjectDisposedException>()
            .WithMessage("*FileTransactionScope*");
    }

    [Fact]
    public void Dispose_WhenOriginalFileDidNotExist_ShouldNotCreateFile()
    {
        // Arrange
        var path = _fileSystem.Path.Combine(RootPath, "NewFile.txt");
        var sut = new FileTransactionScope(_fileSystem, path);

        // Act
        sut.Dispose();

        // Assert
        _fileSystem.File.Exists(path).Should().BeFalse();
        sut.IsDisposed.Should().BeTrue();
        GetBackupFiles(path).Should().BeEmpty();
    }

    private string[] GetBackupFiles(string originalPath)
    {
        var directory = _fileSystem.Path.GetDirectoryName(originalPath)!;
        var fileName = _fileSystem.Path.GetFileName(originalPath);

        return _fileSystem.Directory
            .GetFiles(directory)
            .Where(path =>
                _fileSystem.Path.GetFileName(path)
                    .StartsWith($"{fileName}.bak_", StringComparison.Ordinal))
            .ToArray();
    }
}
