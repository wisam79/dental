using System.Text;
using DentalID.Core.Interfaces;

namespace DentalID.Infrastructure.Services;

public class ImageIntegrityService : IImageIntegrityService
{
    private static readonly byte[] JpegHeader = { 0xFF, 0xD8 };
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47 };
    
    // Software signatures often left by editing tools
    private static readonly string[] EditingSoftwareSignatures = 
    { 
        "Adobe Photoshop", "GIMP", "Paint.NET", "Lightroom", "CorelDRAW", "Picasa" 
    };

    public string ComputeHash(Stream imageStream)
    {
        if (imageStream == null) throw new ArgumentNullException(nameof(imageStream));
        
        long originalPosition = 0;
        if (imageStream.CanSeek)
        {
            originalPosition = imageStream.Position;
            imageStream.Position = 0;
        }

        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(imageStream);
            
            // Restore position if possible
            if (imageStream.CanSeek) imageStream.Position = originalPosition;
            
            return Convert.ToHexString(hashBytes);
        }
    }

    public List<string> AnalyzeIntegrity(string imagePath)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            warnings.Add("Image path cannot be null or empty.");
            return warnings;
        }

        if (!File.Exists(imagePath))
        {
            warnings.Add("File not found.");
            return warnings;
        }

        try
        {
            // Bug #30 fix: Increase buffer to 2MB to catch metadata/signatures at end of file (common in JPEG)
            byte[] buffer = new byte[2 * 1024 * 1024]; // 2MB scan
            if (new FileInfo(imagePath).Length < buffer.Length)
            {
                buffer = new byte[new FileInfo(imagePath).Length];
            }
            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    warnings.Add("Analysis Error: Unable to read image bytes.");
                    return warnings;
                }
                
                // 1. Header Validation
                if (!ValidateHeader(buffer, Path.GetExtension(imagePath)))
                {
                    warnings.Add("Integrity Alert: File header does not match extension (possible spoofing).");
                }

                // 2. Metadata/String Analysis (Simple Heuristic)
                // We scan the raw bytes for known editing software strings.
                // This works because metadata is often stored as clear text within the binary.
                string content = Encoding.ASCII.GetString(buffer, 0, bytesRead); // Most metadata tags are ASCII
                foreach (var sig in EditingSoftwareSignatures)
                {
                    if (content.Contains(sig, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Manipulation Alert: Found trace of editing software '{sig}' in metadata.");
                    }
                }
            }

            // 3. File System Timestamp Analysis
            // Note: Timestamp analysis on copied files is unreliable as the OS resets creation time.
            // We rely on metadata analysis and header validation for forensic integrity.
        }
        catch (Exception ex)
        {
            warnings.Add($"Analysis Error: {ex.Message}");
        }

        return warnings;
    }

    private bool ValidateHeader(byte[] buffer, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        
        var ext = extension.ToLower();
        if (ext == ".jpg" || ext == ".jpeg")
        {
            return buffer.Length >= 2 && buffer[0] == JpegHeader[0] && buffer[1] == JpegHeader[1];
        }
        else if (ext == ".png")
        {
            return buffer.Length >= 4 && 
                   buffer[0] == PngHeader[0] && 
                   buffer[1] == PngHeader[1] && 
                   buffer[2] == PngHeader[2] && 
                   buffer[3] == PngHeader[3];
        }
        
        return true; // Assume true for other formats for now
    }
}

