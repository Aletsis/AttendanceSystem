#!/usr/bin/env python3
"""Genera Docs/tecnico/02b-esquema-columnas.md a partir del ModelSnapshot de EF Core.

El ModelSnapshot es lo que EF Core considera el esquema actual del modelo; las migraciones se generan
comparándolo con el código. Este script NO interpreta el significado de las columnas: sólo transcribe
tipo, nulabilidad, longitud, valor por omisión, claves, índices y relaciones tal como aparecen ahí.

Uso (desde la raíz del repositorio):
    python Docs/tools/generar_esquema.py            # reescribe el archivo
    python Docs/tools/generar_esquema.py --check    # falla (código 1) si el archivo no está al día
"""
from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SNAPSHOT = ROOT / "src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs"
OUTPUT = ROOT / "Docs/tecnico/02b-esquema-columnas.md"
SNAPSHOT_REL = SNAPSHOT.relative_to(ROOT).as_posix()

# Tipos CLR que sin "?" no admiten NULL.
VALUE_TYPES = {"int", "long", "short", "bool", "uint", "double", "decimal", "float", "Guid", "DateTime",
               "DateOnly", "TimeSpan", "DateTimeOffset", "byte"}


@dataclass
class Column:
    name: str
    clr: str
    line: int
    column_type: str = ""
    column_name: str | None = None
    required: bool = False
    max_length: str | None = None
    default: str | None = None
    concurrency: bool = False
    fixed_length: bool = False
    identity: bool = False

    @property
    def db_name(self) -> str:
        return self.column_name or self.name

    def nullable(self, key_columns: set[str]) -> bool:
        if self.name in key_columns or self.required:
            return False
        if self.clr.endswith("?"):
            return True
        base = self.clr
        return base not in VALUE_TYPES


@dataclass
class Index:
    columns: list[str]
    unique: bool = False
    filter: str | None = None
    name: str | None = None


@dataclass
class ForeignKey:
    columns: list[str]
    principal: str
    principal_columns: list[str] | None
    on_delete: str | None
    name: str | None = None


@dataclass
class Table:
    entity: str
    line: int
    table: str | None = None
    owned_by: str | None = None
    columns: list[Column] = field(default_factory=list)
    key: list[str] = field(default_factory=list)
    indexes: list[Index] = field(default_factory=list)
    fks: list[ForeignKey] = field(default_factory=list)
    checks: list[tuple[str, str]] = field(default_factory=list)
    shares_owner_table: bool = False

    def column(self, prop: str) -> str:
        return next((c.db_name for c in self.columns if c.name == prop), prop)


def strings(text: str) -> list[str]:
    return re.findall(r'"((?:[^"\\]|\\.)*)"', text)


def unescape(s: str) -> str:
    return s.replace('\\"', '"')


def split_statements(body: str, start_line: int, var: str):
    """Devuelve (texto, línea) de cada sentencia `var.` de nivel superior del bloque."""
    statements = []
    depth = 0
    current = []
    current_line = None
    line = start_line
    i = 0
    while i < len(body):
        ch = body[i]
        if ch == "\n":
            line += 1
        if ch == '"':
            j = i + 1
            while j < len(body) and not (body[j] == '"' and body[j - 1] != "\\"):
                j += 1
            current.append(body[i:j + 1])
            i = j + 1
            continue
        if current_line is None and depth == 0 and body.startswith(var + ".", i):
            current_line = line
        if ch in "({":
            depth += 1
        elif ch in ")}":
            depth -= 1
        current.append(ch)
        if ch == ";" and depth == 0:
            text = "".join(current).strip()
            if current_line is not None and text.startswith(var + "."):
                statements.append((text, current_line))
            current = []
            current_line = None
        i += 1
    return statements


def block_after(text: str, open_index: int) -> tuple[str, int]:
    """Texto entre la llave en open_index y su cierre; devuelve (contenido, índice de cierre)."""
    depth = 0
    i = open_index
    while i < len(text):
        ch = text[i]
        if ch == '"':
            j = i + 1
            while j < len(text) and not (text[j] == '"' and text[j - 1] != "\\"):
                j += 1
            i = j + 1
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[open_index + 1:i], i
        i += 1
    raise ValueError("bloque sin cerrar")


