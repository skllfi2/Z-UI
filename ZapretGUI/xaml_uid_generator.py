"""
xaml_uid_generator.py
─────────────────────
Обходит все .xaml файлы в папке проекта:
  1. Находит контролы с текстом (TextBlock, Button, ComboBoxItem, ToggleSwitch, …)
  2. Проверяет наличие x:Uid — если есть, пропускает
  3. Генерирует x:Uid из x:Name или из текста контрола
  4. Вставляет x:Uid в .xaml (форматирование сохраняется)
  5. Добавляет строки в Resources.resw для каждой указанной локали
     (не перезаписывает уже существующие ключи)

Запуск (минимальный):
    python xaml_uid_generator.py --root F:\\Dev\\ZapretGUI\\ZapretGUI

Все параметры:
    --root      Корень проекта (.xaml ищутся рекурсивно)
    --resw      Пути к .resw через запятую
                По умолчанию: <root>\\Strings\\ru-RU\\Resources.resw,
                              <root>\\Strings\\en-US\\Resources.resw
    --dry       Только вывод, без записи файлов
    --controls  Список контролов через запятую
"""

import argparse
import re
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

# ─── Контролы и их текстовые атрибуты ────────────────────────────────────────

CONTROL_TEXT_ATTR: dict[str, list[str]] = {
    "TextBlock":          ["Text"],
    "Button":             ["Content"],
    "ComboBoxItem":       ["Content"],
    "ToggleSwitch":       ["Header"],        # OnContent/OffContent часто пустые
    "HyperlinkButton":    ["Content"],
    "RadioButton":        ["Content"],
    "CheckBox":           ["Content"],
    "AppBarButton":       ["Label"],
    "MenuFlyoutItem":     ["Text"],
    "NavigationViewItem": ["Content"],
    "TextBox":            ["PlaceholderText", "Header"],
    "PasswordBox":        ["PlaceholderText", "Header"],
}

DEFAULT_CONTROLS = list(CONTROL_TEXT_ATTR.keys())

X_NS   = "http://schemas.microsoft.com/winfx/2006/xaml"
X_UID  = f"{{{X_NS}}}Uid"
X_NAME = f"{{{X_NS}}}Name"

# ─── Транслитерация и генерация идентификатора ────────────────────────────────

_RU_MAP = {
    "а":"a","б":"b","в":"v","г":"g","д":"d","е":"e","ё":"yo","ж":"zh",
    "з":"z","и":"i","й":"y","к":"k","л":"l","м":"m","н":"n","о":"o",
    "п":"p","р":"r","с":"s","т":"t","у":"u","ф":"f","х":"kh","ц":"ts",
    "ч":"ch","ш":"sh","щ":"sch","ъ":"","ы":"y","ь":"","э":"e","ю":"yu","я":"ya",
}

def _translit(s: str) -> str:
    return "".join(_RU_MAP.get(c.lower(), c) for c in s)

def slugify(text: str, max_len: int = 50) -> str:
    words = re.findall(r"[A-Za-zА-Яа-яЁё0-9]+", text)
    if not words:
        return "Control"
    result = "".join(_translit(w).capitalize() for w in words)
    result = re.sub(r"[^A-Za-z0-9]", "", result)
    return result[:max_len] or "Control"

def make_uid(elem: ET.Element, control: str, used: set) -> str:
    # 1) x:Name
    base = elem.get(X_NAME) or elem.get("x:Name", "")

    # 2) Текст первого непустого атрибута
    if not base:
        for attr in CONTROL_TEXT_ATTR.get(control, []):
            val = elem.get(attr, "").strip()
            if val and not val.startswith("{"):
                base = slugify(val)
                break

    # 3) Запасной вариант
    if not base:
        base = control

    uid = base
    n = 1
    while uid in used:
        uid = f"{base}{n}"
        n += 1
    used.add(uid)
    return uid

# ─── Вставка x:Uid в сырой текст XAML ────────────────────────────────────────

def inject_xuid(xaml: str, elem: ET.Element, control: str, uid: str) -> str:
    """
    Находит нужный открывающий тег в тексте и вставляет x:Uid="..."
    сразу после имени тега. Форматирование остального кода не трогает.
    """
    xname = elem.get(X_NAME) or elem.get("x:Name", "")

    # Паттерн открывающего тега (с учётом возможного namespace-префикса)
    tag_re = re.compile(
        r'<(?:\w+:)?' + re.escape(control) + r'(?=[\s/>])'
    )

    for m in tag_re.finditer(xaml):
        # Ищем конец атрибутов тега (до > или />)
        i = m.start()
        depth = 0
        end = i
        while end < len(xaml):
            ch = xaml[end]
            if ch == '"':   # пропускаем строковые значения
                end += 1
                while end < len(xaml) and xaml[end] != '"':
                    end += 1
            elif ch in (">", "/") and (ch == ">" or (end + 1 < len(xaml) and xaml[end + 1] == ">")):
                break
            end += 1

        tag_slice = xaml[i:end]

        # Пропускаем если уже есть x:Uid
        if "x:Uid" in tag_slice:
            continue

        # Если у нас есть x:Name — убеждаемся, что этот тег содержит нужный Name
        if xname and f'"{xname}"' not in tag_slice:
            continue

        insert_pos = m.end()
        return xaml[:insert_pos] + f' x:Uid="{uid}"' + xaml[insert_pos:]

    return xaml  # тег не найден — возвращаем без изменений

# ─── Обработка одного XAML файла ─────────────────────────────────────────────

