using System.IO.Abstractions;
using PaketUtilityServices.Core.Interfaces;

namespace PaketUtilityServices.Infrastructure.Utils;

/// <summary>
/// Provides a defensive, transaction-like wrapper around dangerous local file modifications.
/// </summary>
public class FileTransactionScope : IFileTransactionScope
{
    private readonly IFileSystem _fileSystem;
    private readonly string _originalPath;
    private readonly string _backupPath;
    private bool _isCommitted;
    private bool _isDisposed;

    public bool IsCommitted => _isCommitted;
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTransactionScope"/> class and creates a secure backup.
    /// </summary>
    /// <param name="fileSystem">The abstracted file system system dependency.</param>
    /// <param name="filePath">The target file path to guard via transaction.</param>
    public FileTransactionScope(IFileSystem fileSystem, string filePath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem, nameof(fileSystem));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Target path cannot be empty.", nameof(filePath));

        _fileSystem = fileSystem;
        _originalPath = filePath;
        _backupPath = filePath + ".bak_" + Guid.NewGuid().ToString("N");

        if (_fileSystem.File.Exists(_originalPath))
        {
            _fileSystem.File.Copy(_originalPath, _backupPath, overwrite: true);
        }
    }

    /// <summary>
    /// Marks this transaction scope as successfully executed.
    /// </summary>
    public void Commit()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FileTransactionScope));
        _isCommitted = true;
    }

    /// <summary>
    /// Disposes the scope, triggering an automatic rollback recovery if the commit flag was not set.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        if (!_isCommitted && _fileSystem.File.Exists(_backupPath))
        {
            try
            {
                _fileSystem.File.Copy(_backupPath, _originalPath, overwrite: true);
            }
            catch
            {
                // Suppress catastrophic nested rollback errors to prevent overriding primary thread exception
            }
        }

        if (_fileSystem.File.Exists(_backupPath))
        {
            try
            {
                _fileSystem.File.Delete(_backupPath);
            }
            catch
            {
                // Suppress background cleanup failures to guard core reliability
            }
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}