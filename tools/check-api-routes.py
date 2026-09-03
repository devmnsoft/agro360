#!/usr/bin/env python3
"""Fail when attribute-routed API controllers expose duplicate operations."""

from __future__ import annotations

import re
import sys
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CONTROLLERS = ROOT / "src" / "Hosts" / "Agro360.Api" / "Controllers"
CLASS = re.compile(
    r"(?P<attributes>(?:\s*\[[^\]]+\]\s*)+)"
    r"(?:public\s+)?(?:sealed\s+)?class\s+(?P<name>\w+Controller)\b"
)
ROUTE = re.compile(r'\bRoute\("(?P<route>[^"]*)"\)')
ACTION = re.compile(
    r'\[Http(?P<verb>Get|Post|Put|Patch|Delete)(?:\("(?P<route>[^"]*)"\))?'
    r'(?:(?!\[Http).)*?\bpublic\s+(?:async\s+)?[^;{=]+?\s+(?P<name>\w+)\s*\(',
    re.DOTALL,
)


def combine(prefix: str, suffix: str) -> str:
    if suffix.startswith("/") or suffix.startswith("~/"):
        path = suffix.removeprefix("~/")
    else:
        path = "/".join(part.strip("/") for part in (prefix, suffix) if part)
    return "/" + re.sub(r"/+", "/", path).strip("/").lower()


def main() -> int:
    operations: dict[tuple[str, str], list[str]] = defaultdict(list)
    controllers: dict[str, list[str]] = defaultdict(list)
    errors: list[str] = []

    for source in sorted(CONTROLLERS.glob("*.cs")):
        text = source.read_text(encoding="utf-8")
        classes = list(CLASS.finditer(text))
        for index, controller in enumerate(classes):
            start = controller.start()
            end = classes[index + 1].start() if index + 1 < len(classes) else len(text)
            attributes = controller.group("attributes")
            owner_type = f"{source.relative_to(ROOT)}:{controller.group('name')}"
            controllers[controller.group("name")].append(owner_type)
            if attributes.count("ApiController") > 1:
                errors.append(f"[ApiController] duplicado: {owner_type}")
            route_match = ROUTE.search(attributes)
            prefix = route_match.group("route") if route_match else ""
            prefix = prefix.replace("[controller]", controller.group("name")[:-10])
            if prefix.count("{") != prefix.count("}"):
                errors.append(f"rota de controller malformada {prefix}: {owner_type}")

            for action in ACTION.finditer(text, controller.end(), end):
                suffix = action.group("route") or ""
                path = combine(prefix, suffix)
                owner = f"{source.relative_to(ROOT)}:{controller.group('name')}.{action.group('name')}"
                if path.count("{") != path.count("}"):
                    errors.append(f"rota malformada {action.group('verb').upper()} {path}: {owner}")
                if "{action}" in path or "{controller}" in path:
                    errors.append(f"parâmetro de rota reservado em {path}: {owner}")
                operations[(action.group("verb").upper(), path)].append(owner)

    for name, owners in sorted(controllers.items()):
        if len(owners) > 1:
            errors.append(f"controller duplicado {name}: " + ", ".join(owners))

    for (verb, path), owners in sorted(operations.items()):
        if len(owners) > 1:
            errors.append(f"rota duplicada {verb} {path}: " + ", ".join(owners))

    if errors:
        print("Falha na auditoria de rotas:\n- " + "\n- ".join(errors), file=sys.stderr)
        return 1
    print(f"OK: {len(operations)} operações HTTP possuem método + path únicos.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
