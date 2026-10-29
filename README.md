# Foldy

Pastas de apps na área de trabalho do Windows, iguais às do iPhone e do Android: um clique no ícone abre uma janelinha com os apps, sem passar pelo Explorador de Arquivos.

## O que faz

- **Um clique** no ícone da pasta abre a grade de apps.
- **Arrastar** um atalho pra cima da pasta coloca o app nela.
- **Arrastar pra fora** da janelinha devolve o app pra área de trabalho.
- O ícone da pasta mostra uma prévia 2x2 dos apps dentro dela.
- A pasta **se apaga sozinha** quando fica vazia.
- Renomear o ícone na área de trabalho (F2) renomeia a pasta.
- **Personalizar** (engrenagem no canto da pasta, ou botão direito → Personalizar pasta): cor, formato do ícone (arredondado, círculo, quadrado), prévia 2x2 ou 3x3, colunas, tamanho dos apps e ordem — com prévia ao vivo. Fica salvo num arquivo oculto `.estilo` dentro da pasta.
- Botão direito na área de trabalho → **Nova pasta de apps**.
- **Painel de controle** (Menu Iniciar → Foldy, ou botão direito dentro de uma pasta → Painel de controle): lista as pastas pra criar, personalizar e excluir, e tem as opções gerais — abrir com um clique, iniciar com o Windows, opção no botão direito, reiniciar o programa e desinstalar.

## Instalar

Precisa só do Windows 10/11.

**Pra quem baixou o `Foldy.exe`:** abra ele e clique em **Instalar**. Não pede senha de administrador. Abrir uma versão mais nova do `.exe` mostra **Atualizar**, e as pastas continuam como estão.

**A partir do código** (usa o compilador C# que já vem no .NET Framework):

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Os dois instalam em `%LOCALAPPDATA%\Foldy`, adicionam a opção no menu do botão direito e no Menu Iniciar, e registram uma tarefa agendada que inicia o programa com o Windows (e reabre se ele fechar).

## Desinstalar

Painel de controle → **Geral** → **Desinstalar**. Os apps de dentro das pastas voltam pra área de trabalho. Pela linha de comando:

```powershell
& "$env:LOCALAPPDATA\Foldy\Foldy.exe" --desinstalar
```

## Observação

Atalhos que ficam na área de trabalho **pública** (`C:\Users\Public\Desktop`, onde muitos instaladores colocam os ícones) normalmente precisam de permissão de administrador pra serem movidos. Se a conta tiver essa permissão, eles são movidos como os outros; se não, o app é copiado pra pasta e o ícone original continua na área de trabalho.
