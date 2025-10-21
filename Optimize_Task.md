**Роль:** опытный .NET/Backend инженер. Внедри оптимизации стоимости перевода в LocoTool, сохраняя обратную совместимость CLI и форматов.

## Цели

1. Расширить **extract**: выявлять дубликаты, сегментировать code-aware, готовить структуру для TM/батчинга/компрессии.
2. Добавить **Translation Memory (TM)**: `cache.json` для повторного использования переводов.
3. Реализовать **группировку коротких строк** в один запрос.
4. Включить **глоссарий и контекстную подстановку** (placeholders).
5. **Code-aware preprocessor**: переводить только строки/комментарии, исключая код.
6. **Частотная компрессия**: выделять шаблоны и подставлять плейсхолдеры.
7. **Кэш батчей**: пропускать уже переведённые блоки по хэшу.
8. **Human-in-loop**: экспорт/импорт для пост-редактирования до apply.

## Требования к структуре (минимальный рефакторинг, без ломки CLI)

* Сохранить команды: `extract`, `translate`, `apply`, `all`, `stats`.
* Добавить опции:

  * `--dedup` (включить устранение дублей в extract/translate),
  * `--tm path` (путь к cache.json; если нет — создать),
  * `--batch-join` (объединять короткие строки),
  * `--min-len`, `--max-join` (порог и макс. длина объединения),
  * `--code-aware` (включить preprocessor для кода),
  * `--compress-freq` (включить частотные шаблоны),
  * `--hl-review` (human-in-loop: подготовить файл для правок и принять правки),
  * `--batch-cache` (кэшировать переведённые батчи по хэшу),
  * `--placeholders` (включить контекст/плейсхолдеры).
* Конфиг `config.json` расширить секцией `"Optimization"`:

  ```json
  "Optimization": {
    "Deduplicate": true,
    "UseTM": true,
    "TMPath": "cache.json",
    "BatchJoin": true,
    "MinLenToJoin": 3,
    "MaxJoinChars": 10000,
    "CodeAware": true,
    "CompressFrequency": true,
    "BatchCache": true,
    "HumanLoop": true,
    "Placeholders": true
  }
  ```

## Новые/обновлённые модули (предпочтительно в LocoTool.Core)

* `IDeduplicator` / `Deduplicator`

  * Вход: список сегментов (orig_text).
  * Выход: `uniqueSegments`, `mapDuplicates: origIndex -> uniqueIndex`.
  * Файл отчёта: `duplicates.tsv` (orig_text, count).
* `ITranslationMemory` / `JsonTranslationMemory`

  * Файл `cache.json` формат:

    ```json
    { "打开地图": "Open Map", "关闭地图": "Close Map" }
    ```
  * Методы: `TryGet(orig, out trans)`, `Add(orig, trans)`, `Save()`.
* `IBatchPlanner` / `BatchPlanner`

  * Объединяет короткие строки в батчи ≤ `MaxCharsPerRequest`.
  * Поддержка `--batch-join`, `--min-len`, `--max-join`.
  * Разделитель внутри батча: `|||` (или `\u241F`), с обратным сплитом после ответа.
* `IPlaceholderService` / `PlaceholderService`

  * Выделяет сущности (имена, ники, теги, ID) → `{ENT_1}`, `{NUM_1}`.
  * Карту замен хранит в сегменте: `Segment.Placeholders`.
* `ICodeAwarePreprocessor` / `CodeAwarePreprocessor`

  * Для типов: `.cs`, `.json`, `.xml`, `.py`, `.lua`: извлекает только переводимый текст (строки/комментарии), остальное — нефункционально.
  * Возвращает список `Segment { Text, Context, RestoreInfo }`.
* `IFrequencyCompressor` / `FrequencyCompressor`

  * Находит повторяющиеся фразы/шаблоны, заменяет `{PAT_1}`, `{PAT_2}`.
  * Отчёт `patterns.tsv`: `pattern`, `freq`.
