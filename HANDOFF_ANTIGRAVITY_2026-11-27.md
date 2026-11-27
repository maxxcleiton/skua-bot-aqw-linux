# Handoff para o Antigravity — Skua-Linux / HDBoN

Atualizado em 27/11/2026 (horário local de Maceió). Este documento registra o estado deste chat para continuar no Antigravity instalado neste notebook.

> **Segredos:** não copie senhas nem tokens deste chat para cá. As credenciais já ficam no arquivo local `~/.config/Skua/Skua.settings.json`; não as imprima em terminal, logs ou commits.

## Objetivo ativo

Concluir a **Hollowborn DragonBlade of Nulgath (HDBoN)** para Bengt. A missão passa por `Fiend's Shadow` e `Fiend's Purgatory`; o runner de Bengt pula as etapas que já constam como completas/itens obtidos e, na Purgatory, luta com Blighted Zubami, Salek Sprayer e Sarah the NerfKitten.

Zubami e Salek já foram obtidos em execução anterior e o runner os pulou nas execuções seguintes. O bloqueio atual é Sarah.

## Estado atual, verificado agora

- Bengt e as cinco alts estão conectadas: **6 processos Skua**.
- `skua-farm.service` (watchdog) e `skua-hdbon-sarah.service` estão ativos.
- A última execução lutou na sala `voidnerfkitten-335480`.
- Às **14:04:10**, o runner pausou Sarah depois de **15 mortes da Bengt**, sem confirmar `Sarah's Souvenir`.
- Os cinco arquivos `hdbon_party_*.status` estão em `WAIT`: as contas continuam conectadas, mas não estão lutando agora.
- O watchdog monitora quedas/desconexões; ele **não retoma um script que pausou por limite**. Não afirmar que a farm está progredindo só porque os processos estão ativos.
- O último drop conhecido continua sendo Zubami e Salek; HDBoN não está concluída.

## O que foi feito neste chat

1. Diagnóstico inicial: Bengt estava parada em Sarah, enquanto as alts estavam em outra sala/mapa. O marcador da sala do líder estava desatualizado.
2. Já existia no repo a correção para Butler entrar na sala numerada/nomeada do líder (`922fedd Join the leader's named room in locked boss maps`). Reiniciar líder e alts carregou essa correção; as alts entraram na mesma sala da Sarah.
3. A primeira execução com sala alinhada teve várias mortes e parou com o limite antigo de 3. Little_mett estava em Void Highlord, que morria repetidamente; foi alterada para ArchPaladin, que a conta possui. Essa mudança está em `~/.config/Skua/options/Butler1.cfg`, fora do repo.
4. O preparo de poções de classe segurou a entrada do grupo por vários minutos. No mesmo arquivo local, foi adicionado `MrPresident BR`, `The Borges Warrion` e `Bella of Worlds` a `Options:SkipClassPotionCraft`. A ideia é não atrasar o grupo com farms longos de poção de classe; poções gerais que o script compra/usa continuam independentes dessa opção.
5. A pedido do usuário, o limite de mortes passou de 3 para **15**. Commit e push: `4dc4fd7 Allow more deaths during HDBoN boss fights`.
6. A execução seguinte ainda parou com 13 mortes após o timeout separado de 12 minutos. O usuário perguntou se a luta seria reiniciada.
7. O timeout por chefe passou de 12 para **30 minutos**. Commit e push: `e54931c Extend HDBoN boss fight timeout`.
8. O grupo foi reiniciado para carregar a alteração. A sala antiga/HP parcial foi perdida ao recriar a luta; o runner voltou para Sarah, e as etapas anteriores ficaram concluídas. A nova execução chegou a 15 mortes às 14:04:10 e pausou antes do drop.

## Composição que o Butler está configurado para usar

- Bengt: Verus DoomKnight (runner principal).
- MrPresident BR: Legion Revenant.
- LeesDragon Prime: Lord of Order.
- The Borges Warrion: StoneCrusher.
- Little_mett: ArchPaladin (mudança nesta conversa; antes era Void Highlord).
- Bella of Worlds: ArchPaladin.

