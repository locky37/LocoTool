# 🧩 LocoTool — инструмент для локализации игровых / промышленных текстовых файлов

**LocoTool** — универсальная утилита для извлечения, перевода и обратной сборки
локализационных файлов, в которых строки разделены символом `#`.

Проект объединяет:

* Python-парсер (через CSnakes) для разбора и восстановления структуры строк;
* REST-клиент **Yandex Cloud Translate API v2** для автоматического перевода;
* поддержку **глоссариев** (терминологические пары src→dst);
* полную конфигурацию через `config.json`. 

---

## 🚀 Возможности

| Команда     | Назначение                                                                                                                                       |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `extract`   | Извлекает все переводимые фрагменты (CJK-текст) в табличный файл. Поддерживаются разделители `\t` (по умолчанию) и `#`.                          |
| `translate` | Переводит таблицу с помощью Яндекс API, заполняя колонку `translated_text`.                                                                      |
| `apply`     | Применяет переводы обратно в исходный `.txt`, сохраняя структуру и `#`.                                                                          |
| `all`       | Полный цикл: `extract → translate → apply` в один шаг.                                                                                           |
| `stats`     | **Новая**: считает суммарное число символов к переводу, число батчей (по лимиту, например 10 000), и **оценочную стоимость** до начала перевода. |

---

## 🧰 Установка и сборка

```bash
git clone https://github.com/locky37/LocoTool.git
cd LocoTool
dotnet build -c Release
```

Убедитесь, что рядом с бинарником лежат:

```
LocoTool/
 ├─ LocoTool.exe
 ├─ config.json
 ├─ glossary.json
 └─ locotool.py
```

---

## ⚙️ Конфигурация (`config.json`)

```json
{
  "Yandex": {
    "ApiKey": "AQVNxxxxxxYOUR_API_KEYxxxxxx",
    "FolderId": "b1gxxxxxxxFOLDERIDxxxxxx",
    "UseBearerToken": false,
    "DefaultSourceLang": "zh",
    "DefaultTargetLang": "en",
    "GlossaryPath": "glossary.json"
  },
  "Limits": {
    "MaxCharsPerRequest": 10000,
    "MaxGlossaryPairs": 50
  },
  "Files": {
    "DefaultInput": "input.txt",
    "DefaultOutput": "output.txt"
  }
}
```

* `ApiKey` — ключ от сервисного аккаунта в Yandex Cloud Translate.
* `FolderId` — идентификатор каталога (обязателен для REST).
* `GlossaryPath` — путь к файлу терминов (`glossary.json`).
* `MaxCharsPerRequest` — лимит символов в одном запросе к API (по умолчанию 10 000). 

---

## 📖 Формат глоссария (`glossary.json`)

```json
[
  { "src": "剑道", "dst": "Swordsmanship", "exact": true },
  { "src": "套路", "dst": "Form", "exact": true },
  { "src": "不夜京", "dst": "City of Eternal Night", "exact": true },
  { "src": "江湖", "dst": "Jianghu", "exact": false }
]
```

> `exact: true` — термин заменяется только при точном совпадении.
> `exact: false` — мягкая подстановка (подходит для общих слов, топонимов и т. п.). 

---

## 🧑‍💻 Примеры использования

### 1️⃣ Извлечение переводимых строк

```bash
# По умолчанию — таб (\t) как разделитель таблицы
LocoTool extract input.txt strings.tsv

# С разделителем '#'
LocoTool extract input.txt strings.hash --delimiter "#"
```

Таблица содержит колонки:

```
original_line_no    field_index    record_id_guess    orig_text    translated_text
```

### 2️⃣ Быстрая оценка статистики и стоимости (без перевода)

```bash
# Считать объём и стоимость (цена за 1 млн символов, вкл. НДС)
LocoTool stats strings.tsv --price 250.00

# То же, но если таблица с разделителем '#'
LocoTool stats strings.hash --delimiter "#" --price-per-million 199.99
```

Пример вывода:

```
[stats] Строк к переводу: 1 583
[stats] Суммарно символов: 842 310
[stats] Батчей по 10000 симв.: 85
[stats] Оценка стоимости (по символам): ~168.46
[stats] Оценка с запасом (по батчам):  ~212.50  (учтено 850 000 симв.)
```

### 3️⃣ Машинный перевод через Яндекс

```bash
# Перевод с таб-разделителем
LocoTool translate strings.tsv strings_out.tsv --price 250

# Перевод файла с разделителем '#'
LocoTool translate strings.hash strings_out.hash --delimiter "#"
```

