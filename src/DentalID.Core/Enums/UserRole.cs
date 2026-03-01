namespace DentalID.Core.Enums;

/// <summary>
/// Dormant technical-debt enum kept only for backward compatibility.
/// Runtime authentication/authorization is disabled in no-login mode.
/// </summary>
public enum UserRole
{
    Viewer,
    Analyst,
    Technician,
    ForensicDoctor,
    Admin
}
