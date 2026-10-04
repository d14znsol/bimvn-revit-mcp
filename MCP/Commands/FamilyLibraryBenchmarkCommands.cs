using System.Diagnostics;
using System.Security.Cryptography;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DSCons.RevitMcp.Contracts;
using DSCons.RevitMcp.Core;
using Newtonsoft.Json.Linq;

namespace DSCons.RevitMcp.Commands;

/// <summary>
/// Measures a declared equivalent monolithic/nested Family pair without
/// retaining any load, instance, type-value or Project change. This is a
/// benchmark harness, not a claim that two arbitrary RFAs are equivalent or
/// that one structure is universally faster/smaller.
/// </summary>
internal sealed class FamilyLibraryBenchmarkPreviewCommand : ReadCommand
{
    public FamilyLibraryBenchmarkPreviewCommand() : base("family_library_benchmark_preview", "Benchmark a declared equivalent monolithic/nested Family pair in temporary Family and Project transactions, then roll everything back.") { Capability.IsWrite = true; }

    public override JObject Execute(UIApplication app, JObject args)
    {
        var project = Document(app);
        var spec = FamilyLibraryBenchmarkSpec.From(project, args);
        var result = FamilyLibraryBenchmark.Preview(app.Application, project, spec);
        return new JObject
        {
            ["operation"] = "family_library_benchmark", ["model_changed"] = false,
            ["validation_level"] = "family_document_and_project_transactiongroup_rollback",
            ["validation"] = result,
            ["next"] = "This reports comparable measurements only. It does not certify a file-size or performance optimization, Dynamic Tag, routing, hosting, or LOD evidence."
        };
    }
}

internal sealed class FamilyLibraryBenchmarkSpec
{
    public FamilyLibraryBenchmarkEntry Monolithic { get; set; } = new();
    public FamilyLibraryBenchmarkEntry Nested { get; set; } = new();
    public int InstanceCount { get; set; }
    public double InstanceSpacingFt { get; set; }
    public bool EquivalentFunctionConfirmed { get; set; }

