# HotspotTray

Liga o **Hotspot Móvel do Windows automaticamente** assim que você entra no PC e mostra o estado dele na bandeja do sistema, ao lado dos ícones de Wi-Fi, som e rede.

<p align="center"><img src="docs/icones.png" alt="Ícones: ligado, desligado e conectando" width="330"></p>

## Recursos

- **Liga sozinho no logon**, por uma tarefa agendada com prioridade alta, sem o atraso dos programas de inicialização comuns.
- **Mantém o hotspot ligado:** se ele desligar (por exemplo, pela economia de energia do Windows quando ninguém está conectado), o app liga de novo em segundos.
- **Ícone na bandeja** no estilo dos ícones de rede do Windows:
  - `((•))` ligado
  - com um corte diagonal: desligado
  - com um círculo girando: conectando
  - A cor acompanha o tema claro ou escuro da barra de tarefas.
- **Clique esquerdo:** abre *Configurações → Rede e Internet → Hotspot Móvel*.
- **Passar o mouse:** mostra o nome da rede e quantos dispositivos estão conectados.
- **Clique direito:** abre um menu com Ligar agora, Desligar (pausa até religar ou reiniciar), Manter sempre ligado, Iniciar com o Windows e Sair.
- **Sem dependências:** um `.exe` de ~25 KB que usa o .NET Framework que já vem no Windows.

## Requisitos

- Windows 10 ou 11.
- O Hotspot Móvel configurado pelo menos uma vez em *Configurações → Rede e Internet → Hotspot Móvel* (nome da rede e senha).
- Uma conexão com a internet para compartilhar (cabo, Wi-Fi etc.).

## Instalação (passo a passo)

1. Baixe o **`HotspotTray.zip`** da página de [**Releases**](../../releases/latest).
2. Crie uma pasta **fora de `AppData`**, por exemplo `C:\Users\SeuUsuario\HotspotTray`, e extraia o zip lá dentro.
   > Não use pastas dentro de `AppData` nem a pasta Downloads, que pode ser limpa. O Windows precisa achar o `.exe` sempre no mesmo lugar.
3. Clique duas vezes em **`Ativar inicializacao rapida.cmd`**.
4. Quando aparecer o aviso do Windows (UAC), clique em **Sim**. A permissão de administrador é pedida **só uma vez**, para criar a tarefa de inicialização. O app em si roda como usuário comum.
5. Deve aparecer *"Pronto! O HotspotTray vai abrir junto com o Windows"*, e o ícone surge na bandeja.

> **Aviso do SmartScreen:** como o `.exe` não é assinado digitalmente, o Windows pode mostrar "O Windows protegeu o computador". Clique em **Mais informações → Executar assim mesmo**. Se preferir, compile você mesmo a partir do código (veja abaixo).

Pronto: a partir de agora, sempre que você entrar no Windows, o hotspot liga sozinho.

### O ícone não aparece?

O Windows 11 pode esconder ícones novos dentro da setinha `^` da bandeja. Para deixá-lo sempre visível, vá em *Configurações → Personalização → Barra de tarefas → Outros ícones da bandeja* e ative o **HotspotTray**.

## Uso

| Ação | O que acontece |
|---|---|
| Clique esquerdo no ícone | Abre as configurações do Hotspot Móvel |
| Passar o mouse | Mostra estado, nome da rede e dispositivos conectados |
| Clique direito → **Ligar hotspot agora** | Liga na hora |
| Clique direito → **Desligar hotspot** | Desliga e pausa o religamento automático até você ligar de novo ou reiniciar o PC |
| Clique direito → **Manter sempre ligado** | Liga ou desliga o religamento automático |
| Clique direito → **Iniciar com o Windows** | Liga ou desliga a abertura automática no logon |
| Clique direito → **Sair** | Fecha o app (o hotspot continua como estiver) |

## Solução de problemas

O app grava um log em **`log.txt`**, na mesma pasta do `.exe`. Ele registra a hora em que o app abriu, cada tentativa de ligar o hotspot e os erros.

- **"sem conexão com a internet"**: o hotspot do Windows precisa de uma conexão para compartilhar. O app espera e tenta de novo sozinho.
- **"tarefa de logon nao encontrada"**: rode `Ativar inicializacao rapida.cmd` de novo. O menu do ícone também mostra a opção *Ativar inicialização rápida* enquanto a tarefa não existir.
- **Hotspot desliga sozinho quando ninguém está conectado**: é a economia de energia do Windows. O app religa automaticamente. Se quiser, desative em *Configurações → Hotspot Móvel → Economia de energia*.

## Desinstalar

1. Clique com o botão direito no ícone e escolha **Sair**.
2. Abra o **Terminal como administrador** e rode:
   ```bash
   schtasks /Delete /TN HotspotTray /F
   ```
3. Apague a pasta onde você extraiu o app.

Se você instalou pelo script, rode `powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall`.

## Compilar a partir do código

Não precisa instalar nada: o compilador C# vem junto com o Windows (.NET Framework 4.x).

```bash
git clone https://github.com/silvaezequias/hotspot-tray.git
```

```bash
cd hotspot-tray
```

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Isso gera o `HotspotTray.exe`. Para instalar em `%USERPROFILE%\HotspotTray` e registrar a inicialização de uma vez:

```bash
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## Como funciona

- Usa a API WinRT `NetworkOperatorTetheringManager`, a mesma que a página de configurações do Windows usa, para consultar, ligar e desligar o hotspot.
- Uma thread em segundo plano verifica o estado a cada 5 segundos e também reage na hora a mudanças de rede.
- No boot, roda com prioridade de processo alta até o hotspot ligar pela primeira vez, depois volta ao normal.
- A inicialização é feita por uma **tarefa agendada no logon** (prioridade 1), com a chave `Run` do registro como reserva. Um mutex impede que duas cópias rodem ao mesmo tempo.

| Arquivo | Para que serve |
|---|---|
| `Program.cs` | Todo o código do app |
| `build.ps1` | Compila o `.exe` |
| `install.ps1` | Instala, registra a inicialização ou desinstala (`-Uninstall`) |
| `Ativar inicializacao rapida.cmd` | Registra a tarefa de logon (pede UAC) e abre o app |
