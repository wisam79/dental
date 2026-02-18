using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

public interface IAuthService
{
    Task<(bool Success, User? User, string? Error)> LoginAsync(string username, string password);
    Task InitializeAdminAsync();
    Task LogoutAsync();
    Task<User?> GetCurrentUserAsync();
    Task<(bool Success, string? Error)> RegisterUserAsync(User user, string password);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
