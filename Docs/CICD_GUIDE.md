# Guía de CI/CD para AttendanceSystem

Este documento describe la arquitectura y operación del proceso de **Integración Continua (CI)** y **Entrega Continua (CD)** implementado con **GitHub Actions**.

---

## 1. Visión General del Pipeline

El flujo de CI/CD está dividido en dos workflows principales ubicados en `.github/workflows/`:

1. **`ci.yml` (Integración Continua)**: Valida la calidad, compilación y pruebas de cada cambio de código.
2. **`release.yml` (Entrega/Despliegue Continuo)**: Automatiza la compilación de binarios (`win-x64` y `win-x86`), descarga de prerrequisitos, generación del instalador con Inno Setup y publicación del Release en GitHub.

---

## 2. Flujo de Integración Continua (CI)

### Disparadores
- Cualquier `push` a las ramas `main`, `develop`, `release/**`, `feature/**`, `fix/**`.
- Cualquier `pull_request` dirigido a `main` o `develop`.
- Ejecución manual desde la pestaña **Actions** en GitHub (`workflow_dispatch`).

### Pasos que ejecuta:
1. **Entorno**: Se ejecuta en un runner `windows-latest` para dar soporte a componentes Windows Desktop (.NET 10 + WPF SDK).
2. **Setup .NET 10**: Configura el SDK oficial de .NET 10.
3. **Cache NuGet**: Guarda en caché los paquetes descargados para acelerar las ejecuciones.
4. **Restauración y Compilación**: Compila la solución completa `AttendanceSystem.sln` en modo `Release`.
5. **Formato de Código**: Verifica que el código cumpla con las reglas de estilo de `dotnet format`.
6. **Ejecución de Pruebas Unitarias**: Ejecuta los proyectos de pruebas:
   - `AttendanceSystem.Domain.UnitTests`
   - `AttendanceSystem.Application.UnitTests`
   - `AttendanceSystem.Infrastructure.Tests`
7. **Cobertura y Reportes**: Recolecta cobertura mediante Coverlet (`XPlat Code Coverage`) y sube los resultados `.trx` como artefactos descargables.

---

## 3. Flujo de Entrega Continua y Releases (CD)

### Disparadores
- **Creación de Tags de versión**: Al hacer push de un tag con formato `v*.*.*` (ej. `v2.2.0`).
- **Ejecución manual**: Desde GitHub Actions > `CD - Build & Publish Release`, indicando la versión deseada y si es *Draft* o *Pre-release*.

### Pasos que ejecuta:
1. **Compilación de la Suite**:
   - `AttendanceSystem.Blazor.Server`: Compilado como autocontenido `win-x64`.
   - `AttendanceSystem.ZKTeco.Service`: Compilado como autocontenido `win-x86` con `PublishSingleFile=false`.
   - DLLs del SDK ZKTeco: Copiadas y validadas en la raíz del servicio.
2. **Descarga de Prerrequisitos**:
   - Ejecuta `Deploy/Download-Prerequisites.ps1` para descargar de forma silenciosa el instalador de **PostgreSQL 16** y el **ASP.NET Core Hosting Bundle** de Microsoft.
3. **Compilación del Instalador Inno Setup**:
   - Inno Setup se instala automáticamente en el runner Windows vía Chocolatey.
   - Ejecuta `ISCC.exe` inyectando la versión dinámica `/DMyAppVersion=X.Y.Z`.
   - Genera `AttendanceSystem_Setup_vX.Y.Z.exe`.
4. **Empaquetado Portable**:
   - Genera archivos `.zip` independientes para Web y Servicio.
5. **Publicación en GitHub Releases**:
   - Crea el release en GitHub con notas automáticas y adjunta los binarios para descarga directa.

---

## 4. Guía Paso a Paso: Cómo Publicar una Nueva Versión

### Opción A: Mediante Git Tag (Recomendada)
1. Actualiza la versión en [Directory.Build.props](../Directory.Build.props) si es necesario.
2. Haz commit y merge de tus cambios a la rama `main`.
3. Crea y sube el tag correspondiente:
   ```bash
   git tag v2.2.0
   git push origin v2.2.0
   ```
4. El workflow `release.yml` iniciará automáticamente. Al terminar, la nueva versión estará disponible en la sección **Releases** del repositorio con su instalador `.exe`.

### Opción B: Ejecución Manual desde GitHub UI
1. Ve a la pestaña **Actions** en el repositorio GitHub.
2. Selecciona el workflow **CD - Build & Publish Release**.
3. Haz clic en **Run workflow**.
4. Ingresa la versión (ej. `2.2.0`) y haz clic en **Run workflow**.

---

## 5. Recomendaciones de Buenas Prácticas

### Protección de Ramas (Branch Protection Rules)
Para asegurar que ningún código roto entre a producción, se recomienda configurar las siguientes reglas en GitHub (**Settings > Branches > Branch protection rules** para `main` y `develop`):
- ✅ **Require a pull request before merging**: Exigir revisión antes de integrar.
- ✅ **Require status checks to pass before merging**:
  - Seleccionar el check `Build & Run Tests` del workflow de CI.
- ✅ **Require branches to be up to date before merging**.
- ❌ No permitir push directo a `main`.

---

## 6. Scripts Auxiliares Locales

- [`Prepare-Release.ps1`](../Prepare-Release.ps1):
  Compila y organiza los artefactos en local:
  ```powershell
  .\Prepare-Release.ps1 -Configuration Release -OutputDir ".\Release"
  ```
- [`Deploy/Download-Prerequisites.ps1`](../Deploy/Download-Prerequisites.ps1):
  Descarga PostgreSQL y el Hosting Bundle para poder compilar el instalador Inno Setup localmente sin descargas manuales:
  ```powershell
  .\Deploy\Download-Prerequisites.ps1
  ```
