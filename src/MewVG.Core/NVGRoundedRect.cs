namespace Aprillz.MewVG;

/// <summary>
/// A rounded rectangle in frame units, axis-aligned with circular corners, filled when
/// <see cref="StrokeHalfWidth"/> is negative and otherwise outlined by a stroke of twice that width centred on it.
/// </summary>
internal readonly record struct NVGRoundedRect(
    float CenterX,
    float CenterY,
    float HalfWidth,
    float HalfHeight,
    float TopLeft,
    float TopRight,
    float BottomRight,
    float BottomLeft,
    float StrokeHalfWidth);
