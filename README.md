# Перо — редактор документов DOCX для Windows

Редактор документов DOCX для Windows по ТЗ «Word Editor Technical Specification» (v1.0, 07.11.2026).

- **Состояние по этапам, замеры и решения:** [docs/STATUS.md](docs/STATUS.md)
- Выбор движка (этап 0): [docs/STAGE0.md](docs/STAGE0.md)
- Черновики дополнений для этапа 3 (P2): [docs/P2-ADDENDA.md](docs/P2-ADDENDA.md)

**Готовая сборка:** вкладка *Actions* → последний зелёный прогон → *Artifacts* → `Pero-Setup` (установщик) или `Pero-win-x64` (папка с `Pero.exe`).
**Результаты автоматической проверки** (скриншоты, журналы, круговой обмен на движке, замеры) — ветка `ci-results`.

## Состав

| Папка | Что внутри |
|---|---|
| `src/Pero.App` | Оболочка WPF (.NET 8): название и статус, меню, компактная панель, две линейки, постраничный редактор, поиск, печать, PDF, автовосстановление. |
| `src/Pero.Doc` | Собственное ядро: модель документа, правка с отменой, разметка страниц, чтение и запись DOCX, шрифты TrueType, отрисовка и PDF. Без сторонних библиотек. |
| `src/Pero.Core` | Ядро без внешних зависимостей: проверка пакета DOCX (SEC-01, DOCX-01/02), реестр совместимости, атомарное сохранение (FILE-02), копии восстановления (FILE-03), обнаружение внешних изменений (FILE-04), перенос отделимых частей OOXML, отпечаток документа для кругового обмена (QA-01). |
| `tools/Pero.Probe` | `pero-probe` — проверка DOCX, сравнение «до/после», матрица совместимости. |
| `tests/Pero.Core.Tests` | Автотесты ядра (`dotnet run`). |
| `tools/Pero.Lab` | Лаборатория для CI на Windows: API движка, круговой обмен на SfRichTextBoxAdv, замеры, UI-сценарии готового `Pero.exe` с FlaUI и скриншотами. |
| `installer/pero.iss` | Установщик Inno Setup. |
| `samples/make_samples.py` | Генератор стартового эталонного набора: стили и списки, таблицы, изображения, неподдерживаемые конструкции, повреждённые и опасные файлы. |
| `src/Pero.App/Resources` | Русская локализация ленты и редактора (533 строки) и скрипт её генерации. |

## Сборка

Нужны Windows 10/11 x64, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), по желанию Python 3 для эталонов.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\artifacts\Pero\Pero.exe
```

Или в Visual Studio 2022: открыть `Pero.sln`, стартовый проект `Pero.App`.

### Лицензия движка

Лицензии и ключи не нужны: приложение использует только .NET 8 и собственное ядро (см. `docs/CORE-PLAN.md`, `src/Pero.App/THIRD-PARTY-NOTICES.txt`).

Без ключа приложение работает, но движок показывает баннер пробной версии.

## Запуск и параметры

```
Pero.exe [файл.docx ...] [--lang=en]
```

- Язык интерфейса: `--lang=en` или `"Language": "en"` в `settings.json` (по умолчанию русский).
- Журнал (без текста документов): `%LOCALAPPDATA%\Pero\Logs\`. Строки `PERF` — замеры открытия, сохранения, автовосстановления.
- Копии восстановления: `%LOCALAPPDATA%\Pero\Recovery\`.
- Реестр совместимости переопределяется файлом `compat-registry.json` рядом с exe (шаблон: `pero-probe registry`).

## Утилита проверки

```powershell
dotnet run --project tools/Pero.Probe -- inspect samples/docx
dotnet run --project tools/Pero.Probe -- compare before.docx after.docx
dotnet run --project tools/Pero.Probe -- matrix samples/docx > matrix.md
```
