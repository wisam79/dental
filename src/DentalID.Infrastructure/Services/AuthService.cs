using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace DentalID.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILoggerService _logger;
    private User? _currentUser;

    // Centralized BCrypt work factor
    private const int BcryptWorkFactor = 12;

    public AuthService(IUnitOfWork unitOfWork, ILoggerService logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<(bool Success, User? User, string? Error)> LoginAsync(string username, string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (false, null, "Username and password are required.");
            }

            var userRepo = _unitOfWork.GetRepository<User>();
            // Bug #4 Fix: Query only the specific user by username instead of loading ALL users into memory.
            // Bug #19 Fix: Removed dead nested null check (if user == null { if user == null }).
            var user = await userRepo.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null) return (false, null, "Invalid credentials.");

            if (!user.IsActive)
            {
                _logger.LogWarning($"Login attempt for inactive user: {username}");
                return (false, null, "User account is inactive.");
            }

            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                _logger.LogWarning($"Failed login attempt for user: {username}");
                return (false, null, "Invalid credentials.");
            }

            _currentUser = user;
            user.LastLogin = DateTime.UtcNow;
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation($"User logged in: {username}");
            
            return (true, user, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return (false, null, "An error occurred during login.");
        }
    }

    public async Task InitializeAdminAsync()
    {
        // Bug Fix: Use FirstOrDefaultAsync with predicate instead of loading ALL users into memory.
        // The old code fetched every user record to find a single admin — O(N) memory when O(1) query works.
        var userRepo = _unitOfWork.GetRepository<User>();
        var existingAdmin = await userRepo.FirstOrDefaultAsync(u => u.Username == "admin");
        if (existingAdmin != null)
        {
            _logger.LogInformation("Admin account already exists. Skipping initialization.");
            return;
        }

        _logger.LogInformation("No users found. Creating default admin account.");
        
        var randomPassword = GenerateStrongRandomPassword();
        var admin = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(randomPassword, BcryptWorkFactor),
            Role = UserRole.Admin,
            FullName = "System Administrator",
            Email = "admin@dentalid.local",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            MustChangePassword = true
        };
        await _unitOfWork.GetRepository<User>().AddAsync(admin);
        await _unitOfWork.SaveChangesAsync();
        
        // Write password to a secure one-time file instead of logging it
        try
        {
            var credentialsPath = Path.Combine(AppContext.BaseDirectory, "data", "initial_credentials.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(credentialsPath)!);
            await File.WriteAllTextAsync(credentialsPath,
                $"DentalID Initial Admin Credentials (DELETE THIS FILE AFTER FIRST LOGIN)\n" +
                $"Username: admin\n" +
                $"Password: {randomPassword}\n" +
                $"Generated: {DateTime.UtcNow:O}\n");
            _logger.LogWarning($"Default admin account created. Credentials saved to: {credentialsPath}");
        }
        catch (Exception ex)
        {
            // If file write fails, log the password as a last resort (dev only)
            _logger.LogError(ex, "Failed to save admin credentials file. Check file system permissions immediately.");
            throw; // Fail authentication setup rather than leaking credentials to logs
        }
    }

    /// <summary>
    /// Generates a cryptographically secure random password.
    /// Uses RandomNumberGenerator instead of System.Random.
    /// </summary>
    private static string GenerateStrongRandomPassword(int length = 20)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+-=";
        var password = new char[length];
        var randomBytes = new byte[length];
        
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        for (int i = 0; i < length; i++)
        {
            password[i] = chars[randomBytes[i] % chars.Length];
        }

        return new string(password);
    }

    public Task LogoutAsync()
    {
        if (_currentUser != null)
        {
            _logger.LogInformation($"User logged out: {_currentUser.Username}");
            _currentUser = null;
        }
        return Task.CompletedTask;
    }

    public Task<User?> GetCurrentUserAsync()
    {
        return Task.FromResult(_currentUser);
    }

    public async Task<(bool Success, string? Error)> RegisterUserAsync(User user, string password)
    {
        try
        {
            if (_currentUser?.Role != UserRole.Admin)
            {
                 return (false, "Only Administrators can register new users.");
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                return (false, "Password must be at least 8 characters long.");
            }

            var userRepo = _unitOfWork.GetRepository<User>();
            var existing = (await userRepo.GetAllAsync()).FirstOrDefault(u => u.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return (false, "Username already exists.");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
            user.CreatedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            await userRepo.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation($"New user registered: {user.Username} by {_currentUser?.Username}");
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering user");
            return (false, "Registration failed.");
        }
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
         try
        {
            if (_currentUser == null) return false;

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                return false;
            }

            if (_currentUser.Id != userId && _currentUser.Role != UserRole.Admin)
            {
                return false;
            }

            var userRepo = _unitOfWork.GetRepository<User>();
            var user = await userRepo.GetByIdAsync(userId);
            if (user == null) return false;

            if (_currentUser.Role != UserRole.Admin || _currentUser.Id == userId)
            {
                if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash)) return false;
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BcryptWorkFactor);
            user.UpdatedAt = DateTime.UtcNow;
            user.MustChangePassword = false;

            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error changing password");
             return false;
        }
    }
}
