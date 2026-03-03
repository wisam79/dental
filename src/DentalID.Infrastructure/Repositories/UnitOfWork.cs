using DentalID.Core.Interfaces;
using DentalID.Core.Entities;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DentalID.Infrastructure.Repositories;

/// <summary>
/// Unit of Work implementation for managing transactions and data access
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly Dictionary<Type, object> _repositories;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(IDbContextFactory<AppDbContext> contextFactory)
    {
        if (contextFactory == null) throw new ArgumentNullException(nameof(contextFactory));
        _context = contextFactory.CreateDbContext();
        _repositories = new Dictionary<Type, object>();
    }

    /// <inheritdoc/>
    public IRepository<T> GetRepository<T>() where T : BaseEntity
    {
        var type = typeof(T);
        
        if (!_repositories.ContainsKey(type))
        {
            var repositoryType = typeof(GenericRepository<>).MakeGenericType(type);
            var repository = Activator.CreateInstance(repositoryType, _context);
            
            if (repository == null)
            {
                throw new InvalidOperationException($"Could not create repository for type {type}");
            }
            
            _repositories[type] = repository;
        }
        
        return (IRepository<T>)_repositories[type];
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync()
    {
        try
        {
            return await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Log the exception and rethrow with a meaningful message
            Console.WriteLine($"Database update failed: {ex.Message}");
            foreach (var entry in ex.Entries)
            {
                Console.WriteLine($"Entity type: {entry.Entity.GetType().Name}, State: {entry.State}");
            }
            
            throw new ApplicationException("Database update failed. Please check the log for details.", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving changes: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task BeginTransactionAsync()
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("Transaction already in progress");
        }
        
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    /// <inheritdoc/>
    public async Task RollbackAsync()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction in progress");
        }
        
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    /// <inheritdoc/>
    public async Task CommitAsync()
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction in progress");
        }
        
        await _transaction.CommitAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
