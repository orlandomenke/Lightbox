namespace Lightbox.Core.Documents;

/// <summary>
/// A single sample of a stroke: position in document pixels plus stylus
/// pressure (0..1; mouse input feeds a constant 0.5). Pressure is part of the
/// model from day one so tablet support is a data-source change, not a
/// model change.
/// </summary>
public readonly record struct StrokePoint(double X, double Y, double Pressure);
