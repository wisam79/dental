using System.Linq.Expressions;
using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

/// <summary>
/// Generic repository interface for data access operations
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public interface IRepository<T> where T : BaseEntity
{
    #region Get Methods
    
    /// <summary>
    /// Gets an entity by its ID
    /// </summary>
    /// <param name="id">Entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity or null if not found</returns>
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an entity by its ID with included navigation properties
    /// </summary>
    /// <param name="id">Entity ID</param>
    /// <param name="includes">Navigation properties to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity or null if not found</returns>
    Task<T?> GetByIdAsync(int id, params Expression<Func<T, object>>[] includes);

    /// <summary>
    /// Gets all entities
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entities</returns>
    Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all entities with a predicate filter
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entities matching the predicate</returns>
    Task<List<T>> GetAllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all entities with a predicate filter and included navigation properties
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="includes">Navigation properties to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entities matching the predicate</returns>
    Task<List<T>> GetAllAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes);

    /// <summary>
    /// Gets the first entity matching the predicate
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>First matching entity or null</returns>
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the first entity matching the predicate with included navigation properties
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="includes">Navigation properties to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>First matching entity or null</returns>
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes);

    #endregion

    #region Query Methods
    
    /// <summary>
    /// Checks if any entities match the predicate
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if any entities match the predicate</returns>
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the number of entities matching the predicate
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of matching entities</returns>
    Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    #endregion

    #region Add Methods
    
    /// <summary>
    /// Adds a single entity
    /// </summary>
    /// <param name="entity">Entity to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a range of entities
    /// </summary>
    /// <param name="entities">Entities to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    #endregion

    #region Update Methods
    
    /// <summary>
    /// Updates an entity
    /// </summary>
    /// <param name="entity">Entity to update</param>
    void Update(T entity);

    /// <summary>
    /// Updates a range of entities
    /// </summary>
    /// <param name="entities">Entities to update</param>
    void UpdateRange(IEnumerable<T> entities);

    #endregion

    #region Remove Methods
    
    /// <summary>
    /// Removes an entity
    /// </summary>
    /// <param name="entity">Entity to remove</param>
    void Remove(T entity);

    /// <summary>
    /// Removes a range of entities
    /// </summary>
    /// <param name="entities">Entities to remove</param>
    void RemoveRange(IEnumerable<T> entities);

    /// <summary>
    /// Removes an entity by its ID
    /// </summary>
    /// <param name="id">Entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RemoveAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an entity by ID and returns whether it was found and deleted.
    /// Prefer this over <see cref="RemoveAsync"/> when callers need to distinguish
    /// "not found" from a genuine deletion.
    /// </summary>
    /// <param name="id">Entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entity existed and was marked for deletion; false if not found</returns>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    #endregion

    #region Advanced Methods
    
    /// <summary>
    /// Executes a raw SQL query and returns the number of affected rows
    /// </summary>
    /// <param name="sql">SQL query</param>
    /// <param name="parameters">Query parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of affected rows</returns>
    Task<int> ExecuteSqlRawAsync(string sql, params object[] parameters);

    /// <summary>
    /// Gets entities using raw SQL query
    /// </summary>
    /// <param name="sql">SQL query</param>
    /// <param name="parameters">Query parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of entities</returns>
    Task<List<T>> FromSqlRawAsync(string sql, params object[] parameters);

    /// <summary>
    /// Gets an IQueryable for the entity type for query composition
    /// </summary>
    /// <returns>IQueryable for the entity type</returns>
    IQueryable<T> AsQueryable();

    #endregion
}
