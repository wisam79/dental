namespace DentalID.Core.Enums;

/// <summary>
/// Defines the roles available in the DentalID system.
/// NOTE: Authentication is intentionally skipped during development phase.
/// This enum will be used when login/auth is implemented before production.
/// </summary>
public enum UserRole
{
    Viewer,
    Analyst,
    Technician,
    ForensicDoctor,
    Admin
}
