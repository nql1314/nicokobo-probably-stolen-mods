import hashlib
import json
import pathlib
import re
import sys

import UnityPy

if len(sys.argv) != 7:
    raise RuntimeError("Usage: patch_product.py <clone settings> <product> <new run root> <company> <formal product> <version>")
target = pathlib.Path(sys.argv[1]).resolve()
product = sys.argv[2]
workspace = pathlib.Path(sys.argv[3]).resolve()
company_name, formal_product, version = sys.argv[4:7]
if (not re.fullmatch(r"native-[0-9a-f]{32}", workspace.name)
        or not (workspace / "ownership.json").is_file()
        or target != workspace / "game" / "Probably Stolen_Data" / "globalgamemanagers"):
    raise RuntimeError("Only a copied task-workspace globalgamemanagers may be patched")
if not re.fullmatch(r"NicoT_[0-9a-f]{9}", product):
    raise RuntimeError("Unexpected test product identity")


def settings(path):
    env = UnityPy.load(str(path))
    rows = [obj.read_typetree() for obj in env.objects if obj.type.name == "PlayerSettings"]
    if len(rows) != 1:
        raise RuntimeError("Expected one PlayerSettings object")
    row = rows[0]
    return {key: row.get(key) for key in ("companyName", "productName", "bundleVersion")}


before = settings(target)
if before != {"companyName": company_name, "productName": formal_product, "bundleVersion": version}:
    raise RuntimeError(f"PlayerSettings baseline changed: {before}")
data = target.read_bytes()
old = before["productName"].encode("utf-8")
new = product.encode("utf-8")
company = before["companyName"].encode("utf-8")
matches = [match.start() for match in re.finditer(re.escape(old), data)
           if company in data[max(0, match.start() - 128):match.start()]]
if len(new) != len(old) or len(matches) != 1:
    raise RuntimeError("Product name must have one equal-length, company-anchored occurrence")
offset = matches[0]
patched = data[:offset] + new + data[offset + len(old):]
if len(data) != len(patched):
    raise RuntimeError("Serialized asset length changed")
target.write_bytes(patched)
after = settings(target)
if after != {**before, "productName": product}:
    raise RuntimeError("Patched PlayerSettings did not read back correctly")
evidence = {"target": str(target), "before": before, "after": after, "offset": offset,
            "changedByteRangeLength": len(old), "beforeSha256": hashlib.sha256(data).hexdigest(),
            "afterSha256": hashlib.sha256(patched).hexdigest()}
target.with_name("product-isolation.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
app_info = target.with_name("app.info")
app_data = app_info.read_bytes()
if app_data.decode("utf-8-sig").splitlines() != [company_name, formal_product]:
    raise RuntimeError("Early bootstrap app.info identity changed")
app_patched = app_data.replace(old, new)
if app_patched.decode("utf-8-sig").splitlines() != [company_name, product]:
    raise RuntimeError("Early bootstrap app.info identity was not isolated")
app_info.write_bytes(app_patched)
ownership = json.loads((workspace / "ownership.json").read_text(encoding="utf-8-sig"))
(workspace / "app-info-isolation.json").write_text(json.dumps({
    "environmentRunId": ownership["runId"], "target": str(app_info),
    "beforeSha256": hashlib.sha256(app_data).hexdigest(),
    "afterSha256": hashlib.sha256(app_patched).hexdigest(),
    "company": company_name, "beforeProduct": formal_product, "afterProduct": product
}, indent=2), encoding="utf-8")
print(json.dumps(evidence, ensure_ascii=False))
