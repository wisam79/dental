using System.Collections.Generic;

namespace DentalID.Desktop.Assets;

public static class ToothShapes
{
    // Simplified SVG Paths for Dental Chart 
    // These are stylized representations: 
    // Incisors (Rectangular/Spade), Canines (Pointed), Premolars (Ovoid), Molars (Large/Square)

    public static string GetPathForFdi(int fdi)
    {
        // 1. Determine Tooth Type based on FDI last digit
        // 1,2 = Incisor
        // 3 = Canine
        // 4,5 = Premolar
        // 6,7,8 = Molar
        
        int type = fdi % 10;
        bool isUpper = (fdi / 10) == 1 || (fdi / 10) == 2;
        
        return type switch
        {
            1 => isUpper ? UpperIncisor : LowerIncisor,
            2 => isUpper ? UpperLateral : LowerIncisor,
            3 => isUpper ? UpperCanine : LowerCanine,
            4 => Premolar,
            5 => Premolar,
            6 => Molar,
            7 => Molar,
            8 => Molar,
            _ => Molar
        };
    }

    // SVG Path Data (ViewBox 0 0 32 40 approx)
    
    // Central Incisor: Broad, Shovel-like
    public const string UpperIncisor = "M 4,4 L 28,4 L 26,36 Q 16,40 6,36 Z"; 
    
    // Lateral Incisor: Narrower
    public const string UpperLateral = "M 6,6 L 26,6 L 24,34 Q 16,38 8,34 Z";

    // Lower Incisors: Very narrow
    public const string LowerIncisor = "M 8,8 L 24,8 L 22,34 Q 16,36 10,34 Z";

    // Canine: Pointed
    public const string UpperCanine = "M 4,10 L 16,2 L 28,10 L 24,36 Q 16,40 8,36 Z";
    public const string LowerCanine = "M 6,10 L 16,4 L 26,10 L 22,34 Q 16,38 10,34 Z";

    // Premolar: Ovoid/Bicuspid look
    public const string Premolar = "M 4,10 Q 16,0 28,10 Q 32,20 28,30 Q 16,40 4,30 Q 0,20 4,10 Z";

    // Molar: Large, Square-ish with cusp hints
    public const string Molar = "M 2,10 Q 8,5 16,8 Q 24,5 30,10 L 30,30 Q 24,35 16,32 Q 8,35 2,30 Z";
}
