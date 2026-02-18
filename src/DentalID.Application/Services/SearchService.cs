using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DentalID.Application.Services;

/// <summary>
/// Search service implementation
/// </summary>
public class SearchService : ISearchService
{
    private readonly IUnitOfWork _unitOfWork;

    public SearchService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SearchResultDto<SubjectSearchDto>> SearchSubjectsAsync(SearchParametersDto parameters)
    {
        // Bug #25 Fix: Validate and clamp pagination parameters to prevent negative Skip() values
        // which cause ArgumentOutOfRangeException in EF Core.
        if (parameters.Page < 1) parameters.Page = 1;
        if (parameters.PageSize < 1) parameters.PageSize = 20;

        var repository = _unitOfWork.GetRepository<Subject>();
        var query = repository.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.SearchQuery))
        {
            var searchQuery = parameters.SearchQuery.ToLower();
            query = query.Where(s =>
                s.FullName.ToLower().Contains(searchQuery) ||
                s.SubjectId.ToLower().Contains(searchQuery) ||
                (s.NationalId != null && s.NationalId.ToLower().Contains(searchQuery)) ||
                (s.ContactInfo != null && s.ContactInfo.ToLower().Contains(searchQuery)) ||
                (s.Notes != null && s.Notes.ToLower().Contains(searchQuery)));
        }

        // Apply sorting
        query = ApplySubjectSorting(query, parameters);

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var items = await query
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(s => new SubjectSearchDto
            {
                Id = s.Id,
                SubjectId = s.SubjectId,
                FullName = s.FullName,
                Gender = s.Gender,
                DateOfBirth = s.DateOfBirth,
                NationalId = s.NationalId,
                DentalImageCount = s.DentalImages.Count,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();

        return new SearchResultDto<SubjectSearchDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<SearchResultDto<DentalImageSearchDto>> SearchDentalImagesAsync(SearchParametersDto parameters)
    {
        if (parameters.Page < 1) parameters.Page = 1;
        if (parameters.PageSize < 1) parameters.PageSize = 20;

        var repository = _unitOfWork.GetRepository<DentalImage>();
        var query = repository.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.SearchQuery))
        {
            var searchQuery = parameters.SearchQuery.ToLower();
            query = query.Where(d =>
                d.ImagePath.ToLower().Contains(searchQuery) ||
                (d.Quadrant != null && d.Quadrant.ToLower().Contains(searchQuery)) ||
                (d.FingerprintCode != null && d.FingerprintCode.ToLower().Contains(searchQuery)) ||
                (d.AnalysisResults != null && d.AnalysisResults.ToLower().Contains(searchQuery)));
        }

        // Apply sorting
        query = ApplyDentalImageSorting(query, parameters);

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var items = await query
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(d => new DentalImageSearchDto
            {
                Id = d.Id,
                SubjectId = d.SubjectId,
                ImagePath = d.ImagePath,
                ImageType = d.ImageType,
                JawType = d.JawType,
                CaptureDate = d.CaptureDate,
                QualityScore = d.QualityScore,
                IsProcessed = d.IsProcessed,
                UploadedAt = d.UploadedAt
            })
            .ToListAsync();

        return new SearchResultDto<DentalImageSearchDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<SearchResultDto<CaseSearchDto>> SearchCasesAsync(SearchParametersDto parameters)
    {
        if (parameters.Page < 1) parameters.Page = 1;
        if (parameters.PageSize < 1) parameters.PageSize = 20;

        var repository = _unitOfWork.GetRepository<Case>();
        var query = repository.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.SearchQuery))
        {
            var searchQuery = parameters.SearchQuery.ToLower();
            query = query.Where(c =>
                c.CaseNumber.ToLower().Contains(searchQuery) ||
                c.Title.ToLower().Contains(searchQuery) ||
                (c.Description != null && c.Description.ToLower().Contains(searchQuery)) ||
                (c.ReportedBy != null && c.ReportedBy.ToLower().Contains(searchQuery)) ||
                (c.Location != null && c.Location.ToLower().Contains(searchQuery)) ||
                (c.Result != null && c.Result.ToLower().Contains(searchQuery)));
        }

        // Apply sorting
        query = ApplyCaseSorting(query, parameters);

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var items = await query
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(c => new CaseSearchDto
            {
                Id = c.Id,
                CaseNumber = c.CaseNumber,
                Title = c.Title,
                Status = c.Status,
                Priority = c.Priority,
                EvidenceCount = c.EvidenceCount,
                MatchCount = c.Matches.Count,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync();

        return new SearchResultDto<CaseSearchDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<SearchResultDto<MatchSearchDto>> SearchMatchesAsync(SearchParametersDto parameters)
    {
        if (parameters.Page < 1) parameters.Page = 1;
        if (parameters.PageSize < 1) parameters.PageSize = 20;

        var repository = _unitOfWork.GetRepository<Match>();
        var query = repository.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.SearchQuery))
        {
            var searchQuery = parameters.SearchQuery.ToLower();
            query = query.Where(m =>
                (m.ResultType != null && m.ResultType.ToLower().Contains(searchQuery)) ||
                (m.Notes != null && m.Notes.ToLower().Contains(searchQuery)));
        }

        // Apply sorting
        query = ApplyMatchSorting(query, parameters);

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var items = await query
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(m => new MatchSearchDto
            {
                Id = m.Id,
                CaseId = m.CaseId,
                QueryImageId = m.QueryImageId,
                MatchedSubjectId = m.MatchedSubjectId,
                ConfidenceScore = m.ConfidenceScore,
                ResultType = m.ResultType,
                IsConfirmed = m.IsConfirmed,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return new SearchResultDto<MatchSearchDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    private IQueryable<Subject> ApplySubjectSorting(IQueryable<Subject> query, SearchParametersDto parameters)
    {
        Expression<Func<Subject, object>> sortExpr = parameters.SortBy?.ToLower() switch
        {
            "id" => s => s.SubjectId,
            "dob" => s => s.DateOfBirth ?? DateTime.MinValue,
            "created" => s => s.CreatedAt,
            _ => s => s.FullName // Default
        };

        return parameters.SortDescending 
            ? query.OrderByDescending(sortExpr) 
            : query.OrderBy(sortExpr);
    }

    private IQueryable<DentalImage> ApplyDentalImageSorting(IQueryable<DentalImage> query, SearchParametersDto parameters)
    {
        Expression<Func<DentalImage, object>> sortExpr = parameters.SortBy?.ToLower() switch
        {
            "path" => d => d.ImagePath,
            "type" => d => d.ImageType,
            "quality" => d => d.QualityScore ?? 0,
            _ => d => d.UploadedAt
        };

        return parameters.SortDescending 
            ? query.OrderByDescending(sortExpr) 
            : query.OrderBy(sortExpr);
    }

    private IQueryable<Case> ApplyCaseSorting(IQueryable<Case> query, SearchParametersDto parameters)
    {
        Expression<Func<Case, object>> sortExpr = parameters.SortBy?.ToLower() switch
        {
            "number" => c => c.CaseNumber,
            "title" => c => c.Title,
            "status" => c => c.Status,
            "priority" => c => c.Priority,
            "created" => c => c.CreatedAt,
            "updated" => c => c.UpdatedAt,
            _ => c => c.CaseNumber
        };

        return parameters.SortDescending 
            ? query.OrderByDescending(sortExpr) 
            : query.OrderBy(sortExpr);
    }

    private IQueryable<Match> ApplyMatchSorting(IQueryable<Match> query, SearchParametersDto parameters)
    {
        Expression<Func<Match, object>> sortExpr = parameters.SortBy?.ToLower() switch
        {
            "confidence" => m => m.ConfidenceScore,
            "result" => m => m.ResultType ?? string.Empty,
            "confirmed" => m => m.IsConfirmed,
            _ => m => m.CreatedAt
        };

        return parameters.SortDescending 
            ? query.OrderByDescending(sortExpr) 
            : query.OrderBy(sortExpr);
    }
}
