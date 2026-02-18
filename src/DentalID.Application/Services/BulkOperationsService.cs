using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Core.Validators;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using FluentValidation;

namespace DentalID.Application.Services;

/// <summary>
/// Bulk operations service implementation
/// </summary>
public class BulkOperationsService : IBulkOperationsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateSubjectDto> _subjectValidator;
    private readonly IValidator<CreateDentalImageDto> _dentalImageValidator;
    private readonly IValidator<CreateCaseDto> _caseValidator;
    private readonly IValidator<CreateMatchDto> _matchValidator;

    public BulkOperationsService(
        IUnitOfWork unitOfWork,
        IValidator<CreateSubjectDto> subjectValidator,
        IValidator<CreateDentalImageDto> dentalImageValidator,
        IValidator<CreateCaseDto> caseValidator,
        IValidator<CreateMatchDto> matchValidator)
    {
        _unitOfWork = unitOfWork;
        _subjectValidator = subjectValidator;
        _dentalImageValidator = dentalImageValidator;
        _caseValidator = caseValidator;
        _matchValidator = matchValidator;
    }

    public async Task<ImportResultDto> ImportSubjectsFromCsvAsync(string filePath)
    {
        var result = new ImportResultDto();

        try
        {
            ValidateFilePath(filePath, ".csv");

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            var records = new List<CreateSubjectDto>();
            await foreach (var record in csv.GetRecordsAsync<CreateSubjectDto>())
            {
                records.Add(record);
            }

            result.TotalRecords = records.Count;
            var subjectsToAdd = new List<Subject>();

            foreach (var record in records)
            {
                var validationResult = await _subjectValidator.ValidateAsync(record);

                if (validationResult.IsValid)
                {
                    subjectsToAdd.Add(new Subject
                    {
                        FullName = record.FullName,
                        Gender = record.Gender,
                        DateOfBirth = record.DateOfBirth,
                        NationalId = record.NationalId,
                        ContactInfo = record.ContactInfo,
                        Notes = record.Notes,
                    // Bug #42: Use 8 hex chars of GUID (not 4) to drastically reduce SubjectId collision probability in bulk imports
                    SubjectId = $"SUB-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.AddRange(validationResult.Errors.Select(e => e.ErrorMessage));
                }
            }

            if (subjectsToAdd.Any())
            {
                await _unitOfWork.GetRepository<Subject>().AddRangeAsync(subjectsToAdd);
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = result.TotalRecords;
        }

        return result;
    }

    public async Task<ImportResultDto> ImportDentalImagesFromCsvAsync(string filePath)
    {
        var result = new ImportResultDto();

        try
        {
            ValidateFilePath(filePath, ".csv");
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            var records = new List<CreateDentalImageDto>();
            await foreach (var record in csv.GetRecordsAsync<CreateDentalImageDto>())
            {
                records.Add(record);
            }

            result.TotalRecords = records.Count;

            // Bug #40: Batch inserts into a list, then AddRangeAsync once (avoids N individual tracker registrations)
            var imagesToAdd = new List<DentalImage>();
            foreach (var record in records)
            {
                var validationResult = await _dentalImageValidator.ValidateAsync(record);

                if (validationResult.IsValid)
                {
                    imagesToAdd.Add(new DentalImage
                    {
                        SubjectId = record.SubjectId,
                        ImagePath = record.ImagePath,
                        FileHash = record.FileHash,
                        ImageType = record.ImageType,
                        JawType = record.JawType,
                        Quadrant = record.Quadrant,
                        CaptureDate = record.CaptureDate,
                        QualityScore = record.QualityScore,
                        AnalysisResults = record.AnalysisResults,
                        FingerprintCode = record.FingerprintCode,
                        UniquenessScore = record.UniquenessScore,
                        IsProcessed = record.IsProcessed,
                        UploadedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.AddRange(validationResult.Errors.Select(e => e.ErrorMessage));
                }
            }

            if (imagesToAdd.Any())
            {
                await _unitOfWork.GetRepository<DentalImage>().AddRangeAsync(imagesToAdd);
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = result.TotalRecords;
        }

        return result;
    }

    public async Task<ImportResultDto> ImportCasesFromCsvAsync(string filePath)
    {
        var result = new ImportResultDto();

        try
        {
            ValidateFilePath(filePath, ".csv");
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            var records = new List<CreateCaseDto>();
            await foreach (var record in csv.GetRecordsAsync<CreateCaseDto>())
            {
                records.Add(record);
            }

            result.TotalRecords = records.Count;

            // Bug #40: Batch inserts into a list, then AddRangeAsync once
            var casesToAdd = new List<Case>();
            foreach (var record in records)
            {
                var validationResult = await _caseValidator.ValidateAsync(record);

                if (validationResult.IsValid)
                {
                    casesToAdd.Add(new Case
                    {
                        CaseNumber = record.CaseNumber,
                        Title = record.Title,
                        Description = record.Description,
                        CaseType = record.CaseType,
                        Status = record.Status,
                        Priority = record.Priority,
                        AssignedToId = record.AssignedToId,
                        ReportedBy = record.ReportedBy,
                        IncidentDate = record.IncidentDate,
                        Location = record.Location,
                        EvidenceCount = record.EvidenceCount,
                        Result = record.Result,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.AddRange(validationResult.Errors.Select(e => e.ErrorMessage));
                }
            }

            if (casesToAdd.Any())
            {
                await _unitOfWork.GetRepository<Case>().AddRangeAsync(casesToAdd);
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = result.TotalRecords;
        }

        return result;
    }

    public async Task<ImportResultDto> ImportMatchesFromCsvAsync(string filePath)
    {
        var result = new ImportResultDto();

        try
        {
            ValidateFilePath(filePath, ".csv");
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            var records = new List<CreateMatchDto>();
            await foreach (var record in csv.GetRecordsAsync<CreateMatchDto>())
            {
                records.Add(record);
            }

            result.TotalRecords = records.Count;

            // Bug #40: Batch inserts into a list, then AddRangeAsync once
            var matchesToAdd = new List<Match>();
            foreach (var record in records)
            {
                var validationResult = await _matchValidator.ValidateAsync(record);

                if (validationResult.IsValid)
                {
                    matchesToAdd.Add(new Match
                    {
                        CaseId = record.CaseId,
                        QueryImageId = record.QueryImageId,
                        MatchedSubjectId = record.MatchedSubjectId,
                        MatchedImageId = record.MatchedImageId,
                        ConfidenceScore = record.ConfidenceScore,
                        ResultType = record.ResultType,
                        Notes = record.Notes,
                        IsConfirmed = record.IsConfirmed,
                        ConfirmedById = record.ConfirmedById,
                        ConfirmedAt = record.ConfirmedAt,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.AddRange(validationResult.Errors.Select(e => e.ErrorMessage));
                }
            }

            if (matchesToAdd.Any())
            {
                await _unitOfWork.GetRepository<Match>().AddRangeAsync(matchesToAdd);
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = result.TotalRecords;
        }

        return result;
    }

    public async Task<ExportResultDto> ExportSubjectsToCsvAsync(string filePath, List<int>? subjectIds = null)
    {
        var result = new ExportResultDto();
        result.FilePath = filePath;

        try
        {
            ValidateExportPath(filePath, ".csv");
            var query = _unitOfWork.GetRepository<Subject>().AsQueryable();

            if (subjectIds != null && subjectIds.Any())
            {
                query = query.Where(s => subjectIds.Contains(s.Id));
            }

            // Bug #41: Offload synchronous EF materialization to a thread-pool thread to avoid blocking the UI/async context
            var subjects = await Task.Run(() => query
                .Select(s => new SubjectDto
                {
                    Id = s.Id,
                    SubjectId = s.SubjectId,
                    FullName = s.FullName,
                    Gender = s.Gender,
                    DateOfBirth = s.DateOfBirth,
                    NationalId = s.NationalId,
                    ContactInfo = s.ContactInfo,
                    Notes = s.Notes,
                    FeatureVector = s.FeatureVector,
                    ThumbnailPath = s.ThumbnailPath,
                    CreatedAt = s.CreatedAt,
                    UpdatedAt = s.UpdatedAt,
                    CreatedById = s.CreatedById
                })
                .ToList());

            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
            await csv.WriteRecordsAsync(subjects);

            result.RecordsExported = subjects.Count;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<ExportResultDto> ExportDentalImagesToCsvAsync(string filePath, List<int>? imageIds = null)
    {
        var result = new ExportResultDto();
        result.FilePath = filePath;

        try
        {
            ValidateExportPath(filePath, ".csv");
            var query = _unitOfWork.GetRepository<DentalImage>().AsQueryable();

            if (imageIds != null && imageIds.Any())
            {
                query = query.Where(d => imageIds.Contains(d.Id));
            }

            // Bug #41: Offload synchronous EF materialization to a thread-pool thread
            var images = await Task.Run(() => query
                .Select(d => new DentalImageDto
                {
                    Id = d.Id,
                    SubjectId = d.SubjectId,
                    ImagePath = d.ImagePath,
                    FileHash = d.FileHash,
                    ImageType = d.ImageType,
                    JawType = d.JawType,
                    Quadrant = d.Quadrant,
                    CaptureDate = d.CaptureDate,
                    QualityScore = d.QualityScore,
                    AnalysisResults = d.AnalysisResults,
                    FingerprintCode = d.FingerprintCode,
                    UniquenessScore = d.UniquenessScore,
                    IsProcessed = d.IsProcessed,
                    CreatedAt = d.CreatedAt
                })
                .ToList());

            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
            await csv.WriteRecordsAsync(images);

            result.RecordsExported = images.Count;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<ExportResultDto> ExportCasesToCsvAsync(string filePath, List<int>? caseIds = null)
    {
        var result = new ExportResultDto();
        result.FilePath = filePath;

        try
        {
            ValidateExportPath(filePath, ".csv");
            var query = _unitOfWork.GetRepository<Case>().AsQueryable();

            if (caseIds != null && caseIds.Any())
            {
                query = query.Where(c => caseIds.Contains(c.Id));
            }

            // Bug #41: Offload synchronous EF materialization to a thread-pool thread
            var cases = await Task.Run(() => query
                .Select(c => new CaseDto
                {
                    Id = c.Id,
                    CaseNumber = c.CaseNumber,
                    Title = c.Title,
                    Description = c.Description,
                    CaseType = c.CaseType,
                    Status = c.Status,
                    Priority = c.Priority,
                    AssignedToId = c.AssignedToId,
                    ReportedBy = c.ReportedBy,
                    IncidentDate = c.IncidentDate,
                    Location = c.Location,
                    EvidenceCount = c.EvidenceCount,
                    Result = c.Result,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    ClosedAt = c.ClosedAt,
                    CreatedById = c.CreatedById
                })
                .ToList());

            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
            await csv.WriteRecordsAsync(cases);

            result.RecordsExported = cases.Count;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<ExportResultDto> ExportMatchesToCsvAsync(string filePath, List<int>? matchIds = null)
    {
        var result = new ExportResultDto();
        result.FilePath = filePath;

        try
        {
            ValidateExportPath(filePath, ".csv");
            var query = _unitOfWork.GetRepository<Match>().AsQueryable();

            if (matchIds != null && matchIds.Any())
            {
                query = query.Where(m => matchIds.Contains(m.Id));
            }

            // Bug #41: Offload synchronous EF materialization to a thread-pool thread
            var matches = await Task.Run(() => query
                .Select(m => new MatchDto
                {
                    Id = m.Id,
                    CaseId = m.CaseId,
                    QueryImageId = m.QueryImageId,
                    MatchedSubjectId = m.MatchedSubjectId,
                    MatchedImageId = m.MatchedImageId,
                    ConfidenceScore = m.ConfidenceScore,
                    ResultType = m.ResultType,
                    Notes = m.Notes,
                    IsConfirmed = m.IsConfirmed,
                    ConfirmedById = m.ConfirmedById,
                    ConfirmedAt = m.ConfirmedAt,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt
                })
                .ToList());

            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
            await csv.WriteRecordsAsync(matches);

            // Bug #44: Use .Count property (not .Count() method) on materialized List<T>
            result.RecordsExported = matches.Count;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<DeleteResultDto> DeleteSubjectsAsync(List<int> subjectIds)
    {
        var result = new DeleteResultDto();
        result.TotalRecords = subjectIds.Count;

        try
        {
            foreach (var subjectId in subjectIds)
            {
                var subject = await _unitOfWork.GetRepository<Subject>().GetByIdAsync(subjectId);

                if (subject != null)
                {
                    _unitOfWork.GetRepository<Subject>().Remove(subject);
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.Add($"Subject with ID {subjectId} not found");
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = subjectIds.Count;
        }

        return result;
    }

    public async Task<DeleteResultDto> DeleteDentalImagesAsync(List<int> imageIds)
    {
        var result = new DeleteResultDto();
        result.TotalRecords = imageIds.Count;

        try
        {
            foreach (var imageId in imageIds)
            {
                var image = await _unitOfWork.GetRepository<DentalImage>().GetByIdAsync(imageId);

                if (image != null)
                {
                    _unitOfWork.GetRepository<DentalImage>().Remove(image);
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.Add($"Dental image with ID {imageId} not found");
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = imageIds.Count;
        }

        return result;
    }

    public async Task<DeleteResultDto> DeleteCasesAsync(List<int> caseIds)
    {
        var result = new DeleteResultDto();
        result.TotalRecords = caseIds.Count;

        try
        {
            foreach (var caseId in caseIds)
            {
                var caseEntity = await _unitOfWork.GetRepository<Case>().GetByIdAsync(caseId);

                if (caseEntity != null)
                {
                    _unitOfWork.GetRepository<Case>().Remove(caseEntity);
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.Add($"Case with ID {caseId} not found");
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = caseIds.Count;
        }

        return result;
    }

    public async Task<DeleteResultDto> DeleteMatchesAsync(List<int> matchIds)
    {
        var result = new DeleteResultDto();
        result.TotalRecords = matchIds.Count;

        try
        {
            foreach (var matchId in matchIds)
            {
                var match = await _unitOfWork.GetRepository<Match>().GetByIdAsync(matchId);

                if (match != null)
                {
                    _unitOfWork.GetRepository<Match>().Remove(match);
                    result.SuccessfulRecords++;
                }
                else
                {
                    result.FailedRecords++;
                    result.Errors.Add($"Match with ID {matchId} not found");
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.FailedRecords = matchIds.Count;
        }

        return result;
    }
    private void ValidateFilePath(string filePath, string expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);

        // Security: Prevent access to system directories
        var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        // Bug #43: Also block ProgramFiles and ProgramFilesX86 from import paths
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        
        if (fullPath.StartsWith(systemPath, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(programFilesX86) && fullPath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException("Access to system directories is restricted.");
        }

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Import file not found", fullPath);

        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid file format. Expected {expectedExtension}, got {extension}.", nameof(filePath));
    }

    private void ValidateExportPath(string filePath, string expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Export path cannot be empty", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);

        // Security: Prevent writing to system directories
        var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (fullPath.StartsWith(systemPath, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase) || 
            fullPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Exporting to system or program directories is restricted.");
        }

        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid file format. Expected {expectedExtension}, got {extension}.", nameof(filePath));

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            // Optional: Create directory if it doesn't exist? 
            // Better to throw if the user selected a non-existent folder
            throw new DirectoryNotFoundException($"Export directory not found: {directory}");
        }
    }
}
