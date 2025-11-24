using System.Collections.Generic;
using System.Windows.Media;

namespace FloatingNote
{
    public static class GradientPresets
    {
        public static List<Brush> SpotifyLikeGradients { get; } = new List<Brush>
        {
            // Preset 0: Mauve -> Deepest Dark Purple (Moody & Elegant)
            new LinearGradientBrush(
                Color.FromRgb(0x85, 0x4F, 0x6C), // #854F6C (Light Top)
                Color.FromRgb(0x19, 0x00, 0x19), // #190019 (Dark Bottom)
                new System.Windows.Point(0.5, 0), new System.Windows.Point(0.5, 1) // Vertical flow for more "lighting" effect
            ),
            
            // Preset 1: Dusty Pink -> Dark Purple (High Contrast)
            new LinearGradientBrush(
                Color.FromRgb(0xDF, 0xB6, 0xB2), // #DFB6B2 (Light Top)
                Color.FromRgb(0x2B, 0x12, 0x4C), // #2B124C (Dark Bottom)
                new System.Windows.Point(0, 0), new System.Windows.Point(1, 1) // Diagonal flow
            ),

            // Preset 2: Cream -> Medium Purple (Soft & Warm)
            new LinearGradientBrush(
                Color.FromRgb(0xFB, 0xE4, 0xD8), // #FBE4D8 (Light Top)
                Color.FromRgb(0x52, 0x2B, 0x5B), // #522B5B (Dark Bottom)
                new System.Windows.Point(0.5, 0), new System.Windows.Point(0.5, 1)
            ),

            // Preset 3: Rich Multi-Stop (Cream -> Mauve -> Deep Purple)
            new LinearGradientBrush()
            {
                StartPoint = new System.Windows.Point(0.5, 0), // Top Center
                EndPoint = new System.Windows.Point(0.5, 1),   // Bottom Center
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0xFB, 0xE4, 0xD8), 0.0), // Cream (Top)
                    new GradientStop(Color.FromRgb(0x85, 0x4F, 0x6C), 0.4), // Mauve (Middle-ish)
                    new GradientStop(Color.FromRgb(0x19, 0x00, 0x19), 1.0)  // Deepest Purple (Bottom)
                }
            },

             // Preset 4: "Spotlight" Radial (Very light top center, fading to dark)
            new RadialGradientBrush()
            {
                Center = new System.Windows.Point(0.5, -0.2), // Light source above the window
                GradientOrigin = new System.Windows.Point(0.5, -0.2),
                RadiusX = 1.5, RadiusY = 1.5,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0xDF, 0xB6, 0xB2), 0.0), // Dusty Pink (Center of light)
                    new GradientStop(Color.FromRgb(0x52, 0x2B, 0x5B), 0.5), // Medium Purple
                    new GradientStop(Color.FromRgb(0x19, 0x00, 0x19), 1.0)  // Deep Purple (Edges)
                }
            }
        };
    }
}