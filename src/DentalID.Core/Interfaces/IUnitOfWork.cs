using System;
using System.Threading.Tasks;
using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

/// <summary>
/// Represents a unit of work for managing transactions and database operations
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Gets the repository for the specified entity type
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <returns>An instance of the repository for the specified entity type</returns>
    IRepository<T> GetRepository<T>() where T : BaseEntity;

    /// <summary>
    /// Saves all changes made in this unit of work
    /// </summary>
    /// <returns>The number of entities affected</returns>
    Task<int> SaveChangesAsync();

    /// <summary>
    /// Starts a transaction
    /// </summary>
    Task BeginTransactionAsync();

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    Task CommitAsync();

    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    Task RollbackAsync();
}
