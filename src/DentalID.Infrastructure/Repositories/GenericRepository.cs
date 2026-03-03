using System.Linq.Expressions;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Repositories;

/// <summary>
/// Generic repository implementation
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public class GenericRepository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public GenericRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = _context.Set<T>();
    }

    #region Get Methods

    public virtual async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync([id], cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<T?> GetByIdAsync(int id, params Expression<Func<T, object>>[] includes)
    {
        IQueryable<T> query = _dbSet;
        
        foreach (var include in includes)
        {
            query = query.Include(include);
        }

        return await query.FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);
    }

    public virtual async Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<List<T>> GetAllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<List<T>> GetAllAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes)
    {
        IQueryable<T> query = _dbSet.Where(predicate);
        
        foreach (var include in includes)
        {
            query = query.Include(include);
        }

        return await query.ToListAsync().ConfigureAwait(false);
    }

    public virtual async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includes)
    {
        IQueryable<T> query = _dbSet.Where(predicate);
        
        foreach (var include in includes)
        {
            query = query.Include(include);
        }

        return await query.FirstOrDefaultAsync().ConfigureAwait(false);
    }

    #endregion

    #region Query Methods

    public virtual async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.CountAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Add Methods

    // Bug #56 fix: Remove auto-SaveChangesAsync from AddAsync.
    // The Unit of Work pattern requires callers to call SaveChangesAsync explicitly.
    // Previously this caused double-save when callers also called UoW.SaveChangesAsync().
    public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        // NOTE: Do NOT call SaveChangesAsync here. Use IUnitOfWork.SaveChangesAsync() instead.
    }

    // Bug #56 fix: AddRangeAsync is now consistent with AddAsync — no auto-save
    public virtual async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Update Methods

    public virtual void Update(T entity)
    {
        _dbSet.Update(entity);
    }

    public virtual void UpdateRange(IEnumerable<T> entities)
    {
        _dbSet.UpdateRange(entities);
    }

    #endregion

    #region Remove Methods

    public virtual void Remove(T entity)
    {
        _dbSet.Remove(entity);
    }

    public virtual void RemoveRange(IEnumerable<T> entities)
    {
        _dbSet.RemoveRange(entities);
    }

    public virtual async Task RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        
        if (entity != null)
        {
            Remove(entity);
        }
    }

    /// <summary>
    /// Removes an entity by ID and returns true if found and marked for deletion, false if not found.
    /// NOTE: Does NOT call SaveChangesAsync — caller must commit via IUnitOfWork.
    /// </summary>
    public virtual async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return false;

        Remove(entity);
        return true;
    }

    #endregion

    #region Advanced Methods

    // Bug #58 fix: These methods accept raw SQL — NEVER pass user input directly.
    // Always use parameterized queries: ExecuteSqlRawAsync("SELECT ... WHERE Id = {0}", userId)
    public virtual async Task<int> ExecuteSqlRawAsync(string sql, params object[] parameters)
    {
        return await _context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    public virtual async Task<List<T>> FromSqlRawAsync(string sql, params object[] parameters)
    {
        return await _dbSet.FromSqlRaw(sql, parameters).ToListAsync().ConfigureAwait(false);
    }

    public virtual IQueryable<T> AsQueryable()
    {
        return _dbSet.AsQueryable();
    }

    #endregion
}
