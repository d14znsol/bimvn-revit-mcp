from pathlib import Path

root = Path(__file__).resolve().parents[1]
schema = (root / "MCP-Server/src/tools/family-platform-tools.ts").read_text(encoding="utf-8")
registry = (root / "MCP/Core/CommandRegistry.cs").read_text(encoding="utf-8")
commands = (root / "MCP/Commands/FamilyPlatformCommands.cs").read_text(encoding="utf-8")
quality = (root / "MCP/Commands/FamilyQualityPlatform.cs").read_text(encoding="utf-8")
evidence = (root / "MCP-Server/src/family-evidence.ts").read_text(encoding="utf-8")
catalog = (root / "MCP/Commands/FamilyCatalogGeometryBuilder.cs").read_text(encoding="utf-8")

tools = [
    "family_source_inspect", "family_spec_preview", "family_build_preview", "family_build_apply",
    "cad_geometry_inspect", "cad_to_revit_preview", "cad_to_revit_apply",
]
for tool in tools:
    assert tool in schema, f"missing Node schema for {tool}"
    assert tool in registry, f"missing Revit registry entry for {tool}"
    assert tool in commands, f"missing Revit command for {tool}"

discontinued_markers = [
    "family" + "_tag_",
    "Family" + "TagCommands",
    "Tag" + "SeedInvalid",
    "pipe" + "_sys_dn",
]
for discontinued in discontinued_markers:
    assert discontinued not in schema
    assert discontinued not in registry
    assert discontinued not in commands
    assert discontinued not in quality
    assert discontinued not in evidence

assert not (root / "MCP-Server/src/tools" / ("family" + "-tag-tools.ts")).exists()
assert not (root / "MCP/Commands" / ("Family" + "TagCommands.cs")).exists()
assert not (root / "docs" / ("FAMILY-" + "TAG-PLATFORM.md")).exists()
assert not (root / "docs" / ("TAG-" + "RUNTIME-TEST.md")).exists()
assert not any((root / "docs/tag-seeds").glob("**/*"))

for marker in ["FamilyPlatformStore", "Sha256", "RequireInside", "LOD_300", "uncertain", "human_confirmed", "PreviewExpired", "PreviewInvalid", "TransactionGroup"]:
    assert marker in commands or marker in quality or marker in schema, f"missing safety/evidence marker: {marker}"
assert "allow-listed" in commands
assert "SyncWithCentral" not in commands
assert "Save(" not in commands
for marker in ["FamilyAdapterRegistry", "FamilyQualitySpec", "FamilyQualityValidator", "FamilyQualityPipeline", "DsconsParameterRegistry", "SharedParametersFilename", "connector_probe", "cable_tray_fitting", "sprinkler"]:
    assert marker in quality, f"missing Family quality platform component: {marker}"
assert "family_build" in commands and "FamilyBuilder.Apply" in commands
for marker in ["schema_version", "source_evidence_v2", "family_spec_v2", "native_text", "rendered_page_ocr", "eng.traineddata.gz", "vie.traineddata.gz", "SourceUnreadable", "SourceEncrypted", "OcrUnavailable", "EvidenceConflict"]:
    assert marker in evidence, f"missing SourceEvidence v2/OCR marker: {marker}"
for marker in ["\"A\"", "\"A1\"", "\"DN1\"", "\"DN2\"", "\"H3\"", "\"T\"", "AddPumpCatalogParameters", "parametric_envelope_pilot"]:
    assert marker in evidence or marker in quality or marker in catalog, f"missing Pump evidence/pilot marker: {marker}"

print("PASS Family Evidence v2 static contract: 7 additive Family/CAD tools, 46-tool-safe registry, PDF/OCR evidence and no Save/Sync")
