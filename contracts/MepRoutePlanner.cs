using System;
using System.Collections.Generic;

namespace DSCons.RevitMcp.Contracts;

/// <summary>Unit-free route geometry validation. Coordinates are millimetres.</summary>
public static class MepRoutePlanner
{
    private const double EpsilonMm = 0.001;

    public static RouteValidationResult Validate(IReadOnlyList<MepPointMm> points)
    {
        if (points == null || points.Count < 2)
            return RouteValidationResult.Invalid("points requires at least two points.");

        int? previousAxis = null;
        for (var i = 1; i < points.Count; i++)
        {
            var delta = new MepPointMm(points[i].X - points[i - 1].X, points[i].Y - points[i - 1].Y, points[i].Z - points[i - 1].Z);
            var axes = 0;
            var axis = -1;
            if (Math.Abs(delta.X) > EpsilonMm) { axes++; axis = 0; }
            if (Math.Abs(delta.Y) > EpsilonMm) { axes++; axis = 1; }
            if (Math.Abs(delta.Z) > EpsilonMm) { axes++; axis = 2; }

            if (axes == 0) return RouteValidationResult.Invalid($"points[{i - 1}] and points[{i}] are coincident.");
            if (axes != 1) return RouteValidationResult.Invalid($"Segment {i} is diagonal. Each route segment must follow exactly one X, Y or Z axis.");
            if (previousAxis.HasValue && previousAxis.Value == axis)
                return RouteValidationResult.Invalid($"Points {i - 1} and {i} are a redundant collinear corner. Remove the intermediate point.");
            previousAxis = axis;
        }

        return RouteValidationResult.Valid();
    }
}

public sealed class MepPointMm
{
    public MepPointMm(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

public sealed class RouteValidationResult
{
    private RouteValidationResult(bool isValid, string message) { IsValid = isValid; Message = message; }
    public bool IsValid { get; }
    public string Message { get; }
    public static RouteValidationResult Valid() => new(true, string.Empty);
    public static RouteValidationResult Invalid(string message) => new(false, message);
}