def line_of(text: str, index: int) -> int:
    return text.count("\n", 0, index) + 1


def parse_block(entity: str, body: str, body_line: int, var: str, tables: dict[str, Table], owned_by: str | None = None,
                entity_line: int | None = None):
    table = tables.setdefault(entity, Table(entity=entity, line=entity_line or body_line, owned_by=owned_by))
    if owned_by and not table.owned_by:
        table.owned_by = owned_by

    # Tipos propios (OwnsOne / OwnsMany) se procesan como tablas aparte o columnas de la misma tabla.
    for m in re.finditer(rf'{var}\.Owns(One|Many)\("([^"]+)", "([^"]+)", (\w+) =>\s*\{{', body):
        inner, _ = block_after(body, m.end() - 1)
        inner_line = body_line + body.count("\n", 0, m.end())
        parse_block(m.group(2), inner, inner_line, m.group(4), tables, owned_by=entity,
                    entity_line=body_line + body.count("\n", 0, m.start()))
        if m.group(1) == "One":
            tables[m.group(2)].shares_owner_table = True

    # Quitar los bloques Owns* para no mezclar sus sentencias con las del dueño.
    cleaned = body
    for m in reversed(list(re.finditer(rf'{var}\.Owns(One|Many)\(', body))):
        brace = body.index("{", m.end())
        _, close = block_after(body, brace)
        end = body.index(";", close)
        segment = body[m.start():end + 1]
        cleaned = cleaned[:m.start()] + "\n" * segment.count("\n") + cleaned[end + 1:]

    to_table_inline = re.search(rf'{var}\.ToTable\("([^"]+)"[^;]*?=>\s*\{{', cleaned)
    if to_table_inline:
        inner, _ = block_after(cleaned, to_table_inline.end() - 1)
        for c in re.finditer(r'HasCheckConstraint\("([^"]+)", "((?:[^"\\]|\\.)*)"\)', inner):
            table.checks.append((c.group(1), unescape(c.group(2))))

    for stmt, line in split_statements(cleaned, body_line, var):
        head = stmt[len(var) + 1:]
        if head.startswith("Property<") or head.startswith("PrimitiveCollection<"):
            m = re.match(r'(?:Property|PrimitiveCollection)<(.+?)>\("([^"]+)"\)', head)
            clr, name = m.group(1), m.group(2)
            col = next((c for c in table.columns if c.name == name), None)
            if col is None:
                col = Column(name=name, clr=clr, line=line)
                table.columns.append(col)
            if (t := re.search(r'HasColumnType\("([^"]+)"\)', stmt)):
                col.column_type = t.group(1)
            if (t := re.search(r'HasColumnName\("([^"]+)"\)', stmt)):
                col.column_name = t.group(1)
            if ".IsRequired()" in stmt:
                col.required = True
            if (t := re.search(r'HasMaxLength\((\d+)\)', stmt)):
                col.max_length = t.group(1)
            if (t := re.search(r'HasDefaultValueSql\("([^"]+)"\)', stmt)):
                col.default = f"`{t.group(1)}` (SQL)"
            elif (arg := call_argument(stmt, "HasDefaultValue(")) is not None:
                col.default = format_default(arg)
            if "IsConcurrencyToken()" in stmt:
                col.concurrency = True
            if "IsFixedLength()" in stmt:
                col.fixed_length = True
        elif head.startswith("HasKey("):
            table.key = strings(head.split(")")[0])
        elif head.startswith("HasIndex("):
            cols = strings(re.match(r'HasIndex\(([^)]*)\)', head).group(1))
            idx = Index(columns=cols, unique=".IsUnique()" in stmt)
            if (t := re.search(r'HasFilter\("((?:[^"\\]|\\.)*)"\)', stmt)):
                idx.filter = unescape(t.group(1))
            if (t := re.search(r'HasDatabaseName\("([^"]+)"\)', stmt)):
                idx.name = t.group(1)
            table.indexes.append(idx)
        elif head.startswith("ToTable("):
            name = strings(head)[0] if strings(head) else None
            if name:
                table.table = name
        elif head.startswith("HasOne("):
            principal = strings(head)[0]
            fk_cols = strings(re.search(r'HasForeignKey\(([^)]*)\)', stmt).group(1))
            pk = re.search(r'HasPrincipalKey\(([^)]*)\)', stmt)
            on_delete = re.search(r'OnDelete\(DeleteBehavior\.(\w+)\)', stmt)
            cname = re.search(r'HasConstraintName\("([^"]+)"\)', stmt)
            table.fks.append(ForeignKey(fk_cols, principal, strings(pk.group(1)) if pk else None,
                                        on_delete.group(1) if on_delete else None,
                                        cname.group(1) if cname else None))
        elif head.startswith("WithOwner()"):
            fk = re.search(r'HasForeignKey\(([^)]*)\)', stmt)
            if fk and owned_by:
                table.fks.append(ForeignKey(strings(fk.group(1)), owned_by, None, "Cascade (tipo propio)"))

    # UseIdentityByDefaultColumn aparece como sentencia de NpgsqlPropertyBuilderExtensions.
    for m in re.finditer(r'UseIdentityByDefaultColumn\(\w+\.Property<\w+>\("([^"]+)"\)\)', body):
        for c in table.columns:
            if c.name == m.group(1):
                c.identity = True


