using Xunit;
using Moq;
using DentalID.Infrastructure.Services;
using DentalID.Core.Interfaces;
using DentalID.Core.Entities;
using DentalID.Core.Enums;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

namespace DentalID.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ILoggerService> _mockLogger;
    private readonly Mock<IRepository<User>> _mockUserRepo;
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockLogger = new Mock<ILoggerService>();
        _mockUserRepo = new Mock<IRepository<User>>();

        _mockUnitOfWork.Setup(u => u.GetRepository<User>()).Returns(_mockUserRepo.Object);
        _service = new AuthService(_mockUnitOfWork.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task LoginAsync_ShouldSeedAdmin_WhenNoUsersExist()
    {
        // Arrange
        _mockUserRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<User>()); // Empty first
        
        // After seeding, return the seeded user
        var seededUser = new User 
        { 
            Username = "admin", 
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"), 
            IsActive = true 
        };
        
        _mockUserRepo.SetupSequence(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<User>()) // First call (empty)
                     .ReturnsAsync(new List<User> { seededUser }); // Second call (after seed)

        _mockUserRepo.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        // Act
        var result = await _service.LoginAsync("admin", "admin123");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.User);
        Assert.Equal("admin", result.User.Username);
        _mockUserRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.Username == "admin"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnSuccess_WithValidCredentials()
    {
        // Arrange
        var password = "password123";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var user = new User { Username = "user", PasswordHash = hash, IsActive = true };

        _mockUserRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<User> { user });

        // Act
        var result = await _service.LoginAsync("user", password);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(user, result.User);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnFail_WithInvalidPassword()
    {
        // Arrange
        var user = new User { Username = "user", PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct"), IsActive = true };

        _mockUserRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<User> { user });

        // Act
        var result = await _service.LoginAsync("user", "wrong");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid credentials.", result.Error);
    }
}
