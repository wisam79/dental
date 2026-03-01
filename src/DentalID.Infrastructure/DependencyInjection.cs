using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;
using DentalID.Core.Validators;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Repositories;
using DentalID.Infrastructure.Services;
using DentalID.Core.Entities;
using DentalID.Application.Interfaces;
using DentalID.Application.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalID.Infrastructure;

/// <summary>
/// Extension methods to register all infrastructure services with the DI container.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string dbPath)
    {
        // Database - Use IDbContextFactory for thread-safe context creation
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));
            
        // Register AppDbContext as Transient via Factory for repositories that inject it directly
        services.AddTransient<AppDbContext>(sp => 
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        
        // Also register the Context itself as Transient for backward compatibility (if needed by simple consumers)
        // But preventing its direct injection is better to force Factory usage.
        // We'll keep it as scoped factory-created for now implicitly by AddDbContextFactory? 
        // No, AddDbContextFactory registers the factory. We usually need to register basic context resolution too if we want it.
        // But our UoW will use the Factory.
        
        // Register Common Repositories

        // Repositories - Transient to match DbContext lifetime
        services.AddTransient<ISubjectRepository, SubjectRepository>();
        services.AddTransient<ICaseRepository, CaseRepository>();
        services.AddTransient<IMatchRepository, MatchRepository>();
        services.AddTransient<IDentalImageRepository, DentalImageRepository>();
        services.AddTransient(typeof(IRepository<>), typeof(GenericRepository<>));
        services.AddTransient<IUnitOfWork, UnitOfWork>();

        // Services
        // IIntegrityService: Singleton — stateless file hashing
        services.AddSingleton<IIntegrityService, IntegrityService>();
        // IImageIntegrityService: Singleton — must match OnnxInferenceService lifetime (avoids captive dependency)
        services.AddSingleton<IImageIntegrityService, ImageIntegrityService>();
        // IEncryptionService: Singleton — key consistency across the application
        services.AddSingleton<IEncryptionService, EncryptionService>();
        // Auth runtime is disabled (no-login mode). AuthService remains in codebase as dormant technical debt.
        // IBulkOperationsService: Transient — stateless, uses transient DbContext
        services.AddTransient<IBulkOperationsService, BulkOperationsService>();
        // IAuditService: Transient — stateless, uses transient DbContext
        services.AddTransient<IAuditService, AuditService>();
        
        // Caching
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, CacheService>();

        // Application Services
        services.AddTransient<ISearchService, SearchService>();
        // Bulk Operations registration moved below

        // Validators
        services.AddTransient<IValidator<CreateSubjectDto>, CreateSubjectValidator>();
        services.AddTransient<IValidator<UpdateSubjectDto>, UpdateSubjectValidator>();
        services.AddTransient<IValidator<CreateDentalImageDto>, CreateDentalImageValidator>();
        services.AddTransient<IValidator<UpdateDentalImageDto>, UpdateDentalImageValidator>();
        services.AddTransient<IValidator<CreateCaseDto>, CreateCaseValidator>();
        services.AddTransient<IValidator<UpdateCaseDto>, UpdateCaseValidator>();
        services.AddTransient<IValidator<CreateMatchDto>, CreateMatchValidator>();
        services.AddTransient<IValidator<UpdateMatchDto>, UpdateMatchValidator>();

        // Bulk Operations
        // Bulk Operations registration moved below

        return services;
    }
}
