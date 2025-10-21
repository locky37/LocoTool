refactor(cli,core): очистить Program.Main и разнести логику по слоям; добавить CompositionRoot и команды; сохранить поведение 1:1

Что сделано

Program.Main упрощён до парсинга аргументов, сборки зависимостей и делегирования в роутер команд.
Добавлен простой DI-компоновщик без внешних библиотек: CompositionRoot.
Введены абстракции (интерфейсы) и сервисы:
IConfigService / ConfigService — загрузка config.json с безопасным результатом.
IGlossaryService / GlossaryService — загрузка глоссария и обрезание по лимиту.
ITranslateClient + адаптер RestTranslateClientAdapter над существующим Service/RestTranslateClient.
ITableIo / TableIo — ReadRows, WriteRows, ResolveDelimiter (перенос из Program.cs).
IStatsService / StatsService — подсчёт символов/батчей/стоимости.
IParsingService / ParsingService — фасад над ParserManager/ILocParser.
Добавлены команды (ICommandRunner + CommandRouter):
ExtractCommand, TranslateCommand, ApplyCommand, AllCommand, StatsCommand.
Команды принимают CommandContext (CLI-параметры) и CancellationToken.
Вынесены и централизованы константы заголовков таблицы в ExchangeTable (уже были в LocoTool.Abstractions/ParserOptions.cs) — сохранено использование.
Сохранён существующий формат CLI, вывод и плагинная архитектура парсеров (ParserManager).
Разделены обязанности: сервисы возвращают данные/DTO, печать в консоль — в командах.
Тесты

Добавлены базовые unit-тесты (xUnit):
tests/LocoTool.Tests/TableIoTests.cs — проверка ResolveDelimiter и roundtrip Read/Write.
tests/LocoTool.Tests/StatsServiceTests.cs — проверка батчей/стоимости.
tests/LocoTool.Tests/ParsingServiceTests.cs — безопасность Resolve при отсутствии парсеров.
Все тесты проходят: dotnet test — зелёный.
Структура

Program.cs — минимальный вход.
CompositionRoot.cs — сборка зависимостей и роутера.
Cli/: CommandContext, ICommandRunner/CommandRouter, команды.
Core/Abstractions/: IConfigService, IGlossaryService, ITranslateClient, ITableIo, IStatsService, IParsingService.
Core/Services/: соответствующие реализации.
Service/: RestTranslateClient, GlossaryLoader — оставлены и переиспользуются.
Core/ParserManager.cs — без изменений, используется ParsingService.
tests/LocoTool.Tests — новый тестовый проект.
Совместимость и поведение

Команды: extract, translate, apply, all, stats — без изменения.
Ключи: --config, --glossary, --delimiter, --apply-empty, --price|--price-per-million, --parser — без изменения.
Формат входных/выходных таблиц и логика батчирования перевода — как раньше.
Сообщения в консоль сохранены по смыслу и порядку.
Почему это изменение

Program.cs был перегружен логикой I/O, парсеров, статистики и переводов.
Рефакторинг снижает связанность, улучшает читаемость и тестируемость, соответствует SOLID и разделению слоёв (CLI ↔ Core ↔ Infrastructure).
Технические детали

Translate/All создают адаптер RestTranslateClientAdapter с реальными auth header/folderId из config на вызове команды (поведение идентично прежнему).
TableIo реализует прежние ReadRows/WriteRows/ResolveDelimiter, включая заголовок и обработку перевода строк.
StatsService повторяет вычисления символов и стоимости, выдаёт оценки по символам/по пачкам.
Безопасные правки

Удалён неиспользуемый в текущем поведении Python bootstrap (CSnakes) из Program.cs (в legacy пути он не влиял на вывод — параллельно существовал parsing через ILocParser; теперь весь парсинг идёт через ParsingService/ParserManager). Поведение команд сохранено 1:1.
TODO

ITranslatorFactory для провайдеров (yandex/deepl/azure/openai).
ICacheService для Translation Memory (JSON/SQLite).
StatsFormatter (табличный/обычный/JSON) — вынести форматирование из команд.
Result<T> повсеместно вместо исключений (начато в ConfigService).
PathResolver для нормализации путей и базовой директории.
IProgressReporter (в перспективе Spectre.Console).
Раздел README «Архитектура» с диаграммой слоёв.