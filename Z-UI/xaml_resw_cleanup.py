"""
xaml_resw_cleanup.py
────────────────────
Находит и удаляет из Resources.resw проблемные ключи:

  1. КОНФЛИКТ СВОЙСТВ — один uid используется в разных типах контролов
     (например, Nastroyki.Text из TextBlock И Nastroyki.Content из NavigationViewItem)
     → оставляем только ключи того контрола, у которого uid встречается ЧАЩЕ

  2. ПРОНУМЕРОВАННЫЕ ДУБЛИ — Glavnaya1, ObkhodDpiAktiven2 и т.п.
     которые возникли из-за повторной обработки одного файла
     → удаляем, если есть ненумерованный аналог с тем же значением

  3. ОСИРОТЕВШИЕ КЛЮЧИ — uid которого нет ни в одном .xaml
     → удаляем (опционально, флаг --keep-orphans)

Запуск:
    python xaml_resw_cleanup.py --root "F:\\Dev\\ZUI\\ZUI"
    python xaml_resw_cleanup.py --root "F:\\Dev\\ZUI\\ZUI" --dry
"""

import argparse
import re
import sys
from collections import defaultdict
from pathlib import Path
import xml.etree.ElementTree as ET

# ─── Настройки ────────────────────────────────────────────────────────────────

CONTROL_TEXT_ATTR: dict[str, list[str]] = {
    "TextBlock":          ["Text"],
    "Button":             ["Content"],
    "ComboBoxItem":       ["Content"],
    "ToggleSwitch":       ["Header"],
    "HyperlinkButton":    ["Content"],
    "RadioButton":        ["Content"],
    "CheckBox":           ["Content"],
    "AppBarButton":       ["Label"],
    "MenuFlyoutItem":     ["Text"],
    "NavigationViewItem": ["Content"],
    "TextBox":            ["PlaceholderText", "Header"],
    "PasswordBox":        ["PlaceholderText", "Header"],
}

SKIP_DIRS = {"obj", "bin", ".vs", "packages"}
X_NS  = "http://schemas.microsoft.com/winfx/2006/xaml"
X_UID = f"{{{X_NS}}}Uid"

# ─── Сканирование .xaml ───────────────────────────────────────────────────────

def scan_xaml(root_dir: Path) -> tuple[
    dict[str, set[str]],   # uid → допустимые ключи (.Text / .Content / ...)
    dict[str, int],        # uid → сколько раз встречается
]:
    """
    uid_valid_keys  — объединение всех валидных ключей для каждого uid
    uid_occurrences — для разрешения конфликтов считаем вхождения по типу контрола:
                      ключ "uid|ControlType" → количество
    """
    uid_valid_keys:  dict[str, set[str]]     = defaultdict(set)
    uid_ctrl_count:  dict[str, int]          = defaultdict(int)  # "uid|CtrlType" → count

    xaml_files = sorted(
        p for p in root_dir.rglob("*.xaml")
        if not any(part in SKIP_DIRS for part in p.relative_to(root_dir).parts)
    )

    print(f"   .xaml файлов: {len(xaml_files)}")

    for xaml_path in xaml_files:
        text = xaml_path.read_text(encoding="utf-8-sig")
        for prefix, uri in re.findall(r'xmlns(?::(\w+))?="([^"]+)"', text):
            try:
                ET.register_namespace(prefix or "", uri)
            except ValueError:
                pass
        ET.register_namespace("x", X_NS)

        try:
            root = ET.fromstring(text)
        except ET.ParseError as e:
            print(f"   [!] ParseError {xaml_path.name}: {e}", file=sys.stderr)
            continue

        for elem in root.iter():
            local = elem.tag.split("}")[-1] if "}" in elem.tag else elem.tag
            if local not in CONTROL_TEXT_ATTR:
                continue
            uid = elem.get(X_UID) or elem.get("x:Uid")
            if not uid:
                continue

            attrs = CONTROL_TEXT_ATTR[local]
            for attr in attrs:
                uid_valid_keys[uid].add(f"{uid}.{attr}")
            uid_ctrl_count[f"{uid}|{local}"] += 1

    return dict(uid_valid_keys), dict(uid_ctrl_count)


def resolve_conflicts(
    uid_valid_keys: dict[str, set[str]],
    uid_ctrl_count: dict[str, int],
) -> dict[str, set[str]]:
    """
    Если для uid есть ключи от разных типов контролов — оставляем только
    те, которые относятся к чаще встречающемуся типу контрола.
    Например: Nastroyki → TextBlock×1 vs NavigationViewItem×1 → берём оба,
    но если TextBlock×3 vs NavigationViewItem×1 → берём только .Text
    """
    resolved: dict[str, set[str]] = {}

    for uid, valid_keys in uid_valid_keys.items():
        # Группируем ключи по "ControlType"
        # Восстанавливаем тип по атрибуту: .Text → TextBlock, .Content → многие
        prop_to_ctrl: dict[str, str] = {}
        for ctrl, attrs in CONTROL_TEXT_ATTR.items():
            for attr in attrs:
                key = f"{uid}.{attr}"
                if key in valid_keys:
                    prop_to_ctrl[key] = ctrl

        # Считаем суммарные вхождения для каждого ключа
        key_score: dict[str, int] = {}
        for key, ctrl in prop_to_ctrl.items():
            score = uid_ctrl_count.get(f"{uid}|{ctrl}", 0)
            key_score[key] = score

        if not key_score:
            resolved[uid] = valid_keys
            continue

        max_score = max(key_score.values())

        # Если есть явный победитель — оставляем только его ключи
        # Если ничья — оставляем все (не можем решить автоматически)
        winners = {k for k, s in key_score.items() if s == max_score}
        losers  = {k for k, s in key_score.items() if s < max_score}

        resolved[uid] = winners  # только победители

        if losers:
            loser_str = ", ".join(sorted(losers))
            winner_str = ", ".join(sorted(winners))
            print(f"   ⚠  конфликт uid '{uid}': "
                  f"удаляем {loser_str!r}, оставляем {winner_str!r}")

    return resolved