* `IBatchCache` / `BatchCache`

  * Кэширует ответы по `SHA256(batchPayload) -> translation[]`.
  * Файл `batchcache.json`.
* `IHumanLoop` / `HumanLoopService`

  * `export`: `review.tsv` с колонками `orig_text`, `mt_suggest`, `final_text`.
  * `import`: слияние `final_text` обратно в таблицу перед `apply`.

## Расширение структуры extract

* На выходе, кроме общей таблицы, сохранять **служебные артефакты** (если включены опции):

  * `duplicates.tsv` (orig_text, occurrences, indices),
  * `review.tsv` (для human-in-loop, пока пустой `final_text`),
  * `patterns.tsv` (если `--compress-freq`),
  * `segments.json` (с контекстом, плейсхолдерами, restore-инфо).
* Не ломать базовый формат таблицы обмена (`original_line_no, field_index, record_id_guess, orig_text, translated_text`).

## Поток данных (обновлённый)

1. `extract`

   * Если `--code-aware`: применить `ICodeAwarePreprocessor` (строим Segment[]).
   * Если `--placeholders`: применить `IPlaceholderService`.
   * Если `--compress-freq`: `IFrequencyCompressor`.
   * Добавить `IDeduplicator` → собрать Unique[] и map.
   * Сохранить: основную таблицу + `duplicates.tsv` + `patterns.tsv` (если есть) + `segments.json`.
2. `translate`

   * Загрузить `cache.json` (TM) и `batchcache.json`.
   * Для каждого Unique: если в TM → взять перевод; иначе подготовить к батчам.
   * Если `--batch-join`: объединять короткие строки, учитывая `MaxCharsPerRequest`.
   * Переводить батчи с глоссарием; перед отправкой применять плейсхолдеры.
   * Сохранить новые переводы в TM и BatchCache.
   * Размножить переводы на дубликаты `mapDuplicates`.
   * Если `--hl-review`: сформировать/обновить `review.tsv` (`mt_suggest` заполнить).
3. `apply`

   * Если `--hl-review` и есть `review.tsv` с `final_text`: применить его поверх `mt_suggest`.
   * Обратно раскрыть плейсхолдеры и шаблоны, восстановить код (по `segments.json/RestoreInfo`).
   * Собрать исходный файл с исходным количеством `#`.

## Интерфейсы (сигнатуры)

```csharp
public record Segment(string Text, string? Context, object? RestoreInfo, Dictionary<string,string>? Placeholders);

public interface IDeduplicator {
  (IReadOnlyList<Segment> unique, int[] map) Deduplicate(IReadOnlyList<Segment> segments);
}

public interface ITranslationMemory {
  bool TryGet(string src, out string dst);
  void Add(string src, string dst);
  void Save();
}

public interface IBatchPlanner {
  IEnumerable<string[]> PlanBatches(IEnumerable<string> texts, int maxChars, bool joinShort, int minLen, int maxJoinChars);
}

public interface IPlaceholderService {
  Segment Apply(Segment s);     // to placeholders
  string Restore(string text, Segment original);
}

public interface ICodeAwarePreprocessor {
  IReadOnlyList<Segment> ExtractTranslatables(string path, string content);
  string Rebuild(string original, IReadOnlyList<string> translated, IReadOnlyList<Segment> segments);
}

public interface IFrequencyCompressor {
  (IReadOnlyList<Segment> compressed, Dictionary<string,string> patterns) Compress(IReadOnlyList<Segment> segments);
  IReadOnlyList<string> Decompress(IReadOnlyList<string> texts, Dictionary<string,string> patterns);
}

public interface IBatchCache {
  bool TryGet(string batchHash, out IReadOnlyList<string> translated);
  void Add(string batchHash, IReadOnlyList<string> translated);
  void Save();
}

public interface IHumanLoop {
  void ExportReview(string path, IEnumerable<(string orig, string mt)> items);
  Dictionary<string,string> ImportReview(string path); // orig -> final
}
```

