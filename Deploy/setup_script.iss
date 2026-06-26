; Script generado para Inno Setup
; Este script crea un instalador "Todo en Uno" para AttendanceSystem + PostgreSQL

#define MyAppName "AttendanceSystem"
#define MyAppVersion "2.1.3"
#define MyAppPublisher "Tu Empresa"
#define MyAppURL "http://www.tuempresa.com/"
#define MyAppExeName "AttendanceSystem.Blazor.Server.exe"
#define MyServiceExeName "AttendanceSystem.ZKTeco.Service.exe"

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
Source: "..\Release\Web\*"; DestDir: "{app}\Web"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. Archivos del Servicio Windows (incluyendo SDK ZKTeco)
Source: "..\Release\Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2.1 Archivos del SDK ZKTeco para System32 y SysWOW64
Source: "..\src\Infrastructure\AttendanceSystem.ZKTeco\lib\*"; DestDir: "{sys}"; Flags: ignoreversion sharedfile restartreplace
Source: "..\src\Infrastructure\AttendanceSystem.ZKTeco\lib\*"; DestDir: "{syswow64}"; Check: IsWin64; Flags: ignoreversion sharedfile restartreplace

; 3. Scripts SQL y Herramientas
Source: "init_db.sql"; DestDir: "{app}\Database"; Flags: ignoreversion
Source: "configure_db.bat"; DestDir: "{app}\Database"; Flags: ignoreversion
Source: "enable_iis.ps1"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "configure_iis.bat"; DestDir: "{app}\Tools"; Flags: ignoreversion

; 4. Instaladores de Prerrequisitos (DEBEN ESTAR EN LA CARPETA DEPLOY)
Source: "postgresql-installer.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "dotnet-hosting.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; 1. Instalar PostgreSQL silenciosamente (SOLO SI NO ESTÁ INSTALADO)
Filename: "{tmp}\postgresql-installer.exe"; Parameters: "--mode unattended --unattendedmodeui none --superpassword ""Blanquita.123"" --serverport 5432 --servicepassword ""Blanquita.123"""; \
    StatusMsg: "Instalando base de datos PostgreSQL... (Esto puede tardar unos minutos)"; \
    Check: PostgresNotInstalled

; 2. Esperar a que el servicio de Postgres arranque
Filename: "timeout"; Parameters: "/t 10"; Flags: runhidden; StatusMsg: "Verificando servicios de base de datos..."

; 3. Ejecutar script de configuración de Base de Datos (Crear DB y Usuario)
; Usamos configure_db.bat que ya maneja la lógica de creación idempotente
Filename: "{app}\Database\configure_db.bat"; \
    Parameters: """{code:GetPostgresPassword}"" ""{code:GetPostgresCLIPath}"""; \
    StatusMsg: "Configurando base de datos (Creando usuario y tablas)..."; Flags: runhidden; \
    Check: CanConfigureDatabase

; 4. Instalar Servicio de Windows (ZKTeco)
; IMPORTANTE: El nombre "AttendanceSystem.ZKTeco.Service" DEBE coincidir con el ServiceName en Program.cs (AddWindowsService)
Filename: "{sys}\sc.exe"; Parameters: "create AttendanceSystem.ZKTeco.Service binPath= ""{app}\Service\{#MyServiceExeName}"" start= auto displayname= ""Attendance System ZKTeco Service"""; \
    Flags: runhidden; StatusMsg: "Registrando servicio de Windows..."
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

; 7. Abrir Puertos en Firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AttendanceSystem Web"" dir=in action=allow protocol=TCP localport=8081"; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AttendanceSystem Service"" dir=in action=allow protocol=TCP localport=5001"; \
    Flags: runhidden; StatusMsg: "Configurando Firewall..."

; 8. Habilitar IIS y Componentes (Solo si se seleccionó la tarea IIS)
Filename: "powershell.exe"; \
    Parameters: "-ExecutionPolicy Bypass -File ""{app}\Tools\enable_iis.ps1"""; \
    StatusMsg: "Habilitando características de Windows (IIS)..."; Flags: runhidden 64bit; \
    Check: ShouldConfigureIIS

