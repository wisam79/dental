using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

public interface IDataImportService
{
    /// <summary>
    /// Parses a CSV file and returns a list of dictionaries (key=header, value=cell).
    /// </summary>
    Task<List<Dictionary<string, string>>> ParseCsvAsync(Stream stream);

    /// <summary>
    /// Validates and saves a batch of subjects.
    /// </summary>
    Task<ImportResult> ImportSubjectsAsync(IEnumerable<Subject> subjects);

    /// <summary>
    /// Imports subjects and images from a structured folder (e.g. unzipped archive).
    /// </summary>
    Task<ImportResult> ImportCaseFolderAsync(string folderPath, bool moveFiles = false);
}

public class ImportResult
{
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