def process_xaml(
    path: Path,
    controls: list,
    used_uids: set,
    dry: bool,
) -> list:
    """
    Возвращает список (uid, resw_key, value).
    """
    text = path.read_text(encoding="utf-8-sig")  # utf-8-sig убирает BOM

    # Регистрируем все xmlns для корректной работы ET
    for prefix, uri in re.findall(r'xmlns(?::(\w+))?="([^"]+)"', text):
        try:
            ET.register_namespace(prefix or "", uri)
        except ValueError:
            pass
    ET.register_namespace("x", X_NS)

    try:
        root = ET.fromstring(text)
    except ET.ParseError as e:
        print(f"  [!] ParseError в {path.name}: {e}", file=sys.stderr)
        return []

    entries = []
    modified = False

    for elem in root.iter():
        local = elem.tag.split("}")[-1] if "}" in elem.tag else elem.tag
        if local not in controls:
            continue

        existing_uid = elem.get(X_UID) or elem.get("x:Uid")

        if existing_uid:
            uid = existing_uid
        else:
            uid = make_uid(elem, local, used_uids)
            new_text = inject_xuid(text, elem, local, uid)
            if new_text != text:
                text = new_text
                modified = True
            else:
                print(f"    [warn] не удалось вставить x:Uid для {local} (uid={uid})")

        for attr in CONTROL_TEXT_ATTR.get(local, []):
            val = elem.get(attr, "").strip()
            if val and not val.startswith("{"):
                entries.append((uid, f"{uid}.{attr}", val))

    status = "✏  Изменён" if modified else "✓  Без изменений"
    if dry:
        status = "~  [dry] " + ("будет изменён" if modified else "без изменений")

    print(f"  {status}: {path.name}  ({len(entries)} строк)")

    if modified and not dry:
        path.write_text(text, encoding="utf-8")

    return entries

# ─── Обработка Resources.resw ─────────────────────────────────────────────────

RESW_SKELETON = (
    '<?xml version="1.0" encoding="utf-8"?>\n'
    "<root>\n"
    '  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema"\n'
    '              xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">\n'
    '    <xsd:element name="root" msdata:IsDataSet="true"/>\n'
    "  </xsd:schema>\n"
    '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>\n'
    '  <resheader name="version"><value>2.0</value></resheader>\n'
    "</root>\n"
)

def update_resw(resw_path: Path, entries: list, dry: bool) -> int:
    file_ok = resw_path.exists() and resw_path.stat().st_size > 0
    if file_ok:
        try:
            tree = ET.parse(resw_path)
            root = tree.getroot()
        except ET.ParseError:
            file_ok = False

    if not file_ok:
        root = ET.fromstring(RESW_SKELETON)
        tree = ET.ElementTree(root)

    existing = {d.get("name") for d in root.findall("data")}
    added = 0

    for _uid, key, value in entries:
        if key in existing:
            continue
        data = ET.SubElement(root, "data")
        data.set("name", key)
        data.set("xml:space", "preserve")
        ET.SubElement(data, "value").text = value
        existing.add(key)
        added += 1

    if dry:
        print(f"  [dry] {resw_path.name}: +{added} новых строк")
        return added

    if added:
        ET.indent(tree, space="  ")
        resw_path.parent.mkdir(parents=True, exist_ok=True)
        tree.write(resw_path, encoding="utf-8", xml_declaration=True)
        print(f"  ✏  +{added} строк → {resw_path}")
    else:
        print(f"  ✓  Без изменений: {resw_path}")

    return added

# ─── main ─────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(description="XAML x:Uid + .resw generator")
    ap.add_argument("--root",     required=True, help="Корень проекта")
    ap.add_argument("--resw",     default=None,
                    help="Пути к .resw через запятую. "
                         "По умолчанию: Strings/ru-RU/Resources.resw, Strings/en-US/Resources.resw")
    ap.add_argument("--dry",      action="store_true", help="Просмотр без изменений")
    ap.add_argument("--controls", default=",".join(DEFAULT_CONTROLS),
                    help="Контролы через запятую")
    args = ap.parse_args()

    root_dir = Path(args.root)
    controls = [c.strip() for c in args.controls.split(",") if c.strip()]

    if args.resw:
        resw_paths = [Path(p.strip()) for p in args.resw.split(",")]
    else:
        resw_paths = [
            root_dir / "Strings" / "ru-RU" / "Resources.resw",
            root_dir / "Strings" / "en-US" / "Resources.resw",
        ]

    print(f"📁 Корень:    {root_dir.resolve()}")
    for p in resw_paths:
        print(f"📄 .resw:     {p}")
    if args.dry:
        print("⚠️  DRY-RUN\n")
    print()

    SKIP_DIRS = {"obj", "bin", ".vs", "packages"}
    xaml_files = sorted(
        p for p in root_dir.rglob("*.xaml")
        if not any(part in SKIP_DIRS for part in p.relative_to(root_dir).parts)
    )
    if not xaml_files:
        print("Файлы .xaml не найдены.")
        return

    used_uids: set = set()
    all_entries: list = []

    for xaml_path in xaml_files:
        rel = xaml_path.relative_to(root_dir)
        print(f"📄 {rel}")
        all_entries.extend(process_xaml(xaml_path, controls, used_uids, args.dry))

    print(f"\n{'─'*55}")
    print(f"Файлов:         {len(xaml_files)}")
    print(f"Строк для .resw: {len(all_entries)}")
    print()

    for resw_path in resw_paths:
        update_resw(resw_path, all_entries, args.dry)

    print("\n✅ Готово!")

if __name__ == "__main__":
    main()