## Примеры форматов

**duplicates.tsv**

```
orig_text#count#indices
打开地图#27#[1,7,22,...]
```

**review.tsv**

```
orig_text#mt_suggest#final_text#comment
打开地图#Open Map##
```

**segments.json (упрощённо)**

```json
[
  { "text":"打开地图", "context":"ui.main", "restoreInfo": { "line":123, "field":4 }, "placeholders":{} }
]
```

## Критерии приёмки

* `extract` создаёт доп. артефакты при включённых флагах, основной формат таблицы сохранён.
* При `translate` с `--tm` и `--dedup` количество отправленных в API символов меньше или равно уникальным без дубликатов.
* При `--batch-join` отправка побатчево, итог совпадает с поштучным переводом.
* Плейсхолдеры/шаблоны корректно восстанавливаются на `apply`.
* `cache.json` и `batchcache.json` пополняются и переиспользуются.
* Human-in-loop: правки из `review.tsv` приоритетнее MT.
* Вся новая логика закрыта модульными тестами на: dedup, TM, batch planner, placeholders, code-aware (минимальный набор).

## Подсказки по реализации

* Хэши батчей: `SHA256(string.Join("|||", texts))`.
* Разделитель внутри батча хранить в константе и экранировать при split.
* Частотные шаблоны: top-N n-grams (2–5 слов) с порогом ≥3, избегать пересечений.
* Code-aware: для JSON/XML — значения узлов/атрибутов; для C#/Py — string literals + comments (regex/roslyn/simple AST).
* TM/BatchCache JSON писать atomically (`.tmp` → rename).
* Никакой бизнес-логики в `Program.cs`: только вызов сервисов.

**Начинай с интерфейсов и проведи “сквозной” путь для `extract → translate → apply` на небольшом файле с дублями.**

**Постоянный общий кэш (Global Translation Memory, GTM)**

Добавь в проект **глобальную переводческую память**, общую для всех проектов/репозиториев, которая **постоянно расширяется, модифицируется и наполняется** по мере работы. Требования:

### Цели

* Снизить стоимость и ускорить перевод за счёт повторного использования ранее подтверждённых переводов.
* Единый источник истины для терминов/фраз между командами и проектами.
* Безопасная многопроцессная работа, атомарные обновления и управляемые права записи.

### Конфиг и CLI

* `config.json` → секция:

  ```json
  "GlobalTM": {
    "Enabled": true,
    "RootPath": "~/.locotool/gtm",     // база GTM на диске
    "ShardBy": "langpair",             // схема шардирования: langpair|hash
    "WritePolicy": "append",           // append|merge|readonly
    "Namespace": "default",            // пространство имён (команда/продукт)
    "MinConfidence": 0.85,             // мин. доверие для авто-использования
    "PreferHumanEdited": true          // человеческие правки приоритетнее MT
  }
  ```
* Новые флаги:

  * `--global-tm on|off` (переопределяет конфиг);
  * `--tm-mode append|merge|readonly` (режим записи);
  * `--tm-namespace <name>` (логическая область);
  * `--tm-import <file.json|tsv>` / `--tm-export <file.json|tsv>`;
  * `--tm-learn` (дообучать GTM из текущего перевода/пост-правок);
  * `--tm-priority global|local` (что искать сначала).

### Формат хранения

* **Шардирование** по паре языков и namespace:
  `~/.locotool/gtm/{namespace}/{src}-{tgt}.tm.jsonl` (JSON Lines).
* Запись — объект:

  ```json
  {
    "src": "打开地图",
    "dst": "Open Map",
    "srcLang": "zh",
    "dstLang": "en",
    "createdUtc": "2025-10-21T12:34:56Z",
    "updatedUtc": "2025-10-21T12:34:56Z",
    "source": "project:ui",
    "confidence": 0.92,
    "humanEdited": true,
    "context": ["ui.main", "button.label"],
    "hash": "sha256:...."
  }
  ```
