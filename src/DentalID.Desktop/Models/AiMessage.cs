using System;

namespace DentalID.Desktop.Models;

/// <summary>
/// Represents a message in an AI conversation or insight feed.
/// </summary>
public class AiMessage
{
    /// <summary>The role of the message sender (e.g. "System", "User", "Assistant").</summary>
    public string Role { get; set; } = "System";

    /// <summary>The text content of the message.</summary>
    public string Content { get; set; } = "";

    /// <summary>Timestamp when this message was generated.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
