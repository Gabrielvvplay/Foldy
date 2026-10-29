# Compila o Foldy.exe, instala em %LOCALAPPDATA%\Foldy e deixa rodando
$ErrorActionPreference = 'Stop'
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $env:LOCALAPPDATA 'Foldy'
$exe = Join-Path $out 'Foldy.exe'
New-Item -ItemType Directory -Force $out | Out-Null

Get-Process Foldy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /out:"$exe" `
    /lib:"$fw\WPF" `
    /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Accessibility.dll `
    /win32icon:"$PSScriptRoot\src\icone.ico" /resource:"$PSScriptRoot\src\icone.ico",icone.ico `
    "$PSScriptRoot\src\Foldy.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação' }

# O próprio .exe registra o menu do botão direito, o Menu Iniciar e o início com o Windows, e já
# deixa o programa rodando (é o mesmo que o botão "Instalar" do painel faz pra quem baixou o .exe).
# Se achar uma instalação antiga do "Pastas Desktop", traz as pastas de lá.
& $exe --instalar | Out-Null
Write-Host "Instalado em $out e rodando."
