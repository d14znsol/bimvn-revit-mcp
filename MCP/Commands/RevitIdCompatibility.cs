using Autodesk.Revit.DB;

namespace DSCons.RevitMcp.Commands;

/// <summary>Centralizes the ElementId API break between Revit 2023 and 2024+.</summary>
internal static class RevitIdCompatibility
{
    public static long Val(this ElementId id)
    {
        if (id == null) return -1;
#if REVIT2019 || REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023
#pragma warning disable CS0618
        return id.IntegerValue;
#pragma warning restore CS0618
#else
        return id.Value;
#endif
    }

    public static int IntVal(this ElementId id) => checked((int)id.Val());

    public static ElementId Eid(long value)
    {
#if REVIT2019 || REVIT2020 || REVIT2021 || REVIT2022 || REVIT2023
#pragma warning disable CS0618
        return new ElementId(checked((int)value));
#pragma warning restore CS0618
#else
        return new ElementId(value);
#endif
    }
}
