# Стратегии

Стратегия — это набор аргументов, которые передаются `winws.exe`. Всё остальное
(запуск, остановка, watchdog, тест) делает приложение.

Файлы: `data/strategies/*.json`. Все `.json` из этой папки загружаются при старте.
Стратегии с одинаковым `id` игнорируются (побеждает первый файл по алфавиту).

## Встроенные стратегии

### 1. `general-multisplit` (рекомендуется)

Базовый набор для Discord + YouTube:

- **UDP/443 (QUIC)** — поддельный QUIC Initial пакет (`fake`), 6 повторов.
  Заставляет DPI принять поддельный пакет за начало соединения.
- **UDP 19294–19344, 50000–50100** — голос Discord и STUN: поддельные
  Discord/STUN-пакеты.
- **TCP 2053/2083/2087/2096/8443** — медиа Discord: `multisplit` с перекрытием
  последовательностей (`split-seqovl=681`).
- **TCP 443** — домены Google/YouTube: `multisplit` + `ip-id=zero`.
- **TCP 80/443** — общий список доменов: `multisplit` с шаблоном
  `tls_clienthello_4pda_to.bin`.
- **UDP/443 и TCP 80/443/8443 по ipset** — зарезервированные правила; по умолчанию
  `ipset-all.txt` содержит заглушку, поэтому ничего не делают (см. ниже).

Это лучший выбор «поставил и работает» для большинства провайдеров.

### 2. `general-fake-tls`

Более агрессивная стратегия:

- поддельный TLS ClientHello с модификаторами `rnd,dupsid,sni=www.google.com`;
- `fake,multidisorder` и `badseq` (подделка TCP-последовательности);
- 11 повторов.

Помогает, когда DPI уже научился распознавать простой multisplit. Может быть
менее стабильной на «умных» провайдерах и чаще ломает отдельные сайты.

### 3. `general-simple-fake` (TCP timestamp)

- поддельные TLS/HTTP пакеты с фулингом `ts` (TCP timestamp);
- `hostfakesplit` для Google;
- 6 повторов.

Хорошо работает там, где режут именно ClientHello, но провайдер не проверяет
TCP timestamp. Часто используется как запасной вариант.

## Как выбрать

1. Начните с `general-multisplit`.
2. Если Discord/YouTube не открываются — **«Тест стратегий»**. Приложение проверит
   каждую и покажет, сколько целей ответило.
3. Если ни одна не работает полностью — смотрите, какие цели падают, и правьте
   списки/стратегию.
4. Если работает, но нестабильно (то грузится, то нет) — попробуйте
   `general-fake-tls`; иногда лучше срабатывает `general-simple-fake`.

## Куда применяются правила

Ключевые аргументы `winws`:

| Аргумент | Смысл |
|----------|-------|
| `--wf-tcp=...`, `--wf-udp=...` | Какие порты вообще перехватывать (WinDivert filter) |
| `--filter-tcp=...`, `--filter-udp=...` | Фильтр внутри winws |
| `--filter-l7=discord,stun` | Определять протокол по содержимому |
| `--hostlist=FILE` | Применять к доменам из файла |
| `--hostlist-exclude=FILE` | Исключить домены |
| `--ipset=FILE` | Применять к IP-подсетям |
| `--ipset-exclude=FILE` | Исключить подсети |
| `--hostlist-domains=a,b` | Домены прямо в командной строке |
| `--dpi-desync=fake` | Отправлять поддельный пакет |
| `--dpi-desync=multisplit` | Разбивать TLS-запись на части |
| `--dpi-desync=multidisorder` | Отправлять части в обратном/перемешанном порядке |
| `--dpi-desync=hostfakesplit` | Поддельный пакет с другим SNI |
| `--dpi-desync-repeats=N` | Сколько раз повторять |
| `--dpi-desync-split-pos=1,midsld` | Позиции разбиения |
| `--dpi-desync-split-seqovl=N` | Перекрытие sequence numbers |
| `--dpi-desync-fake-tls=FILE` | Шаблон поддельного TLS |
| `--dpi-desync-fake-quic=FILE` | Шаблон поддельного QUIC |
| `--dpi-desync-fake-discord=FILE` | Шаблон для Discord UDP |
| `--dpi-desync-fake-stun=FILE` | Шаблон для STUN |
| `--dpi-desync-fooling=ts,badseq` | Как «испортить» поддельный пакет, чтобы DPI его принял |
| `--new` | Начать новое правило |