    public static FamilyLibraryBenchmarkSpec From(Document project, JObject args)
    {
        if (args.Value<bool?>("copied_project_confirmed") != true)
            throw new CommandResultException(ErrorCodes.InvalidParam, "copied_project_confirmed=true is required before a nested Family benchmark.");
        if (args.Value<bool?>("isolated_origin_confirmed") != true)
            throw new CommandResultException(ErrorCodes.InvalidParam, "isolated_origin_confirmed=true is required; the benchmark temporarily places multiple instances at the supplied isolated origin.");
        if (args.Value<bool?>("equivalent_function_confirmed") != true)
            throw new CommandResultException(ErrorCodes.InvalidParam, "equivalent_function_confirmed=true is required. MCP cannot infer functional equivalence merely from two RFA files.");

        var entries = (args["families"] as JArray)?.OfType<JObject>().ToList()
            ?? throw new CommandResultException(ErrorCodes.InvalidParam, "families must contain exactly one monolithic and one nested entry.");
        if (entries.Count != 2)
            throw new CommandResultException(ErrorCodes.InvalidParam, "families must contain exactly two entries: monolithic and nested.");

        FamilyLibraryBenchmarkEntry Parse(JObject entry)
        {
            var role = entry.Value<string>("role")?.Trim() ?? string.Empty;
            if (role is not ("monolithic" or "nested"))
                throw new CommandResultException(ErrorCodes.InvalidParam, "Each benchmark Family role must be monolithic or nested.");
            var placementArgs = (JObject)args.DeepClone();
            placementArgs["family_path"] = entry.Value<string>("family_path");
            placementArgs["type_name"] = entry.Value<string>("type_name");
            var placement = FamilySafety.ValidatePlaceSpec(project, placementArgs);
            var parameter = entry.Value<string>("flex_parameter")?.Trim();
            if (string.IsNullOrWhiteSpace(parameter) || parameter!.Length > 128)
                throw new CommandResultException(ErrorCodes.InvalidParam, "flex_parameter must be a non-empty Family Type Length parameter name.");
            var cases = (entry["flex_cases"] as JArray)?.OfType<JObject>().ToList()
                ?? throw new CommandResultException(ErrorCodes.InvalidParam, "flex_cases must contain min, nominal and max values.");
            if (cases.Count != 3)
                throw new CommandResultException(ErrorCodes.InvalidParam, "flex_cases must contain exactly min, nominal and max.");
            var expectedNames = new[] { "min", "nominal", "max" };
            var parsed = cases.Select(item => new FamilyLibraryFlexCase
            {
                Name = item.Value<string>("name")?.Trim() ?? string.Empty,
                ValueFt = (item.Value<double?>("value_mm") ?? 0) / 304.8
            }).ToList();
            if (parsed.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != 3 || expectedNames.Any(name => parsed.All(item => item.Name != name)))
                throw new CommandResultException(ErrorCodes.InvalidParam, "flex_cases must contain one case named min, nominal and max.");
            parsed = expectedNames.Select(name => parsed.Single(item => item.Name == name)).ToList();
            if (parsed.Any(item => item.ValueFt <= 0) || !(parsed[0].ValueFt < parsed[1].ValueFt && parsed[1].ValueFt < parsed[2].ValueFt))
                throw new CommandResultException(ErrorCodes.InvalidParam, "flex_cases values must be positive and strictly increase from min through nominal to max.");
            return new FamilyLibraryBenchmarkEntry { Role = role, Placement = placement, FlexParameterName = parameter, FlexCases = parsed };
        }

        var parsedEntries = entries.Select(Parse).ToList();
        if (parsedEntries.Select(item => item.Role).Distinct(StringComparer.Ordinal).Count() != 2)
            throw new CommandResultException(ErrorCodes.InvalidParam, "families must contain exactly one monolithic and one nested entry.");
        var monolithic = parsedEntries.Single(item => item.Role == "monolithic");
        var nested = parsedEntries.Single(item => item.Role == "nested");
        if (monolithic.Placement.LevelId.Val() != nested.Placement.LevelId.Val()
            || !PointsEqual(monolithic.Placement.Point, nested.Placement.Point))
            throw new CommandResultException(ErrorCodes.InvalidParam, "Both benchmark entries must use the exact same Level and isolated origin.");
        for (var index = 0; index < 3; index++)
            if (Math.Abs(monolithic.FlexCases[index].ValueFt - nested.FlexCases[index].ValueFt) > 0.000003281)
                throw new CommandResultException(ErrorCodes.InvalidParam, "Both benchmark entries must declare identical min/nominal/max flex values for a comparable measurement.");

        var instanceCount = args.Value<int?>("instance_count") ?? 0;
        if (instanceCount < 1 || instanceCount > 100)
            throw new CommandResultException(ErrorCodes.InvalidParam, "instance_count must be from 1 through 100.");
        var spacingMm = args.Value<double?>("instance_spacing_mm") ?? 0;
        if (spacingMm < 100 || spacingMm > 1_000_000)
            throw new CommandResultException(ErrorCodes.InvalidParam, "instance_spacing_mm must be from 100 through 1000000 mm.");
        return new FamilyLibraryBenchmarkSpec
        {
            Monolithic = monolithic, Nested = nested, InstanceCount = instanceCount,
            InstanceSpacingFt = spacingMm / 304.8, EquivalentFunctionConfirmed = true
        };
    }

    private static bool PointsEqual(XYZ left, XYZ right) => Math.Abs(left.X - right.X) < 0.000003281 && Math.Abs(left.Y - right.Y) < 0.000003281 && Math.Abs(left.Z - right.Z) < 0.000003281;
}

internal sealed class FamilyLibraryBenchmarkEntry
{
    public string Role { get; set; } = string.Empty;
    public PlaceSpec Placement { get; set; } = new();
    public string FlexParameterName { get; set; } = string.Empty;
    public List<FamilyLibraryFlexCase> FlexCases { get; set; } = new();
}

internal sealed class FamilyLibraryFlexCase
{
    public string Name { get; set; } = string.Empty;
    public double ValueFt { get; set; }
}

internal sealed class FamilyLibraryBenchmarkEvidence
{
    public string Role { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public FamilyPlacementType PlacementType { get; set; }
    public JObject File { get; set; } = new();
    public JObject Flex { get; set; } = new();
}

internal static class FamilyLibraryBenchmark
{
    private const double ValueToleranceFt = 0.000003281;

