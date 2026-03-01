using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Services;

public class DataImportService : IDataImportService
{
    private readonly ISubjectRepository _subjectRepo;
    private readonly IDentalImageRepository _imageRepo;

    public DataImportService(ISubjectRepository subjectRepo, IDentalImageRepository imageRepo)
    {
        _subjectRepo = subjectRepo;
        _imageRepo = imageRepo;
    }

    public async Task<List<Dictionary<string, string>>> ParseCsvAsync(Stream stream)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var reader = new StreamReader(stream);
                // Allow flexibility in CSV format but validate basic structure
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    BadDataFound = null,
                };

                using var csv = new CsvReader(reader, config);

                // dynamic reading
                var records = new List<Dictionary<string, string>>();
                
                csv.Read();
                csv.ReadHeader();
                var headers = csv.HeaderRecord;
                if (headers == null || headers.Length == 0)
                {
                    throw new InvalidOperationException("CSV header row is missing or empty.");
                }

                while (csv.Read())
                {
                    var row = new Dictionary<string, string>();
                    foreach (var header in headers)
                    {
                        if (!row.ContainsKey(header))
                        {
                            row[header] = csv.GetField(header) ?? string.Empty;
                        }
                    }
                    records.Add(row);
                }

                return records;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error parsing CSV file: {ex.Message}", ex);
            }
        });
    }

    public async Task<ImportResult> ImportSubjectsAsync(IEnumerable<Subject> subjects)
    {
        var result = new ImportResult();
        var batch = new List<Subject>();
        const int BatchSize = 100;

        foreach (var subject in subjects)
        {
            try
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(subject.FullName))
                {
                    result.Errors.Add("Skipped row with empty Name.");
                    result.ErrorCount++;
                    continue;
                }

                // Check for duplicate ID if provided, otherwise generate new one
                if (string.IsNullOrEmpty(subject.SubjectId))
                {
                    subject.SubjectId = $"SUB-{Guid.NewGuid().ToString().ToUpper()}";
                }

                if (subject.CreatedAt == default) subject.CreatedAt = DateTime.UtcNow; // Use UtcNow
                if (subject.UpdatedAt == default) subject.UpdatedAt = DateTime.UtcNow;

                batch.Add(subject);

                if (batch.Count >= BatchSize)
                {
                    await ProcessBatchAsync(batch, result);
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error preparing '{subject.FullName}': {ex.Message}");
                result.ErrorCount++;
            }
        }

        // Process remaining
        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch, result);
        }

        return result;
    }

    private async Task ProcessBatchAsync(List<Subject> batch, ImportResult result)
    {
        try
        {
            // Filter duplicates
            var ids = batch.Select(s => s.SubjectId).ToList();
            var existingIds = await _subjectRepo.GetExistingSubjectIdsAsync(ids);
            
            var newSubjects = batch.Where(s => !existingIds.Contains(s.SubjectId)).ToList();
            var duplicates = batch.Count - newSubjects.Count;

            if (duplicates > 0)
            {
                result.ErrorCount += duplicates;
                result.Errors.Add($"Skipped {duplicates} duplicate subjects (IDs already exist).");
            }

            if (newSubjects.Count > 0)
            {
                // Process each subject individually to handle failures
                foreach (var subject in newSubjects)
                {
                    try
                    {
                        await _subjectRepo.AddAsync(subject);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.ErrorCount++;
                        result.Errors.Add($"Failed to import subject '{subject.FullName}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.ErrorCount += batch.Count;
            result.Errors.Add($"Batch processing failed: {ex.Message}");
        }
    }

    public async Task<ImportResult> ImportCaseFolderAsync(string folderPath, bool moveFiles = false)
    {
        var result = new ImportResult();
        
        if (!Directory.Exists(folderPath))
        {
            result.Errors.Add("Source folder not found.");
            result.ErrorCount++;
            return result;
        }

        // Logic:
        // Root
        //  - Subject Name 1
        //      - Image1.jpg
        //      - Image2.png
        //  - Subject Name 2
        // ...

        var subDirs = Directory.GetDirectories(folderPath);
        
        foreach (var subDir in subDirs)
        {
            try
            {
                string subjectName = Path.GetFileName(subDir);
                
                // Bug #27 fix: Check for existing subject by name to prevent duplicates
                var existingSubject = await _subjectRepo.GetByFullNameExactAsync(subjectName);
                Subject subject;

                if (existingSubject != null)
                {
                    subject = existingSubject;
                    // Optional: Log that we are appending to existing subject
                }
                else
                {
                    subject = new Subject
                    {
                        FullName = subjectName,
                        SubjectId = $"IMP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4)}",
                        CreatedAt = DateTime.UtcNow
                    };
                    await _subjectRepo.AddAsync(subject);
                }

                // Process Images
                var images = Directory.GetFiles(subDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));

                foreach (var imgPath in images)
                {
                    string finalPath = imgPath;

                    // Move evidence into managed storage when requested.
                    if (moveFiles)
                    {
                        var secureDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DentalEvidence", subject.SubjectId);
                        Directory.CreateDirectory(secureDir);

                        var fileName = Path.GetFileName(imgPath);
                        var destPath = GetUniqueDestinationPath(secureDir, fileName);
                        File.Move(imgPath, destPath);
                        finalPath = destPath;
                    }

                    var dentalImage = new DentalImage
                    {
                        SubjectId = subject.Id,
                        ImagePath = finalPath, 
                        UploadedAt = DateTime.UtcNow,
                        ImageType = ImageType.Panoramic, // Default
                        IsProcessed = false
                    };
                    
                    await _imageRepo.AddAsync(dentalImage);
                }

                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to import folder {Path.GetFileName(subDir)}: {ex.Message}");
                result.ErrorCount++;
            }
        }

        return result;
    }

    private static string GetUniqueDestinationPath(string directory, string originalFileName)
    {
        var safeFileName = Path.GetFileName(originalFileName);
        var name = Path.GetFileNameWithoutExtension(safeFileName);
        var ext = Path.GetExtension(safeFileName);
        var candidate = Path.Combine(directory, safeFileName);

        int attempt = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name}_{attempt}{ext}");
            attempt++;
        }

        return candidate;
    }
}



