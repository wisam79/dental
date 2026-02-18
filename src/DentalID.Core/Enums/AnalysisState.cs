namespace DentalID.Core.Enums;

public enum AnalysisState
{
    Idle,
    LoadingImage,
    Ready, // Image Loaded
    Analyzing,
    Review, // Analysis Complete
    Error,
    Saving
}