    public static JObject Preview(Application application, Document project, FamilyLibraryBenchmarkSpec spec)
    {
        var monolithic = InspectAndFlex(application, spec.Monolithic);
        var nested = InspectAndFlex(application, spec.Nested);
        VerifyComparablePair(monolithic, nested, spec);
        using var revisionSuppression = DocumentRevisionTracker.Suppress();
        using var group = new TransactionGroup(project, "Benchmark DSCons nested Family library"); group.Start();
        try
        {
            var projectMeasurements = MeasureProjectLoadPlaceAndRegenerate(project, spec, monolithic, nested);
            group.RollBack();
            return new JObject
            {
                ["status"] = "passed", ["model_changed"] = false,
                ["comparability"] = new JObject
                {
                    ["equivalent_function_confirmed_by_operator"] = spec.EquivalentFunctionConfirmed,
                    ["minimum_api_checks"] = new JObject { ["same_category"] = true, ["same_placement_type"] = true, ["same_flex_cases"] = true, ["eligible_type_length_parameter"] = true, ["both_flex_geometry_changed"] = true },
                    ["status"] = "declared_equivalent_with_minimum_api_checks_passed",
                    ["boundary"] = "Matching category, placement and flex values do not mathematically prove equal geometry, connector behavior, manufacturer data or LOD. Interpret timings only for this declared pair and environment."
                },
                ["families"] = new JArray(Describe(monolithic), Describe(nested)),
                ["project_measurements"] = projectMeasurements,
                ["measurement_boundary"] = "RFA file bytes/hashes are read from disk. Family flex, Project LoadFamily, bounded placement and regeneration were executed only inside rollback transactions; no Family, type value, instance, Schedule, Project Save or Sync persists."
            };
        }
        catch (CommandResultException) { group.RollBack(); throw; }
        catch (Exception exception)
        {
            group.RollBack();
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Nested Family library benchmark rolled back: " + exception.Message);
        }
    }

    private static FamilyLibraryBenchmarkEvidence InspectAndFlex(Application application, FamilyLibraryBenchmarkEntry entry)
    {
        Document? family = null; string? scratch = null;
        try
        {
            // A staged RFA can remain readable while an antivirus indexer or an
            // unrelated Revit document holds a sharing handle which prevents
            // OpenDocumentFile from taking its required lock. Copy only after
            // hashing the declared immutable input, verify the copy byte-for-
            // byte, and open that one-use scratch file instead. Measurements
            // still report the declared RFA's bytes/hash; scratch never reaches
            // LoadFamily or the benchmark result and is removed in finally.
            var sourceFile = FileEvidence(entry.Placement.FamilyPath);
            scratch = CreateVerifiedScratchCopy(entry.Placement.FamilyPath, sourceFile);
            family = application.OpenDocumentFile(scratch);
            if (!family.IsFamilyDocument || family.OwnerFamily == null)
                throw new CommandResultException(ErrorCodes.DocumentTypeInvalid, "Benchmark input is not a loadable Revit Family document.");
            var owner = family.OwnerFamily;
            var category = owner.FamilyCategory ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Family has no category.");
            var type = family.FamilyManager.Types.Cast<FamilyType>().SingleOrDefault(item => string.Equals(item.Name, entry.Placement.TypeName, StringComparison.Ordinal));
            if (type == null)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Family " + entry.Role + " does not contain the declared exact Type.");
            var parameters = family.FamilyManager.Parameters.Cast<FamilyParameter>().Where(item => string.Equals(item.Definition.Name, entry.FlexParameterName, StringComparison.Ordinal)).ToList();
            if (parameters.Count != 1)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Family " + entry.Role + " must contain exactly one parameter named " + entry.FlexParameterName + ".");
            var parameter = parameters[0];
            if (parameter.IsInstance || parameter.StorageType != StorageType.Double || parameter.Formula != null || !IsLength(parameter))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Family " + entry.Role + " flex_parameter must be a non-formula Type Length parameter.");
            var flex = FlexFamilyDocument(family, type, parameter, entry);
            return new FamilyLibraryBenchmarkEvidence
            {
                Role = entry.Role, FamilyName = owner.Name, TypeName = type.Name,
                CategoryId = category.Id.Val(), CategoryName = category.Name, PlacementType = owner.FamilyPlacementType,
                File = sourceFile, Flex = flex
            };
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Could not inspect/flex benchmark Family " + entry.Role + ": " + exception.Message);
        }
        finally
        {
            if (family != null)
                family.Close(false);
            if (!string.IsNullOrWhiteSpace(scratch) && File.Exists(scratch))
                try { File.Delete(scratch); } catch { }
        }
    }

