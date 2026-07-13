# vpnconf — VPN Route Conflict Resolver

Небольшое кроссплатформенное CLI (C# / .NET 10) для сосуществования личного и корпоративного VPN.
Корпоративный VPN при коннекте прописывает маршруты в систему; личный VPN обычно маршрутизирует
широкие подсети. `vpnconf` находит пересечения и «пробивает дырки» в личном списке маршрутов —
вычитает корпоративные подсети и сворачивает остаток в минимальный набор CIDR, чтобы обе сети
работали одновременно.

Область: **IPv4**. Live-сбор маршрутов реализован для **Windows**; на macOS/Linux используйте
файловый режим (`analyze`).

## Интерактивный режим (рекомендуется)

Запустите приложение без аргументов — откроется меню, которое **остаётся открытым**:

```
vpnconf
```

Порядок работы:

1. Запустите `vpnconf` — появится меню.
2. Подключите корпоративный VPN в системе (приложение продолжает работать).
3. Выберите **Collect routes** — сбор маршрутов происходит именно сейчас; выберите VPN-адаптер(ы).
4. Выберите **Resolve conflicts** — укажите ip-list, просмотрите план и при желании запишите
   исправленный список.

Меню не завершается после действия и не падает на ошибках ввода — можно повторять шаги.
(Требуется терминал. Явный вызов: `vpnconf menu`.)

## Быстрый старт (одношаговый сценарий)

1. Подключите корпоративный VPN.
2. Запустите одношаговую команду:

   ```
   vpnconf resolve --ip-list proxy-list.json
   ```

   Программа покажет список сетевых адаптеров с числом маршрутов, вы интерактивно выберете
   адаптер(ы) корпоративного VPN. Затем она выведет конфликты и предложит план патча.

3. Чтобы сразу записать исправленный список (не трогая оригинал):

   ```
   vpnconf resolve --ip-list proxy-list.json --out proxy-list.patched.json
   ```

Неинтерактивно можно указать адаптер явно: `--interface "Ethernet 2"`
(несколько — через запятую). `--dry-run` считает всё, но ничего не пишет.

`--ip-list` необязателен: без него `resolve` всё равно вытащит маршруты VPN, покажет их
(и сохранит через `--extract-out <file>`), а путь к списку спросит интерактивно.

### Формат списка

Парсер выбирается опцией `--format <auto|json|plain>` (по умолчанию — интерактивный выбор в
`resolve` или авто-детект). Поддерживаются:

- `json` — исходный формат `[{ "hostname": "<cidr>", "ip": "" }]`;
- `plain` — один CIDR в строке (комментарии `#`/`//`, голый IP = `/32`).

Новый формат добавляется одной реализацией `IIpListParser` в `IpListParserRegistry`.

## Команды

| Команда | Назначение |
|---|---|
| `resolve` | Live: прочитать маршруты → выбрать адаптер → конфликты → план → (опц.) patched-файл |
| `analyze` | Сравнить дампы `before`/`after`, найти добавленные VPN-маршруты и конфликты |
| `plan`    | Вычесть конфликтующие CIDR из ip-list, построить план `remove`/`add` (JSON) |
| `apply`   | Применить план к ip-list и записать patched-копию |

Файловый пайплайн (например, для macOS/Linux — сохраните дампы маршрутов в файлы):

```
vpnconf analyze --ip-list ip-list.json --before before.txt --after after.txt --out conflicts.txt
vpnconf plan    --ip-list ip-list.json --conflicts conflicts.txt --out patch-plan.json
vpnconf apply   --ip-list ip-list.json --plan patch-plan.json --output ip-list.patched.json
```

## Безопасность

- Оригинальный файл никогда не перезаписывается без `--backup` (создаётся `<file>.bak`).
- `--dry-run` для команд, меняющих файлы.
- Ненулевой код возврата и понятное сообщение при ошибке.

## Сборка и тесты

```
dotnet build
dotnet test
```

### Native AOT

Требуется C++-тулчейн (VS workload «Desktop development with C++»). Сборка:

```
./publish-aot.ps1
```

Скрипт добавляет каталог VS Installer в PATH (чтобы линкер-шаг нашёл `vswhere.exe`/MSVC) и
выполняет `dotnet publish -r win-x64 -c Release`. Результат — самодостаточный
`…\bin\Release\net10.0\win-x64\publish\vpnconf.exe` (~5 МБ, без внешних DLL).

Если запускать `dotnet publish` вручную и линковка падает на `vswhere.exe` — добавьте в PATH
`C:\Program Files (x86)\Microsoft Visual Studio\Installer` либо запускайте из «Developer PowerShell for VS».

## Архитектура

Модульный монолит (см. `.cursor/rules/project-standards.mdc` и `.cursor/docs/…-spec.md`):

- `Model` — контракты данных (`Ipv4Cidr`, `RouteEntry`, `CidrEntry`, `PatchPlan`, `RouteConflict`).
- `Engine` — чистые детерминированные алгоритмы (`ConflictDetector`, `CidrSubtractEngine`,
  `CidrMinimizer`, `PatchPlanner`, `PatchApplier`).
- `Parsing` — нормализация входа: подключаемые парсеры списка (`IIpListParser` +
  `IpListParserRegistry`: `JsonIpListParser`, `PlainTextIpListParser`), `RouteTableParser`.
- `Infrastructure/Routes` — адаптеры к ОС (`IRouteTableProvider` →
  `WindowsRouteTableProvider` через `GetIpForwardTable`; выбор через `RouteTableProviderFactory`).
- `Output` — отчёты Spectre.Console и сериализация JSON.
- `Cli` — разбор аргументов и команды.
