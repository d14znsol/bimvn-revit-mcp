from pathlib import Path


SOURCE = Path(__file__).resolve().parents[1] / "MCP" / "Commands" / "FamilyBlueprintCompiler.cs"
TEXT = SOURCE.read_text(encoding="utf-8")


def require(value: str) -> None:
    assert value in TEXT, f"missing stable family-constraint contract: {value}"


for helper in (
    "ConstraintFailure(",
    "CreateDatumPlane(",
    "RequireCurveReference(",
    "RequireCenterPointReference(",
    "AlignToDatum(",
    "CreateStableLinearDimension(",
    "CreateStableRadialDimension(",
    "CreateStableDiameterDimension(",
):
    require(helper)

for role in (
    "constraint_role=",
    "bend_radial",
    "tangent_intersection_x",
    "tangent_intersection_lateral",
):
    require(role)
require('"profile_" + (property == "width_parameter" ? "width" : "height")')
require("curve.CenterPointReference")
assert "as Arc)?.CenterPointReference" not in TEXT
require("BuiltInParameter.ELEM_REFERENCE_NAME")
assert "BuiltInParameter.ELEM_IS_REFERENCE" not in TEXT, "ReferencePlane 'Is Reference' must use ELEM_REFERENCE_NAME; ELEM_IS_REFERENCE is a different native parameter"
require("declared_type_value_restored_after_family_label")
require("value_after_restore_mm")
require("dimension_reference_type")
require('ReferencePlaneReference(plane, key + " dimension witness")')
require('var cutDirection = Direction(contract["cut_vector"], "reference plane cut_vector")')
require("var thirdPoint = bubbleEnd.Add(freeEnd).Multiply(.5).Add(cutDirection.Multiply")
require("must be a non-zero direction vector")
require("ReferenceParameterValue(FamilyInstanceReferenceType type)")
require("ReferenceTypeFromParameterValue(int value)")
require("FamilyInstanceReferenceType.StrongReference => 13")
require("FamilyInstanceReferenceType.WeakReference => 14")
require("FamilyInstanceReferenceType.NotAReference => 12")
require("NewAlignment(view, new Reference(datum), source)")
require("candidate.StyleType == DimensionStyleType.Radial")
require("NewRadialDimension(view, arcReference, leaderPoint, radialType)")
require("The selected DimensionType did not create a radial dimension")
require("alignment.IsLocked = true")
require("Revit did not retain the datum alignment lock")
require("ProfileArcRadii")
require("profile_arc_radii_mm=")
create_method = TEXT[TEXT.index("private static JObject Create(Document family"):TEXT.index("private static JObject EvaluateComplexityBudget")]
assert create_method.index("transaction.Commit()") < create_method.index("EvaluateComplexityBudget(family, spec, null)"), "Family inspection/complexity must run after the authoring transaction commits"
require("ElementTypeGroup.DiameterDimensionType")
require("profile_diameter_plane_alignment")
require("The plane-to-plane Diameter label did not drive both circular profile arcs")
require("FamilyCompileFailuresPreprocessor")
require("failure_diagnostic=")
require("Blueprint compilation failed during ")
require("NewDiameterDimension(view, arcReference, leaderPoint)")
circle_profile = TEXT[TEXT.index("private static void LabelDiameter"):TEXT.index("private static void LabelRectangle")]
assert "CreateStableRadialDimension" not in circle_profile and "CreateStableLinearDimension" in circle_profile, "circular extrusion profile must use the tested plane-to-plane Diameter constraint rather than an invalid radial label"
require("type == FamilyElementVisibilityType.ViewSpecific")
require("only_when_cut is valid only for view-specific Family elements")
require("Connector \" + label + \" has no writable Revit parameter")
require("IncludeNonVisibleObjects = true")
require("Connector creation must bind a")
require("Revit makes the flow/value connector parameter associable only after")
datum_helper = TEXT[TEXT.index("private static ReferencePlane CreateDatumPlane"):TEXT.index("private static Reference RequireCurveReference")]
assert "referenceType.Set(" not in datum_helper

# Arc centres are first aligned to datums; a direct centre reference inside a
# linear NewDimension call is the regression that caused the 90-degree elbow
# transaction to fail.  Native radial dimensions and the radius parameter must
# remain present so the fix cannot degrade flex behavior into static geometry.
arc_sweep = TEXT[TEXT.index("private static void LabelSweepPathRadius"):TEXT.index("internal static JObject? SweepPathSnapshot")]
assert "NewDimension(view, Line.CreateBound" not in arc_sweep
assert "ELEM_REFERENCE_NAME" not in arc_sweep and "NotAReference" not in arc_sweep, "Arc-sweep tangent datum witnesses must preserve their native reference role through CreateDatumPlane."
require("NewRadialDimension")
require("radius_parameter")
require("FamilyLabel = radiusParameter")
require("FlexAffectedParameterKeys(parameterContracts, key)")
require('["affected_parameter_keys"]')
require('["dependent_parameter_checks"]')
require("family_type_evaluated_formula_or_lookup_read_back")
require("VerifyReopenedLookupTables(spec, readBack)")
require("Reopened Family lookup-table inventory mismatch")

print("family constraint static contract PASS")