    private static string CreateVerifiedScratchCopy(string sourcePath, JObject sourceFile)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new CommandResultException(ErrorCodes.InvalidParam, "Benchmark RFA directory is unavailable for an immutable scratch copy.");
        var scratch = Path.Combine(directory, ".dscons-benchmark-" + Guid.NewGuid().ToString("N") + ".rfa");
        var verified = false;
        try
        {
            File.Copy(sourcePath, scratch, false);
            var copiedFile = FileEvidence(scratch);
            if (sourceFile.Value<long?>("bytes") != copiedFile.Value<long?>("bytes")
                || !string.Equals(sourceFile.Value<string>("sha256"), copiedFile.Value<string>("sha256"), StringComparison.OrdinalIgnoreCase))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark scratch RFA hash/byte verification failed; the declared source was not opened.");
            verified = true;
            return scratch;
        }
        catch (CommandResultException) { throw; }
        catch (Exception exception)
        {
            throw new CommandResultException(ErrorCodes.TransactionFailed, "Could not create a verified immutable scratch copy for benchmark Family input: " + exception.Message);
        }
        finally
        {
            if (!verified && File.Exists(scratch))
                try { File.Delete(scratch); } catch { }
        }
    }

    private static JObject FlexFamilyDocument(Document family, FamilyType type, FamilyParameter parameter, FamilyLibraryBenchmarkEntry entry)
    {
        var manager = family.FamilyManager;
        using var group = new TransactionGroup(family, "Flex DSCons nested Family benchmark input"); group.Start();
        try
        {
            var cases = new JArray();
            foreach (var item in entry.FlexCases)
            {
                var clock = Stopwatch.StartNew();
                JObject geometry;
                using (var transaction = new Transaction(family, "Flex " + item.Name + " benchmark Family"))
                {
                    transaction.Start();
                    manager.CurrentType = type;
                    manager.Set(parameter, item.ValueFt);
                    family.Regenerate();
                    var readBack = manager.CurrentType.AsDouble(parameter) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Flex parameter value was unavailable after Revit regeneration.");
                    if (Math.Abs(readBack - item.ValueFt) > ValueToleranceFt)
                        throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Flex parameter did not retain the requested " + item.Name + " value.");
                    geometry = GeometryEvidence(family);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected benchmark Family flex transaction.");
                }
                clock.Stop();
                cases.Add(new JObject { ["name"] = item.Name, ["requested_value_mm"] = Math.Round(item.ValueFt * 304.8, 3), ["read_back_value_mm"] = Math.Round(item.ValueFt * 304.8, 3), ["geometry"] = geometry, ["elapsed_ms"] = Math.Round(clock.Elapsed.TotalMilliseconds, 3) });
            }
            var snapshots = cases.OfType<JObject>().Select(item => item["geometry"] as JObject ?? new JObject()).ToList();
            if (GeometryEqual(snapshots[0], snapshots[1]) || GeometryEqual(snapshots[1], snapshots[2]))
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Family " + entry.Role + " did not change GenericForm bounds for every min/nominal/max flex transition.");
            group.RollBack();
            return new JObject { ["parameter"] = parameter.Definition.Name, ["scope"] = "type", ["data_type"] = "length", ["formula"] = null, ["cases"] = cases, ["geometry_changed_min_to_nominal"] = true, ["geometry_changed_nominal_to_max"] = true, ["rolled_back"] = true };
        }
        catch { group.RollBack(); throw; }
    }

    private static void VerifyComparablePair(FamilyLibraryBenchmarkEvidence monolithic, FamilyLibraryBenchmarkEvidence nested, FamilyLibraryBenchmarkSpec spec)
    {
        if (monolithic.CategoryId != nested.CategoryId)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark pair categories differ; no file-size or timing comparison is allowed.");
        if (monolithic.PlacementType != nested.PlacementType)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark pair FamilyPlacementType values differ; no file-size or timing comparison is allowed.");
        if (monolithic.PlacementType != FamilyPlacementType.OneLevelBased)
            throw new CommandResultException(ErrorCodes.Unsupported, "The current generic benchmark supports only matching unhosted OneLevelBased pairs. Hosted/work-plane/two-level placement requires its own rollback placement contract.");
        if (string.Equals(monolithic.FamilyName, nested.FamilyName, StringComparison.OrdinalIgnoreCase))
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark pair uses the same internal Family name, so Revit LoadFamily would conflict instead of measuring two exact inputs.");
        for (var index = 0; index < 3; index++)
            if (Math.Abs(spec.Monolithic.FlexCases[index].ValueFt - spec.Nested.FlexCases[index].ValueFt) > ValueToleranceFt)
                throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark pair no longer has matching flex values.");
    }

