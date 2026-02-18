using System.Threading.Tasks;

namespace DentalID.Application.Interfaces;

public interface IBackupService
{
    /// <summary>
    /// Creates a hot backup of the database to the specified directory.
    /// </summary>
    /// <param name="backupDirectory">Directory to save the backup file.</param>
    /// <returns>Path to the created backup file.</returns>
    Task<string> CreateBackupAsync(string backupDirectory);
}
