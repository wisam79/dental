using DentalID.Core.DTOs;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Interface for bulk operations service
/// </summary>
public interface IBulkOperationsService
{
    /// <summary>
    /// Imports subjects from CSV file
    /// </summary>
    /// <param name="filePath">Path to CSV file</param>
    /// <returns>Import result</returns>
    Task<ImportResultDto> ImportSubjectsFromCsvAsync(string filePath);

    /// <summary>
    /// Imports dental images from CSV file
    /// </summary>
    /// <param name="filePath">Path to CSV file</param>
    /// <returns>Import result</returns>
    Task<ImportResultDto> ImportDentalImagesFromCsvAsync(string filePath);

    /// <summary>
    /// Imports cases from CSV file
    /// </summary>
    /// <param name="filePath">Path to CSV file</param>
    /// <returns>Import result</returns>
    Task<ImportResultDto> ImportCasesFromCsvAsync(string filePath);

    /// <summary>
    /// Imports matches from CSV file
    /// </summary>
    /// <param name="filePath">Path to CSV file</param>
    /// <returns>Import result</returns>
    Task<ImportResultDto> ImportMatchesFromCsvAsync(string filePath);

    /// <summary>
    /// Exports subjects to CSV file
    /// </summary>
    /// <param name="filePath">Path to save CSV file</param>
    /// <param name="subjectIds">Optional list of subject IDs to export</param>
    /// <returns>Export result</returns>
    Task<ExportResultDto> ExportSubjectsToCsvAsync(string filePath, List<int>? subjectIds = null);

    /// <summary>
    /// Exports dental images to CSV file
    /// </summary>
    /// <param name="filePath">Path to save CSV file</param>
    /// <param name="imageIds">Optional list of image IDs to export</param>
    /// <returns>Export result</returns>
    Task<ExportResultDto> ExportDentalImagesToCsvAsync(string filePath, List<int>? imageIds = null);

    /// <summary>
    /// Exports cases to CSV file
    /// </summary>
    /// <param name="filePath">Path to save CSV file</param>
    /// <param name="caseIds">Optional list of case IDs to export</param>
    /// <returns>Export result</returns>
    Task<ExportResultDto> ExportCasesToCsvAsync(string filePath, List<int>? caseIds = null);

    /// <summary>
    /// Exports matches to CSV file
    /// </summary>
    /// <param name="filePath">Path to save CSV file</param>
    /// <param name="matchIds">Optional list of match IDs to export</param>
    /// <returns>Export result</returns>
    Task<ExportResultDto> ExportMatchesToCsvAsync(string filePath, List<int>? matchIds = null);

    /// <summary>
    /// Deletes multiple subjects in bulk
    /// </summary>
    /// <param name="subjectIds">List of subject IDs to delete</param>
    /// <returns>Delete result</returns>
    Task<DeleteResultDto> DeleteSubjectsAsync(List<int> subjectIds);

    /// <summary>
    /// Deletes multiple dental images in bulk
    /// </summary>
    /// <param name="imageIds">List of image IDs to delete</param>
    /// <returns>Delete result</returns>
    Task<DeleteResultDto> DeleteDentalImagesAsync(List<int> imageIds);

    /// <summary>
    /// Deletes multiple cases in bulk
    /// </summary>
    /// <param name="caseIds">List of case IDs to delete</param>
    /// <returns>Delete result</returns>
    Task<DeleteResultDto> DeleteCasesAsync(List<int> caseIds);

    /// <summary>
    /// Deletes multiple matches in bulk
    /// </summary>
    /// <param name="matchIds">List of match IDs to delete</param>
    /// <returns>Delete result</returns>
    Task<DeleteResultDto> DeleteMatchesAsync(List<int> matchIds);
}

/// <summary>
/// DTO for import result
/// </summary>
public class ImportResultDto
{
    public int TotalRecords { get; set; }
    public int SuccessfulRecords { get; set; }
    public int FailedRecords { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool Success => FailedRecords == 0;
}

/// <summary>
/// DTO for export result
/// </summary>
public class ExportResultDto
{
    public int RecordsExported { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// DTO for delete result
/// </summary>
public class DeleteResultDto
{
    public int TotalRecords { get; set; }
    public int SuccessfulRecords { get; set; }
    public int FailedRecords { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool Success => FailedRecords == 0;
}
