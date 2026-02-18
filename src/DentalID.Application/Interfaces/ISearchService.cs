using DentalID.Core.DTOs;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Interface for search service
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches subjects based on search query and parameters
    /// </summary>
    /// <param name="parameters">Search parameters</param>
    /// <returns>Search results</returns>
    Task<SearchResultDto<SubjectSearchDto>> SearchSubjectsAsync(SearchParametersDto parameters);

    /// <summary>
    /// Searches dental images based on search query and parameters
    /// </summary>
    /// <param name="parameters">Search parameters</param>
    /// <returns>Search results</returns>
    Task<SearchResultDto<DentalImageSearchDto>> SearchDentalImagesAsync(SearchParametersDto parameters);

    /// <summary>
    /// Searches cases based on search query and parameters
    /// </summary>
    /// <param name="parameters">Search parameters</param>
    /// <returns>Search results</returns>
    Task<SearchResultDto<CaseSearchDto>> SearchCasesAsync(SearchParametersDto parameters);

    /// <summary>
    /// Searches matches based on search query and parameters
    /// </summary>
    /// <param name="parameters">Search parameters</param>
    /// <returns>Search results</returns>
    Task<SearchResultDto<MatchSearchDto>> SearchMatchesAsync(SearchParametersDto parameters);
}
