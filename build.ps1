# Compila o PastasDesktop.exe, instala em %LOCALAPPDATA%\PastasDesktop e deixa rodando
$ErrorActionPreference = 'Stop'
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $env:LOCALAPPDATA 'PastasDesktop'
$exe = Join-Path $out 'PastasDesktop.exe'
New-Item -ItemType Directory -Force $out | Out-Null

Get-Process PastasDesktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /out:"$exe" `
    /lib:"$fw\WPF" `
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Accessibility.dll `
    "$PSScriptRoot\src\PastasDesktop.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação' }

# "Nova pasta de apps" no botão direito da área de trabalho
& $exe --instalar | Out-Null

# Atalhos no Menu Iniciar
$programs = [Environment]::GetFolderPath('Programs')
$sh = New-Object -ComObject WScript.Shell
foreach ($s in @(@('Pastas', '--residente'), @('Nova pasta de apps', '--nova'))) {
    $l = $sh.CreateShortcut((Join-Path $programs "$($s[0]).lnk"))
    $l.TargetPath = $exe; $l.Arguments = $s[1]; $l.IconLocation = 'imageres.dll,-3'; $l.Save()
}

# Inicia com o Windows e, a cada 5 minutos, reabre se tiver fechado.
# (Se já estiver rodando, a nova cópia percebe e sai na hora.)
$action   = New-ScheduledTaskAction -Execute $exe -Argument '--residente'
$logon    = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$repeat   = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'PastasDesktop' -Action $action -Trigger $logon, $repeat -Settings $settings -Force | Out-Null

Start-Process $exe -ArgumentList '--residente'
Write-Host "Instalado em $out e rodando."
