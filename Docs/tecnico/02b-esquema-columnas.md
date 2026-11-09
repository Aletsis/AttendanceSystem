# Esquema de columnas (generado)

> **No editar a mano.** Este archivo lo genera `Docs/tools/generar_esquema.py` a partir de [`AttendanceDbContextModelSnapshot.cs`](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs). Para regenerarlo:
>
> ```bash
> python Docs/tools/generar_esquema.py
> ```
>
> Transcribe tipo, nulabilidad, longitud y valor por omisión tal como los declara el modelo de EF Core. El significado de cada tabla y de las columnas no evidentes está en [02-base-de-datos.md](02-base-de-datos.md).

Columnas de la tabla: **Columna** es el nombre físico en PostgreSQL (entre comillas dobles en SQL, porque tiene mayúsculas); **Nulo** indica si admite `NULL`; **Omisión** es el `DEFAULT` que crea la migración (vacío = sin valor por omisión en la base; el valor lo pone la aplicación).

Tablas descritas: 41 (todas en el esquema `public`).

| Tabla | Entidad en el código | Línea en el snapshot |
|---|---|---|
| [`AdmsCommands`](#admscommands) | `AdmsCommand` | [951](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L951) |
| [`AdmsIngestionIncidents`](#admsingestionincidents) | `AdmsIngestionIncident` | [1014](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1014) |
| [`AspNetRoleClaims`](#aspnetroleclaims) | `IdentityRoleClaim<string>` | [2104](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2104) |
| [`AspNetRoles`](#aspnetroles) | `IdentityRole` | [2078](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2078) |
| [`AspNetUserClaims`](#aspnetuserclaims) | `IdentityUserClaim<string>` | [2129](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2129) |
| [`AspNetUserLogins`](#aspnetuserlogins) | `IdentityUserLogin<string>` | [2154](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2154) |
| [`AspNetUserRoles`](#aspnetuserroles) | `IdentityUserRole<string>` | [2176](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2176) |
| [`AspNetUsers`](#aspnetusers) | `ApplicationUser` | [1936](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1936) |
| [`AspNetUserTokens`](#aspnetusertokens) | `IdentityUserToken<string>` | [2191](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2191) |
| [`AttendanceAdjustmentHistory`](#attendanceadjustmenthistory) | `AttendanceAdjustmentHistory` | [508](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L508) |
| [`AttendanceAdjustments`](#attendanceadjustments) | `AttendanceAdjustment` | [373](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L373) |
| [`AttendanceConsolidationRequests`](#attendanceconsolidationrequests) | `AttendanceConsolidationRequest` | [563](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L563) |
| [`AttendancePeriodClosures`](#attendanceperiodclosures) | `AttendancePeriodClosure` | [1484](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1484) |
| [`AttendanceRecords`](#attendancerecords) | `AttendanceRecord` | [26](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L26) |
| [`Branches`](#branches) | `Branch` | [335](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L335) |
| [`DailyAttendances`](#dailyattendances) | `DailyAttendance` | [632](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L632) |
| [`DepartmentPositions`](#departmentpositions) | `DepartmentPositions` | [2063](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2063) |
| [`Departments`](#departments) | `Department` | [932](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L932) |
| [`DeviceClockIncidents`](#deviceclockincidents) | `DeviceClockIncident` | [121](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L121) |
| [`Devices`](#devices) | `Device` | [1049](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1049) |
| [`Devices`](#devices) | `DeviceHardwareInfo` (propia de `Device`) | [2232](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2232) |
| [`DownloadLogs`](#downloadlogs) | `DownloadLog` | [1126](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1126) |
| [`EmployeeAuditLogs`](#employeeauditlogs) | `EmployeeAuditLog` | [1321](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1321) |
| [`EmployeeEmploymentPeriods`](#employeeemploymentperiods) | `EmploymentPeriod` (propia de `Employee`) | [2343](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2343) |
| [`EmployeeFingerprints`](#employeefingerprints) | `EmployeeFingerprint` (propia de `Employee`) | [2313](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2313) |
| [`Employees`](#employees) | `Employee` | [1188](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1188) |
| [`EmployeeScheduleAssignments`](#employeescheduleassignments) | `ScheduleAssignment` (propia de `Employee`) | [2368](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2368) |
| [`EmployeeStatusPeriods`](#employeestatusperiods) | `StatusPeriod` (propia de `Employee`) | [2403](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2403) |
| [`ExternalAttendanceLogs`](#externalattendancelogs) | `ExternalAttendanceLog` | [1428](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1428) |
| [`ExternalEmployees`](#externalemployees) | `ExternalEmployee` | [1364](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1364) |
| [`OutboxMessages`](#outboxmessages) | `OutboxMessage` | [2013](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2013) |
| [`Positions`](#positions) | `Position` | [1524](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1524) |
| [`PunchAssignments`](#punchassignments) | `PunchAssignment` | [889](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L889) |
| [`PunchTimeCorrectionEntries`](#punchtimecorrectionentries) | `PunchTimeCorrectionEntry` | [201](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L201) |
| [`PunchTimeCorrections`](#punchtimecorrections) | `PunchTimeCorrection` | [155](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L155) |
| [`QuarantinedPunches`](#quarantinedpunches) | `QuarantinedPunch` | [256](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L256) |
| [`RotationPatterns`](#rotationpatterns) | `RotationPattern` | [1624](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1624) |
| [`ShiftDays`](#shiftdays) | `ShiftDay` (propia de `Shift`) | [2442](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2442) |
| [`ShiftRosters`](#shiftrosters) | `ShiftRoster` | [1661](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1661) |
| [`Shifts`](#shifts) | `Shift` | [1551](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1551) |
| [`SystemAlerts`](#systemalerts) | `SystemAlert` | [1704](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1704) |
| [`SystemConfiguration`](#systemconfiguration) | `SystemConfiguration` | [1732](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1732) |

## AdmsCommands

Entidad: `AttendanceSystem.Domain.Aggregates.DeviceAggregate.AdmsCommand` · [snapshot línea 951](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L951)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `CommandId` | `character varying(50)` | no |  |  |
| `CommandText` | `character varying(2000)` | no |  |  |
| `ConfirmedAtUtc` | `timestamp with time zone` | sí |  |  |
| `CreatedAtUtc` | `timestamp with time zone` | no |  |  |
| `DownloadLogId` | `uuid` | sí |  |  |
| `ErrorMessage` | `text` | sí |  |  |
| `IsDestructive` | `boolean` | no |  |  |
| `ResultPayload` | `text` | sí |  |  |
| `RetryCount` | `integer` | no | `0` |  |
| `ReturnCode` | `integer` | sí |  |  |
| `SentAtUtc` | `timestamp with time zone` | sí |  |  |
| `SerialNumber` | `character varying(100)` | no |  |  |
| `Status` | `integer` | no |  |  |

Índices:

- (`CreatedAtUtc`)
- (`SerialNumber`, `CommandId`)
- (`SerialNumber`, `Status`)

## AdmsIngestionIncidents

Entidad: `AttendanceSystem.Domain.Aggregates.DeviceAggregate.AdmsIngestionIncident` · [snapshot línea 1014](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1014)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `CreatedAtUtc` | `timestamp with time zone` | no |  |  |
| `Error` | `text` | no |  |  |
| `RawData` | `text` | no |  |  |
| `Resolved` | `boolean` | no | `false` |  |
| `SerialNumber` | `character varying(100)` | no |  |  |

Índices:

- (`CreatedAtUtc`)
- (`SerialNumber`, `Resolved`)

## AspNetRoleClaims

Entidad: `Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>` · [snapshot línea 2104](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2104)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `integer` | no |  | PK, identity |
| `ClaimType` | `text` | sí |  |  |
| `ClaimValue` | `text` | sí |  |  |
| `RoleId` | `text` | no |  |  |

Índices:

- (`RoleId`)

Llaves foráneas declaradas:

- (`RoleId`) → `AspNetRoles`, al borrar: Cascade

## AspNetRoles

Entidad: `Microsoft.AspNetCore.Identity.IdentityRole` · [snapshot línea 2078](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2078)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `text` | no |  | PK |
| `ConcurrencyStamp` | `text` | sí |  | token de concurrencia |
| `Name` | `character varying(256)` | sí |  |  |
| `NormalizedName` | `character varying(256)` | sí |  |  |

Índices:

- (`NormalizedName`) — **único**, nombre `RoleNameIndex`

## AspNetUserClaims

Entidad: `Microsoft.AspNetCore.Identity.IdentityUserClaim<string>` · [snapshot línea 2129](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2129)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `integer` | no |  | PK, identity |
| `ClaimType` | `text` | sí |  |  |
| `ClaimValue` | `text` | sí |  |  |
| `UserId` | `text` | no |  |  |

Índices:

- (`UserId`)

Llaves foráneas declaradas:

- (`UserId`) → `AspNetUsers`, al borrar: Cascade

## AspNetUserLogins

Entidad: `Microsoft.AspNetCore.Identity.IdentityUserLogin<string>` · [snapshot línea 2154](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2154)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `LoginProvider` | `text` | no |  | PK |
| `ProviderKey` | `text` | no |  | PK |
| `ProviderDisplayName` | `text` | sí |  |  |
| `UserId` | `text` | no |  |  |

Índices:

- (`UserId`)

Llaves foráneas declaradas:

- (`UserId`) → `AspNetUsers`, al borrar: Cascade

## AspNetUserRoles

Entidad: `Microsoft.AspNetCore.Identity.IdentityUserRole<string>` · [snapshot línea 2176](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2176)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `UserId` | `text` | no |  | PK |
| `RoleId` | `text` | no |  | PK |

Índices:

- (`RoleId`)

Llaves foráneas declaradas:

- (`RoleId`) → `AspNetRoles`, al borrar: Cascade
- (`UserId`) → `AspNetUsers`, al borrar: Cascade

## AspNetUsers

Entidad: `AttendanceSystem.Domain.Entities.ApplicationUser` · [snapshot línea 1936](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1936)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `text` | no |  | PK |
| `AccessFailedCount` | `integer` | no |  |  |
| `ConcurrencyStamp` | `text` | sí |  | token de concurrencia |
| `CreatedAt` | `timestamp with time zone` | no |  |  |
| `Email` | `character varying(256)` | sí |  |  |
| `EmailConfirmed` | `boolean` | no |  |  |
| `FullName` | `text` | no |  |  |
| `IsActive` | `boolean` | no |  |  |
| `LastModifiedAt` | `timestamp with time zone` | sí |  |  |
| `LockoutEnabled` | `boolean` | no |  |  |
| `LockoutEnd` | `timestamp with time zone` | sí |  |  |
| `NormalizedEmail` | `character varying(256)` | sí |  |  |
| `NormalizedUserName` | `character varying(256)` | sí |  |  |
| `PasswordHash` | `text` | sí |  |  |
| `PhoneNumber` | `text` | sí |  |  |
| `PhoneNumberConfirmed` | `boolean` | no |  |  |
| `SecurityStamp` | `text` | sí |  |  |
| `TwoFactorEnabled` | `boolean` | no |  |  |
| `UserName` | `character varying(256)` | sí |  |  |

Índices:

- (`NormalizedEmail`) — nombre `EmailIndex`
- (`NormalizedUserName`) — **único**, nombre `UserNameIndex`

## AspNetUserTokens

Entidad: `Microsoft.AspNetCore.Identity.IdentityUserToken<string>` · [snapshot línea 2191](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2191)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `UserId` | `text` | no |  | PK |
| `LoginProvider` | `text` | no |  | PK |
| `Name` | `text` | no |  | PK |
| `Value` | `text` | sí |  |  |

Llaves foráneas declaradas:

- (`UserId`) → `AspNetUsers`, al borrar: Cascade

## AttendanceAdjustmentHistory

Entidad: `AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate.AttendanceAdjustmentHistory` · [snapshot línea 508](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L508)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `Action` | `character varying(50)` | no |  |  |
| `AdjustmentId` | `uuid` | no |  |  |
| `After` | `character varying(2000)` | no |  |  |
| `Before` | `character varying(2000)` | no |  |  |
| `ChangedAt` | `timestamp with time zone` | no |  |  |
| `ChangedBy` | `character varying(100)` | no |  |  |
| `Date` | `date` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `Reason` | `character varying(500)` | sí |  |  |
| `Revision` | `integer` | no |  |  |

Índices:

- (`AdjustmentId`)
- (`EmployeeId`, `Date`, `Revision`)

## AttendanceAdjustments

Entidad: `AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate.AttendanceAdjustment` · [snapshot línea 373](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L373)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `AuthorizedOvertimeForRawMinutes` | `integer` | sí |  |  |
| `AuthorizedOvertimeMinutes` | `integer` | sí |  |  |
| `Block1CheckOutOverride` | `integer` | no | `0` |  |
| `Block1CheckOutRecordId` | `uuid` | sí |  |  |
| `Block1CheckOutTimeOverride` | `timestamp with time zone` | sí |  |  |
| `Block2CheckInOverride` | `integer` | no | `0` |  |
| `Block2CheckInRecordId` | `uuid` | sí |  |  |
| `Block2CheckInTimeOverride` | `timestamp with time zone` | sí |  |  |
| `CalculateOvertimeBeforeEntryOverride` | `boolean` | sí |  |  |
| `CheckInOverride` | `integer` | no | `0` |  |
| `CheckInRecordId` | `uuid` | sí |  |  |
| `CheckInTimeOverride` | `timestamp with time zone` | sí |  |  |
| `CheckOutOverride` | `integer` | no | `0` |  |
| `CheckOutRecordId` | `uuid` | sí |  |  |
| `CheckOutTimeOverride` | `timestamp with time zone` | sí |  |  |
| `CreatedAt` | `timestamp with time zone` | no |  |  |
| `CreatedBy` | `character varying(100)` | no |  |  |
| `Date` | `date` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `ExcludedPunches` | `uuid[]` | no |  |  |
| `ExitClassifications` | `text` | no |  |  |
| `IsRestDayOverride` | `boolean` | sí |  |  |
| `LunchExcessForgiven` | `boolean` | no | `false` |  |
| `OvertimeAuthorizedOverride` | `boolean` | sí |  |  |
| `Reason` | `character varying(500)` | sí |  |  |
| `Revision` | `integer` | no | `0` |  |
| `ShiftIdOverride` | `uuid` | sí |  |  |
| `TemporaryExitNote` | `character varying(500)` | sí |  |  |
| `TemporaryExitStatus` | `integer` | sí |  |  |
| `UpdatedAt` | `timestamp with time zone` | sí |  |  |
| `UpdatedBy` | `character varying(100)` | sí |  |  |
| `xmin` | `xid` | no |  | token de concurrencia (columna de sistema `xmin`), propiedad `Version` |

Índices:

- (`Date`)
- (`EmployeeId`, `Date`) — **único**

## AttendanceConsolidationRequests

Entidad: `AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate.AttendanceConsolidationRequest` · [snapshot línea 563](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L563)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `ClaimedBy` | `character varying(200)` | sí |  |  |
| `ClaimedVersion` | `integer` | sí |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `EndDate` | `date` | no |  |  |
| `ErrorMessage` | `character varying(2000)` | sí |  |  |
| `LastRequestedAt` | `timestamp with time zone` | no |  |  |
| `LeaseExpiresAt` | `timestamp with time zone` | sí |  |  |
| `NextAttemptAt` | `timestamp with time zone` | sí |  |  |
| `ProcessedAt` | `timestamp with time zone` | sí |  |  |
| `RequestedAt` | `timestamp with time zone` | no |  |  |
| `RetryCount` | `integer` | no | `0` |  |
| `StartDate` | `date` | no |  |  |
| `Status` | `integer` | no |  |  |
| `Version` | `integer` | no | `1` |  |

Índices:

- (`EmployeeId`) — **único**, filtro `"Status" IN (0, 1)`, nombre `IX_AttendanceConsolidationRequests_EmployeeId_Active`
- (`EmployeeId`, `Status`)
- (`Status`, `RequestedAt`)

## AttendancePeriodClosures

Entidad: `AttendanceSystem.Domain.Aggregates.PeriodClosureAggregate.AttendancePeriodClosure` · [snapshot línea 1484](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1484)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `Automatic` | `boolean` | no |  |  |
| `ClosedAt` | `timestamp with time zone` | no |  |  |
| `ClosedBy` | `character varying(256)` | no |  |  |
| `EndDate` | `date` | no |  |  |
| `ReopenReason` | `character varying(500)` | sí |  |  |
| `ReopenedAt` | `timestamp with time zone` | sí |  |  |
| `ReopenedBy` | `character varying(256)` | sí |  |  |
| `StartDate` | `date` | no |  |  |

Índices:

- (`StartDate`, `EndDate`)

## AttendanceRecords

Entidad: `AttendanceSystem.Domain.Aggregates.AttendanceAggregate.AttendanceRecord` · [snapshot línea 26](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L26)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `CheckTime` | `timestamp with time zone` | no |  |  |
| `CheckTypeId` | `int` | no |  | propiedad `CheckType` |
| `DeviceId` | `character varying(50)` | no |  |  |
| `DownloadLogId` | `uuid` | sí |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `OriginalCheckTime` | `timestamp with time zone` | sí |  |  |
| `OriginalCheckTypeId` | `int` | sí |  | propiedad `OriginalCheckType` |
| `Reason` | `character varying(500)` | sí |  |  |
| `ReceivedAt` | `timestamp with time zone` | sí |  |  |
| `RegisteredAt` | `timestamp with time zone` | sí |  |  |
| `RegisteredBy` | `character varying(256)` | sí |  |  |
| `ReplacedByRecordId` | `uuid` | sí |  |  |
| `ReplacesRecordId` | `uuid` | sí |  |  |
| `Source` | `character varying(50)` | sí |  |  |
| `StatusId` | `int` | no |  | propiedad `Status` |
| `TimeZoneId` | `character varying(100)` | sí |  |  |
| `VerifyMethodId` | `int` | no |  | propiedad `VerifyMethod` |
| `VoidReason` | `character varying(500)` | sí |  |  |
| `VoidedAt` | `timestamp with time zone` | sí |  |  |
| `VoidedBy` | `character varying(256)` | sí |  |  |
| `WorkDate` | `date` | sí |  |  |

Índices:

- (`DeviceId`)
- (`EmployeeId`, `CheckTime`) — **único**, filtro `"VoidedAt" IS NULL`
- (`EmployeeId`, `WorkDate`)

## Branches

Entidad: `AttendanceSystem.Domain.Aggregates.BranchAggregate.Branch` · [snapshot línea 335](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L335)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `Address` | `character varying(255)` | sí |  |  |
| `Code` | `character(3)` | no |  | longitud fija |
| `ExternalHost` | `character varying(255)` | sí |  |  |
| `IsExternal` | `boolean` | no | `false` |  |
| `Name` | `character varying(100)` | no |  |  |
| `TimeZoneId` | `character varying(100)` | sí |  |  |

## DailyAttendances

Entidad: `AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate.DailyAttendance` · [snapshot línea 632](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L632)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `ActualBlock1CheckOut` | `timestamp with time zone` | sí |  |  |
| `ActualBlock2CheckIn` | `timestamp with time zone` | sí |  |  |
| `ActualCheckIn` | `timestamp with time zone` | sí |  |  |
| `ActualCheckOut` | `timestamp with time zone` | sí |  |  |
| `AuthorizedOvertimeForRawMinutes` | `integer` | sí |  |  |
| `AuthorizedOvertimeMinutes` | `integer` | sí |  |  |
| `Block1CheckOutRecordId` | `uuid` | sí |  |  |
| `Block2CheckInRecordId` | `uuid` | sí |  |  |
| `CalculateOvertimeBeforeEntry` | `boolean` | no |  |  |
| `CalculationIssue` | `character varying(500)` | sí |  |  |
| `CheckInRecordId` | `uuid` | sí |  |  |
| `CheckOutRecordId` | `uuid` | sí |  |  |
| `Date` | `date` | no |  |  |
| `DeductedExitMinutes` | `integer` | no | `0` |  |
| `DynamicScheduledCheckOut` | `timestamp with time zone` | sí |  |  |
| `EarlyDepartureMinutes` | `integer` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `FlexWindowEndTime` | `interval` | sí |  |  |
| `HasEntryWindow` | `boolean` | no | `false` |  |
| `HasManualAdjustments` | `boolean` | no | `false` |  |
| `HasOpenInterval` | `boolean` | no | `false` |  |
| `HasTemporaryExits` | `boolean` | no | `false` |  |
| `IntervalsData` | `text` | sí |  |  |
| `IsAbsent` | `boolean` | no |  |  |
| `IsAutoDetectedShift` | `boolean` | no | `false` |  |
| `IsRestDay` | `boolean` | no |  |  |
| `IsRestDayOverridden` | `boolean` | no | `false` |  |
| `LateMinutes` | `integer` | no |  |  |
| `LunchBreakMinutesApplied` | `integer` | no | `0` |  |
| `LunchExcessMinutes` | `integer` | no | `0` |  |
| `LunchMinutesTaken` | `integer` | no | `0` |  |
| `MissingBlock1CheckOut` | `boolean` | no | `false` |  |
| `MissingBlock2CheckIn` | `boolean` | no | `false` |  |
| `MissingCheckIn` | `boolean` | no |  |  |
| `MissingCheckOut` | `boolean` | no |  |  |
| `OvertimeAuthorized` | `boolean` | no |  |  |
| `OvertimeCalculationMethod` | `integer` | no | `0` |  |
| `OvertimeMinutes` | `integer` | no |  |  |
| `PaidLunchMinutes` | `integer` | no | `0` |  |
| `PunchTrackingMode` | `integer` | no | `0` |  |
| `RawOvertimeMinutes` | `integer` | no | `0` |  |
| `RoundingInterval` | `integer` | no | `0` |  |
| `RoundingsEnabled` | `boolean` | no | `false` |  |
| `ScheduledBlock2CheckIn` | `interval` | sí |  |  |
| `ScheduledBlock2CheckOut` | `interval` | sí |  |  |
| `ScheduledCheckIn` | `interval` | sí |  |  |
| `ScheduledCheckOut` | `interval` | sí |  |  |
| `SecondBlockToleranceMinutes` | `integer` | sí |  |  |
| `ShiftId` | `uuid` | sí |  |  |
| `ShiftName` | `character varying(100)` | sí |  |  |
| `ShiftType` | `integer` | sí |  |  |
| `TemporaryExitMinutes` | `integer` | no | `0` |  |
| `TemporaryExitNote` | `character varying(500)` | sí |  |  |
| `TemporaryExitStatus` | `integer` | no | `0` |  |
| `TemporaryExitsData` | `text` | sí |  |  |
| `TimeZoneId` | `character varying(100)` | sí |  |  |
| `ToleranceMinutes` | `integer` | no |  |  |
| `TotalWorkedMinutes` | `integer` | no | `0` |  |
| `xmin` | `xid` | no |  | token de concurrencia (columna de sistema `xmin`), propiedad `Version` |
| `WeeklyShortfallMinutes` | `integer` | no | `0` |  |
| `WeeklyWorkHours` | `interval` | sí |  |  |
| `WorkHours` | `interval` | sí |  |  |
| `WorkedOnRestDay` | `boolean` | no |  |  |

Índices:

- (`Date`)
- (`EmployeeId`, `Date`) — **único**

## DepartmentPositions

Entidad: `DepartmentPositions` · [snapshot línea 2063](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2063)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `DepartmentId` | `uuid` | no |  | PK |
| `PositionsId` | `uuid` | no |  | PK |

Índices:

- (`PositionsId`)

Llaves foráneas declaradas:

- (`DepartmentId`) → `Departments`, al borrar: Cascade
- (`PositionsId`) → `Positions`, al borrar: Cascade

## Departments

Entidad: `AttendanceSystem.Domain.Aggregates.DepartmentAggregate.Department` · [snapshot línea 932](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L932)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `Description` | `character varying(255)` | sí |  |  |
| `Name` | `character varying(100)` | no |  |  |

## DeviceClockIncidents

Entidad: `AttendanceSystem.Domain.Aggregates.AttendanceAggregate.DeviceClockIncident` · [snapshot línea 121](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L121)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `DetectedAtUtc` | `timestamp with time zone` | no |  |  |
| `DeviceId` | `character varying(50)` | no |  |  |
| `OffsetMinutes` | `integer` | sí |  |  |
| `Reason` | `character varying(500)` | no |  |  |
| `ReportedFrom` | `timestamp with time zone` | no |  |  |
| `ReportedTo` | `timestamp with time zone` | no |  |  |

Índices:

- (`DeviceId`)

## Devices

Entidad: `AttendanceSystem.Domain.Aggregates.DeviceAggregate.Device` · [snapshot línea 1049](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1049)

Entidad: `AttendanceSystem.Domain.Aggregates.DeviceAggregate.DeviceHardwareInfo` — tipo propio de `Device` · [snapshot línea 2232](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2232)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `character varying(50)` | no |  | PK |
| `AcceptPunchesFrom` | `date` | sí |  |  |
| `Brand` | `integer` | no |  |  |
| `CreatedAt` | `timestamp with time zone` | no |  |  |
| `DeviceType` | `text` | sí |  |  |
| `DownloadMethod` | `integer` | no |  |  |
| `IpAddress` | `character varying(50)` | no |  |  |
| `IsActive` | `boolean` | no |  |  |
| `LastDownloadAt` | `timestamp with time zone` | sí |  |  |
| `Location` | `character varying(200)` | sí |  |  |
| `Name` | `character varying(100)` | no |  |  |
| `Password` | `text` | sí |  |  |
| `Port` | `integer` | no |  |  |
| `RealtimeEnabled` | `boolean` | no | `false` |  |
| `ShouldClearAfterDownload` | `boolean` | no |  |  |
| `StatusId` | `int` | no |  | propiedad `Status` |
| `TimeZoneId` | `character varying(100)` | sí |  |  |
| `TotalDownloadCount` | `integer` | no |  |  |
| `Username` | `text` | sí |  |  |
| `AttendanceRecordCapacity` | `integer` | sí |  |  |
| `AttendanceRecordCount` | `integer` | sí |  |  |
| `FaceCapacity` | `integer` | sí |  |  |
| `FaceCount` | `integer` | sí |  |  |
| `FingerprintCapacity` | `integer` | sí |  |  |
| `FingerprintCount` | `integer` | sí |  |  |
| `FirmwareVersion` | `character varying(100)` | sí |  |  |
| `MacAddress` | `character varying(50)` | sí |  |  |
| `Platform` | `character varying(100)` | sí |  |  |
| `PushVersion` | `character varying(100)` | sí |  |  |
| `SdkVersion` | `character varying(100)` | sí |  |  |
| `SerialNumber` | `character varying(100)` | sí |  |  |
| `UserCapacity` | `integer` | sí |  |  |
| `UserCount` | `integer` | sí |  |  |

Índices:

- (`IpAddress`) — **único**

## DownloadLogs

Entidad: `AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog` · [snapshot línea 1126](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1126)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `CompletedAt` | `timestamp with time zone` | sí |  |  |
| `DeviceId` | `character varying(50)` | no |  |  |
| `DownloadType` | `integer` | no |  |  |
| `DurationMs` | `integer` | no |  |  |
| `ErrorMessage` | `character varying(2000)` | sí |  |  |
| `FromDate` | `timestamp with time zone` | sí |  |  |
| `InitiatedByUserId` | `character varying(450)` | sí |  |  |
| `InitiatedByUserName` | `character varying(256)` | sí |  |  |
| `IsSuccessful` | `boolean` | no |  |  |
| `NewRecordsAdded` | `integer` | no |  |  |
| `StartedAt` | `timestamp with time zone` | no |  |  |
| `ToDate` | `timestamp with time zone` | sí |  |  |
| `TotalRecordsDownloaded` | `integer` | no |  |  |

Índices:

- (`DeviceId`)
- (`DownloadType`)
- (`IsSuccessful`)
- (`StartedAt`)

## EmployeeAuditLogs

Entidad: `AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmployeeAuditLog` · [snapshot línea 1321](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1321)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `Action` | `character varying(100)` | no |  |  |
| `ChangesJson` | `text` | sí |  |  |
| `Details` | `character varying(1000)` | sí |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `Timestamp` | `timestamp with time zone` | no |  |  |
| `UserId` | `character varying(450)` | sí |  |  |
| `UserName` | `character varying(256)` | sí |  |  |

Índices:

- (`EmployeeId`)
- (`EmployeeId`, `Timestamp`)

## EmployeeEmploymentPeriods

Entidad: `AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmploymentPeriod` — tipo propio de `Employee` · [snapshot línea 2343](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2343)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `EndDate` | `date` | sí |  |  |
| `StartDate` | `date` | no |  |  |

Índices:

- (`EmployeeId`)

Llaves foráneas declaradas:

- (`EmployeeId`) → `Employees`, al borrar: Cascade (tipo propio)

## EmployeeFingerprints

Entidad: `AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmployeeFingerprint` — tipo propio de `Employee` · [snapshot línea 2313](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2313)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `integer` | no |  | PK, identity |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `FingerIndex` | `integer` | no |  |  |
| `Template` | `character varying(16000)` | no |  |  |

Índices:

- (`EmployeeId`)

Llaves foráneas declaradas:

- (`EmployeeId`) → `Employees`, al borrar: Cascade (tipo propio)

## Employees

Entidad: `AttendanceSystem.Domain.Aggregates.EmployeeAggregate.Employee` · [snapshot línea 1188](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1188)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `character varying(20)` | no |  | PK, identity |
| `BranchId` | `uuid` | no |  |  |
| `CalculateOvertimeBeforeEntry` | `boolean` | no |  |  |
| `CardNumber` | `character varying(50)` | sí |  |  |
| `CreatedAt` | `timestamp with time zone` | no | `CURRENT_TIMESTAMP` (SQL) |  |
| `CreatedBy` | `character varying(256)` | sí |  |  |
| `DepartmentId` | `uuid` | no |  |  |
| `DevicePassword` | `character varying(20)` | sí |  |  |
| `DevicePrivilege` | `integer` | no | `0` |  |
| `Email` | `character varying(255)` | sí |  |  |
| `FaceTemplate` | `character varying(200000)` | sí |  |  |
| `FirstName` | `character varying(100)` | no |  |  |
| `Gender` | `integer` | no |  |  |
| `HireDate` | `timestamp with time zone` | no |  |  |
| `LastName` | `character varying(100)` | no |  |  |
| `OvertimeAuthorized` | `boolean` | no | `false` |  |
| `OvertimeCalculationMethod` | `integer` | no | `0` |  |
| `OvertimeCapMinutes` | `double precision` | sí |  |  |
| `OvertimeCapType` | `integer` | no | `0` |  |
| `PhoneNumber` | `character varying(20)` | sí |  |  |
| `Photo` | `character varying(1000000)` | sí |  |  |
| `PositionId` | `uuid` | no |  |  |
| `RestDay` | `integer` | sí |  |  |
| `RestDays` | `character varying(50)` | sí |  |  |
| `ScheduleId` | `uuid` | sí |  |  |
| `ShiftType` | `integer` | sí |  |  |
| `Status` | `integer` | no |  |  |
| `TerminationDate` | `timestamp with time zone` | sí |  |  |
| `UpdatedAt` | `timestamp with time zone` | sí |  |  |
| `UpdatedBy` | `character varying(256)` | sí |  |  |

Índices:

- (`BranchId`)
- (`DepartmentId`)
- (`Email`) — **único**, filtro `"Email" IS NOT NULL`
- (`PositionId`)
- (`Status`, `BranchId`, `DepartmentId`)

## EmployeeScheduleAssignments

Entidad: `AttendanceSystem.Domain.Aggregates.EmployeeAggregate.ScheduleAssignment` — tipo propio de `Employee` · [snapshot línea 2368](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2368)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `BranchId` | `uuid` | no |  |  |
| `EffectiveFrom` | `date` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `RestDays` | `character varying(50)` | sí |  |  |
| `ScheduleId` | `uuid` | sí |  |  |
| `ShiftType` | `integer` | sí |  |  |

Índices:

- (`EmployeeId`)

Llaves foráneas declaradas:

- (`EmployeeId`) → `Employees`, al borrar: Cascade (tipo propio)

## EmployeeStatusPeriods

Entidad: `AttendanceSystem.Domain.Aggregates.EmployeeAggregate.StatusPeriod` — tipo propio de `Employee` · [snapshot línea 2403](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2403)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `EndDate` | `date` | sí |  |  |
| `StartDate` | `date` | no |  |  |
| `Status` | `integer` | no |  |  |

Índices:

- (`EmployeeId`)

Llaves foráneas declaradas:

- (`EmployeeId`) → `Employees`, al borrar: Cascade (tipo propio)

## ExternalAttendanceLogs

Entidad: `AttendanceSystem.Domain.Aggregates.ExternalLogAggregate.ExternalAttendanceLog` · [snapshot línea 1428](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1428)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `BranchCode` | `character varying(10)` | no |  |  |
| `CheckTime` | `timestamp with time zone` | no |  |  |
| `CheckType` | `integer` | no |  |  |
| `CreatedAt` | `timestamp with time zone` | no |  |  |
| `EmployeeId` | `character varying(50)` | no |  |  |
| `ErrorMessage` | `character varying(1000)` | sí |  |  |
| `RetryCount` | `integer` | no | `0` |  |
| `SourceDevice` | `character varying(100)` | sí |  |  |
| `StatusId` | `int` | no |  | propiedad `Status` |
| `TransferredAt` | `timestamp with time zone` | sí |  |  |
| `VerifyMethod` | `integer` | no |  |  |

Índices:

- (`CreatedAt`)
- (`BranchCode`, `StatusId`)

## ExternalEmployees

Entidad: `AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate.ExternalEmployee` · [snapshot línea 1364](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1364)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `BranchId` | `uuid` | no |  |  |
| `CardNumber` | `character varying(50)` | sí |  |  |
| `CreatedAt` | `timestamp with time zone` | no |  |  |
| `Department` | `character varying(100)` | sí |  |  |
| `Email` | `character varying(255)` | sí |  |  |
| `EmployeeNumber` | `character varying(50)` | no |  |  |
| `FirstName` | `character varying(100)` | no |  |  |
| `LastName` | `character varying(100)` | no |  |  |
| `PhoneNumber` | `character varying(20)` | sí |  |  |
| `Position` | `character varying(100)` | sí |  |  |
| `Status` | `integer` | no |  |  |
| `UpdatedAt` | `timestamp with time zone` | sí |  |  |

Índices:

- (`BranchId`)
- (`BranchId`, `EmployeeNumber`) — **único**

## OutboxMessages

Entidad: `AttendanceSystem.Domain.Entities.OutboxMessage` · [snapshot línea 2013](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2013)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `Content` | `text` | no |  |  |
| `DeadLetteredOnUtc` | `timestamp with time zone` | sí |  |  |
| `Error` | `text` | sí |  |  |
| `LockedBy` | `character varying(200)` | sí |  |  |
| `LockedUntilUtc` | `timestamp with time zone` | sí |  |  |
| `NextAttemptOnUtc` | `timestamp with time zone` | sí |  |  |
| `OccurredOnUtc` | `timestamp with time zone` | no |  |  |
| `ProcessedOnUtc` | `timestamp with time zone` | sí |  |  |
| `RetryCount` | `integer` | no | `0` |  |
| `Type` | `character varying(500)` | no |  |  |

Índices:

- (`ProcessedOnUtc`, `OccurredOnUtc`)
- (`ProcessedOnUtc`, `DeadLetteredOnUtc`, `NextAttemptOnUtc`)

## Positions

Entidad: `AttendanceSystem.Domain.Aggregates.PositionAggregate.Position` · [snapshot línea 1524](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1524)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `BaseSalary` | `decimal(18,2)` | no |  |  |
| `Description` | `character varying(255)` | sí |  |  |
| `IsCritical` | `boolean` | no | `false` |  |
| `Name` | `character varying(100)` | no |  |  |

## PunchAssignments

Entidad: `AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate.PunchAssignment` · [snapshot línea 889](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L889)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `AttendanceRecordId` | `uuid` | no |  | PK, propiedad `Id` |
| `AssignedAt` | `timestamp with time zone` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `Origin` | `integer` | no |  |  |
| `Role` | `integer` | no |  |  |
| `xmin` | `xid` | no |  | token de concurrencia (columna de sistema `xmin`), propiedad `Version` |
| `WorkDate` | `date` | no |  |  |

Índices:

- (`EmployeeId`, `WorkDate`)
- (`AttendanceRecordId`, `EmployeeId`)

Llaves foráneas declaradas:

- (`AttendanceRecordId`, `EmployeeId`) → `AttendanceRecords` (`Id`, `EmployeeId`), al borrar: Cascade, nombre `FK_PunchAssignments_AttendanceRecords_Owner`

Restricciones CHECK:

- `CK_PunchAssignments_Origin`: `"Origin" IN (1, 2)`
- `CK_PunchAssignments_Role`: `"Role" BETWEEN 1 AND 9`

## PunchTimeCorrectionEntries

Entidad: `AttendanceSystem.Domain.Aggregates.AttendanceAggregate.PunchTimeCorrectionEntry` · [snapshot línea 201](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L201)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `AttendanceRecordId` | `uuid` | no |  |  |
| `CorrectionId` | `uuid` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `NewCheckTimeUtc` | `timestamp with time zone` | no |  |  |
| `NewCivilTime` | `timestamp with time zone` | no |  |  |
| `NewRecordId` | `uuid` | sí |  |  |
| `NewTimeZoneId` | `character varying(100)` | no |  |  |
| `NewWorkDate` | `date` | no |  |  |
| `OldCheckTimeUtc` | `timestamp with time zone` | no |  |  |
| `OldCivilTime` | `timestamp with time zone` | sí |  |  |
| `OldTimeZoneId` | `character varying(100)` | sí |  |  |
| `OldWorkDate` | `date` | sí |  |  |

Índices:

- (`AttendanceRecordId`)
- (`CorrectionId`)

Llaves foráneas declaradas:

- (`CorrectionId`) → `PunchTimeCorrections`, al borrar: Cascade

## PunchTimeCorrections

Entidad: `AttendanceSystem.Domain.Aggregates.AttendanceAggregate.PunchTimeCorrection` · [snapshot línea 155](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L155)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `AppliedAtUtc` | `timestamp with time zone` | no |  |  |
| `AppliedBy` | `character varying(100)` | no |  |  |
| `DeviceId` | `character varying(50)` | no |  |  |
| `FromDate` | `date` | no |  |  |
| `OffsetMinutes` | `integer` | no |  |  |
| `Reason` | `character varying(500)` | no |  |  |
| `SkippedCount` | `integer` | no |  |  |
| `TimeZoneId` | `character varying(100)` | sí |  |  |
| `ToDate` | `date` | no |  |  |

Índices:

- (`AppliedAtUtc`)

## QuarantinedPunches

Entidad: `AttendanceSystem.Domain.Aggregates.AttendanceAggregate.QuarantinedPunch` · [snapshot línea 256](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L256)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `CheckTypeCode` | `integer` | no |  |  |
| `DeviceId` | `character varying(50)` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `Reason` | `character varying(500)` | no |  |  |
| `ReceivedAtUtc` | `timestamp with time zone` | no |  |  |
| `ReleasedRecordId` | `uuid` | sí |  |  |
| `ReleasedTime` | `timestamp with time zone` | sí |  |  |
| `ReportedTime` | `timestamp with time zone` | no |  |  |
| `ResolutionNote` | `character varying(500)` | sí |  |  |
| `ResolvedAtUtc` | `timestamp with time zone` | sí |  |  |
| `ResolvedBy` | `character varying(100)` | sí |  |  |
| `Rule` | `character varying(60)` | sí |  |  |
| `Source` | `character varying(50)` | no |  |  |
| `Status` | `integer` | no |  |  |
| `SuggestedOffsetMinutes` | `integer` | sí |  |  |
| `TimeZoneId` | `character varying(100)` | no |  |  |
| `VerifyMethodCode` | `integer` | no |  |  |

Índices:

- (`Status`, `ReceivedAtUtc`)
- (`DeviceId`, `EmployeeId`, `ReportedTime`) — **único**

## RotationPatterns

Entidad: `AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate.RotationPattern` · [snapshot línea 1624](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1624)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `CreatedAt` | `timestamp with time zone` | no |  |  |
| `Definition` | `text` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `EndDate` | `date` | sí |  |  |
| `GeneratedThrough` | `date` | no |  |  |
| `StartDate` | `date` | no |  |  |
| `UpdatedAt` | `timestamp with time zone` | sí |  |  |

Índices:

- (`EmployeeId`) — **único**

Llaves foráneas declaradas:

- (`EmployeeId`) → `Employees`, al borrar: Cascade

## ShiftDays

Entidad: `AttendanceSystem.Domain.Aggregates.ShiftAggregate.ShiftDay` — tipo propio de `Shift` · [snapshot línea 2442](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L2442)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `ShiftId` | `uuid` | no |  | PK |
| `DayOfWeek` | `integer` | no |  | PK |
| `DayShiftId` | `uuid` | sí |  |  |
| `EndTime` | `interval` | no |  |  |
| `ShiftType` | `integer` | no | `0` |  |
| `StartTime` | `interval` | no |  |  |
| `WorkHours` | `interval` | no |  |  |

Índices:

- (`DayShiftId`)

Llaves foráneas declaradas:

- (`ShiftId`) → `Shifts`, al borrar: Cascade (tipo propio)

## ShiftRosters

Entidad: `AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate.ShiftRoster` · [snapshot línea 1661](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1661)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `CreatedAt` | `timestamp with time zone` | no |  |  |
| `Date` | `date` | no |  |  |
| `EmployeeId` | `character varying(20)` | no |  |  |
| `IsRestDay` | `boolean` | no | `false` |  |
| `Notes` | `character varying(500)` | sí |  |  |
| `ShiftId` | `uuid` | sí |  |  |
| `UpdatedAt` | `timestamp with time zone` | sí |  |  |

Índices:

- (`Date`)
- (`ShiftId`)
- (`EmployeeId`, `Date`) — **único**

Llaves foráneas declaradas:

- (`EmployeeId`) → `Employees`, al borrar: Cascade
- (`ShiftId`) → `Shifts`, al borrar: SetNull

## Shifts

Entidad: `AttendanceSystem.Domain.Aggregates.ShiftAggregate.Shift` · [snapshot línea 1551](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1551)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `EndTime` | `interval` | no |  |  |
| `FlexWindowEndTime` | `interval` | sí |  |  |
| `HasEntryWindow` | `boolean` | no | `true` |  |
| `LunchBreakMinutes` | `integer` | no | `0` |  |
| `Name` | `character varying(100)` | no |  |  |
| `PaidLunchMinutes` | `integer` | sí |  |  |
| `PunchTrackingMode` | `integer` | no | `0` |  |
| `RoundingInterval` | `integer` | no | `0` |  |
| `RoundingsEnabled` | `boolean` | no | `false` |  |
| `SecondBlockEndTime` | `interval` | sí |  |  |
| `SecondBlockStartTime` | `interval` | sí |  |  |
| `SecondBlockToleranceMinutes` | `integer` | sí |  |  |
| `ShiftType` | `integer` | no |  |  |
| `StartTime` | `interval` | no |  |  |
| `ToleranceMinutes` | `integer` | no |  |  |
| `WeeklyWorkHours` | `interval` | sí |  |  |
| `WorkHours` | `interval` | no |  |  |

## SystemAlerts

Entidad: `AttendanceSystem.Domain.Aggregates.SystemAlertAggregate.SystemAlert` · [snapshot línea 1704](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1704)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `IsResolved` | `boolean` | no |  |  |
| `Message` | `character varying(500)` | no |  |  |
| `ReferenceId` | `character varying(100)` | sí |  |  |
| `Timestamp` | `timestamp with time zone` | no |  |  |
| `Type` | `integer` | no |  |  |

## SystemConfiguration

Entidad: `AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate.SystemConfiguration` · [snapshot línea 1732](../../src/Infrastructure/AttendanceSystem.Infrastructure/Migrations/AttendanceDbContextModelSnapshot.cs#L1732)

| Columna | Tipo | Nulo | Omisión | Notas |
|---|---|---|---|---|
| `Id` | `uuid` | no |  | PK |
| `AbsenceAlertEmails` | `text` | sí |  |  |
| `AdmsPort` | `integer` | no |  |  |
| `AreAlertsEnabled` | `boolean` | no | `false` |  |
| `AutoBackupTime` | `interval` | sí |  |  |
| `AutoClearDevicesAfterDownload` | `boolean` | no |  |  |
| `AutoDownloadOnlyToday` | `boolean` | no |  |  |
| `AutoDownloadTime` | `interval` | sí |  |  |
| `AutoReportEmails` | `text` | sí |  |  |
| `AutoReportForToday` | `boolean` | no | `false` |  |
| `AutoReportTime` | `interval` | sí |  |  |
| `BackupDirectory` | `text` | no |  |  |
| `BackupTimeoutMinutes` | `integer` | no |  |  |
| `CloudDbConnectionString` | `text` | sí |  |  |
| `CompanyLogo` | `bytea` | sí |  |  |
| `CompanyName` | `character varying(100)` | no | `"Mi Empresa"` |  |
| `DefaultTimeZoneId` | `character varying(100)` | no | `"America/Mexico_City"` |  |
| `DoubleTapMinutes` | `integer` | no | `15` |  |
| `FortnightFirstDay` | `integer` | no |  |  |
| `FortnightSecondDay` | `integer` | no |  |  |
| `IsAutoBackupEnabled` | `boolean` | no | `false` |  |
| `IsAutoDownloadEnabled` | `boolean` | no | `false` |  |
| `IsAutoReportEnabled` | `boolean` | no | `false` |  |
| `IsCloudSyncEnabled` | `boolean` | no | `true` |  |
| `LateAlertEmails` | `text` | sí |  |  |
| `LateTolerance` | `interval` | no |  |  |
| `LunchWindowEnabled` | `boolean` | no | `true` |  |
| `LunchWindowEnd` | `interval` | no | `15:00:00` |  |
| `LunchWindowStart` | `interval` | no | `12:30:00` |  |
| `MonthlyStartDay` | `integer` | no |  |  |
| `NightLunchWindowEnd` | `interval` | no | `04:00:00` |  |
| `NightLunchWindowStart` | `interval` | no | `23:00:00` |  |
| `PaidLunchMinutes` | `integer` | no | `30` |  |
| `QuarantineBackwardJumpMinutes` | `integer` | no | `60` |  |
| `QuarantineClockDriftAlertMinutes` | `integer` | no | `10` |  |
| `QuarantineClockSyncToleranceMinutes` | `integer` | no | `2` |  |
| `QuarantineFutureToleranceMinutes` | `integer` | no | `5` |  |
| `QuarantineMinDateDaysBeforeDevice` | `integer` | no | `30` |  |
| `QuarantineRealtimeLagMinutes` | `integer` | no | `30` |  |
| `QuarantineSdkReadMarginDays` | `integer` | no | `1` |  |
| `SmtpEnableSsl` | `boolean` | no |  |  |
| `SmtpHost` | `text` | sí |  |  |
| `SmtpPassword` | `text` | sí |  |  |
| `SmtpPort` | `integer` | no |  |  |
| `SmtpUser` | `text` | sí |  |  |
| `StandardWorkHours` | `interval` | no |  |  |
| `SystemFailureAlertEmails` | `text` | sí |  |  |
| `WeeklyStartDay` | `integer` | no |  |  |
| `WorkPeriodMode` | `integer` | no |  |  |
