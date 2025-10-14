# py/loctool.py
from __future__ import annotations
import csv, re
from typing import List, Tuple, Dict

_CJK_RE = re.compile(r'[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]')

def _split_hash_preserve_trailing(line: str) -> Tuple[List[str], int]:
    total_hash = line.count('#')
    parts = line.rstrip('\n').split('#')
    explained_hash = max(0, len(parts) - 1)
    trailing = total_hash - explained_hash
    for _ in range(trailing):
        parts.append('')
    return parts, trailing

def _join_hash_preserve_trailing(fields: List[str], trailing_hash_count: int) -> str:
    s = '#'.join(fields)
    if trailing_hash_count > 0:
        s += '#' * trailing_hash_count
    return s

def extract_strings(input_text: str, delimiter: str = "#") -> str:
    """
    Вход: полный текст исходного файла с #
    Выход: текст таблицы с колонками
    original_line_no, field_index, record_id_guess, orig_text, translated_text
    Разделитель между колонками задаётся параметром delimiter.
    """
    out = []
    for line_no, raw in enumerate(input_text.splitlines(), start=1):
        fields, _ = _split_hash_preserve_trailing(raw)
        rec_id = fields[0] if fields else ''
        for idx, val in enumerate(fields):
            if _CJK_RE.search(val):
                # формируем строку с нужным разделителем
                out.append(
                    delimiter.join([
                        str(line_no),
                        str(idx),
                        rec_id if rec_id.isdigit() else '',
                        val,
                        ''
                    ])
                )

    header = delimiter.join(["original_line_no", "field_index", "record_id_guess", "orig_text", "translated_text"])
    return header + "\n" + "\n".join(out)

def apply_translations(input_text: str, table_tsv: str, apply_empty: bool=False) -> str:
    """
    Применяет переводы из TSV к исходному тексту и возвращает собранный файл.
    Ключи: (line_no, field_index, orig_text) + fallback (line_no, field_index).
    """
    # Загрузим таблицу
    map_strict: Dict[Tuple[int,int,str], str] = {}
    map_loose: Dict[Tuple[int,int], str] = {}
    lines = table_tsv.splitlines()
    if not lines:
        return input_text
    header = lines[0].split('\t')
    cols = {name: i for i, name in enumerate(header)}
    for row in lines[1:]:
        if not row.strip(): 
            continue
        cells = row.split('\t')
        try:
            line_no = int(cells[cols['original_line_no']])
            field_idx = int(cells[cols['field_index']])
        except Exception:
            continue
        orig = cells[cols['orig_text']] if cols.get('orig_text') is not None and cols['orig_text'] < len(cells) else ''
        trans = cells[cols['translated_text']] if cols.get('translated_text') is not None and cols['translated_text'] < len(cells) else ''
        map_strict[(line_no, field_idx, orig)] = trans
        map_loose[(line_no, field_idx)] = trans

    # Применим
    out_lines: List[str] = []
    for line_no, raw in enumerate(input_text.splitlines(), start=1):
        fields, trailing = _split_hash_preserve_trailing(raw)
        for idx, val in enumerate(fields):
            key_s = (line_no, idx, val)
            key_l = (line_no, idx)
            tr = map_strict.get(key_s, None)
            if tr is None:
                tr = map_loose.get(key_l, None)
            if tr is not None:
                if tr == '' and not apply_empty:
                    pass
                else:
                    fields[idx] = tr
        out_lines.append(_join_hash_preserve_trailing(fields, trailing))
    return "\n".join(out_lines)