Перед началом перевода утилита выведет статистику и оценку стоимости.

### 4️⃣ Применение перевода обратно

```bash
# Применить переводы, сохраняя структуру исходника
LocoTool apply input.txt strings_out.tsv output.txt

# Разрешить затирать оригинал пустыми переводами (необязательно)
LocoTool apply input.txt strings_out.tsv output.txt --apply-empty
```

### 5️⃣ Полный автоматический цикл

```bash
# extract → translate → apply (используются default пути из config.json, если указаны)
LocoTool all input.txt output.txt --price 250.00
```

---

## 📦 Аргументы командной строки

| Аргумент                     | Где использовать                                | Назначение                                                                                             |                        |
| ---------------------------- | ----------------------------------------------- | ------------------------------------------------------------------------------------------------------ | ---------------------- |
| `--config path.json`         | все команды                                     | Загрузить альтернативный `config.json`.                                                                |                        |
| `--glossary path.json`       | `translate`, `all`                              | Задать альтернативный `glossary.json`.                                                                 |                        |
| `--delimiter <val>`          | `extract`, `translate`, `apply`, `stats`, `all` | Разделитель таблицы: `"#"`, `"\t"`, `","`, `";"`, `"                                                   | "`. По умолчанию `\t`. |
| `--apply-empty`              | `apply`                                         | Разрешить замену текстов пустыми значениями.                                                           |                        |
| `--price <perM>`             | `translate`, `all`, `stats`                     | Цена за 1 000 000 символов (вкл. НДС), например `250` или `199.99`. Считает стоимость **до** перевода. |                        |
| `--price-per-million <perM>` | `translate`, `all`, `stats`                     | Синоним `--price`.                                                                                     |                        |
| `--help`                     | все команды                                     | Показать краткую справку.                                                                              |                        |

---

## 📚 Пример исходных данных

### `input.txt`

```text
162510#0#玄冥双生#7#黄泉引路，冰狱双生！真气彻骨极寒...#
40100#苗圩#
109750#加套路伤害#1#41#...#剑道#当剑术大于200，套路为剑...#
```

### `strings.tsv` (фрагмент после `extract`)

```
original_line_no    field_index    record_id_guess    orig_text    translated_text
1    2    162510    玄冥双生    
1    4    162510    黄泉引路，冰狱双生！真气彻骨极寒...    
3    5    109750    剑道    
```

После `translate` → `apply` файл `output.txt` будет содержать переведённые фрагменты в исходных позициях. 

---

## 🧩 Внутреннее устройство

* **CSnakes** — позволяет вызывать Python-модуль `locotool.py` напрямую из C# (без подпроцессов).
* **Yandex Cloud Translate (REST)** — `POST /translate/v2/translate` с авторизацией `Api-Key`/`Bearer`.
* **GlossaryLoader** — загружает `glossary.json`.
* **AppConfig** — читает `config.json`.
* **RestTranslateClient** — отправляет запросы и обрабатывает лимиты/ошибки.
* **Batch-режим** — режет тексты на блоки по `MaxCharsPerRequest` (по умолчанию 10 000). 

---

## 🛠️ Использованные инструменты

При подготовке архитектуры, шаблонов кода и примеров использовались инструменты написания кода **ChatGPT** (подсказки, генерация и рефакторинг). Финальная интеграция и проверка выполнены вручную.

---

## ⚠️ Ограничения и примечания

* Максимум **10 000 символов** в одном REST-запросе.
* Глоссарий — до **50 пар** за запрос (ограничение API).
* Требуется активированный сервис **Yandex Translate API** и валидный ключ/папка.
* Работает с UTF-8 файлами с разделителем `#` в исходнике; разделитель таблицы настраивается через `--delimiter`. 

---

## 🧾 Типовой workflow

```bash
# 1. Подготовить конфигурацию и глоссарий
nano config.json
nano glossary.json

# 2. Извлечь строки (таб-разделитель)
LocoTool extract input.txt strings.tsv

# 3. Оценить объём/батчи/стоимость
LocoTool stats strings.tsv --price 250

# 4. Перевести и применить
LocoTool translate strings.tsv strings_out.tsv --price 250
LocoTool apply input.txt strings_out.tsv output.txt

# 5. Или всё сразу
LocoTool all input.txt output.txt --price 250
```

---

## 🧩 Лицензия

MIT License © 2025
Автор: *locky37*