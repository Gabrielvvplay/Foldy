# PastasDesktop

Pastas de apps na área de trabalho do Windows, iguais às do iPhone e do Android: um clique no ícone abre uma janelinha com os apps, sem passar pelo Explorador de Arquivos.

## O que faz

- **Um clique** no ícone da pasta abre a grade de apps.
- **Arrastar** um atalho pra cima da pasta coloca o app nela.
- **Arrastar pra fora** da janelinha devolve o app pra área de trabalho.
- O ícone da pasta mostra uma prévia 2x2 dos apps dentro dela.
- A pasta **se apaga sozinha** quando fica vazia.
- Renomear o ícone na área de trabalho (F2) renomeia a pasta.
- Botão direito na área de trabalho → **Nova pasta de apps**.

## Instalar

Precisa só do Windows 10/11 (usa o compilador C# que já vem no .NET Framework).

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

O script compila, instala em `%LOCALAPPDATA%\PastasDesktop`, adiciona a opção no menu do botão direito e no Menu Iniciar, e registra uma tarefa agendada que inicia o programa com o Windows (e reabre se ele fechar).

## Desinstalar

```powershell
& "$env:LOCALAPPDATA\PastasDesktop\PastasDesktop.exe" --desinstalar
Unregister-ScheduledTask -TaskName PastasDesktop -Confirm:$false
```

## Observação

Atalhos que ficam na área de trabalho **pública** (`C:\Users\Public\Desktop`, onde muitos instaladores colocam os ícones) precisam de permissão de administrador pra serem movidos. Sem ela, o app é copiado pra pasta e o ícone original continua na área de trabalho.
