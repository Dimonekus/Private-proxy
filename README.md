# Private Proxy

Свой менеджер обхода DPI для Windows: **Discord (включая голос), YouTube и другие сервисы**.
Тонкая обёртка на C#/WPF вокруг официального движка [zapret](https://github.com/bol-van/zapret)
(`winws.exe`). Никаких «сборок с форумов»: движок скачивается из официальных
репозиториев по закреплённым коммитам и проверяется по SHA-256.

> **Важно.** Обход DPI на Windows невозможен без перехвата пакетов на уровне ядра.
> Для этого используется открытый подписанный драйвер **WinDivert**. Некоторые
> антивирусы помечают его как `RiskTool.WinDivert` — это ожидаемое поведение
> инструмента перехвата трафика, а не вирус. Подробности: [docs/TRUST.md](docs/TRUST.md).

## Возможности

- Готовые стратегии для Discord + YouTube, кнопка **«Тест стратегий»** с автоподбором лучшей.
- Автоматический перезапуск движка при падении (watchdog с backoff).
- Автозапуск через Планировщик задач с правами администратора — без UAC при входе в систему.
- Работа в трее: закрытие окна не выключает обход.
- Списки доменов/IP можно править без пересборки приложения.
- Проверка целостности движка по SHA-256 при каждом запуске.
- Подробный журнал в `%LOCALAPPDATA%\PrivateProxy\logs`.

## Требования

- Windows 10/11 x64.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (или SDK для сборки).

## Быстрый старт

```powershell
git clone https://github.com/Dimonekus/Private-proxy.git
cd Private-proxy

# 1. Скачать и проверить движок (нужен интернет)
powershell -ExecutionPolicy Bypass -File scripts\fetch-engine.ps1

# 2. Собрать
powershell -ExecutionPolicy Bypass -File scripts\build.ps1

# 3. Запустить
.\src\PrivateProxy.App\bin\Release\net8.0-windows\PrivateProxy.exe
```

Или собрать переносимую папку:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Publish
# результат: artifacts\PrivateProxy\PrivateProxy.exe
```

При первом запуске приложение скопирует списки в `%LOCALAPPDATA%\PrivateProxy\lists`.
Дальше `winws.exe` читает именно эту копию, поэтому обновление приложения не затирает
ваши правки.

## Как пользоваться

1. Выберите стратегию в списке (по умолчанию — **General · multisplit**).
2. Нажмите **«Запустить»** и подтвердите UAC. Движку нужны права администратора.
3. Если Discord/YouTube не работают — нажмите **«Тест стратегий»**: приложение по очереди
   запустит каждую стратегию, проверит доступность сервисов и предложит лучшую.
4. Для постоянной работы включите **«Добавить в автозапуск»**. Задача создаётся
   с уровнем `HIGHEST`, поэтому при входе в систему обход стартует без запроса UAC.

Кнопка **«Остановить»** завершает `winws.exe` и снимает перехват. Закрытие окна
(крестик) сворачивает приложение в трей; полностью выйти можно через меню трея → «Выход».

## Где что лежит

| Путь | Назначение |
|------|------------|
| `%LOCALAPPDATA%\PrivateProxy\settings.json` | Настройки приложения |
| `%LOCALAPPDATA%\PrivateProxy\logs\app-YYYYMMDD.log` | Журнал |
| `%LOCALAPPDATA%\PrivateProxy\lists\` | Рабочие списки доменов и IP (можно править) |
| `data\strategies\*.json` | Описание стратегий (рядом с exe) |
| `engine\bin\` | Движок `winws.exe` + WinDivert (скачивается скриптом) |

### Списки

- `list-general.txt` — домены Discord, Cloudflare и т.п.
- `list-google.txt` — YouTube/Google.
- `list-exclude.txt` — домены, которые **не** надо трогать (банки, госуслуги, Steam и т.д.).
- `*-user.txt` — ваши личные дополнения; эти файлы не перезаписываются.
- `ipset-all.txt` — IP-подсети для дополнительной фильтрации. По умолчанию содержит
  только заглушку `203.0.113.113/32`, то есть IPSet-правила фактически выключены.
  Чтобы включить, замените файл реальным списком (например,
  [.service/ipset-service.txt](https://github.com/Flowseal/zapret-discord-youtube/blob/main/.service/ipset-service.txt)).

## Решение проблем

| Симптом | Что делать |
|---------|------------|
| «Движок не установлен» | Выполните `scripts\fetch-engine.ps1` |
| «Движок найден, но не прошёл проверку» | Удалите `engine\bin` и снова запустите `scripts\fetch-engine.ps1` |
| «Нужны права администратора» | Запустите приложение от имени администратора |
| После запуска пропал интернет | Нажмите «Остановить»; проверьте, не конфликтует ли другая копия zapret/VPN |
| Discord работает, голос — нет | Включите UDP-правила: они уже есть в стратегиях; проверьте, не блокирует ли голос VPN/фаервол |
| Антивирус ругается на `WinDivert64.sys` | Ожидаемо: `RiskTool.WinDivert`. Файл проверяется по SHA-256 из `engine\manifest.json` |
| Не работает автозапуск | Задача `PrivateProxy` в Планировщике задач; создаётся с правами администратора |

Если что-то пошло не так, приложите свежий лог из `%LOCALAPPDATA%\PrivateProxy\logs`.

## Добавить свою стратегию

Создайте `data\strategies\my.json`:

```json
{
  "schemaVersion": 1,
  "strategies": [
    {
      "id": "my-strategy",
      "name": "My strategy",
      "category": "Custom",
      "description": "Что и зачем",
      "recommended": false,
      "args": [
        "--wf-tcp=80,443",
        "--wf-udp=443",
        "--filter-tcp=443",
        "--hostlist={LISTS}\\list-general.txt",
        "--dpi-desync=fake,multidisorder",
        "--new"
      ]
    }
  ]
}
```

Доступные подстановки: `{ENGINE}` → `engine\bin`, `{LISTS}` → рабочая папка списков,
`{DATA}` → `data`, `{APP}` → папка приложения. Аргументы передаются в `winws.exe`
как есть. Подробнее: [docs/STRATEGIES.md](docs/STRATEGIES.md).

## Документация

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — как устроено приложение.
- [docs/TRUST.md](docs/TRUST.md) — откуда бинарники и как проверить, что они не изменены.
- [docs/STRATEGIES.md](docs/STRATEGIES.md) — стратегии и параметры winws.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — лицензии сторонних компонентов.

## Лицензия

Собственный код — MIT (см. [LICENSE](LICENSE)). Движок zapret — MIT, WinDivert —
LGPLv3/GPLv2, Cygwin — GPL + исключение для компоновки. Полные тексты — в
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) и папке `licenses/`.