; 9. Instalar ASP.NET Core Hosting Bundle (SOLO SI FALTA)
Filename: "{tmp}\dotnet-hosting.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Instalando ASP.NET Core Runtime & Hosting Bundle..."; \
    Check: DotNetNotInstalled

; 10. Configurar IIS (AppPool y Sitio)
Filename: "{app}\Tools\configure_iis.bat"; Parameters: """{app}\Web"""; \
    StatusMsg: "Configurando Sitio Web en IIS..."; Flags: runhidden 64bit; \
    Check: ShouldConfigureIIS

[Tasks]
Name: "configure_iis"; Description: "Configurar como servidor IIS (Recomendado para servidores)"; GroupDescription: "Configuración Adicional:"

[UninstallRun]
; Eliminar sitio IIS al desinstalar
Filename: "{sys}\inetsrv\appcmd.exe"; Parameters: "delete site ""AttendanceSystem"""; Flags: runhidden 64bit; Check: ShouldConfigureIIS
Filename: "{sys}\inetsrv\appcmd.exe"; Parameters: "delete apppool ""AttendanceSystem"""; Flags: runhidden 64bit; Check: ShouldConfigureIIS
; Detener y eliminar el servicio de Windows
; IMPORTANTE: El nombre debe coincidir con el usado en [Run] y en Program.cs
Filename: "{sys}\sc.exe"; Parameters: "stop AttendanceSystem.ZKTeco.Service"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete AttendanceSystem.ZKTeco.Service"; Flags: runhidden

[Code]
var
  PostgresPage: TInputQueryWizardPage;
  UserPostgresPassword: String;
  IsPostgresDetected: Boolean;

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

// --- Detección de IIS Flag ---
function ShouldConfigureIIS: Boolean;
begin
  Result := IsTaskSelected('configure_iis');
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

// --- Helper para verificar si podemos configurar la DB ---
function CanConfigureDatabase: Boolean;
begin
  // Solo intentamos configurar si encontramos psql
  Result := FileExists(GetPostgresCLIPath(''));
end;

// --- Retornar Contraseña (Default o Usuario) ---
function GetPostgresPassword(Param: String): String;
begin
  if IsPostgresDetected then
    Result := UserPostgresPassword // La que ingresó el usuario
  else
    Result := 'Blanquita.123'; // La por defecto que instalamos nosotros
end;

// --- Inicialización del ASISTENTE ---
procedure InitializeWizard;
begin
  IsPostgresDetected := not PostgresNotInstalled;

  // Si Postgres YA existe, pedimos contraseña
  if IsPostgresDetected then
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
  if IsPostgresDetected and (CurPageID = PostgresPage.ID) then
  begin
    UserPostgresPassword := PostgresPage.Values[0];
    if Length(UserPostgresPassword) = 0 then
    begin
      MsgBox('Por favor ingrese la contraseña de PostgreSQL para continuar.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// --- DETENER/INICIAR SERVICIOS AL ACTUALIZAR ---
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // 1. Detener Servicio Windows
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop AttendanceSystem.ZKTeco.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Esperar un poco a que libere archivos
    Sleep(1000);

    // 2. Detener Sitio IIS (si existe y el appcmd está disponible)
    if FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe')) then
    begin
      Exec(ExpandConstant('{sys}\inetsrv\appcmd.exe'), 'stop site "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec(ExpandConstant('{sys}\inetsrv\appcmd.exe'), 'stop apppool "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
    
    Sleep(1000);
  end
  else if CurStep = ssPostInstall then
  begin
    // Iniciar sitio IIS y AppPool automáticamente si ya están configurados
    if FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe')) then
    begin
      Exec(ExpandConstant('{sys}\inetsrv\appcmd.exe'), 'start apppool "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec(ExpandConstant('{sys}\inetsrv\appcmd.exe'), 'start site "AttendanceSystem"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