    private static JObject MeasureProjectLoadPlaceAndRegenerate(Document project, FamilyLibraryBenchmarkSpec spec, FamilyLibraryBenchmarkEvidence monolithic, FamilyLibraryBenchmarkEvidence nested)
    {
        FamilySymbol monoSymbol;
        FamilySymbol nestedSymbol;
        double monoLoadMs;
        double nestedLoadMs;
        using (var transaction = new Transaction(project, "Load benchmark Family pair"))
        {
            transaction.Start();
            (monoSymbol, monoLoadMs) = LoadSymbol(project, spec.Monolithic.Placement, monolithic.FamilyName);
            (nestedSymbol, nestedLoadMs) = LoadSymbol(project, spec.Nested.Placement, nested.FamilyName);
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected benchmark Family load transaction.");
        }
        var placement = new JObject
        {
            ["monolithic"] = PlaceMany(project, spec, monoSymbol, "monolithic"),
            ["nested"] = PlaceMany(project, spec, nestedSymbol, "nested")
        };
        var regenerateClock = Stopwatch.StartNew();
        using (var transaction = new Transaction(project, "Regenerate benchmark Family instances"))
        {
            transaction.Start();
            project.Regenerate();
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected temporary benchmark regeneration transaction.");
        }
        regenerateClock.Stop();
        return new JObject
        {
            ["temporary_load_ms"] = new JObject { ["monolithic"] = monoLoadMs, ["nested"] = nestedLoadMs },
            ["temporary_placement"] = placement,
            ["document_regenerate_ms_after_both_sets"] = Math.Round(regenerateClock.Elapsed.TotalMilliseconds, 3),
            ["instance_count_per_family"] = spec.InstanceCount, ["instance_spacing_mm"] = Math.Round(spec.InstanceSpacingFt * 304.8, 3),
            ["rollback"] = true
        };
    }

    private static (FamilySymbol Symbol, double ElapsedMs) LoadSymbol(Document project, PlaceSpec placement, string expectedFamilyName)
    {
        if (new FilteredElementCollector(project).OfClass(typeof(Family)).Cast<Family>().Any(item => string.Equals(item.Name, expectedFamilyName, StringComparison.OrdinalIgnoreCase)))
            throw new CommandResultException(ErrorCodes.FileConflict, "Benchmark Family " + expectedFamilyName + " is already loaded in the copied Project; use a fresh Project copy.");
        var clock = Stopwatch.StartNew();
        if (!project.LoadFamily(placement.FamilyPath, new RejectBenchmarkFamilyLoadOptions(), out var family) || family == null)
            throw new CommandResultException(ErrorCodes.FileConflict, "Benchmark Family could not be loaded safely. Same-name replacement is blocked.");
        var symbol = new FilteredElementCollector(project).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().SingleOrDefault(item => item.Family.Id.Val() == family.Id.Val() && string.Equals(item.Name, placement.TypeName, StringComparison.Ordinal));
        if (symbol == null)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Loaded benchmark Family does not contain the declared exact Type.");
        if (!symbol.IsActive) symbol.Activate();
        clock.Stop();
        return (symbol, Math.Round(clock.Elapsed.TotalMilliseconds, 3));
    }

    private static JObject PlaceMany(Document project, FamilyLibraryBenchmarkSpec spec, FamilySymbol symbol, string role)
    {
        var level = project.GetElement(spec.Monolithic.Placement.LevelId) as Level
            ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Level disappeared before temporary placement.");
        var start = spec.Monolithic.Placement.Point;
        var direction = role == "monolithic" ? -1.0 : 1.0;
        var clock = Stopwatch.StartNew();
        var count = 0;
        using (var transaction = new Transaction(project, "Place " + role + " benchmark instances"))
        {
            transaction.Start();
            for (var index = 0; index < spec.InstanceCount; index++)
            {
                var point = new XYZ(start.X + direction * spec.InstanceSpacingFt * (index + 1), start.Y, start.Z);
                var instance = project.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
                if (instance.Symbol.Id.Val() != symbol.Id.Val())
                    throw new CommandResultException(ErrorCodes.VerificationFailed, "Temporary benchmark placement returned a different Family Type.");
                count++;
            }
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new CommandResultException(ErrorCodes.TransactionFailed, "Revit rejected temporary " + role + " benchmark placement.");
        }
        clock.Stop();
        return new JObject { ["placed_instance_count"] = count, ["elapsed_ms"] = Math.Round(clock.Elapsed.TotalMilliseconds, 3), ["verified_exact_type"] = true, ["rollback"] = true };
    }

