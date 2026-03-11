#!/usr/bin/env python3
"""
Скрипт проверки качества документации проекта Z-UI
"""

import os
import re
import sys
from pathlib import Path
from typing import List, Dict, Tuple

class DocumentationChecker:
    def __init__(self):
        self.errors = []
        self.warnings = []
        self.stats = {
            'total_files': 0,
            'doc_files': 0,
            'code_files': 0,
            'missing_docs': 0,
            'broken_links': 0
        }

    def check_readme(self, readme_path: Path) -> None:
        """Проверка README.md файла"""
        if not readme_path.exists():
            self.errors.append(f"README.md не найден: {readme_path}")
            return

        with open(readme_path, 'r', encoding='utf-8') as f:
            content = f.read()

        # Проверка основных секций
        required_sections = [
            "# Z-UI",
            "## Быстрый старт",
            "### Установка",
            "## Разработка",
            "## Тестирование",
            "## Ссылки"
        ]

        for section in required_sections:
            if section not in content:
                self.warnings.append(f"Отсутствует секция: {section}")

        # Проверка кодовых блоков
        code_blocks = re.findall(r'```(csharp|python|bash|xml|json).*?```', content, re.DOTALL)
        self.stats['code_blocks'] = len(code_blocks)

        # Проверка ссылок
        links = re.findall(r'\[(.*?)\]\((.*?)\)', content)
        for text, url in links:
            if not url.startswith(('http://', 'https://', 'ftp://', '#')) and not url.endswith(('.md', '.txt', '.pdf')):
                self.warnings.append(f"Создательная ссылка: [{text}]({url})")

    def check_csharp_files(self, project_path: Path) -> None:
        """Проверка C# файлов на наличие XML комментариев"""
        csharp_files = list(project_path.rglob("*.cs"))
        self.stats['code_files'] = len(csharp_files)

        for file_path in csharp_files:
            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()

            # Проверка наличия XML комментариев
            if not re.search(r'/// <summary>', content) and not re.search(r'/// <remarks>', content):
                if not re.search(r'class|interface|enum|struct', content.splitlines()[0]):
                    continue  # Пропускаем файлы без объявлений классов
                self.warnings.append(f"Отсутствуют XML комментарии: {file_path}")
                self.stats['missing_docs'] += 1

    def check_xml_docs(self, project_path: Path) -> None:
        """Проверка XML документации"""
        xml_files = list(project_path.rglob("*.xml"))

        for xml_file in xml_files:
            if 'bin' in str(xml_file) or 'obj' in str(xml_file):
                continue

            with open(xml_file, 'r', encoding='utf-8') as f:
                content = f.read()

            # Проверка структуры XML
            if not re.search(r'<members>', content):
                self.errors.append(f"Неверная структура XML: {xml_file}")

    def check_links(self, base_path: Path) -> None:
        """Проверка внутренних ссылок"""
        markdown_files = list(base_path.rglob("*.md"))

        for md_file in markdown_files:
            with open(md_file, 'r', encoding='utf-8') as f:
                content = f.read()

            links = re.findall(r'\[(.*?)\]\((.*?)\)', content)
            for text, url in links:
                if url.startswith('#'):
                    # Внутренняя ссылка
                    if url not in content:
                        self.errors.append(f"Побита внутренняя ссылка: [{text}]({url}) в файле {md_file}")
                        self.stats['broken_links'] += 1
                elif url.endswith('.md'):
                    target_path = base_path / Path(url)
                    if not target_path.exists():
                        self.errors.append(f"Неисполнимая ссылка: [{text}]({url}) в файле {md_file}")
                        self.stats['broken_links'] += 1

    def generate_report(self) -> str:
        """Генерация отчета"""
        report = []
        report.append("=" * 60)
        report.append("REPORT: Проверка качества документации")
        report.append("=" * 60)
        report.append("Статистика:")
        report.append(f"  Общие файлы: {self.stats['total_files']}")
        report.append(f"  Файлы документации: {self.stats['doc_files']}")
        report.append(f"  Файлы кода: {self.stats['code_files']}")
        report.append(f"  Отсутствующие XML комментарии: {self.stats['missing_docs']}")
        report.append(f"  Пробитые ссылки: {self.stats['broken_links']}")
        report.append(f"  Кодовых блоков: {self.stats.get('code_blocks', 0)}")
        report.append("")

        if self.errors:
            report.append("Ошибки:")
            for error in self.errors:
                report.append(f"  ❌ {error}")
            report.append("")

        if self.warnings:
            report.append("Предупреждения:")
            for warning in self.warnings:
                report.append(f"  ⚠️ {warning}")
            report.append("")

        if not self.errors and not self.warnings:
            report.append("✅ Все проверки пройдены успешно!")

        report.append("=" * 60)
        return "\n".join(report)

def main():
    """Основная функция проверки"""
    checker = DocumentationChecker()

    # Пути к файлам
    project_root = Path(__file__).parent.parent
    readme_path = project_root / "README.md"

    # Проверка README
    checker.check_readme(readme_path)

    # Проверка C# файлов
    checker.check_csharp_files(project_root)

    # Проверка XML документации
    checker.check_xml_docs(project_root)

    # Проверка ссылок
    checker.check_links(project_root)

    # Статистика
    checker.stats['total_files'] = len(list(project_root.rglob("*")))
    checker.stats['doc_files'] = len(list(project_root.rglob("*.md"))) + len(list(project_root.rglob("*.txt"))) + len(list(project_root.rglob("*.xml")))

    # Генерация отчета
    print(checker.generate_report())

    # Возврат кода выполнения
    if checker.errors:
        sys.exit(1)
    elif checker.warnings:
        sys.exit(2)
    else:
        sys.exit(0)

if __name__ == "__main__":
    main()