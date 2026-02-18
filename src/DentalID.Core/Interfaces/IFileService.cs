namespace DentalID.Core.Interfaces;

public interface IFileService
{
    /// <summary>
    /// Opens a file stream for reading.
    /// </summary>
    Stream OpenRead(string path);
    
    /// <summary>
    /// Checks if a file exists.
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Writes bytes to a file asynchronously.
    /// </summary>
    Task WriteAllBytesAsync(string path, byte[] bytes);

    /// <summary>
    /// Launches the file with the default associated application.
    /// </summary>
    void LaunchFile(string path);

    void Copy(string source, string destination, bool overwrite = false);
    void Move(string source, string destination);
    void Delete(string path);
}