    private static JObject Describe(FamilyLibraryBenchmarkEvidence evidence) => new()
    {
        ["role"] = evidence.Role, ["family"] = evidence.FamilyName, ["type"] = evidence.TypeName,
        ["category"] = new JObject { ["id"] = evidence.CategoryId, ["name"] = evidence.CategoryName },
        ["family_placement_type"] = evidence.PlacementType.ToString(), ["rfa_file"] = evidence.File, ["flex"] = evidence.Flex
    };

    private static JObject FileEvidence(string path)
    {
        using var hash = SHA256.Create();
        using var stream = File.OpenRead(path);
        var bytes = hash.ComputeHash(stream);
        return new JObject { ["file_name"] = Path.GetFileName(path), ["bytes"] = new FileInfo(path).Length, ["sha256"] = BitConverter.ToString(bytes).Replace("-", string.Empty) };
    }

    private static JObject GeometryEvidence(Document family)
    {
        var forms = new FilteredElementCollector(family).OfClass(typeof(GenericForm)).Cast<GenericForm>().ToList();
        if (forms.Count == 0)
            throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark Family has no GenericForm bounds to prove flex geometry.");
        BoundingBoxXYZ? union = null;
        foreach (var form in forms)
        {
            var bounds = form.get_BoundingBox(null) ?? throw new CommandResultException(ErrorCodes.VerificationFailed, "Benchmark GenericForm has no bounding box.");
            if (union == null) union = new BoundingBoxXYZ { Min = bounds.Min, Max = bounds.Max };
            else union = new BoundingBoxXYZ
            {
                Min = new XYZ(Math.Min(union.Min.X, bounds.Min.X), Math.Min(union.Min.Y, bounds.Min.Y), Math.Min(union.Min.Z, bounds.Min.Z)),
                Max = new XYZ(Math.Max(union.Max.X, bounds.Max.X), Math.Max(union.Max.Y, bounds.Max.Y), Math.Max(union.Max.Z, bounds.Max.Z))
            };
        }
        return new JObject
        {
            ["generic_form_count"] = forms.Count,
            ["overall_bounds_mm"] = new JObject
            {
                ["min"] = PointMm(union!.Min), ["max"] = PointMm(union.Max),
                ["size"] = PointMm(union.Max - union.Min)
            }
        };
    }

    private static bool GeometryEqual(JObject left, JObject right)
    {
        var leftBounds = left["overall_bounds_mm"] as JObject;
        var rightBounds = right["overall_bounds_mm"] as JObject;
        if (leftBounds == null || rightBounds == null) return false;
        return PointsEqual(leftBounds["min"] as JObject, rightBounds["min"] as JObject) && PointsEqual(leftBounds["max"] as JObject, rightBounds["max"] as JObject);
    }

    private static bool PointsEqual(JObject? left, JObject? right)
    {
        if (left == null || right == null) return false;
        return new[] { "x", "y", "z" }.All(axis => Math.Abs((left.Value<double?>(axis) ?? double.NaN) - (right.Value<double?>(axis) ?? double.NaN)) < 0.001);
    }

    private static JObject PointMm(XYZ point) => new() { ["x"] = Math.Round(point.X * 304.8, 3), ["y"] = Math.Round(point.Y * 304.8, 3), ["z"] = Math.Round(point.Z * 304.8, 3) };

    private static bool IsLength(FamilyParameter parameter)
    {
#if REVIT2019 || REVIT2020 || REVIT2021
#pragma warning disable CS0618
        return parameter.Definition.ParameterType == ParameterType.Length;
#pragma warning restore CS0618
#else
        return parameter.Definition.GetDataType() == SpecTypeId.Length;
#endif
    }

    private sealed class RejectBenchmarkFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = false; return false; }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues) { source = FamilySource.Family; overwriteParameterValues = false; return false; }
    }
}