# ─── Нумерованные дубли ───────────────────────────────────────────────────────

def find_numbered_duplicates(
    all_keys: dict[str, str],   # key → value
    uid_valid_keys: dict[str, set[str]],
) -> set[str]:
    """
    Возвращает ключи вида 'Uid1.Prop', 'Uid2.Prop' которые являются дублями
    ненумерованного 'Uid.Prop' с тем же значением (или uid не в xaml вообще).
    """
    numbered_re = re.compile(r'^(.+?)(\d+)(\..+)$')
    duplicates: set[str] = set()

    for key in all_keys:
        m = numbered_re.match(key)
        if not m:
            continue
        base_uid = m.group(1)
        prop     = m.group(3)
        base_key = base_uid + prop

        # Если базовый ключ существует — это дубль
        if base_key in all_keys:
            duplicates.add(key)
            uid = key.rsplit(".", 1)[0]
            print(f"   📋 дубль: {key!r} → заменяет {base_key!r}")
            continue

        # Если uid вообще нет в xaml — тоже лишнее
        uid = key.rsplit(".", 1)[0]
        if uid not in uid_valid_keys:
            duplicates.add(key)
            print(f"   👻 осиротевший дубль: {key!r} (uid не найден в .xaml)")

    return duplicates


# ─── Чистка .resw ─────────────────────────────────────────────────────────────

def cleanup_resw(
    resw_path: Path,
    uid_valid_keys: dict[str, set[str]],
    uid_ctrl_count: dict[str, int],
    dry: bool,
    keep_orphans: bool,
) -> None:
    if not resw_path.exists() or resw_path.stat().st_size == 0:
        print(f"  [skip] Не найден или пустой: {resw_path}")
        return

    try:
        tree = ET.parse(resw_path)
    except ET.ParseError as e:
        print(f"  [!] ParseError {resw_path.name}: {e}", file=sys.stderr)
        return

    root = tree.getroot()

    # Все data-ключи → значения
    all_keys: dict[str, str] = {}
    for d in root.findall("data"):
        name = d.get("name", "")
        val  = (d.findtext("value") or "").strip()
        if "." in name:
            all_keys[name] = val

    # Разрешаем конфликты типов
    print(f"\n  Анализ {resw_path.name}:")
    resolved_keys = resolve_conflicts(uid_valid_keys, uid_ctrl_count)

    # Ищем нумерованные дубли
    numbered_dups = find_numbered_duplicates(all_keys, uid_valid_keys)

    to_remove: list[tuple[ET.Element, str, str]] = []

    for data in root.findall("data"):
        name = data.get("name", "")
        if "." not in name:
            continue

        uid, prop = name.rsplit(".", 1)
        val = (data.findtext("value") or "").strip()

        # 1. Нумерованный дубль
        if name in numbered_dups:
            to_remove.append((data, name, "нумерованный дубль"))
            continue

        # 2. Uid не в xaml
        if uid not in uid_valid_keys:
            if not keep_orphans:
                to_remove.append((data, name, "uid не найден в .xaml"))
            continue

        # 3. Неверное свойство для этого uid
        valid = resolved_keys.get(uid, uid_valid_keys.get(uid, set()))
        if name not in valid:
            to_remove.append((data, name, f"неверное свойство (ожидается: {', '.join(sorted(valid))})"))

    if not to_remove:
        print(f"  ✓  Без изменений")
        return

    print(f"\n  {'[dry] ' if dry else ''}Будет удалено ({len(to_remove)}):")
    for data, name, reason in to_remove:
        val = (data.findtext("value") or "").strip()
        print(f"    ✗  {name!r:60s}  [{reason}]  =  {val!r}")
        if not dry:
            root.remove(data)

    if not dry:
        ET.indent(tree, space="  ")
        tree.write(resw_path, encoding="utf-8", xml_declaration=True)
        print(f"\n  ✏  Сохранён: {resw_path}  (-{len(to_remove)} записей)")

# ─── main ─────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(description="XAML .resw smart cleanup")
    ap.add_argument("--root", required=True)
    ap.add_argument("--resw", default=None)
    ap.add_argument("--dry",  action="store_true")
    ap.add_argument("--keep-orphans", action="store_true")
    args = ap.parse_args()

    root_dir = Path(args.root)

    if args.resw:
        resw_paths = [Path(p.strip()) for p in args.resw.split(",")]
    else:
        resw_paths = [
            root_dir / "Strings" / "ru-RU" / "Resources.resw",
            root_dir / "Strings" / "en-US" / "Resources.resw",
        ]

    print(f"📁 Корень: {root_dir.resolve()}")
    if args.dry:
        print("⚠️  DRY-RUN\n")

    print("🔍 Сканируем .xaml...")
    uid_valid_keys, uid_ctrl_count = scan_xaml(root_dir)
    print(f"   Найдено uid: {len(uid_valid_keys)}\n")

    for resw_path in resw_paths:
        print(f"📄 {resw_path}")
        cleanup_resw(resw_path, uid_valid_keys, uid_ctrl_count, args.dry, args.keep_orphans)

    print("\n✅ Готово!")

if __name__ == "__main__":
    main()