def call_argument(stmt: str, opener: str) -> str | None:
    """Argumento completo de la primera llamada `opener` (paréntesis balanceados)."""
    start = stmt.find(opener)
    if start < 0:
        return None
    i = start + len(opener)
    depth = 1
    while i < len(stmt):
        if stmt[i] == "(":
            depth += 1
        elif stmt[i] == ")":
            depth -= 1
            if depth == 0:
                return stmt[start + len(opener):i].strip()
        i += 1
    return None


def format_default(raw: str) -> str:
    m = re.match(r'new TimeSpan\((\d+), (\d+), (\d+), (\d+), (\d+)\)', raw)
    if m:
        d, h, mi, s, _ = (int(x) for x in m.groups())
        return f"`{h + 24 * d:02d}:{mi:02d}:{s:02d}`"
    return f"`{raw}`"


def short(entity: str) -> str:
    return entity.split(".")[-1]


def parse() -> dict[str, Table]:
    text = SNAPSHOT.read_text(encoding="utf-8")
    tables: dict[str, Table] = {}
    for m in re.finditer(r'modelBuilder\.Entity\("([^"]+)", b =>\s*\{', text):
        body, _ = block_after(text, m.end() - 1)
        parse_block(m.group(1), body, line_of(text, m.end()), "b", tables, entity_line=line_of(text, m.start()))
    return tables


