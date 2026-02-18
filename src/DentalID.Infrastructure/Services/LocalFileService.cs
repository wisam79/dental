using DentalID.Core.Interfaces;

namespace DentalID.Infrastructure.Services;

public class LocalFileService : IFileService
{
    public Stream OpenRead(string path)
    {
        return File.OpenRead(path);
    }

    public bool Exists(string path)
    {
        return File.Exists(path);
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes)
    {
        return File.WriteAllBytesAsync(path, bytes);
    }

    public void LaunchFile(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // Log if possible, but for now just swallow or let bubble up depending on desire
            // Given void return, maybe throw? 
            // Better to let VM handle the error for UI feedback.
            throw new Exception($"Failed to launch file: {ex.Message}", ex);
        }
    }

    public void Copy(string source, string destination, bool overwrite = false)
    {
        File.Copy(source, destination, overwrite);
    }

    public void Move(string source, string destination)
    {
        File.Move(source, destination);
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
