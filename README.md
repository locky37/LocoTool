# 🧩 LocoTool — инструмент для локализации игровых / промышленных текстовых файлов

**LocoTool** — универсальная утилита для извлечения, перевода и обратной сборки
локализационных файлов, в которых строки разделены символом `#`.

Проект объединяет:
- Python-парсер (через CSnakes) для разбора и восстановления структуры строк;
- REST-клиент **Yandex Cloud Translate API v2** для автоматического перевода;
- поддержку **глоссариев** (терминологические пары src→dst);
- полную конфигурацию через `config.json`.

---

## 🚀 Возможности

| Команда | Назначение |
|----------|-------------|
| `extract`  | Извлекает все переводимые фрагменты (CJK-текст) в `.tsv` / `.csv` файл. |
| `translate`| Переводит таблицу с помощью Яндекс API, заполняя колонку `translated_text`. |
| `apply`    | Применяет переводы обратно в исходный `.txt`, сохраняя структуру и `#`. |
| `all`      | Полный цикл: `extract → translate → apply` в один шаг. |

---

## 🧰 Установка и сборка

```bash
git clone https://github.com/locky37/LocoTool.git
cd LocoTool
dotnet build -c Release
````

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

* `ApiKey` — ключ от сервисного аккаунта в [Yandex Cloud Translate](https://yandex.cloud/ru/docs/translate/).
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
LocoTool extract input.txt strings.tsv
```

Создаётся файл `strings.tsv` с колонками:

```
original_line_no	field_index	record_id_guess	orig_text	translated_text
```

### 2️⃣ Машинный перевод через Яндекс

```bash
LocoTool translate strings.tsv strings_out.tsv
```

`translated_text` будет автоматически заполнен английскими вариантами.

### 3️⃣ Применение перевода обратно

```bash
LocoTool apply input.txt strings_out.tsv output.txt
```

Результат (`output.txt`) — тот же файл, но со вставленными переводами.

### 4️⃣ Полный автоматический цикл

```bash
LocoTool all input.txt output.txt
```

Одной командой:

1. Извлекает CJK-строки;
2. Переводит их с учётом глоссария;
3. Собирает обратно.

---

## 📦 Дополнительные параметры CLI

| Аргумент               | Назначение                                                 |
| ---------------------- | ---------------------------------------------------------- |
| `--config path.json`   | Загрузить другой конфиг вместо `config.json`.              |
| `--glossary path.json` | Задать альтернативный словарь терминов.                    |
| `--apply-empty`        | Разрешить затирать оригинальные строки пустыми переводами. |
| `--help`               | Вывод краткой справки.                                     |

---

## 📚 Пример исходных данных

### input.txt

```text
162510#0#玄冥双生#7#黄泉引路，冰狱双生！真气彻骨极寒...#
40100#苗圩#
109750#加套路伤害#1#41#...#剑道#当剑术大于200，套路为剑...#
```

### strings.tsv (фрагмент)

```
original_line_no	field_index	record_id_guess	orig_text	translated_text
1	2	162510	玄冥双生	Xuanming Twins
1	4	162510	黄泉引路，冰狱双生！真气彻骨极寒...	Path of the Underworld, Frostbound Twins! ...
3	5	109750	剑道	Swordsmanship
```

После `apply` файл `output.txt` будет содержать английские переводы в соответствующих позициях.

---

## 🧩 Внутреннее устройство

* **CSnakes** позволяет вызывать Python-код `loctool.py` напрямую из C#, без подпроцессов.
* **REST-клиент** реализует `POST /translate/v2/translate` с авторизацией `Api-Key`.
* **GlossaryLoader** и **AppConfig** отвечают за загрузку `glossary.json` и `config.json`.
* **Batch-режим** автоматически режет запросы по лимиту 10 000 символов.
* Формат вывода полностью сохраняет количество `#` и пустые поля.

---

## 🧾 Типовой workflow

```bash
# 1. Подготовить конфигурацию и глоссарий
nano config.json
nano glossary.json

# 2. Извлечь строки
LocoTool extract input.txt strings.tsv

# 3. Проверить/дополнить переводы вручную при необходимости
nano strings.tsv

# 4. Применить переводы
LocoTool apply input.txt strings.tsv output.txt

# 5. (или одним шагом)
LocoTool all input.txt output.txt
```

---

## ⚠️ Ограничения и примечания

* Максимум **10 000 символов** в одном REST-запросе.
* Поддерживаются только текстовые форматы (`#`-разделённые файлы).
* Глоссарий применяется для всех запросов (до 50 терминов).
* Для перевода требуется активированный сервис **Yandex Translate API**.

---

## 🧩 Лицензия

MIT License © 2025
Автор: *locky37*