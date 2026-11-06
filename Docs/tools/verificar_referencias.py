#!/usr/bin/env python3
"""Comprueba los enlaces de la documentación hacia el código.

Para cada enlace Markdown relativo de los archivos de Docs/tecnico/ verifica que el archivo exista y, si el enlace
lleva #Lnn o #Lnn-Lmm, que esas líneas existan. No comprueba que la línea diga lo que el documento afirma: eso se
revisa a mano en el PR (el script sólo evita citas rotas).

Uso (desde la raíz del repositorio):
    python Docs/tools/verificar_referencias.py
Sale con código 1 si encuentra referencias rotas.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "Docs" / "tecnico"
LINK = re.compile(r"\]\(([^)\s]+)\)")


def main() -> int:
    errors = []
    checked = 0
    for doc in sorted(DOCS.glob("*.md")):
        for lineno, line in enumerate(doc.read_text(encoding="utf-8").splitlines(), 1):
            for target in LINK.findall(line):
                if re.match(r"^[a-z]+://", target) or target.startswith("#"):
                    continue
                path_part, _, fragment = target.partition("#")
                path = (doc.parent / path_part).resolve()
                checked += 1
                where = f"{doc.relative_to(ROOT).as_posix()}:{lineno}"
                if not path.exists():
                    errors.append(f"{where}: no existe {path_part}")
                    continue
                m = re.fullmatch(r"L(\d+)(?:-L(\d+))?", fragment or "")
                if m and path.is_file():
                    total = len(path.read_text(encoding="utf-8", errors="replace").splitlines())
                    first, last = int(m.group(1)), int(m.group(2) or m.group(1))
                    if first > last or last > total:
                        errors.append(f"{where}: {path_part}#{fragment} fuera de rango (el archivo tiene {total} líneas)")
    for e in errors:
        print(e)
    print(f"{checked} enlaces revisados, {len(errors)} con error.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
