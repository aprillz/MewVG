namespace Aprillz.MewVG;

/// <summary>
/// The stored geometry a fill's vertices come from and the version of it they were taken at; two fills with the same
/// owner and version have the same vertices, up to the offset each is drawn at. Default for geometry built for one call.
/// </summary>
internal readonly record struct NVGGeometrySource(object? Owner, int Version);
