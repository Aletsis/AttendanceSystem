; Script generado para Inno Setup
; Este script crea un instalador "Todo en Uno" para AttendanceSystem + PostgreSQL
; Sistemas soportados: Windows 10 / 11 y Windows Server 2019 o posterior, x64 (MinVersion 10.0.17763 = 1809 / Server 2019)

#ifndef MyAppName
#define MyAppName "AttendanceSystem"
#endif

#ifndef MyAppVersion
#define MyAppVersion "2.3.2"
#endif

#ifndef MyAppPublisher
#define MyAppPublisher "Aletsis"
#endif

#ifndef MyAppURL
#define MyAppURL "https://github.com/Aletsis/AttendanceSystem"
#endif

#ifndef MyAppExeName
#define MyAppExeName "AttendanceSystem.Blazor.Server.exe"
#endif

#ifndef MyServiceExeName
#define MyServiceExeName "AttendanceSystem.ZKTeco.Service.exe"
#endif

#ifndef MyDesktopExeName
#define MyDesktopExeName "AttendanceSystem.WPF.exe"
#endif

; Puerto ADMS por defecto (SystemConfiguration.AdmsPort / WpfBackendHost.DefaultAdmsPort). Si se cambia en la
; configuración del sistema, hay que ajustar a mano el enlace de IIS y la regla de firewall "AttendanceSystem ADMS".
#define DefaultAdmsPort "18373"

; --- Verificaciones de compilación: el Hosting Bundle incluido debe ser de la misma versión de .NET que la app ---
#define ReleaseWebDir AddBackslash(SourcePath) + "..\Release\Web\"
#define HostingBundleFile AddBackslash(SourcePath) + "dotnet-hosting.exe"

#if !FileExists(ReleaseWebDir + "aspnetcorev2_inprocess.dll") || !FileExists(ReleaseWebDir + "Microsoft.AspNetCore.dll")
  #error Falta Release\Web (o no es una publicación para IIS). Ejecute Prepare-Release.ps1 antes de compilar.
#endif
#if !FileExists(AddBackslash(SourcePath) + "..\Release\Desktop\AttendanceSystem.WPF.exe")
  #error Falta Release\Desktop (programa de escritorio). Ejecute Prepare-Release.ps1 antes de compilar.
#endif
#if !FileExists(HostingBundleFile)
  #error Falta Deploy\dotnet-hosting.exe. Ejecute Deploy\Download-Prerequisites.ps1.
#endif

#define AspNetVersion GetVersionNumbersString(ReleaseWebDir + "Microsoft.AspNetCore.dll")
#define AspNetMajor Copy(AspNetVersion, 1, Pos(".", AspNetVersion) - 1)
; Versión del módulo ANCM con el que se publicó la app (aspnetcorev2_inprocess.dll: 20.x = .NET 10)
#define AncmVersion GetVersionNumbersString(ReleaseWebDir + "aspnetcorev2_inprocess.dll")
#define AncmMajor Copy(AncmVersion, 1, Pos(".", AncmVersion) - 1)
#define HostingBundleVersion GetVersionNumbersString(HostingBundleFile)
#define HostingBundleMajor Copy(HostingBundleVersion, 1, Pos(".", HostingBundleVersion) - 1)

#if HostingBundleMajor != AspNetMajor
  #define BundleMismatch "dotnet-hosting.exe es .NET " + HostingBundleVersion + " y la app es .NET " + AspNetVersion
  #pragma message BundleMismatch
  #error dotnet-hosting.exe no es de la misma versión de .NET que la app. Ejecute Deploy\Download-Prerequisites.ps1.
#endif

