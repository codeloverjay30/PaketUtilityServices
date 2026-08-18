namespace PaketUtilityServices.Core.Interfaces;

/// <summary>
/// Defines file-level rollback actions to ensure data integrity during unsafe I/O updates.
/// </summary>
public interface IFileTransactionScope : IDisposable
{
    /// <summary>
    /// Confirms and commits all structural filesystem changes.
    /// </summary>
    void Commit();

    bool IsCommitted { get; }
    bool IsDisposed { get; }
}