Полный список — в документации zapret (`docs/readme.md` в репозитории
[bol-van/zapret](https://github.com/bol-van/zapret)).

## Списки

Рабочие файлы лежат в `%LOCALAPPDATA%\PrivateProxy\lists`:

| Файл | Назначение |
|------|------------|
| `list-general.txt` | Discord, Cloudflare, Twitch-подобные сервисы |
| `list-google.txt` | YouTube, Google, googlevideo |
| `list-exclude.txt` | Не трогать (банки, госуслуги, Steam, Microsoft, магазины) |
| `list-general-user.txt` | Ваши дополнительные домены |
| `list-exclude-user.txt` | Ваши исключения |
| `ipset-all.txt` | IP-подсети для ipset-правил |
| `ipset-exclude.txt` | Исключённые подсети |
| `ipset-*-user.txt` | Ваши дополнения |

Формат hostlist: один домен на строку. `^domain` — привязка к началу имени.
Пустые строки и `#`-комментарии допустимы.

### IPSet

По умолчанию `ipset-all.txt` содержит `203.0.113.113/32` — это документированная
заглушка (TEST-NET-3), которая ни на что не влияет. IPSet-правила в стратегиях
существуют, но фактически выключены.

Чтобы включить:

1. Скачайте актуальный список, например
   [.service/ipset-service.txt](https://github.com/Flowseal/zapret-discord-youtube/blob/main/.service/ipset-service.txt).
2. Замените содержимое `%LOCALAPPDATA%\PrivateProxy\lists\ipset-all.txt`.
3. Перезапустите стратегию.

IPSet полезен для сервисов, которые не перечислены в hostlist, но может замедлить
работу и затронуть лишние сайты. Начинайте с hostlist-правил.

## Добавление своей стратегии

```json
{
  "schemaVersion": 1,
  "strategies": [
    {
      "id": "my-own",
      "name": "My own",
      "category": "Custom",
      "description": "Что делает и когда использовать",
      "recommended": false,
      "args": [
        "--wf-tcp=80,443",
        "--wf-udp=443",
        "--filter-tcp=443",
        "--hostlist={LISTS}\\list-general.txt",
        "--hostlist={LISTS}\\list-general-user.txt",
        "--hostlist-exclude={LISTS}\\list-exclude.txt",
        "--hostlist-exclude={LISTS}\\list-exclude-user.txt",
        "--dpi-desync=fake,multidisorder",
        "--dpi-desync-split-pos=1,midsld",
        "--dpi-desync-repeats=11",
        "--dpi-desync-fooling=badseq",
        "--new"
      ]
    }
  ]
}
```

Правила:

- `id` — уникальный, латиница/цифры/дефис.
- `name` — то, что видно в UI.
- `args` — **каждый аргумент отдельной строкой**. Не собирайте всю команду в одну
  строку: приложение передаёт элементы через `ArgumentList`, поэтому пробелы в
  путях не ломаются.
- Плейсхолдеры: `{ENGINE}`, `{LISTS}`, `{DATA}`, `{APP}`.
- Пути к `.bin`-шаблонам — только из `{ENGINE}`.

После правки файла перезапустите приложение (каталог читается при старте).

## Отладка

- Журнал: `%LOCALAPPDATA%\PrivateProxy\logs\app-YYYYMMDD.log`.
- Ошибки `winws` (например, `windivert: error`) попадают в журнал с префиксом `winws:`.
- Если `winws` завершается сразу после старта, приложение покажет код выхода и
  последнюю строку вывода.
- Для ручной проверки можно запустить движок из консоли:

```powershell
cd engine\bin
.\winws.exe --wf-tcp=443 --filter-tcp=443 --hostlist="%LOCALAPPDATA%\PrivateProxy\lists\list-google.txt" --dpi-desync=multisplit --dpi-desync-split-pos=1 --new
```

(Запускать из консоли **от имени администратора**.)