* Разрешить **мульти-варианты** для одного `src` с разным `context`/`confidence`.

### Интерфейсы и реализация

* Новый интерфейс:

  ```csharp
  public interface IGlobalTranslationMemory {
    bool TryGet(string src, string srcLang, string dstLang, string? context, out string dst);
    void Append(string src, string dst, string srcLang, string dstLang, double confidence, bool humanEdited, IEnumerable<string>? contexts = null, string? source = null);
    void Merge(IEnumerable<(string src, string dst, string? context, double? confidence, bool? humanEdited)> entries);
    void Import(string path); // json|jsonl|tsv
    void Export(string path); // jsonl|tsv
    void Vacuum();            // сжатие/дефрагментация/индексация
    GlobalTmStats Stats();    // hits/misses/size/latency
  }
  ```
* Реализация `JsonlGlobalTranslationMemory`:

  * **Индексация** в памяти: `Dictionary<string, List<Entry>>` с ключом `src` (+ опц. n-граммы).
  * **Блокировки** на запись: межпроцессный file-lock + атомарная запись (`.tmp` → rename).
  * **Пакетная** запись (debounce) для снижения IO.

### Поведение в пайплайне

1. **extract**: не меняет GTM, но добавляет поле `context` в segments (для лучшего подбора).
2. **translate**:

   * Перед обращением к API: lookup в **local TM** → затем в **GTM** (при `--tm-priority` учитывать порядок).
   * Хиты GTM помечать как `source="gtm"`, `confidence=1.0` если `humanEdited=true`.
   * Мисы → отправлять в API; результаты:

     * в **local TM** всегда;
     * в **GTM** — по политике (`append|merge|readonly`) и порогу `MinConfidence`.
3. **apply / human-in-loop**:

   * Если есть `final_text` (правки человека), записывать/обновлять запись в **GTM** с `humanEdited=true`, `confidence=1.0`, расширять `context`.
   * Конфликты: если существует иной `dst`, хранить **оба варианта** и повышать приоритет по совпадающему `context`.

### Слияние и разрешение конфликтов

* Ключ слияния: `(src, srcLang, dstLang, normalizedContext)`; нормализовать регистр/пробелы.
* При разных `dst`:

  * если один `humanEdited=true` — он приоритетен;
  * иначе — выше `confidence`; при равенстве — хранить оба, выбирать по ближайшему `context` (Jaccard/Levenshtein).
* Логировать конфликты в `gtm_conflicts.log`.

### Импорт/экспорт

* Поддержать `jsonl` и `tsv`:

  * TSV колонки: `src\t dst\t srcLang\t dstLang\t context\t humanEdited\t confidence`.
* Импорт: нормализовать, дедуплицировать, объединить контексты, пересчитать `updatedUtc`.

### Надёжность и производительность

* **Кэш в памяти** с lazy-load по шардy (открывать только нужный langpair).
* Опционально Bloom-filter для быстрых miss-check.
* **Вакуум/сжатие**: слияние дублей, пересборка индекса, удаление низкоконфидентных дублей (по порогу).
* Метрики: `hits`, `misses`, `hitRate`, `entries`, `files`, `avgLookupMs`.

### Отчёты и статусы

* В `stats` добавить блок:

  ```
  [gtm] hit-rate: 62.4% (hits: 12,480 / misses: 7,520) entries: 98,311 shards: 4
  ```
* В `translate` печатать: сколько строк взято из GTM, сколько добавлено.

### Тесты

* Unit-тесты: hit/miss, merge, конфликт с humanEdited, import/export, vacuum, file-lock под параллельной записью.
* Интеграционный: два параллельных процесса переводят один и тот же набор — отсутствуют повреждения и гонки.

### Критерии приёмки

* При повторных прогонах объём запросов в API уменьшается за счёт GTM.
* `--tm-mode readonly` не изменяет файлы GTM.
* Импорт/экспорт воспроизводимы, порядок не важен.
* Нет регресса CLI; поведение без GTM остаётся прежним.