[Setup]
; Identificador único de la App
AppId={{A1B2C3D4-E5F6-7890-1234-56789ABCDEF0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Permisos de administrador requeridos para instalar servicios, DB y registrar COM
PrivilegesRequired=admin
OutputDir=..\Output
; ARQUITECTURA: la web es win-x64, así que sólo se instala en Windows x64 (o ARM64 con emulación x64), en modo 64 bits
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 / Windows Server 2019 o posterior
MinVersion=10.0.17763

OutputBaseFilename=AttendanceSystem_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Icono del instalador y del desinstalador (Agregar o quitar programas)
SetupIconFile=Assets\app.ico
; Log de Inno Setup siempre en %TEMP% ("Setup Log <fecha> #NNN.txt"), aunque no se ejecute con /LOG
SetupLogging=yes
; Se copia siempre a {app} (ver [Files]): con sólo el sitio web no existe el .exe de escritorio
UninstallDisplayIcon={app}\app.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

; --- Qué se instala ---
; El sitio web (IIS) y el programa de escritorio (WPF) funcionan cada uno por su cuenta o juntos sobre la misma base.
; Siempre se instalan PostgreSQL (si no hay uno), la base de datos y el servicio ZKTeco, que usan los dos.
; Sólo el programa: Hosting:Standalone = true (el programa aplica migraciones, corre las tareas y recibe los relojes ADMS).
; Con el sitio: Hosting:Standalone = false (eso lo hace el sitio). El instalador fija el valor según lo instalado.
[Types]
Name: "full"; Description: "Sitio web y programa de escritorio"
Name: "web"; Description: "Sólo sitio web (IIS)"
Name: "desktop"; Description: "Sólo programa de escritorio"
Name: "custom"; Description: "Personalizada"; Flags: iscustom

[Components]
Name: "core"; Description: "Base de datos y servicio de relojes ZKTeco (obligatorio)"; Types: full web desktop custom; Flags: fixed
Name: "web"; Description: "Sitio web (Blazor, hospedado en IIS)"; Types: full web
Name: "desktop"; Description: "Programa de escritorio (WPF)"; Types: full desktop

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Programa de escritorio:"; Components: desktop
Name: "startup"; Description: "Abrir el programa al iniciar sesión en Windows (recomendado sin sitio web: las tareas automáticas sólo corren con el programa abierto)"; GroupDescription: "Programa de escritorio:"; Components: desktop; Flags: unchecked

[Dirs]
Name: "{app}\Logs"
Name: "{app}\Backups"; Permissions: users-modify; Components: desktop
Name: "{app}\Backups"; Components: not desktop
; El programa de escritorio corre con la cuenta de quien lo abre (sin permisos de administrador): necesita escribir
; sus logs y respaldos junto al ejecutable, y en la carpeta de respaldos del sitio cuando conviven.
Name: "{app}\Desktop\logs"; Permissions: users-modify; Components: desktop
Name: "{app}\Desktop\Backups"; Permissions: users-modify; Components: desktop
Name: "{app}\Web\Backups"; Permissions: users-modify; Components: web and desktop

[Files]
; Icono del desinstalador (UninstallDisplayIcon)
Source: "Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion
; 1. Archivos de la Aplicación Web
; appsettings.Production.json NO se copia: lo genera el instalador la primera vez (cadena de conexión y
; ApiKey propias del equipo) y una actualización lo conserva, junto con lo que el cliente haya cambiado en él.
; Tampoco los demás appsettings*.json: en el repositorio no existen (.gitignore) y un Release publicado en el equipo
; de un desarrollador traería los suyos (su cadena de conexión y contraseña). Así el instalador es igual que el de CI.
Source: "..\Release\Web\*"; DestDir: "{app}\Web"; Excludes: "appsettings.json,appsettings.*.json"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: web

; 1.1 Programa de escritorio. Igual que en la web, su appsettings.json lo genera el instalador y se conserva.
Source: "..\Release\Desktop\*"; DestDir: "{app}\Desktop"; Excludes: "appsettings.json,appsettings.*.json"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: desktop

; 2. Archivos del Servicio Windows (incluyendo SDK ZKTeco, que Prepare-Release copia junto al ejecutable)
; Igual que en la web, ningún appsettings*.json del Release: el instalador genera appsettings.Production.json.
Source: "..\Release\Service\*"; DestDir: "{app}\Service"; Excludes: "appsettings.json,appsettings.*.json,zkemkeeper.dll"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2.1 SDK ZKTeco (32 bits): zkemkeeper.dll se registra desde la carpeta del servicio, junto a sus dependencias.
; regsvr32 y COM cargan un DLL registrado buscando sus dependencias primero en su propia carpeta, así que no importa
; qué versiones del SDK haya en SysWOW64: otros programas de ZKTeco dejan ahí DLLs con el mismo nombre pero de otro
; SDK (p. ej. plcommpro.dll 6.0.0.5 del PullSDK), con los que zkemkeeper.dll no carga (regsvr32 sale con código 3).
; Tampoco se tocan los DLLs de esos programas. El registro (de 32 bits) se quita al desinstalar.
Source: "..\Release\Service\zkemkeeper.dll"; DestDir: "{app}\Service"; Flags: ignoreversion restartreplace regserver 32bit

; Plantillas de configuración (se extraen a {tmp} sólo cuando falta el archivo de configuración)
Source: "appsettings.Web.Production.template.json"; Flags: dontcopy
Source: "appsettings.Desktop.template.json"; Flags: dontcopy
Source: "appsettings.Service.Production.template.json"; Flags: dontcopy

; 3. Scripts SQL y Herramientas
Source: "init_db.sql"; DestDir: "{app}\Database"; Flags: ignoreversion
Source: "rename_db.sql"; DestDir: "{app}\Database"; Flags: ignoreversion
Source: "enable_iis.ps1"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "configure_iis.bat"; DestDir: "{app}\Tools"; Flags: ignoreversion

; 4. Instaladores de Prerrequisitos (DEBEN ESTAR EN LA CARPETA DEPLOY)
Source: "postgresql-installer.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "dotnet-hosting.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; 1. Instalar PostgreSQL silenciosamente (SOLO SI NO SE DETECTÓ NINGUNO)
Filename: "{tmp}\postgresql-installer.exe"; Parameters: "--mode unattended --unattendedmodeui none --superpassword ""{code:GetPostgresPassword}"" --serverport 5432 --servicepassword ""{code:GetPostgresPassword}"""; \
    StatusMsg: "Instalando base de datos PostgreSQL... (Esto puede tardar unos minutos)"; \
    Check: PostgresNotInstalled

; 2-3. La base de datos (usuario, BD, permisos) y los appsettings.Production.json se configuran en
; ConfigureDatabase (BeforeInstall del registro del servicio), que espera a que PostgreSQL acepte conexiones
; y avisa si algo falla. Sólo actúa en la primera instalación.

; 4. Instalar Servicio de Windows (ZKTeco)
; IMPORTANTE: El nombre "AttendanceSystem.ZKTeco.Service" DEBE coincidir con el ServiceName en Program.cs (AddWindowsService)
Filename: "{sys}\sc.exe"; Parameters: "create AttendanceSystem.ZKTeco.Service binPath= ""{app}\Service\{#MyServiceExeName}"" start= auto displayname= ""Attendance System ZKTeco Service"""; \
    Flags: runhidden; StatusMsg: "Registrando servicio de Windows..."; BeforeInstall: ConfigureDatabase
Filename: "{sys}\sc.exe"; Parameters: "description AttendanceSystem.ZKTeco.Service ""Servicio de comunicación con dispositivos ZKTeco para AttendanceSystem"""; \
    Flags: runhidden

; 5. Iniciar el Servicio ZKTeco (el SDK ya quedó registrado por [Files])
Filename: "{sys}\sc.exe"; Parameters: "start AttendanceSystem.ZKTeco.Service"; \
    Flags: runhidden; StatusMsg: "Iniciando servicio..."

; 6. Sitio web en IIS (sólo con el componente web)
; 6.1 Abrir Puertos en Firewall para Web: 80, o 8081 si IIS no pudo usar el 80 (el servicio gRPC 5001 se mantiene
; local/loopback por seguridad H02). Se borra antes la regla para no acumular duplicados en cada actualización.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AttendanceSystem Web"""; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."; Components: web
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AttendanceSystem Web"" dir=in action=allow protocol=TCP localport=80,8081"; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."; Components: web

; 6.2 Habilitar IIS y Componentes. La web sólo se hospeda en IIS (no se registra como servicio).
; Debe ir ANTES del Hosting Bundle: el Hosting Bundle registra el módulo ANCM sólo en un IIS ya habilitado.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\enable_iis.ps1"" -LogFile ""{app}\Logs\enable_iis.log"""; \
    StatusMsg: "Habilitando características de Windows (IIS)..."; Flags: runhidden; Components: web

; 6.3 Instalar ASP.NET Core Hosting Bundle (si falta el módulo ANCM {#AncmMajor}.x o no está registrado en IIS)
Filename: "{tmp}\dotnet-hosting.exe"; Parameters: "{code:GetHostingBundleArgs} /quiet /norestart /log ""{app}\Logs\dotnet_hosting.log"""; \
    StatusMsg: "Instalando ASP.NET Core {#HostingBundleVersion} Hosting Bundle..."; \
    Check: HostingBundleNeeded; Components: web

; 6.4 Configurar IIS (AppPool y Sitio, con el puerto ADMS como segundo enlace)
Filename: "{app}\Tools\configure_iis.bat"; Parameters: """{app}\Web"" ""{app}\Logs\iis_setup.log"" {#DefaultAdmsPort}"; \
    StatusMsg: "Configurando Sitio Web en IIS..."; Flags: runhidden; Components: web

; 7. Puerto ADMS (relojes que envían sus marcajes por HTTP). Lo atiende el sitio (enlace de IIS) o, sin sitio, el
; programa de escritorio; la regla de firewall evita que Windows pida permiso a un usuario sin derechos de administrador.
; Es el último paso que siempre se ejecuta: al terminar verifica la instalación completa.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AttendanceSystem ADMS"""; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AttendanceSystem ADMS"" dir=in action=allow protocol=TCP localport={#DefaultAdmsPort}"; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."; AfterInstall: VerifyInstallation

; 8. Abrir el programa de escritorio al terminar (como el usuario que instala, no como administrador)
Filename: "{app}\Desktop\{#MyDesktopExeName}"; Description: "Abrir AttendanceSystem (programa de escritorio)"; \
    Flags: postinstall nowait skipifsilent runasoriginaluser; Components: desktop

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\Desktop\{#MyDesktopExeName}"; WorkingDir: "{app}\Desktop"; Components: desktop
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\Desktop\{#MyDesktopExeName}"; WorkingDir: "{app}\Desktop"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\Desktop\{#MyDesktopExeName}"; WorkingDir: "{app}\Desktop"; Tasks: startup

[UninstallRun]
; Eliminar sitio IIS al desinstalar
Filename: "{sys}\inetsrv\appcmd.exe"; Parameters: "delete site ""AttendanceSystem"""; Flags: runhidden; RunOnceId: "DeleteIisSite"; Components: web
Filename: "{sys}\inetsrv\appcmd.exe"; Parameters: "delete apppool ""AttendanceSystem"""; Flags: runhidden; RunOnceId: "DeleteIisAppPool"; Components: web
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AttendanceSystem Web"""; Flags: runhidden; RunOnceId: "DeleteFirewallRule"; Components: web
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AttendanceSystem ADMS"""; Flags: runhidden; RunOnceId: "DeleteAdmsFirewallRule"
; Detener y eliminar el servicio de Windows
; IMPORTANTE: El nombre debe coincidir con el usado en [Run] y en Program.cs
Filename: "{sys}\sc.exe"; Parameters: "stop AttendanceSystem.ZKTeco.Service"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete AttendanceSystem.ZKTeco.Service"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
const
  AppDbName = 'AttendanceSystem';
  AppDbUser = 'attendancesystem_user';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-E5F6-7890-1234-56789ABCDEF0}_is1';
  // Versión mínima de PostgreSQL soportada por la app (Npgsql / EF Core)
  MinPgMajor = 14;
  // Versión del módulo ANCM con que se publicó la app (se lee de Release\Web al compilar)
  RequiredAncmMajor = {#AncmMajor};

var
  PostgresPage: TInputQueryWizardPage;
  UserPostgresPassword: String;
  IsPostgresDetected: Boolean;
  // Instancia de PostgreSQL que usará la app (detectada, o la que instaló este instalador)
  PgMajor: Integer;
  PgPort: Integer;
  PgBaseDir: String;
  PgDataDir: String;
  PgServiceId: String;
  // Carpeta de una instalación anterior (vacía si no la hay) y si ya dejó configurada la aplicación
  PrevAppPath: String;
  PreviousInstallConfigured: Boolean;
  // Esta ejecución debe crear usuario/BD (primera vez en esta carpeta)
  NeedsDbSetup: Boolean;
  // Secretos generados en esta instalación (hexadecimales: seguros en JSON, cadenas de conexión y línea de comandos)
  PgSuperPassword: String;
  AppDbPassword: String;
  // ApiKey del servicio y cadena de conexión que se escriben en la configuración (generadas o de la instalación existente)
  GrpcApiKey: String;
  AppConnection: String;
  // Versión instalada y comparación con la de este instalador (-1 más vieja, 0 misma, 1 más nueva)
  PrevVersion: String;
  VersionCompare: Integer;
  // Página de instalación existente (con la opción de base nueva) y página de base ya existente en una primera instalación
  VersionPage: TInputOptionWizardPage;
  ExistingDbPage: TInputOptionWizardPage;
  ExistingDbFound: Boolean;
  // Crear una base de datos nueva: la actual (si se llama AttendanceSystem) se conserva renombrada
  CreateNewDb: Boolean;
  RenamedDbName: String;
  PreviousDbName: String;
  // Contraseña de postgres guardada por este instalador cuando instaló PostgreSQL (vacía si no la hay)
  SavedSuperPassword: String;
  // Resultado de ConfigureDatabase y de VerifyInstallation, para avisar al final
  DbConfigError: String;
  InstallWarnings: String;
  PgSuperPasswordFile: String;

function SetEnvironmentVariable(lpName: String; lpValue: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

// ============================================================================================
// Detección de PostgreSQL
// ============================================================================================

// Lee "port = N" de un postgresql.conf / postgresql.auto.conf; deja Port intacto si no aparece.
procedure ReadPortFromConf(FileName: String; var Port: Integer);
var
  Lines: TArrayOfString;
  I, P: Integer;
  Value: String;
begin
  if not LoadStringsFromFile(FileName, Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Value := Trim(Lines[I]);
    if Pos('port', Lowercase(Value)) <> 1 then Continue;
    Value := Trim(Copy(Value, 5, Length(Value)));
    if (Length(Value) > 0) and (Value[1] = '=') then
      Value := Trim(Copy(Value, 2, Length(Value)));
    P := Pos('#', Value);
    if P > 0 then
      Value := Trim(Copy(Value, 1, P - 1));
    StringChangeEx(Value, '''', '', True);
    // "port_xxx = ..." no es un número y se ignora; la última aparición gana, como en PostgreSQL
    if StrToIntDef(Value, 0) > 0 then
      Port := StrToInt(Value);
  end;
end;

function MajorFromVersion(Version, Fallback: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', Version);
  if P > 0 then
    Version := Copy(Version, 1, P - 1);
  Result := StrToIntDef(Version, StrToIntDef(Fallback, 0));
end;

// Se queda con la instalación de mayor versión que tenga servidor (carpeta data) y psql.exe
procedure ConsiderPostgres(BaseDir, DataDir, ServiceId: String; Major: Integer);
begin
  BaseDir := RemoveBackslashUnlessRoot(BaseDir);
  if (Major > PgMajor) and FileExists(BaseDir + '\bin\psql.exe') and DirExists(DataDir) then
  begin
    PgMajor := Major;
    PgBaseDir := BaseDir;
    PgDataDir := RemoveBackslashUnlessRoot(DataDir);
    PgServiceId := ServiceId;
  end;
end;

function DetectPostgres: Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
  Key, BaseDir, DataDir, Version, ServiceId, Root: String;
  FindRec: TFindRec;
  RegPort: Cardinal;
begin
  PgMajor := 0;
  PgPort := 0;
  PgBaseDir := '';
  PgDataDir := '';
  PgServiceId := '';

  // 1. Instalaciones de EnterpriseDB registradas (cualquier versión)
  if RegGetSubkeyNames(HKLM, 'SOFTWARE\PostgreSQL\Installations', Keys) then
    for I := 0 to GetArrayLength(Keys) - 1 do
    begin
      Key := 'SOFTWARE\PostgreSQL\Installations\' + Keys[I];
      if not RegQueryStringValue(HKLM, Key, 'Base Directory', BaseDir) then Continue;
      if not RegQueryStringValue(HKLM, Key, 'Data Directory', DataDir) then
        DataDir := AddBackslash(BaseDir) + 'data';
      Version := '';
      RegQueryStringValue(HKLM, Key, 'Version', Version);
      ServiceId := Keys[I];
      RegQueryStringValue(HKLM, Key, 'Service ID', ServiceId);
      ConsiderPostgres(BaseDir, DataDir, ServiceId, MajorFromVersion(Version, ExtractFileName(RemoveBackslashUnlessRoot(BaseDir))));
    end;

  // 2. Carpetas estándar (instalaciones que no dejaron registro)
  Root := ExpandConstant('{commonpf64}\PostgreSQL\');
  if FindFirst(Root + '*', FindRec) then
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and (StrToIntDef(FindRec.Name, 0) > 0) then
        ConsiderPostgres(Root + FindRec.Name, Root + FindRec.Name + '\data', 'postgresql-x64-' + FindRec.Name, StrToInt(FindRec.Name));
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;

  Result := PgMajor > 0;
  if not Result then Exit;

  // Puerto: el de la configuración real del servidor (el del registro puede haber quedado viejo)
  ReadPortFromConf(PgDataDir + '\postgresql.conf', PgPort);
  ReadPortFromConf(PgDataDir + '\postgresql.auto.conf', PgPort);
  if (PgPort = 0) and RegQueryDWordValue(HKLM, 'SOFTWARE\PostgreSQL\Services\' + PgServiceId, 'Port', RegPort) then
    PgPort := RegPort;
  if PgPort = 0 then
    PgPort := 5432;

  Log(Format('PostgreSQL detectado: %d en %s (datos %s, puerto %d)', [PgMajor, PgBaseDir, PgDataDir, PgPort]));
end;

function PostgresNotInstalled: Boolean;
begin
  Result := not IsPostgresDetected;
end;

function PsqlPath: String;
begin
  Result := PgBaseDir + '\bin\psql.exe';
end;

// --- Retornar Contraseña del superusuario postgres ---
function GetPostgresPassword(Param: String): String;
begin
  if not IsPostgresDetected then
    Result := PgSuperPassword // La generada ahora para el PostgreSQL que instalamos
  else if UserPostgresPassword <> '' then
    Result := UserPostgresPassword // La que ingresó el usuario
  else
    Result := SavedSuperPassword; // La que guardamos al instalar PostgreSQL en una instalación anterior
end;

// --- Ejecuta psql como postgres (contraseña por PGPASSWORD) y anexa su salida al log ---
function RunPsql(Args, LogFile: String): Integer;
var
  ResultCode: Integer;
begin
  // -w: nunca pedir contraseña (en una ventana oculta psql se quedaría esperando)
  if not Exec(ExpandConstant('{cmd}'),
    '/C ""' + PsqlPath + '" -w -h localhost -p ' + IntToStr(PgPort) + ' -U postgres ' + Args + ' >> "' + LogFile + '" 2>&1"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

function TestPostgresConnection(Password, LogFile: String): Boolean;
begin
  SetEnvironmentVariable('PGPASSWORD', Password);
  try
    Result := RunPsql('-d postgres -c "SELECT 1"', LogFile) = 0;
  finally
    SetEnvironmentVariable('PGPASSWORD', '');
  end;
end;

// ¿Existe ya la base de datos de la aplicación? (como postgres)
function AppDatabaseExists(Password: String): Boolean;
var
  OutFile: String;
  Content: AnsiString;
begin
  Result := False;
  OutFile := ExpandConstant('{tmp}\db_exists.txt');
  DeleteFile(OutFile);
  SetEnvironmentVariable('PGPASSWORD', Password);
  try
    if RunPsql('-d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = ''' + AppDbName + '''"', OutFile) = 0 then
      Result := LoadStringFromFile(OutFile, Content) and (Trim(String(Content)) = '1');
  finally
    SetEnvironmentVariable('PGPASSWORD', '');
  end;
  Log(Format('Base de datos %s existente: %d', [AppDbName, Ord(Result)]));
end;

// Contraseña de postgres que guardó este instalador al instalar PostgreSQL (segunda línea del archivo)
function ReadSavedSuperPassword(AppDir: String): String;
var
  Lines: TArrayOfString;
begin
  Result := '';
  if (AppDir <> '') and LoadStringsFromFile(AddBackslash(AppDir) + 'Database\postgres_superuser.txt', Lines) and
     (GetArrayLength(Lines) >= 2) then
    Result := Trim(Lines[1]);
end;

// ============================================================================================
// Versiones
// ============================================================================================

// Siguiente componente numérico de una versión ("2.10.0-rc1": 2, 10, 0); 0 cuando se acaban
function NextVersionPart(var Version: String): Integer;
var
  I: Integer;
begin
  I := 1;
  while (I <= Length(Version)) and (Version[I] >= '0') and (Version[I] <= '9') do
    I := I + 1;
  Result := StrToIntDef(Copy(Version, 1, I - 1), 0);
  if (I <= Length(Version)) and (Version[I] = '.') then
    Version := Copy(Version, I + 1, Length(Version))
  else
    Version := ''; // fin o sufijo ("-rc1"): no cuenta para comparar
end;

// -1 si A es anterior a B, 0 si son la misma, 1 si A es posterior
function CompareVersions(A, B: String): Integer;
var
  I, PartA, PartB: Integer;
begin
  Result := 0;
  for I := 1 to 4 do
  begin
    PartA := NextVersionPart(A);
    PartB := NextVersionPart(B);
    if PartA < PartB then
    begin
      Result := -1;
      Exit;
    end;
    if PartA > PartB then
    begin
      Result := 1;
      Exit;
    end;
  end;
end;

function HasParam(Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
      Result := True;
end;

// ============================================================================================
// ASP.NET Core Hosting Bundle (módulo ANCM de IIS)
// ============================================================================================

function GetAncmMajor: Integer;
var
  MS, LS: Cardinal;
begin
  if GetVersionNumbers(ExpandConstant('{commonpf64}\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'), MS, LS) then
    Result := MS shr 16
  else
    Result := 0;
end;

function IsAncmRegisteredInIis: Boolean;
var
  Config: AnsiString;
begin
  Result := LoadStringFromFile(ExpandConstant('{sys}\inetsrv\config\applicationHost.config'), Config) and
    (Pos('name="AspNetCoreModuleV2"', String(Config)) > 0);
end;

// Check del paso 8: se evalúa después de habilitar IIS
function HostingBundleNeeded: Boolean;
begin
  Result := (GetAncmMajor < RequiredAncmMajor) or not IsAncmRegisteredInIis;
  Log(Format('ANCM instalado: %d (requerido %d), registrado en IIS: %d -> instalar Hosting Bundle: %d', [
    GetAncmMajor, RequiredAncmMajor, Ord(IsAncmRegisteredInIis), Ord(Result)]));
end;

// Con el módulo ya instalado pero sin registrar (IIS se habilitó después del Hosting Bundle) hay que reparar
function GetHostingBundleArgs(Param: String): String;
begin
  if GetAncmMajor >= RequiredAncmMajor then
    Result := '/repair'
  else
    Result := '/install';
end;

// ============================================================================================
// Configuración de la aplicación
// ============================================================================================

// --- Secreto aleatorio criptográfico, en hexadecimal ---
function GenerateSecret(ByteCount: Integer): String;
var
  TmpFile: String;
  Content: AnsiString;
  ResultCode: Integer;
begin
  Result := '';
  TmpFile := ExpandConstant('{tmp}\secret.txt');
  DeleteFile(TmpFile);
  if Exec('powershell.exe',
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' +
    '$b = New-Object byte[] ' + IntToStr(ByteCount) + '; ' +
    '[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); ' +
    '[IO.File]::WriteAllText(''' + TmpFile + ''', [BitConverter]::ToString($b).Replace(''-'', ''''))"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    if LoadStringFromFile(TmpFile, Content) then
      Result := Trim(String(Content));
  end;
  DeleteFile(TmpFile);
  if Length(Result) <> ByteCount * 2 then
    Result := '';
end;

function AppConnectionString: String;
begin
  Result := 'Host=localhost;Port=' + IntToStr(PgPort) + ';Database=' + AppDbName + ';Username=' + AppDbUser + ';Password=' + AppDbPassword + ';';
end;

function JsonEscapePath(Path: String): String;
begin
  Result := Path;
  StringChangeEx(Result, '\', '\\', True);
end;

// Problemas que se muestran al final de la instalación
procedure AddWarning(Text: String);
begin
  Log('ADVERTENCIA: ' + Text);
  InstallWarnings := InstallWarnings + '- ' + Text + #13#10;
end;

// --- Archivos de configuración de cada parte (en una carpeta de instalación dada) ---
function WebConfigFile(AppDir: String): String;
begin
  Result := AddBackslash(AppDir) + 'Web\appsettings.Production.json';
end;

function DesktopConfigFile(AppDir: String): String;
begin
  Result := AddBackslash(AppDir) + 'Desktop\appsettings.json';
end;

function ServiceConfigFile(AppDir: String): String;
begin
  Result := AddBackslash(AppDir) + 'Service\appsettings.Production.json';
end;

// La aplicación ya quedó configurada antes (la base de datos y su usuario existen) si el sitio o el programa
// tienen su archivo de configuración.
function AppIsConfiguredAt(AppDir: String): Boolean;
begin
  Result := (AppDir <> '') and (FileExists(WebConfigFile(AppDir)) or FileExists(DesktopConfigFile(AppDir)));
end;

function IsWebInstalledAt(AppDir: String): Boolean;
begin
  Result := (AppDir <> '') and FileExists(AddBackslash(AppDir) + 'Web\AttendanceSystem.Blazor.Server.dll');
end;

function IsDesktopInstalledAt(AppDir: String): Boolean;
begin
  Result := (AppDir <> '') and FileExists(AddBackslash(AppDir) + 'Desktop\{#MyDesktopExeName}');
end;

// Lee el primer valor de texto de "Key": "valor" en un JSON (los valores que escribe el instalador no llevan comillas)
function ReadJsonString(FileName, Key: String; var Value: String): Boolean;
var
  Content: AnsiString;
  Text: String;
  P: Integer;
begin
  Result := False;
  if not LoadStringFromFile(FileName, Content) then Exit;
  Text := String(Content);
  P := Pos('"' + Key + '"', Text);
  if P = 0 then Exit;
  Text := Copy(Text, P + Length(Key) + 2, Length(Text));
  P := Pos(':', Text);
  if P = 0 then Exit;
  Text := TrimLeft(Copy(Text, P + 1, Length(Text)));
  if (Length(Text) = 0) or (Text[1] <> '"') then Exit;
  Text := Copy(Text, 2, Length(Text));
  P := Pos('"', Text);
  if P = 0 then Exit;
  Value := Copy(Text, 1, P - 1);
  Result := Value <> '';
end;

// "Database=X" de una cadena de conexión
function DbNameFromConnection(Connection: String): String;
var
  P: Integer;
begin
  Result := '';
  P := Pos('database=', Lowercase(Connection));
  if P = 0 then Exit;
  Result := Copy(Connection, P + Length('database='), Length(Connection));
  P := Pos(';', Result);
  if P > 0 then
    Result := Copy(Result, 1, P - 1);
end;

// Cambia la cadena de conexión anterior por la nueva en las configuraciones existentes (conserva todo lo demás)
function ReplaceConnectionInConfigs(OldConnection, NewConnection: String): Boolean;
var
  Files: array of String;
  I: Integer;
  Content: AnsiString;
  Text: String;
begin
  Result := True;
  SetArrayLength(Files, 2);
  Files[0] := WebConfigFile(ExpandConstant('{app}'));
  Files[1] := DesktopConfigFile(ExpandConstant('{app}'));
  for I := 0 to GetArrayLength(Files) - 1 do
  begin
    if not LoadStringFromFile(Files[I], Content) then Continue;
    Text := String(Content);
    if StringChangeEx(Text, OldConnection, NewConnection, True) > 0 then
    begin
      Result := SaveStringToFile(Files[I], AnsiString(Text), False) and Result;
      Log('Cadena de conexión actualizada en ' + Files[I]);
    end;
  end;
end;

// --- Aparta la base actual de la aplicación (si existe) renombrándola, para crear una nueva ---
// Se llama con PGPASSWORD ya fijada; AppDatabaseExists la limpia, así que se vuelve a fijar.
function RenameExistingDatabase(LogFile: String): Boolean;
var
  Password: String;
begin
  Result := True;
  RenamedDbName := '';
  Password := GetPostgresPassword('');
  // Una instalación anterior pudo usar otra base (p. ej. AttendanceSystemTest): ésa no se toca
  if not AppDatabaseExists(Password) then
  begin
    SetEnvironmentVariable('PGPASSWORD', Password);
    Exit;
  end;

  RenamedDbName := AppDbName + '_anterior_' + GetDateTimeString('yyyymmdd_hhnnss', #0, #0);
  WizardForm.StatusLabel.Caption := 'Apartando la base de datos actual como ' + RenamedDbName + '...';
  SetEnvironmentVariable('PGPASSWORD', Password);
  RunPsql('-v ON_ERROR_STOP=1 -d postgres -v old_name=' + AppDbName + ' -v new_name=' + RenamedDbName +
    ' -f "' + ExpandConstant('{app}\Database\rename_db.sql') + '"', LogFile);
  // Comprobar el resultado real: si la base sigue con su nombre, no se pudo apartar
  Result := not AppDatabaseExists(Password);
  SetEnvironmentVariable('PGPASSWORD', Password);
  if Result then
    Log('Base de datos ' + AppDbName + ' apartada como ' + RenamedDbName);
end;

// --- Escribe un archivo de configuración a partir de su plantilla ---
function WriteConfigFromTemplate(TemplateName, DestFile: String; Standalone: Boolean): Boolean;
var
  Content: AnsiString;
  Text: String;
begin
  Result := False;
  ExtractTemporaryFile(TemplateName);
  if not LoadStringFromFile(ExpandConstant('{tmp}\' + TemplateName), Content) then Exit;
  Text := String(Content);
  StringChangeEx(Text, '__DB_CONNECTION__', AppConnection, True);
  StringChangeEx(Text, '__API_KEY__', GrpcApiKey, True);
  // pg_dump / pg_restore de la misma instalación que el servidor
  if PgBaseDir <> '' then
    StringChangeEx(Text, '__PG_BIN__', JsonEscapePath(PgBaseDir + '\bin'), True)
  else
    StringChangeEx(Text, '__PG_BIN__', '', True);
  if Standalone then
    StringChangeEx(Text, '__STANDALONE__', 'true', True)
  else
    StringChangeEx(Text, '__STANDALONE__', 'false', True);
  ForceDirectories(ExtractFileDir(DestFile));
  Result := SaveStringToFile(DestFile, AnsiString(Text), False);
  Log('Configuración escrita: ' + DestFile);
end;

// --- Hosting:Standalone del programa de escritorio según si el sitio web está instalado ---
// Se ajusta en cada instalación: al agregar el sitio a un equipo que sólo tenía el programa, éste deja de aplicar
// migraciones y de recibir relojes ADMS (lo hace el sitio).
procedure SetDesktopStandalone(FileName: String; Standalone: Boolean);
var
  Content: AnsiString;
  Text, Wanted, Other: String;
begin
  if not LoadStringFromFile(FileName, Content) then Exit;
  Text := String(Content);
  if Standalone then
  begin
    Wanted := '"Standalone": true';
    Other := '"Standalone": false';
  end
  else
  begin
    Wanted := '"Standalone": false';
    Other := '"Standalone": true';
  end;
  if Pos(Wanted, Text) > 0 then Exit;
  if StringChangeEx(Text, Other, Wanted, True) > 0 then
  begin
    SaveStringToFile(FileName, AnsiString(Text), False);
    Log('Programa de escritorio: ' + Wanted);
  end
  else
    AddWarning('No se pudo fijar ' + Wanted + ' (Hosting) en ' + FileName + '. Ajústelo a mano.');
end;

function IsJsonSpace(C: Char): Boolean;
begin
  Result := (C = ' ') or (C = #9) or (C = #13) or (C = #10);
end;

// --- Quita "WriteTo": { ... } de la configuración del sitio ---
// La plantilla de v2.3.0-beta.1 escribía "WriteTo": { "3": { "Args": {...} } }, que sólo completa el cuarto sink de un
// appsettings.json base. El instalador no copia ese archivo, así que Serilog fallaba ("Serilog:WriteTo:3:Name") y el
// sitio no arrancaba. Sólo se quita la forma de objeto (índices): una lista ("WriteTo": [...]) declara sinks completos
// y se respeta.
function RemoveSerilogWriteToOverlay(FileName: String): Boolean;
var
  Content: AnsiString;
  Text: String;
  KeyPos, P, Depth, StartPos, EndPos: Integer;
  InString: Boolean;
begin
  Result := True;
  if not LoadStringFromFile(FileName, Content) then Exit;
  Text := String(Content);
  KeyPos := Pos('"WriteTo"', Text);
  if KeyPos = 0 then Exit;

  P := KeyPos + Length('"WriteTo"');
  while (P <= Length(Text)) and IsJsonSpace(Text[P]) do P := P + 1;
  if (P > Length(Text)) or (Text[P] <> ':') then Exit;
  P := P + 1;
  while (P <= Length(Text)) and IsJsonSpace(Text[P]) do P := P + 1;
  if (P > Length(Text)) or (Text[P] <> '{') then Exit;

  // Llave que cierra el objeto (las que haya dentro de textos no cuentan)
  Depth := 0;
  EndPos := 0;
  InString := False;
  while (P <= Length(Text)) and (EndPos = 0) do
  begin
    if InString then
    begin
      if Text[P] = '\' then P := P + 1
      else if Text[P] = '"' then InString := False;
    end
    else if Text[P] = '"' then InString := True
    else if Text[P] = '{' then Depth := Depth + 1
    else if Text[P] = '}' then
    begin
      Depth := Depth - 1;
      if Depth = 0 then EndPos := P;
    end;
    P := P + 1;
  end;
  if EndPos = 0 then Exit;

  // Con la coma que lo separa del miembro anterior o, si era el primero, la del siguiente
  StartPos := KeyPos - 1;
  while (StartPos > 0) and IsJsonSpace(Text[StartPos]) do StartPos := StartPos - 1;
  if (StartPos > 0) and (Text[StartPos] = ',') then
    Delete(Text, StartPos, EndPos - StartPos + 1)
  else
  begin
    P := EndPos + 1;
    while (P <= Length(Text)) and IsJsonSpace(Text[P]) do P := P + 1;
    if (P <= Length(Text)) and (Text[P] = ',') then EndPos := P;
    Delete(Text, KeyPos, EndPos - KeyPos + 1);
  end;

  Result := SaveStringToFile(FileName, AnsiString(Text), False);
  if Result then
    Log('Serilog:WriteTo (sin appsettings.json base) quitado de ' + FileName);
end;

// --- Edición mínima de JSON (sólo lo que necesitan EnsureApiKey y LoadExistingSettings) ---

// Posición de la llave que cierra el objeto que abre Text[OpenPos]; 0 si no cierra
function MatchingBrace(Text: String; OpenPos: Integer): Integer;
var
  P, Depth: Integer;
  InString: Boolean;
begin
  Result := 0;
  Depth := 0;
  InString := False;
  P := OpenPos;
  while (P <= Length(Text)) and (Result = 0) do
  begin
    if InString then
    begin
      if Text[P] = '\' then P := P + 1
      else if Text[P] = '"' then InString := False;
    end
    else if Text[P] = '"' then InString := True
    else if Text[P] = '{' then Depth := Depth + 1
    else if Text[P] = '}' then
    begin
      Depth := Depth - 1;
      if Depth = 0 then Result := P;
    end;
    P := P + 1;
  end;
end;

// Primera aparición de "Key": entre FromPos y ToPos; ValuePos queda en el primer carácter de su valor
function FindJsonMember(Text, Key: String; FromPos, ToPos: Integer; var ValuePos: Integer): Boolean;
var
  P, Q: Integer;
begin
  Result := False;
  P := Pos('"' + Key + '"', Copy(Text, FromPos, ToPos - FromPos + 1));
  if P = 0 then Exit;
  Q := FromPos + P - 1 + Length(Key) + 2;
  while (Q <= ToPos) and IsJsonSpace(Text[Q]) do Q := Q + 1;
  if (Q > ToPos) or (Text[Q] <> ':') then Exit;
  Q := Q + 1;
  while (Q <= ToPos) and IsJsonSpace(Text[Q]) do Q := Q + 1;
  if Q > ToPos then Exit;
  ValuePos := Q;
  Result := True;
end;

// Agrega Member como primer miembro del objeto que abre Text[OpenPos]
procedure InsertJsonMember(var Text: String; OpenPos: Integer; Member, Indent: String);
var
  P: Integer;
  Separator: String;
begin
  P := OpenPos + 1;
  while (P <= Length(Text)) and IsJsonSpace(Text[P]) do P := P + 1;
  if (P <= Length(Text)) and (Text[P] = '}') then
    Separator := ''
  else
    Separator := ',';
  Insert(#13#10 + Indent + Member + Separator, Text, OpenPos + 1);
end;

// --- db_setup.log y lectura de valores de la configuración ---

function DbLogFile: String;
begin
  Result := ExpandConstant('{app}\Logs\db_setup.log');
end;

// Línea con fecha en db_setup.log (y en el log de Inno Setup)
procedure DbLog(LogFile, Text: String);
begin
  Log(Text);
  SaveStringToFile(LogFile, GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + '  ' + Text + #13#10, True);
end;

// "Section": { ... "Key": "valor" } (sin comillas escapadas en el valor)
function ReadJsonSectionString(FileName, Section, Key: String; var Value: String): Boolean;
var
  Content: AnsiString;
  Text, Current: String;
  OpenPos, ClosePos, ValuePos, EndQuote: Integer;
begin
  Result := False;
  if not LoadStringFromFile(FileName, Content) then Exit;
  Text := String(Content);
  OpenPos := 0;
  if not FindJsonMember(Text, Section, 1, Length(Text), OpenPos) or (Text[OpenPos] <> '{') then Exit;
  ClosePos := MatchingBrace(Text, OpenPos);
  if ClosePos = 0 then Exit;
  if not FindJsonMember(Text, Key, OpenPos, ClosePos, ValuePos) or (Text[ValuePos] <> '"') then Exit;
  Current := Copy(Text, ValuePos + 1, ClosePos - ValuePos);
  EndQuote := Pos('"', Current);
  if EndQuote = 0 then Exit;
  Value := Copy(Current, 1, EndQuote - 1);
  Result := Value <> '';
end;

// Las claves de ejemplo de las plantillas y del repositorio son públicas: no sirven como ApiKey del equipo
function IsUsableApiKey(Value: String): Boolean;
begin
  Result := (Value <> '') and (Value <> '__API_KEY__') and (Value <> 'LA_API_KEY_DEL_SERVICIO_ZKTECO') and
    (Value <> 'REEMPLAZAR_CON_UNA_CLAVE_SEGURA_DURANTE_LA_INSTALACION') and (Value <> 'AttendanceSystemSecretApiKey123!');
end;

// ZKTecoService:Url apunta al servicio de este equipo. Sin Url (o con una que no es texto) se usa el valor por
// defecto, http://localhost:5001, así que también cuenta como local.
function UsesLocalService(FileName: String): Boolean;
var
  Url: String;
begin
  Result := True;
  Url := '';
  if not ReadJsonSectionString(FileName, 'ZKTecoService', 'Url', Url) then Exit;
  Url := Lowercase(Url);
  Result := (Pos('://localhost', Url) > 0) or (Pos('://127.0.0.1', Url) > 0) or (Pos('://[::1]', Url) > 0);
end;

// --- La misma ApiKey en el servicio, el sitio y el programa (y el tiempo real por SDK en el sitio) ---
// Una actualización conserva los archivos de configuración, pero uno escrito a mano o por una versión anterior puede
// no traer la ApiKey. El sitio tomaba entonces la de un appsettings.json viejo (la clave de ejemplo) y el servicio
// rechazaba cada llamada ("x-api-key inválida"). GrpcApiKey manda: se agrega si falta y se corrige si no coincide.
// Section vacía es la raíz (el "ApiKey" del servicio); si no, ZKTecoService (el sitio y el programa).
// Realtime:Enabled sólo se agrega si falta (AddRealtime, el sitio); un "false" puesto a mano se respeta.
// Si ZKTecoService:Url apunta a otro equipo, su ApiKey es la de ese servicio: no se toca (queda anotado).
// Cada ApiKey agregada o cambiada queda en db_setup.log (el archivo, nunca la clave).
procedure EnsureApiKey(FileName, Section: String; AddRealtime: Boolean);
var
  Content: AnsiString;
  Text, Original, Current, Name, Extra: String;
  OpenPos, ClosePos, ValuePos, EndQuote: Integer;
  KeepKey: Boolean;
begin
  if GrpcApiKey = '' then Exit;
  if not LoadStringFromFile(FileName, Content) then Exit;
  Text := String(Content);
  Original := Text;
  OpenPos := 0;
  if Section = '' then Name := 'ApiKey' else Name := Section + ':ApiKey';

  if Section = '' then
  begin
    OpenPos := Pos('{', Text);
    if OpenPos = 0 then
    begin
      AddWarning('No se pudo leer ' + FileName + '. Ponga a mano su ApiKey (la del sitio y el programa).');
      Exit;
    end;
  end
  else if not FindJsonMember(Text, Section, 1, Length(Text), OpenPos) or (Text[OpenPos] <> '{') then
  begin
    OpenPos := Pos('{', Text);
    if OpenPos = 0 then
    begin
      AddWarning('No se pudo agregar la sección ' + Section + ' a ' + FileName + '. Agréguela a mano con la ApiKey del servicio.');
      Exit;
    end;
    Extra := '';
    if AddRealtime then Extra := ',' + #13#10 + '    "Realtime": { "Enabled": true }';
    InsertJsonMember(Text, OpenPos,
      '"' + Section + '": {' + #13#10 +
      '    "Url": "http://localhost:5001",' + #13#10 +
      '    "ApiKey": "' + GrpcApiKey + '"' + Extra + #13#10 +
      '  }', '  ');
    if SaveStringToFile(FileName, AnsiString(Text), False) then
      DbLog(DbLogFile, 'Sección ' + Section + ' (con la ApiKey del servicio) agregada a ' + FileName)
    else
      AddWarning('No se pudo escribir la sección ' + Section + ' en ' + FileName + '. Ponga a mano la ApiKey del servicio.');
    Exit;
  end;

  ClosePos := MatchingBrace(Text, OpenPos);
  if ClosePos = 0 then
  begin
    AddWarning('No se pudo leer ' + Name + ' de ' + FileName + '. Revise que sea la del servicio.');
    Exit;
  end;

  KeepKey := (Section <> '') and not UsesLocalService(FileName);
  if KeepKey then
    DbLog(DbLogFile, Name + ' de ' + FileName + ' no se revisó: su ' + Section + ':Url apunta a otro equipo.')
  else if FindJsonMember(Text, 'ApiKey', OpenPos, ClosePos, ValuePos) then
  begin
    Current := Copy(Text, ValuePos + 1, ClosePos - ValuePos);
    EndQuote := Pos('"', Current);
    if (Text[ValuePos] <> '"') or (EndQuote = 0) then
    begin
      AddWarning(Name + ' de ' + FileName + ' no es un texto. Cámbiela a mano por la del servicio.');
      Exit;
    end;
    if Copy(Current, 1, EndQuote - 1) <> GrpcApiKey then
    begin
      Delete(Text, ValuePos + 1, EndQuote - 1);
      Insert(GrpcApiKey, Text, ValuePos + 1);
      DbLog(DbLogFile, Name + ' de ' + FileName + ' no coincidía; se cambió por la del servicio.');
    end;
  end
  else
  begin
    if Section = '' then
      InsertJsonMember(Text, OpenPos, '"ApiKey": "' + GrpcApiKey + '"', '  ')
    else
      InsertJsonMember(Text, OpenPos, '"ApiKey": "' + GrpcApiKey + '"', '    ');
    DbLog(DbLogFile, Name + ' agregada a ' + FileName);
  end;

  // Las ediciones de arriba quedan después de OpenPos: sólo se mueve el cierre
  if AddRealtime then
  begin
    ClosePos := MatchingBrace(Text, OpenPos);
    if (ClosePos > 0) and not FindJsonMember(Text, 'Realtime', OpenPos, ClosePos, ValuePos) then
    begin
      InsertJsonMember(Text, OpenPos, '"Realtime": { "Enabled": true }', '    ');
      Log(Section + ':Realtime:Enabled agregado a ' + FileName);
    end;
  end;

  if Text = Original then Exit;
  if not SaveStringToFile(FileName, AnsiString(Text), False) then
    AddWarning('No se pudo escribir ' + Name + ' en ' + FileName + '. Póngala a mano (la del servicio).');
end;

// --- Lectura de la configuración de una instalación existente ---

// Cadena de conexión y ApiKey de una instalación ya configurada, para configurar la parte que se agrega ahora.
// Instalaciones hechas con versiones anteriores pueden no tener Service\appsettings.Production.json: la ApiKey se toma
// entonces del sitio o del programa (los que ya hablan con el servicio) y, si ninguno tiene una válida, se genera una
// nueva. WriteMissingConfigs la escribe en el servicio y la iguala en el sitio y el programa.
// Si falla, deja el motivo en DbConfigError y en db_setup.log.
function LoadExistingSettings(LogFile: String): Boolean;
var
  AppDir, Value, Source: String;
begin
  Result := False;
  AppDir := ExpandConstant('{app}');
  AppConnection := '';
  GrpcApiKey := '';

  Source := '';
  if ReadJsonString(WebConfigFile(AppDir), 'AttendanceDb', AppConnection) then
    Source := WebConfigFile(AppDir)
  else if ReadJsonString(DesktopConfigFile(AppDir), 'AttendanceDb', AppConnection) then
    Source := DesktopConfigFile(AppDir)
  else if ReadJsonString(AppDir + '\Web\appsettings.json', 'AttendanceDb', AppConnection) then
    Source := AppDir + '\Web\appsettings.json';
  if Source = '' then
  begin
    AppConnection := '';
    DbConfigError := 'No se encontró la cadena de conexión (ConnectionStrings:AttendanceDb) de la instalación existente.';
    DbLog(LogFile, 'Se buscó ConnectionStrings:AttendanceDb en ' + WebConfigFile(AppDir) + ', ' + DesktopConfigFile(AppDir) +
      ' y ' + AppDir + '\Web\appsettings.json (que falten, no tengan la clave o su valor no sea un texto).');
    Exit;
  end;
  DbLog(LogFile, 'Cadena de conexión existente tomada de ' + Source);

  // La ApiKey que valida el servicio y, si no la tiene, la que ya usan el sitio o el programa con este servicio
  // (la de uno que apunta a otro equipo es la de ese servicio)
  Source := '';
  Value := '';
  if ReadJsonString(ServiceConfigFile(AppDir), 'ApiKey', Value) and IsUsableApiKey(Value) then
    Source := ServiceConfigFile(AppDir)
  else if ReadJsonString(AppDir + '\Service\appsettings.json', 'ApiKey', Value) and IsUsableApiKey(Value) then
    Source := AppDir + '\Service\appsettings.json'
  else if UsesLocalService(WebConfigFile(AppDir)) and
    ReadJsonSectionString(WebConfigFile(AppDir), 'ZKTecoService', 'ApiKey', Value) and IsUsableApiKey(Value) then
    Source := WebConfigFile(AppDir) + ' (ZKTecoService)'
  else if UsesLocalService(DesktopConfigFile(AppDir)) and
    ReadJsonSectionString(DesktopConfigFile(AppDir), 'ZKTecoService', 'ApiKey', Value) and IsUsableApiKey(Value) then
    Source := DesktopConfigFile(AppDir) + ' (ZKTecoService)';

  if Source <> '' then
  begin
    GrpcApiKey := Value;
    DbLog(LogFile, 'ApiKey existente tomada de ' + Source);
  end
  else
  begin
    GrpcApiKey := GenerateSecret(32);
    if GrpcApiKey = '' then
    begin
      DbConfigError := 'La instalación existente no tiene una ApiKey válida y no se pudo generar una nueva (se requiere PowerShell).';
      Exit;
    end;
    DbLog(LogFile, 'Ni el servicio, ni el sitio, ni el programa tenían una ApiKey válida: se generó una nueva ' +
      'y se escribe en los tres.');
  end;
  Result := True;
end;

// --- Escribe la configuración que falte de cada parte instalada ---
function WriteMissingConfigs: Boolean;
var
  AppDir: String;
  WebInstalled: Boolean;
begin
  Result := True;
  AppDir := ExpandConstant('{app}');
  WebInstalled := IsWebInstalledAt(AppDir);

  if not FileExists(ServiceConfigFile(AppDir)) then
    Result := WriteConfigFromTemplate('appsettings.Service.Production.template.json', ServiceConfigFile(AppDir), False) and Result
  else
    EnsureApiKey(ServiceConfigFile(AppDir), '', False);
  if WebInstalled and not FileExists(WebConfigFile(AppDir)) then
    Result := WriteConfigFromTemplate('appsettings.Web.Production.template.json', WebConfigFile(AppDir), False) and Result
  // Actualización de v2.3.0-beta.1. Con un appsettings.json base (instalaciones hechas con un Release local) el
  // WriteTo por índices sí completa sus sinks, así que se deja.
  else if WebInstalled and not FileExists(AddBackslash(AppDir) + 'Web\appsettings.json') then
  begin
    if not RemoveSerilogWriteToOverlay(WebConfigFile(AppDir)) then
      AddWarning('No se pudo quitar "WriteTo" de la sección Serilog de ' + WebConfigFile(AppDir) +
        '; el sitio web no arrancará hasta quitarlo a mano.');
  end;
  if WebInstalled and FileExists(WebConfigFile(AppDir)) then
    EnsureApiKey(WebConfigFile(AppDir), 'ZKTecoService', True);
  if IsDesktopInstalledAt(AppDir) then
  begin
    if not FileExists(DesktopConfigFile(AppDir)) then
      Result := WriteConfigFromTemplate('appsettings.Desktop.template.json', DesktopConfigFile(AppDir), not WebInstalled) and Result
    else
    begin
      SetDesktopStandalone(DesktopConfigFile(AppDir), not WebInstalled);
      EnsureApiKey(DesktopConfigFile(AppDir), 'ZKTecoService', False);
    end;
  end;
end;

// --- Configuración de BD (primera vez) y de cada parte instalada ---
// BeforeInstall del registro del servicio: corre después de instalar PostgreSQL y antes de arrancar el servicio.
procedure ConfigureDatabase;
var
  LogFile, OldConnection: String;
  Attempt, ResultCode: Integer;
  Configured: Boolean;
begin
  LogFile := ExpandConstant('{app}\Logs\db_setup.log');
  ForceDirectories(ExtractFileDir(LogFile));

  // Instalación ya configurada: su cadena de conexión y su ApiKey se conservan (o se reapuntan a la base nueva)
  Configured := AppIsConfiguredAt(ExpandConstant('{app}'));
  OldConnection := '';
  if Configured then
  begin
    // El motivo de un fallo queda en DbConfigError y en db_setup.log
    if not LoadExistingSettings(LogFile) then
      Exit;
    OldConnection := AppConnection;
    PreviousDbName := DbNameFromConnection(OldConnection);
  end;

  // Sin base nueva: sólo falta configurar la parte que se agrega (si alguna) con los datos existentes
  if not NeedsDbSetup then
  begin
    if not WriteMissingConfigs then
      DbConfigError := 'No se pudieron escribir los archivos de configuración.';
    Exit;
  end;

  WizardForm.StatusLabel.Caption := 'Configurando base de datos (usuario, base de datos y permisos)...';

  if not IsPostgresDetected then
  begin
    // Guardar la contraseña del superusuario que generamos al instalar PostgreSQL (sólo Administradores y SYSTEM)
    PgSuperPasswordFile := ExpandConstant('{app}\Database\postgres_superuser.txt');
    SaveStringToFile(PgSuperPasswordFile,
      'Contraseña del superusuario "postgres" generada por el instalador de AttendanceSystem:' + #13#10 +
      PgSuperPassword + #13#10, False);
    Exec(ExpandConstant('{sys}\icacls.exe'),
      '"' + PgSuperPasswordFile + '" /inheritance:r /grant:r *S-1-5-32-544:F *S-1-5-18:F',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Ubicar el PostgreSQL recién instalado
    if not DetectPostgres then
    begin
      DbConfigError := 'PostgreSQL no quedó instalado (revise el log del instalador de PostgreSQL en %TEMP%).';
      Exit;
    end;
  end;

  SetEnvironmentVariable('PGPASSWORD', GetPostgresPassword(''));
  try
    // Esperar a que PostgreSQL acepte conexiones (recién instalado puede tardar en arrancar)
    ResultCode := -1;
    for Attempt := 1 to 30 do
    begin
      ResultCode := RunPsql('-d postgres -c "SELECT 1"', LogFile);
      if ResultCode = 0 then Break;
      Sleep(2000);
    end;
    if ResultCode <> 0 then
    begin
      DbConfigError := Format('No fue posible conectarse a PostgreSQL %d (puerto %d) como "postgres".', [PgMajor, PgPort]);
      Exit;
    end;

    if CreateNewDb and not RenameExistingDatabase(LogFile) then
    begin
      DbConfigError := 'No se pudo apartar la base de datos ' + AppDbName + ' existente (¿sigue en uso?). No se modificó nada.';
      RenamedDbName := '';
      Exit;
    end;

    ResultCode := RunPsql(
      '-v ON_ERROR_STOP=1 -d postgres -v app_password=' + AppDbPassword +
      ' -f "' + ExpandConstant('{app}\Database\init_db.sql') + '"', LogFile);
    if ResultCode <> 0 then
    begin
      DbConfigError := 'Falló la creación del usuario y la base de datos (código ' + IntToStr(ResultCode) + ').';
      Exit;
    end;
  finally
    SetEnvironmentVariable('PGPASSWORD', '');
  end;

  // Sólo con la BD lista se escriben las configuraciones: si falta alguna, la siguiente ejecución lo reintenta todo.
  AppConnection := AppConnectionString;
  if (OldConnection <> '') and not ReplaceConnectionInConfigs(OldConnection, AppConnection) then
    DbConfigError := 'No se pudo cambiar la cadena de conexión en los archivos de configuración.'
  else if not WriteMissingConfigs then
    DbConfigError := 'No se pudieron escribir los archivos de configuración.';
end;

// ============================================================================================
// Verificación final (AfterInstall del último paso de [Run])
// ============================================================================================

// --- Helpers to find system binaries bypassing WOW64 redirection ---
function GetAppCmdPath: String;
begin
  if FileExists(ExpandConstant('{sysnative}\inetsrv\appcmd.exe')) then
    Result := ExpandConstant('{sysnative}\inetsrv\appcmd.exe')
  else if FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe')) then
    Result := ExpandConstant('{sys}\inetsrv\appcmd.exe')
  else
    Result := '';
end;

function IsServiceRunning(ServiceName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'),
    '/C ""' + ExpandConstant('{sys}\sc.exe') + '" query "' + ServiceName + '" | "' + ExpandConstant('{sys}\find.exe') + '" "RUNNING""',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure VerifyInstallation;
var
  AppCmdPath: String;
  ResultCode, Attempt: Integer;
  Running: Boolean;
begin
  WizardForm.StatusLabel.Caption := 'Verificando la instalación...';

  if DbConfigError <> '' then
  begin
    // Algunos fallos ocurren antes de correr psql: sin esto, el log al que remite el aviso podía no existir
    DbLog(ExpandConstant('{app}\Logs\db_setup.log'), 'ERROR: ' + DbConfigError);
    AddWarning('La base de datos NO quedó configurada: ' + DbConfigError + ' Detalle en ' + ExpandConstant('{app}\Logs\db_setup.log') + '.');
  end;

  if IsDesktopInstalledAt(ExpandConstant('{app}')) and not FileExists(DesktopConfigFile(ExpandConstant('{app}'))) then
    AddWarning('El programa de escritorio no tiene su archivo de configuración (' + DesktopConfigFile(ExpandConstant('{app}')) + ').');

  // Sin sitio web no hay nada de IIS que verificar
  if WizardIsComponentSelected('web') then
  begin
    AppCmdPath := GetAppCmdPath;
    if AppCmdPath = '' then
      AddWarning('IIS no quedó habilitado (puede requerir reiniciar Windows). Detalle en ' + ExpandConstant('{app}\Logs\enable_iis.log') + '.')
    else
    begin
      if (GetAncmMajor < RequiredAncmMajor) or not IsAncmRegisteredInIis then
        AddWarning(Format('El módulo ASP.NET Core %d.x de IIS no quedó instalado o registrado. Detalle en %s.', [
          RequiredAncmMajor, ExpandConstant('{app}\Logs\dotnet_hosting.log')]));
      if not Exec(AppCmdPath, 'list site "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        AddWarning('No se creó el sitio "AttendanceSystem" en IIS. Detalle en ' + ExpandConstant('{app}\Logs\iis_setup.log') + '.');
    end;
  end;

  // El servicio puede tardar unos segundos en pasar de START_PENDING a RUNNING
  Running := False;
  for Attempt := 1 to 10 do
  begin
    Running := IsServiceRunning('AttendanceSystem.ZKTeco.Service');
    if Running then Break;
    Sleep(1000);
  end;
  if not Running then
    AddWarning('El servicio AttendanceSystem.ZKTeco.Service no está en ejecución. Revise ' + ExpandConstant('{app}\Service\logs') + '.');

  if InstallWarnings <> '' then
    SuppressibleMsgBox('La instalación terminó con problemas:' + #13#10#13#10 + InstallWarnings + #13#10 +
      'Corrija lo indicado y vuelva a ejecutar el instalador.', mbError, MB_OK, IDOK);
end;

// ============================================================================================
// Asistente
// ============================================================================================

function InitializeSetup: Boolean;
begin
  Result := True;
  IsPostgresDetected := DetectPostgres;
  if not RegQueryStringValue(HKLM, UninstallKey, 'Inno Setup: App Path', PrevAppPath) then
    PrevAppPath := '';
  PreviousInstallConfigured := AppIsConfiguredAt(PrevAppPath);
  SavedSuperPassword := ReadSavedSuperPassword(PrevAppPath);
  // /NEWDATABASE: crear una base nueva también en instalaciones silenciosas (la actual se conserva renombrada)
  CreateNewDb := HasParam('/NEWDATABASE');

  // Versión instalada frente a la de este instalador
  if not RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', PrevVersion) then
    PrevVersion := '';
  VersionCompare := 1;
  if PrevVersion <> '' then
  begin
    VersionCompare := CompareVersions('{#MyAppVersion}', PrevVersion);
    Log(Format('Versión instalada %s, este instalador %s: %d', [PrevVersion, '{#MyAppVersion}', VersionCompare]));
  end;

  // Una versión anterior no conoce las migraciones que la instalada ya aplicó a la base de datos
  if VersionCompare < 0 then
  begin
    if HasParam('/ALLOWDOWNGRADE') then
      Log('Instalación de una versión anterior permitida con /ALLOWDOWNGRADE')
    else
    begin
      SuppressibleMsgBox('Ya está instalada la versión ' + PrevVersion + ' de AttendanceSystem, posterior a la de este instalador ({#MyAppVersion}).' + #13#10#13#10 +
        'No se puede instalar una versión anterior: la base de datos ya tiene cambios que esa versión no conoce y la aplicación podría no arrancar o dañar datos.' + #13#10#13#10 +
        'Use el instalador de la versión ' + PrevVersion + ' o una posterior.', mbCriticalError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
  end;

  if IsPostgresDetected and (PgMajor < MinPgMajor) and not PreviousInstallConfigured then
  begin
    SuppressibleMsgBox(Format('Se detectó PostgreSQL %d en %s. AttendanceSystem requiere PostgreSQL %d o posterior.' + #13#10 +
      'Actualice PostgreSQL y vuelva a ejecutar el instalador.', [PgMajor, PgBaseDir, MinPgMajor]), mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure InitializeWizard;
var
  AfterId: Integer;
  Subtitle, Info: String;
begin
  AfterId := wpWelcome;

  // Instalación existente: qué versión se instala sobre cuál y qué hacer con la base de datos
  if PrevVersion <> '' then
  begin
    if VersionCompare > 0 then
    begin
      Subtitle := 'Actualización de la versión ' + PrevVersion + ' a la {#MyAppVersion}';
      Info := 'Se actualizará AttendanceSystem. Los archivos se reemplazan; la configuración y los datos se conservan.';
    end
    else if VersionCompare = 0 then
    begin
      Subtitle := 'Reinstalación de la versión {#MyAppVersion}';
      Info := 'La versión {#MyAppVersion} ya está instalada. Se reinstalará (reparación): los archivos se reemplazan; la configuración y los datos se conservan.';
    end
    else
    begin
      Subtitle := 'Instalación de una versión ANTERIOR ({#MyAppVersion} sobre ' + PrevVersion + ')';
      Info := 'ATENCIÓN: se instalará una versión anterior a la instalada (/ALLOWDOWNGRADE). La base de datos puede tener cambios que esta versión no conoce.';
    end;

    VersionPage := CreateInputOptionPage(AfterId, 'Instalación existente', Subtitle, Info, True, False);
    // La base nueva necesita un PostgreSQL local y una instalación ya configurada
    if PreviousInstallConfigured and IsPostgresDetected then
    begin
      VersionPage.Add('Conservar la base de datos actual y sus datos (recomendado)');
      VersionPage.Add('Crear una base de datos nueva y vacía (la actual se conserva, renombrada, y no se borra)');
      if CreateNewDb then
        VersionPage.SelectedValueIndex := 1
      else
        VersionPage.SelectedValueIndex := 0;
    end;
    AfterId := VersionPage.ID;
  end;

  // Instalaciones anteriores a la elección de componentes sólo tenían el sitio web: no agregar el programa por defecto
  if (PrevAppPath <> '') and not RegValueExists(HKLM, UninstallKey, 'Inno Setup: Selected Components') then
  begin
    if IsDesktopInstalledAt(PrevAppPath) then
      WizardSelectComponents('web,desktop')
    else
      WizardSelectComponents('web,!desktop');
  end;

  // Contraseña de postgres: para crear la base (primera vez, o base nueva sin contraseña guardada).
  // Se crea siempre que hay un PostgreSQL; ShouldSkipPage decide si se muestra.
  if IsPostgresDetected then
  begin
    PostgresPage := CreateInputQueryPage(AfterId,
      'Configuración de Base de Datos',
      'PostgreSQL detectado en el sistema',
      Format('Se usará PostgreSQL %d instalado en %s (puerto %d).', [PgMajor, PgBaseDir, PgPort]) + #13#10 +
      'Ingrese la contraseña del usuario "postgres" para crear la base de datos de la aplicación:');

    PostgresPage.Add('Contraseña de superusuario (postgres):', True); // True = password mask

    // Valores por defecto
    PostgresPage.Values[0] := '';

    // Primera instalación sobre una base AttendanceSystem que ya existe (p. ej. de una instalación desinstalada)
    if not PreviousInstallConfigured then
    begin
      ExistingDbPage := CreateInputOptionPage(PostgresPage.ID, 'Base de datos existente',
        'Ya existe una base de datos ' + AppDbName + ' en este PostgreSQL',
        'Puede usarla con sus datos (por ejemplo, si reinstala después de desinstalar) o empezar con una base vacía.', True, False);
      ExistingDbPage.Add('Usar la base de datos existente y sus datos');
      ExistingDbPage.Add('Crear una base de datos nueva y vacía (la existente se conserva, renombrada, y no se borra)');
      if CreateNewDb then
        ExistingDbPage.SelectedValueIndex := 1
      else
        ExistingDbPage.SelectedValueIndex := 0;
    end;
  end;
end;

// ¿Hace falta pedir la contraseña de postgres?
function PostgresPasswordNeeded: Boolean;
begin
  Result := IsPostgresDetected and ((not PreviousInstallConfigured) or (CreateNewDb and (SavedSuperPassword = '')));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PostgresPage <> nil) and (PageID = PostgresPage.ID) then
    Result := not PostgresPasswordNeeded
  else if (ExistingDbPage <> nil) and (PageID = ExistingDbPage.ID) then
    Result := not ExistingDbFound;
end;

// --- Selección de componentes: al menos uno, y sin quitar uno ya instalado ---
// Inno no borra los archivos de un componente que se desmarca; para quitar una parte hay que desinstalar.
function ComponentSelectionError(AppDir: String): String;
begin
  Result := '';
  if not WizardIsComponentSelected('web') and not WizardIsComponentSelected('desktop') then
    Result := 'Seleccione el sitio web, el programa de escritorio o ambos.'
  else if IsWebInstalledAt(AppDir) and not WizardIsComponentSelected('web') then
    Result := 'El sitio web ya está instalado en este equipo. Para quitarlo, desinstale AttendanceSystem y vuelva a instalar sólo lo que necesite.'
  else if IsDesktopInstalledAt(AppDir) and not WizardIsComponentSelected('desktop') then
    Result := 'El programa de escritorio ya está instalado en este equipo. Para quitarlo, desinstale AttendanceSystem y vuelva a instalar sólo lo que necesite.';
end;

// --- Validación de Página Siguiente ---
function NextButtonClick(CurPageID: Integer): Boolean;
var
  SelectionError: String;
begin
  Result := True;

  // Base nueva elegida en una instalación existente: si hay contraseña guardada, comprobar que sigue sirviendo
  if (VersionPage <> nil) and (CurPageID = VersionPage.ID) and (VersionPage.SelectedValueIndex >= 0) then
  begin
    CreateNewDb := VersionPage.SelectedValueIndex = 1;
    if CreateNewDb and (SavedSuperPassword <> '') and
       not TestPostgresConnection(SavedSuperPassword, ExpandConstant('{tmp}\pg_check.log')) then
      SavedSuperPassword := ''; // ya no sirve (se cambió): se pedirá en la página siguiente
  end;

  if (ExistingDbPage <> nil) and (CurPageID = ExistingDbPage.ID) then
    CreateNewDb := ExistingDbPage.SelectedValueIndex = 1;

  if CurPageID = wpSelectComponents then
  begin
    SelectionError := ComponentSelectionError(WizardDirValue);
    if SelectionError <> '' then
    begin
      MsgBox(SelectionError, mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  // Guardar y comprobar la contraseña si estamos en nuestra página personalizada
  if (PostgresPage <> nil) and (CurPageID = PostgresPage.ID) then
  begin
    UserPostgresPassword := PostgresPage.Values[0];
    if Length(UserPostgresPassword) = 0 then
    begin
      MsgBox('Por favor ingrese la contraseña de PostgreSQL para continuar.', mbError, MB_OK);
      Result := False;
    end
    else if not TestPostgresConnection(UserPostgresPassword, ExpandConstant('{tmp}\pg_check.log')) then
    begin
      MsgBox(Format('No fue posible conectarse a PostgreSQL %d (puerto %d) como "postgres".', [PgMajor, PgPort]) + #13#10 +
        'Verifique la contraseña y que el servicio de PostgreSQL esté iniciado.', mbError, MB_OK);
      Result := False;
    end
    else if not PreviousInstallConfigured then
      // Primera instalación: ¿ya hay una base AttendanceSystem? (decide si se muestra la página siguiente)
      ExistingDbFound := AppDatabaseExists(UserPostgresPassword);
  end;
end;

// --- Antes de instalar: decidir si es primera instalación y generar secretos ---
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // También en instalaciones silenciosas, que no pasan por la página de componentes
  Result := ComponentSelectionError(ExpandConstant('{app}'));
  if Result <> '' then Exit;

  // Una base nueva sólo tiene sentido con un PostgreSQL ya instalado (si no, la base es nueva de todos modos)
  if not IsPostgresDetected then
    CreateNewDb := False;
  NeedsDbSetup := (not AppIsConfiguredAt(ExpandConstant('{app}'))) or CreateNewDb;

  if NeedsDbSetup and IsPostgresDetected and (GetPostgresPassword('') = '') then
  begin
    if CreateNewDb then
      Result := 'Para crear una base de datos nueva se necesita la contraseña del usuario "postgres". ' +
        'Ejecute el instalador sin /SILENT ni /VERYSILENT para ingresarla.'
    else
      Result := 'Esta carpeta no tiene una instalación configurada y PostgreSQL ya existe en el equipo. ' +
        'Ejecute de nuevo el instalador y elija la carpeta de la instalación anterior, o ingrese la contraseña de "postgres".';
    Exit;
  end;

  if not IsPostgresDetected then
  begin
    PgSuperPassword := GenerateSecret(16);
    if PgSuperPassword = '' then
    begin
      Result := 'No se pudo generar la contraseña de PostgreSQL (se requiere PowerShell).';
      Exit;
    end;
  end;

  if NeedsDbSetup then
  begin
    AppDbPassword := GenerateSecret(24);
    // En una instalación ya configurada la ApiKey se conserva (ConfigureDatabase la lee de la configuración)
    GrpcApiKey := GenerateSecret(32);
    if (AppDbPassword = '') or (GrpcApiKey = '') then
      Result := 'No se pudieron generar las credenciales de la aplicación (se requiere PowerShell).';
  end;
end;

// --- Pantalla final: avisar de problemas o de dónde quedó la contraseña de postgres ---
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> wpFinished then Exit;

  if InstallWarnings <> '' then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'ATENCIÓN, la instalación terminó con problemas:' + #13#10 + InstallWarnings
  else
  begin
    if WizardIsComponentSelected('desktop') and not WizardIsComponentSelected('web') then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'Sin sitio web, las tareas automáticas (descargas, respaldos, reportes) y la recepción de relojes ADMS ' +
        'sólo funcionan mientras el programa de escritorio está abierto.';
    if PgSuperPasswordFile <> '' then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'La contraseña del superusuario "postgres" quedó en ' + PgSuperPasswordFile +
        ' (sólo Administradores). Guárdela en un lugar seguro.';
    if RenamedDbName <> '' then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'Se creó una base de datos nueva y vacía. La anterior se conserva como "' + RenamedDbName + '".'
    else if CreateNewDb and (PreviousDbName <> '') and (PreviousDbName <> AppDbName) then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
        'Se creó la base de datos nueva "' + AppDbName + '". La anterior ("' + PreviousDbName + '") no se modificó.';
  end;
end;

function GetNetPath: String;
begin
  if FileExists(ExpandConstant('{sysnative}\net.exe')) then
    Result := ExpandConstant('{sysnative}\net.exe')
  else if FileExists(ExpandConstant('{sys}\net.exe')) then
    Result := ExpandConstant('{sys}\net.exe')
  else
    Result := '';
end;

function GetTaskKillPath: String;
begin
  if FileExists(ExpandConstant('{sysnative}\taskkill.exe')) then
    Result := ExpandConstant('{sysnative}\taskkill.exe')
  else if FileExists(ExpandConstant('{sys}\taskkill.exe')) then
    Result := ExpandConstant('{sys}\taskkill.exe')
  else
    Result := '';
end;

// --- Force kill a process if it is holding a lock ---
procedure TaskKillProcess(ProcessName: String);
var
  ResultCode: Integer;
  TaskKillPath: String;
begin
  TaskKillPath := GetTaskKillPath;
  if TaskKillPath <> '' then
  begin
    Exec(TaskKillPath, '/f /im "' + ProcessName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

// --- Dynamic wait loop for locked files ---
procedure WaitForFileUnlock(FileName: String);
var
  Limit: Integer;
  TempName: String;
begin
  if not FileExists(FileName) then Exit;

  Limit := 0;
  TempName := FileName + '.tmp_test_lock';

  // Attempt to rename the file. If it fails, it is locked.
  while (Limit < 20) do
  begin
    if RenameFile(FileName, TempName) then
    begin
      // If we successfully renamed it, it is not locked.
      // Rename it back to its original name.
      RenameFile(TempName, FileName);
      Exit;
    end;
    Sleep(500);
    Limit := Limit + 1;
  end;

  // If still locked after 10 seconds (20 * 500ms), force kill the processes
  if Limit >= 20 then
  begin
    TaskKillProcess('w3wp.exe');
    TaskKillProcess('AttendanceSystem.Blazor.Server.exe');
    Sleep(1000); // Give the system a second to release locks
  end;
end;

// --- DETENER/INICIAR SERVICIOS AL ACTUALIZAR ---
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  AppCmdPath: String;
  NetPath: String;
  WebDllPath: String;
begin
  if CurStep = ssInstall then
  begin
    NetPath := GetNetPath;

    // 1. Detener Servicio Windows
    if NetPath <> '' then
      Exec(NetPath, 'stop AttendanceSystem.ZKTeco.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    else
      Exec(ExpandConstant('{sys}\sc.exe'), 'stop AttendanceSystem.ZKTeco.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 2. Detener Sitio IIS y AppPool
    AppCmdPath := GetAppCmdPath;
    if AppCmdPath <> '' then
    begin
      Exec(AppCmdPath, 'stop site "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec(AppCmdPath, 'stop apppool "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

    // 3. Esperar dinámicamente a que IIS libere el DLL del módulo en proceso
    WebDllPath := ExpandConstant('{app}\Web\aspnetcorev2_inprocess.dll');
    WaitForFileUnlock(WebDllPath);
  end
  else if CurStep = ssPostInstall then
  begin
    // Iniciar sitio IIS y AppPool automáticamente si ya están configurados
    AppCmdPath := GetAppCmdPath;
    if AppCmdPath <> '' then
    begin
      Exec(AppCmdPath, 'start apppool "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec(AppCmdPath, 'start site "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