def render(tables: dict[str, Table]) -> str:
    by_entity = tables
    table_name = {e: (t.table or short(e)) for e, t in by_entity.items()}

    # Un tipo propio sin ToTable propio vive en la tabla del dueño (OwnsOne).
    physical: dict[str, list[Table]] = {}
    for e, t in by_entity.items():
        name = t.table or (table_name[t.owned_by] if t.owned_by else short(e))
        physical.setdefault(name, []).append(t)

    out = []
    out.append("# Esquema de columnas (generado)\n")
    out.append("> **No editar a mano.** Este archivo lo genera `Docs/tools/generar_esquema.py` a partir de "
               f"[`{SNAPSHOT.name}`](../../{SNAPSHOT_REL}). Para regenerarlo:\n>\n"
               "> ```bash\n> python Docs/tools/generar_esquema.py\n> ```\n>\n"
               "> Transcribe tipo, nulabilidad, longitud y valor por omisión tal como los declara el modelo de EF Core. "
               "El significado de cada tabla y de las columnas no evidentes está en [02-base-de-datos.md](02-base-de-datos.md).\n")
    out.append("Columnas de la tabla: **Columna** es el nombre físico en PostgreSQL (entre comillas dobles en SQL, "
               "porque tiene mayúsculas); **Nulo** indica si admite `NULL`; **Omisión** es el `DEFAULT` que crea la "
               "migración (vacío = sin valor por omisión en la base; el valor lo pone la aplicación).\n")

    names = sorted(physical, key=str.lower)
    out.append(f"Tablas descritas: {len(names)} (todas en el esquema `public`).\n")
    out.append("| Tabla | Entidad en el código | Línea en el snapshot |\n|---|---|---|")
    for n in names:
        for t in physical[n]:
            out.append(f"| [`{n}`](#{anchor(n)}) | `{short(t.entity)}`{' (propia de `' + short(t.owned_by) + '`)' if t.owned_by else ''} "
                       f"| [{t.line}](../../{SNAPSHOT_REL}#L{t.line}) |")
    out.append("")

    for n in names:
        parts = physical[n]
        out.append(f"## {n}\n")
        for t in parts:
            label = f"`{t.entity}`" + (f" — tipo propio de `{short(t.owned_by)}`" if t.owned_by else "")
            out.append(f"Entidad: {label} · [snapshot línea {t.line}](../../{SNAPSHOT_REL}#L{t.line})\n")
        key_cols = set()
        for t in parts:
            if not t.shares_owner_table:
                key_cols |= {t.column(k) for k in t.key}
        out.append("| Columna | Tipo | Nulo | Omisión | Notas |\n|---|---|---|---|---|")
        seen = set()
        for t in parts:
            for c in t.columns:
                if t.shares_owner_table and c.name in t.key:
                    continue  # es la PK del dueño (división de tabla), no una columna propia
                if c.db_name in seen:
                    continue
                seen.add(c.db_name)
                notes = []
                if c.db_name in key_cols:
                    notes.append("PK")
                if c.concurrency:
                    notes.append("token de concurrencia" + (" (columna de sistema `xmin`)" if c.db_name == "xmin" else ""))
                if c.identity:
                    notes.append("identity")
                if c.column_name and c.column_name != c.name:
                    notes.append(f"propiedad `{c.name}`")
                if c.fixed_length:
                    notes.append("longitud fija")
                nullable = c.nullable({k for k in t.key})
                out.append(f"| `{c.db_name}` | `{c.column_type}` | {'sí' if nullable else 'no'} | {c.default or ''} | {', '.join(notes)} |")
        out.append("")
        idx = [(t, i) for t in parts for i in t.indexes]
        if idx:
            out.append("Índices:\n")
            for t, i in idx:
                desc = ", ".join(f"`{t.column(c)}`" for c in i.columns)
                extra = []
                if i.unique:
                    extra.append("**único**")
                if i.filter:
                    extra.append(f"filtro `{i.filter}`")
                if i.name:
                    extra.append(f"nombre `{i.name}`")
                out.append(f"- ({desc})" + (" — " + ", ".join(extra) if extra else ""))
            out.append("")
        fks = [(t, f) for t in parts for f in t.fks if not t.shares_owner_table]
        if fks:
            out.append("Llaves foráneas declaradas:\n")
            for t, f in fks:
                target = table_name.get(f.principal, short(f.principal))
                principal = by_entity.get(f.principal)
                pcols = (f" ({', '.join(f'`{principal.column(c) if principal else c}`' for c in f.principal_columns)})"
                         if f.principal_columns else "")
                out.append(f"- ({', '.join(f'`{t.column(c)}`' for c in f.columns)}) → `{target}`{pcols}"
                           + (f", al borrar: {f.on_delete}" if f.on_delete else "")
                           + (f", nombre `{f.name}`" if f.name else ""))
            out.append("")
        checks = [c for t in parts for c in t.checks]
        if checks:
            out.append("Restricciones CHECK:\n")
            for name, expr in checks:
                out.append(f"- `{name}`: `{expr}`")
            out.append("")
    return "\n".join(out).rstrip() + "\n"


def anchor(name: str) -> str:
    return re.sub(r"[^a-z0-9_-]", "", name.lower())


def main() -> int:
    content = render(parse())
    if "--check" in sys.argv:
        current = OUTPUT.read_text(encoding="utf-8") if OUTPUT.exists() else ""
        if current != content:
            print(f"{OUTPUT.relative_to(ROOT)} no coincide con el ModelSnapshot; ejecute: python Docs/tools/generar_esquema.py")
            return 1
        print("Esquema documentado al día.")
        return 0
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(content, encoding="utf-8", newline="\n")
    print(f"Escrito {OUTPUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
