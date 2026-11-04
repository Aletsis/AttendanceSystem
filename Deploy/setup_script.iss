; Script generado para Inno Setup
; Este script crea un instalador "Todo en Uno" para AttendanceSystem + PostgreSQL

#ifndef MyAppName
#define MyAppName "AttendanceSystem"
#endif

#ifndef MyAppVersion
#define MyAppVersion "2.3.0"
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
; ARQUITECTURA: Importante para que detecte bien claves de registro de 64 bits y System32
ArchitecturesInstallIn64BitMode=x64

OutputBaseFilename=AttendanceSystem_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Dirs]
Name: "{app}\Logs"
Name: "{app}\Backups"

[Files]
; 1. Archivos de la Aplicación Web
; appsettings.Production.json NO se copia: lo genera el instalador en la primera instalación (cadena de conexión y
; ApiKey propias del equipo) y una actualización lo conserva, junto con lo que el cliente haya cambiado en él.
Source: "..\Release\Web\*"; DestDir: "{app}\Web"; Excludes: "appsettings.Production.json"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. Archivos del Servicio Windows (incluyendo SDK ZKTeco)
Source: "..\Release\Service\*"; DestDir: "{app}\Service"; Excludes: "appsettings.Production.json"; Flags: ignoreversion recursesubdirs createallsubdirs

; Plantillas de appsettings.Production.json (se extraen a {tmp} sólo en la primera instalación)
Source: "appsettings.Web.Production.template.json"; Flags: dontcopy
Source: "appsettings.Service.Production.template.json"; Flags: dontcopy

; 2.1 Archivos del SDK ZKTeco para System32 y SysWOW64
Source: "..\src\Infrastructure\AttendanceSystem.ZKTeco\lib\*"; DestDir: "{sys}"; Flags: ignoreversion sharedfile restartreplace
Source: "..\src\Infrastructure\AttendanceSystem.ZKTeco\lib\*"; DestDir: "{syswow64}"; Check: IsWin64; Flags: ignoreversion sharedfile restartreplace

; 3. Scripts SQL y Herramientas
Source: "init_db.sql"; DestDir: "{app}\Database"; Flags: ignoreversion
Source: "enable_iis.ps1"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "configure_iis.bat"; DestDir: "{app}\Tools"; Flags: ignoreversion

; 4. Instaladores de Prerrequisitos (DEBEN ESTAR EN LA CARPETA DEPLOY)
Source: "postgresql-installer.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "dotnet-hosting.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; 1. Instalar PostgreSQL silenciosamente (SOLO SI NO ESTÁ INSTALADO)
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
; 5. Registrar SDK ZKTeco en System32
Filename: "{sys}\regsvr32.exe"; Parameters: "/s ""{sys}\zkemkeeper.dll"""; \
    Flags: runhidden; StatusMsg: "Registrando librerías ZKTeco en System32..."; WorkingDir: "{sys}"

; 5.1 Registrar SDK ZKTeco en SysWOW64 (si es 64 bits)
Filename: "{syswow64}\regsvr32.exe"; Parameters: "/s ""{syswow64}\zkemkeeper.dll"""; \
    Flags: runhidden; StatusMsg: "Registrando librerías ZKTeco en SysWOW64..."; WorkingDir: "{syswow64}"; Check: IsWin64

; 6. Esperar un momento antes de iniciar (el registro del COM puede tardar)
Filename: "timeout"; Parameters: "/t 5"; Flags: runhidden; StatusMsg: "Esperando registro del SDK ZKTeco..."

; 7. Iniciar el Servicio ZKTeco
Filename: "{sys}\sc.exe"; Parameters: "start AttendanceSystem.ZKTeco.Service"; \
    Flags: runhidden; StatusMsg: "Iniciando servicio..."

; 7. Abrir Puertos en Firewall para Web: 80, o 8081 si IIS no pudo usar el 80 (el servicio gRPC 5001 se mantiene
; local/loopback por seguridad H02). Se borra antes la regla para no acumular duplicados en cada actualización.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AttendanceSystem Web"""; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AttendanceSystem Web"" dir=in action=allow protocol=TCP localport=80,8081"; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."

; 8. Habilitar IIS y Componentes. La web sólo se hospeda en IIS (no hay modo independiente: no se registra
; como servicio ni tiene acceso directo), así que este paso y el 10 son obligatorios.
Filename: "powershell.exe"; \
    Parameters: "-ExecutionPolicy Bypass -File ""{app}\Tools\enable_iis.ps1"""; \
    StatusMsg: "Habilitando características de Windows (IIS)..."; Flags: runhidden 64bit

