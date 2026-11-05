# Движок

Здесь лежит только описание движка. Сами бинарники в git **не коммитятся**
(см. `.gitignore`) — их скачивает `scripts/fetch-engine.ps1`.

## Что скачивается

| Файл | Источник |
|------|----------|
| `winws.exe`, `WinDivert.dll`, `WinDivert64.sys`, `cygwin1.dll` | [bol-van/zapret-win-bundle](https://github.com/bol-van/zapret-win-bundle), закреплённый коммит |
| `quic_initial_*.bin`, `ACTIVE_DISCORD_UDP.bin`, `tls_clienthello_*.bin` | [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube), тег 1.10.4 (закреплённый коммит) |

Каждый файл в `manifest.json` содержит:

- точный URL (raw.githubusercontent.com, закреплённый коммит — не `master`/`latest`);
- SHA-256;
- размер.

`fetch-engine.ps1` отказывается устанавливать файл, если хэш не совпал.

## Почему это безопасно

- Никаких «сборок с форумов»: только официальные репозитории авторов zapret.
- Версии закреплены коммитами, а не плавающими ветками.
- SHA-256 фиксируется в нашем репозитории и проверяется при каждой установке.
- WinDivert — открытый подписанный драйвер. Антивирусы могут ругаться на него
  как на `RiskTool.WinDivert`: это не вирус, а инструмент перехвата пакетов.
- WinDivert можно (и лучше) собрать из исходников самостоятельно — см.
  `docs/TRUST.md`.

## Обновление движка

1. Поменять коммиты/URL в `manifest.json`.
2. Пересчитать хэши: `scripts/fetch-engine.ps1 -UpdateHashes`.
3. Проверить, что приложение показывает «движок проверен».