O runner configura enhancements/gear do HDBoN e poções. Os logs mostram, por exemplo, Ravenous nas Dual DragonBlades de Bengt e Penitence/Vainglory nas capas. Não alterar composição ou crafting sem conferir os logs atuais: tentativas anteriores mostraram mortes mesmo com suporte e poções.

## Commits relevantes

- `7b9df2a Prepare dedicated gear and consumables for Zubami`
- `922fedd Join the leader's named room in locked boss maps`
- `4dc4fd7 Allow more deaths during HDBoN boss fights` — limite atual: 15 mortes do líder.
- `e54931c Extend HDBoN boss fight timeout` — timeout atual: 30 minutos por chefe.

Branch local: `linux-port`, acompanhando `origin/main`. Esses commits já foram enviados para `main`.

Há modificações locais **não relacionadas** e não commitadas em:

- `Skua.Linux/Flash/LinuxFlashUtil.cs`
- `Skua.Linux/Flash/X11FlashHost.cs`
- `Skua.Linux/MainWindow.cs`

Não as inclua em commits deste trabalho.

## Arquivos e estado operacional

- Repo: `/home/max/Skua-Linux`
- Runner: `CustomScripts/HDBoN/HollowbornDragonBladeOfNulgath.cs`
- Butler: `CustomScripts/ButlerV4/Butlerv4.cs`
- Launcher: `CustomScripts/start_farm.sh`
- Config local do supervisor: `~/.config/Skua/farm.conf`
- Config local de opções do Butler: `~/.config/Skua/options/Butler1.cfg`
- Logs: `~/.config/Skua/logs/hdbon_*.log`
- Status do grupo: `~/.config/Skua/logs/hdbon_party_*.status`
- Marcador da sala do líder: `~/.config/Skua/logs/hdbon_leader_room.status`
- Marcador TCP de sync: `~/.config/Skua/leader_sync_ports.json`

Comandos úteis:

```bash
/home/max/.local/bin/farm status
/home/max/.local/bin/farm stop
```

Para iniciar de novo com os scripts/configs atuais:

```bash
systemd-run --user --collect --unit=skua-hdbon-sarah \
  --working-directory=/home/max/Skua-Linux \
  /bin/bash /home/max/Skua-Linux/CustomScripts/start_farm.sh
systemd-run --user --collect --unit=skua-farm \
  /home/max/.local/bin/farm watchdog --reload
```

`farm stop` encerra o grupo todo. Um restart pode criar sala nova e resetar o HP da Sarah; Zubami/Salek devem ser pulados pelo runner se os drops continuarem na inventory/temp inventory conforme o próprio script verifica.

## Perguntas paralelas respondidas

- “As contas estão paradas?” — sim, **script pausado**, apesar dos seis clientes e watchdog ainda ativos.
- “Vai reiniciar a luta?” — foi reiniciada após subir o limite/timeout; a tentativa nova também chegou ao limite de 15 mortes sem drop.
- A razão do timeout original não era o limite de mortes: eram dois limites distintos (3 mortes e 12 minutos). O timeout agora é 30 min, mas a execução mais recente pausou ao chegar a 15 mortes.

## Próximo passo sugerido

Não dizer que o farm está rodando enquanto Bengt estiver em `HDBON paused` e os status das alts estiverem `WAIT`. Para avançar, primeiro revisar estratégia de sobrevivência/dano contra Sarah e decidir se o limite de mortes deve subir novamente ou se a composição/rotação precisa mudar. Sarah causa dano verdadeiro proporcional à vida; a referência consultada foi [AQW Wiki — Sarah the NerfKitten](https://aqwwiki.wikidot.com/sarah-the-nerfkitten). O usuário autorizou ajustes e monitoramento, mas vale explicar qualquer novo reset da sala/HP.

## Contexto mais amplo do Skua-Linux

O usuário mantém o projeto no Git e prefere que scripts próprios fiquem preservados em `CustomScripts`, separados dos scripts oficiais, com commits/push quando a alteração for justificável. O notebook foi configurado anteriormente com clientes a 15 FPS para reduzir carga e ruído enquanto também usa Jellyfin. Não alterar FPS nem parar Jellyfin sem necessidade. O histórico também passou por Ravenous, ArchMage e vários farms de bosses; o objetivo ativo neste handoff é HDBoN da Bengt.