; 9. Instalar ASP.NET Core Hosting Bundle (SOLO SI FALTA)
Filename: "{tmp}\dotnet-hosting.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Instalando ASP.NET Core Runtime & Hosting Bundle..."; \
    Check: DotNetNotInstalled

; 10. Configurar IIS (AppPool y Sitio)
Filename: "{app}\Tools\configure_iis.bat"; Parameters: """{app}\Web"""; \
    StatusMsg: "Configurando Sitio Web en IIS..."; Flags: runhidden 64bit

[UninstallRun]
; Eliminar sitio IIS al desinstalar
Filename: "{sys}\inetsrv\appcmd.exe"; Parameters: "delete site ""AttendanceSystem"""; Flags: runhidden 64bit; RunOnceId: "DeleteIisSite"
Filename: "{sys}\inetsrv\appcmd.exe"; Parameters: "delete apppool ""AttendanceSystem"""; Flags: runhidden 64bit; RunOnceId: "DeleteIisAppPool"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AttendanceSystem Web"""; Flags: runhidden; RunOnceId: "DeleteFirewallRule"
; Detener y eliminar el servicio de Windows
; IMPORTANTE: El nombre debe coincidir con el usado en [Run] y en Program.cs
Filename: "{sys}\sc.exe"; Parameters: "stop AttendanceSystem.ZKTeco.Service"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete AttendanceSystem.ZKTeco.Service"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
const
  AppDbName = 'AttendanceSystem';
  AppDbUser = 'attendancesystem_user';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-E5F6-7890-1234-56789ABCDEF0}_is1';

var
  PostgresPage: TInputQueryWizardPage;
  UserPostgresPassword: String;
  IsPostgresDetected: Boolean;
  // Una instalación previa ya dejó configurada la aplicación (existe su appsettings.Production.json)
  PreviousInstallConfigured: Boolean;
  // Esta ejecución debe crear usuario/BD y escribir los appsettings.Production.json
  NeedsAppConfig: Boolean;
  // Secretos generados en esta instalación (hexadecimales: seguros en JSON, cadenas de conexión y línea de comandos)
  PgSuperPassword: String;
  AppDbPassword: String;
  GrpcApiKey: String;
  // Resultado de ConfigureDatabase, para la pantalla final
  DbConfigError: String;
  PgSuperPasswordFile: String;

function SetEnvironmentVariable(lpName: String; lpValue: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

// --- Detección de Postgres ---
function PostgresNotInstalled: Boolean;
begin
  // Verificamos registro de versiones comunes de Postgres (15 o 16)
  Result := True;
  
  if RegKeyExists(HKLM, 'SOFTWARE\PostgreSQL\Installations\postgresql-x64-16') or
     RegKeyExists(HKLM, 'SOFTWARE\PostgreSQL\Installations\postgresql-x64-15') or
     RegKeyExists(HKLM, 'SOFTWARE\PostgreSQL\Installations\postgresql-x64-14') then
  begin
    Result := False;
  end;
  
  // Como fallback, buscamos si existe el servicio
  if not Result then begin
     // Ya detectamos por registro
  end else begin
     // Verificamos carpetas por defecto
     if DirExists(ExpandConstant('{pf}\PostgreSQL\16')) or DirExists(ExpandConstant('{pf}\PostgreSQL\15')) then
        Result := False;
  end;
end;

// --- Detección de .NET Hosting Bundle ---
function DotNetNotInstalled: Boolean;
begin
  // Verificamos si existe la clave de ASP.NET Core Shared Framework v9.0
  // La ruta suele ser SOFTWARE\Microsoft\ASP.NET Core\Shared Framework\v9.0
  Result := not RegKeyExists(HKLM, 'SOFTWARE\Microsoft\ASP.NET Core\Shared Framework\v9.0');
end;

// --- Obtener Ruta de PSQL ---
function GetPostgresCLIPath(Param: String): String;
begin
  // Intentar encontrar psql.exe en versiones conocidas
  if FileExists(ExpandConstant('{pf}\PostgreSQL\16\bin\psql.exe')) then
    Result := ExpandConstant('{pf}\PostgreSQL\16\bin\psql.exe')
  else if FileExists(ExpandConstant('{pf}\PostgreSQL\15\bin\psql.exe')) then
    Result := ExpandConstant('{pf}\PostgreSQL\15\bin\psql.exe')
  else if FileExists(ExpandConstant('{pf}\PostgreSQL\14\bin\psql.exe')) then
    Result := ExpandConstant('{pf}\PostgreSQL\14\bin\psql.exe')
  else
    Result := 'psql.exe'; // Esperar que esté en el PATH
end;

// --- Retornar Contraseña del superusuario postgres ---
function GetPostgresPassword(Param: String): String;
begin
  if IsPostgresDetected then
    Result := UserPostgresPassword // La que ingresó el usuario
  else
    Result := PgSuperPassword; // La generada para el PostgreSQL que instalamos nosotros
end;

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
  Result := 'Host=localhost;Port=5432;Database=' + AppDbName + ';Username=' + AppDbUser + ';Password=' + AppDbPassword + ';';
end;

// --- Escribe un appsettings.Production.json a partir de su plantilla ---
function WriteConfigFromTemplate(TemplateName, DestFile: String): Boolean;
var
  Content: AnsiString;
  Text: String;
begin
  Result := False;
  ExtractTemporaryFile(TemplateName);
  if not LoadStringFromFile(ExpandConstant('{tmp}\' + TemplateName), Content) then Exit;
  Text := String(Content);
  StringChangeEx(Text, '__DB_CONNECTION__', AppConnectionString, True);
  StringChangeEx(Text, '__API_KEY__', GrpcApiKey, True);
  ForceDirectories(ExtractFileDir(DestFile));
  Result := SaveStringToFile(DestFile, AnsiString(Text), False);
end;

// --- Ejecuta psql como postgres (contraseña por PGPASSWORD) y anexa su salida al log ---
function RunPsql(PsqlPath, Args, LogFile: String): Integer;
var
  ResultCode: Integer;
begin
  // -w: nunca pedir contraseña (en una ventana oculta psql se quedaría esperando)
  if not Exec(ExpandConstant('{cmd}'),
    '/C ""' + PsqlPath + '" -w -h localhost -p 5432 -U postgres ' + Args + ' >> "' + LogFile + '" 2>&1"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

// --- Configuración de BD y appsettings (sólo primera instalación) ---
// BeforeInstall del registro del servicio: corre después de instalar PostgreSQL y antes de arrancar el servicio.
procedure ConfigureDatabase;
var
  PsqlPath, LogFile: String;
  Attempt, ResultCode: Integer;
begin
  if not NeedsAppConfig then Exit;

  WizardForm.StatusLabel.Caption := 'Configurando base de datos (usuario, base de datos y permisos)...';
  LogFile := ExpandConstant('{app}\Logs\db_setup.log');
  ForceDirectories(ExtractFileDir(LogFile));

  // Guardar la contraseña del superusuario que generamos al instalar PostgreSQL (sólo Administradores y SYSTEM)
  if not IsPostgresDetected then
  begin
    PgSuperPasswordFile := ExpandConstant('{app}\Database\postgres_superuser.txt');
    SaveStringToFile(PgSuperPasswordFile,
      'Contraseña del superusuario "postgres" generada por el instalador de AttendanceSystem:' + #13#10 +
      PgSuperPassword + #13#10, False);
    Exec(ExpandConstant('{sys}\icacls.exe'),
      '"' + PgSuperPasswordFile + '" /inheritance:r /grant:r *S-1-5-32-544:F *S-1-5-18:F',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  PsqlPath := GetPostgresCLIPath('');
  if not FileExists(PsqlPath) then
  begin
    DbConfigError := 'No se encontró psql.exe de PostgreSQL 14, 15 o 16.';
    Exit;
  end;

  SetEnvironmentVariable('PGPASSWORD', GetPostgresPassword(''));
  try
    // Esperar a que PostgreSQL acepte conexiones (recién instalado puede tardar en arrancar)
    ResultCode := -1;
    for Attempt := 1 to 30 do
    begin
      ResultCode := RunPsql(PsqlPath, '-d postgres -c "SELECT 1"', LogFile);
      if ResultCode = 0 then Break;
      Sleep(2000);
    end;
    if ResultCode <> 0 then
    begin
      DbConfigError := 'No fue posible conectarse a PostgreSQL como "postgres" (¿contraseña incorrecta o servicio detenido?).';
      Exit;
    end;

    ResultCode := RunPsql(PsqlPath,
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

  // Sólo con la BD lista se escriben los appsettings: si falta alguno, la siguiente ejecución lo reintenta todo.
  if not WriteConfigFromTemplate('appsettings.Service.Production.template.json', ExpandConstant('{app}\Service\appsettings.Production.json')) or
     not WriteConfigFromTemplate('appsettings.Web.Production.template.json', ExpandConstant('{app}\Web\appsettings.Production.json')) then
    DbConfigError := 'No se pudieron escribir los archivos appsettings.Production.json.';
end;

// --- Inicialización del ASISTENTE ---
procedure InitializeWizard;
var
  PrevAppPath: String;
begin
  IsPostgresDetected := not PostgresNotInstalled;
  PreviousInstallConfigured :=
    RegQueryStringValue(HKLM, UninstallKey, 'Inno Setup: App Path', PrevAppPath) and
    FileExists(AddBackslash(PrevAppPath) + 'Web\appsettings.Production.json');

  // Si Postgres YA existe y hay que configurar la BD, pedimos contraseña (en una actualización no hace falta)
  if IsPostgresDetected and not PreviousInstallConfigured then
  begin
    PostgresPage := CreateInputQueryPage(wpWelcome,
      'Configuración de Base de Datos',
      'PostgreSQL detectado en el sistema',
      'Se ha detectado una instalación existente de PostgreSQL.' + #13#10 +
      'Por favor, ingrese la contraseña del usuario "postgres" para configurar la base de datos de la aplicación:');
    
    PostgresPage.Add('Contraseña de superusuario (postgres):', True); // True = password mask
    
    // Valores por defecto
    PostgresPage.Values[0] := '';
  end;
end;

// --- Validación de Página Siguiente ---
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  
  // Guardar la contraseña si estamos en nuestra página personalizada
  if (PostgresPage <> nil) and (CurPageID = PostgresPage.ID) then
  begin
    UserPostgresPassword := PostgresPage.Values[0];
    if Length(UserPostgresPassword) = 0 then
    begin
      MsgBox('Por favor ingrese la contraseña de PostgreSQL para continuar.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// --- Antes de instalar: decidir si es primera instalación y generar secretos ---
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsAppConfig := not FileExists(ExpandConstant('{app}\Web\appsettings.Production.json'));

  if NeedsAppConfig and IsPostgresDetected and (UserPostgresPassword = '') then
  begin
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

  if NeedsAppConfig then
  begin
    AppDbPassword := GenerateSecret(24);
    GrpcApiKey := GenerateSecret(32);
    if (AppDbPassword = '') or (GrpcApiKey = '') then
      Result := 'No se pudieron generar las credenciales de la aplicación (se requiere PowerShell).';
  end;
end;

// --- Pantalla final: avisar dónde quedó la contraseña de postgres o si falló la BD ---
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> wpFinished then Exit;

  if DbConfigError <> '' then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'ATENCIÓN: la base de datos NO quedó configurada. ' + DbConfigError + #13#10 +
      'Revise ' + ExpandConstant('{app}\Logs\db_setup.log') + ' y vuelva a ejecutar el instalador.'
  else if PgSuperPasswordFile <> '' then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'La contraseña del superusuario "postgres" quedó en ' + PgSuperPasswordFile +
      ' (sólo Administradores). Guárdela en un lugar seguro.';
end;

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
    
    // 3. Esperar dinámicamente a que se libere el DLL principal
    WebDllPath := ExpandConstant('{app}\Web\asnetcorev2_inprocess.dll');
    WaitForFileUnlock(WebDllPath);
  end
  else if CurStep = ssPostInstall then
  begin
    if DbConfigError <> '' then
    begin
      Log('Configuración de base de datos fallida: ' + DbConfigError);
      SuppressibleMsgBox('La base de datos NO quedó configurada: ' + DbConfigError + #13#10 +
        'Revise ' + ExpandConstant('{app}\Logs\db_setup.log') + ' y vuelva a ejecutar el instalador.',
        mbCriticalError, MB_OK, IDOK);
    end;


    // Iniciar sitio IIS y AppPool automáticamente si ya están configurados
    AppCmdPath := GetAppCmdPath;
    if AppCmdPath <> '' then
    begin
      Exec(AppCmdPath, 'start apppool "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec(AppCmdPath, 'start site "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
