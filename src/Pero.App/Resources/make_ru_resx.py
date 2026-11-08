"""Строит русские resx для ленты и редактора Syncfusion из английских оригиналов.

Оригиналы: https://github.com/syncfusion/wpf-controls-localization-resx-files
Запуск:  python make_ru_resx.py <папка с Syncfusion.SfRichTextRibbon.WPF.resx и Syncfusion.SfRichTextBoxAdv.WPF.resx>
Технические значения в CamelCase без пробелов (LowLetter, BrightGreen…) не переводятся.
"""
import re
import sys
import html
from xml.sax.saxutils import escape as xml_escape
from pathlib import Path

RU = {
    " of ": " из ", " replacement.": " замен.", "(None)": "(нет)", "(none)": "(нет)",
    "1 result found": "Найден 1 результат",
    "A complete information on Syncfusion RichTextBox control and its usage.": "Полная информация об элементе управления и его использовании.",
    "A table is a great way to organize information with in your document.": "Таблица — удобный способ упорядочить сведения в документе.",
    "Add": "Добавить", "Add a Hyperlink (Ctrl+K)": "Добавить гиперссылку (Ctrl+K)", "Add a Table": "Добавить таблицу",
    "Add a new column directly to the Right of the current column.": "Добавить столбец справа от текущего.",
    "Add a new column directly to the left of the current column.": "Добавить столбец слева от текущего.",
    "Add a new row directly above the current row.": "Добавить строку над текущей.",
    "Add a new row directly below the current row.": "Добавить строку под текущей.",
    "Add a note about this part of the document.": "Добавить примечание к этой части документа.",
    "Add content on the Clipboard to your document.": "Вставить содержимое буфера обмена в документ.",
    "Add to Dictionary": "Добавить в словарь", "Address": "Адрес", "After": "После",
    "Align Bottom Center": "По нижнему краю по центру", "Align Bottom Left": "По нижнему краю слева",
    "Align Bottom Right": "По нижнему краю справа", "Align Center": "По центру",
    "Align Center Left": "По центру слева", "Align Center Right": "По центру справа",
    "Align Left (Ctrl+L)": "Выровнять по левому краю (Ctrl+L)", "Align Right (Ctrl+R)": "Выровнять по правому краю (Ctrl+R)",
    "Align Top Center": "По верхнему краю по центру", "Align Top Left": "По верхнему краю слева",
    "Align Top Right": "По верхнему краю справа",
    "Align text to the bottom left corner of the cell.": "Выровнять текст по нижнему левому углу ячейки.",
    "Align text to the bottom right corner of the cell.": "Выровнять текст по нижнему правому углу ячейки.",
    "Align text to the top left corner of the cell.": "Выровнять текст по верхнему левому углу ячейки.",
    "Align text to the top right corner of the cell.": "Выровнять текст по верхнему правому углу ячейки.",
    "Align your content with the left margin.": "Выровнять содержимое по левому полю.",
    "Align your content with the right margin.": "Выровнять содержимое по правому полю.",
    "Aligned at": "Выравнивание на", "Alignment": "Выравнивание", "All": "Все",
    "All done. We made ": "Готово. Выполнено ", "Allow row to break across pages": "Разрешить перенос строк на следующую страницу",
    "Allow spacing between cells": "Интервалы между ячейками", "Alt Text": "Замещающий текст", "Apply To": "Применить к",
    "Arabic": "Арабские", "At": "Значение", "At least": "Минимум", "AutoFit": "Автоподбор",
    "AutoFit Contents": "Автоподбор по содержимому", "AutoFit Window": "Автоподбор по ширине окна",
    "AutoFit to Contents": "Автоподбор по содержимому", "AutoFit to Window": "Автоподбор по ширине окна",
    "Automatic": "Авто", "Automatically resize the columns in your table.": "Автоматически подобрать ширину столбцов таблицы.",
    "Automatically resize to fit contents": "Автоподбор размеров по содержимому", "Available Templates": "Доступные шаблоны",
    "Before": "Перед", "Black": "Чёрный", "Blank Document": "Новый документ", "Blue": "Синий",
    "Bold": "Полужирный", "Bold (Ctrl+B)": "Полужирный (Ctrl+B)", "Borders": "Границы",
    "Borders and Shading": "Границы и заливка", "Bottom": "Снизу", "Box": "Рамка",
    "Bright Green": "Ярко-зелёный", "Bullet": "Маркер", "Bullet Library": "Библиотека маркеров", "Bullets": "Маркеры",
    "By": "На", "Cancel": "Отмена", "Cell": "Ячейка", "Cell Margins": "Поля ячейки", "Cell Options": "Параметры ячейки",
    "Cell Size": "Размер ячейки", "Cell margins": "Поля ячейки", "Center": "По центру",
    "Center (Ctrl+E)": "По центру (Ctrl+E)",
    "Center text and align it to the bottom of the cell.": "Выровнять текст по центру и по нижнему краю ячейки.",
    "Center text and align it to the top of the cell.": "Выровнять текст по центру и по верхнему краю ячейки.",
    "Center text horizontally and vertically within the cell.": "Выровнять текст по центру ячейки по горизонтали и вертикали.",
    "Center text vertically and align it to the left side of the cell.": "Выровнять текст по центру по вертикали и по левому краю ячейки.",
    "Center text vertically and align it to the right side of the cell.": "Выровнять текст по центру по вертикали и по правому краю ячейки.",
    "Center your content on the page.": "Выровнять содержимое по центру страницы.",
    "Change": "Заменить", "Change All": "Заменить все", "Change the color of you text.": "Изменить цвет текста.",
    "Change the size of your text.": "Изменить размер текста.", "Character": "Знак",
    "Check out how your document will look when it's printed.": "Посмотреть, как документ будет выглядеть при печати.",
    "Choose Level to modify": "Выберите уровень для изменения",
    "Choose how much space appears between lines of text.": "Выбрать интервал между строками текста.",
    "Clear Formatting": "Очистить формат", "Click the arrow for more numbering formats.": "Нажмите стрелку, чтобы выбрать другой формат нумерации.",
    "Click the arrow to change look of the bullet.": "Нажмите стрелку, чтобы изменить вид маркера.",
    "Click to follow link": "Щёлкните для перехода по ссылке", "Clipboard": "Буфер обмена", "Close": "Закрыть",
    "Color": "Цвет", "Column size": "Ширина столбца", "Comments": "Примечания",
    "Control what types of changes people can make to this document.": "Определить, какие изменения можно вносить в документ.",
    "Copy": "Копировать", "Copy (Ctrl+C)": "Копировать (Ctrl+C)", "Copy Hyperlink": "Копировать гиперссылку",
    "Create": "Создать", "Create New Style": "Создание стиля", "Create a Style": "Создать стиль",
    "Create a link in your document for quick access to webpage and files.": "Создать ссылку для быстрого перехода к веб-странице или файлу.",
    "Create a multilevel list to organize items or create an outline.": "Создать многоуровневый список или структуру.",
    "Create bullet list.": "Создать маркированный список.", "Create numbered list.": "Создать нумерованный список.",
    "Cross something out by drawing a line through it.": "Зачеркнуть текст линией.", "Custom": "Другой",
    "Customize cell margins and spacing between cells.": "Настроить поля ячеек и интервалы между ними.",
    "Cut": "Вырезать", "Cut (Ctrl+X)": "Вырезать (Ctrl+X)", "Dark Blue": "Тёмно-синий", "Dark Red": "Тёмно-красный",
    "Dark Yellow": "Тёмно-жёлтый", "Data": "Данные", "Decrease Font": "Уменьшить шрифт",
    "Decrease Font Size": "Уменьшить размер шрифта", "Decrease Font Size (Ctrl+Shift+<) ": "Уменьшить размер шрифта (Ctrl+Shift+<) ",
    "Decrease Indent": "Уменьшить отступ", "Decrease the zoom level.": "Уменьшить масштаб.",
    "Default cell margins": "Поля ячеек по умолчанию", "Default cell spacing": "Интервал между ячейками по умолчанию",
    "Define List": "Определить список", "Define new Multilevel List...": "Определить новый многоуровневый список...",
    "Delete": "Удалить", "Delete Columns": "Удалить столбцы", "Delete Comment": "Удалить примечание",
    "Delete Rows": "Удалить строки", "Delete Table": "Удалить таблицу",
    "Delete rows, columns, or the entire table.": "Удалить строки, столбцы или всю таблицу.",
    "Delete the selected comment.": "Удалить выбранное примечание.", "Description": "Описание", "Direction:": "Направление:",
    "Distribute your text evenly between the margin.": "Распределить текст равномерно между полями.",
    "Double strikethrough": "Двойное зачёркивание", "Edit Hyperlink": "Изменить гиперссылку", "Editing": "Редактирование",
    "Effects": "Видоизменение", "Encrypt Document": "Зашифровать документ", "Encrypt with password": "Зашифровать с помощью пароля",
    "End the current page here and move to the next page.": "Завершить текущую страницу и перейти на следующую.",
    "Enter the password": "Введите пароль", "Exactly": "Точно", "File": "Файл", "Fill": "Заливка",
    "Find": "Найти", "Find (Ctrl+F)": "Найти (Ctrl+F)", "Find Next": "Найти далее", "Find and Replace": "Найти и заменить",
    "Find text or other content in the document.": "Найти текст или другое содержимое в документе.",
    "Find what": "Найти", "Find whole words only": "Только слово целиком", "First line": "Первая строка",
    "Fixed Column Width": "Фиксированная ширина столбцов", "Follow number with": "Символ после номера",
    "Font": "Шрифт", "Font (Ctrl+Shift+F)": "Шрифт (Ctrl+Shift+F)", "Font Color": "Цвет шрифта",
    "Font Size (Ctrl+Shift+P)": "Размер шрифта (Ctrl+Shift+P)", "Font Style": "Начертание", "Font...": "Шрифт...",
    "Format": "Формат", "Formatting": "Форматирование", "Formatting for number": "Формат номера", "From File": "Из файла",
    "General": "Общие", "Getting Started": "Начало работы", "Gray-25%": "Серый 25%", "Gray-50%": "Серый 50%",
    "Green": "Зелёный", "Grow Font": "Увеличить шрифт", "Hanging": "Выступ",
    "Heading 1": "Заголовок 1", "Heading 2": "Заголовок 2", "Heading 3": "Заголовок 3",
    "Heading 4": "Заголовок 4", "Heading 5": "Заголовок 5", "Heading 6": "Заголовок 6",
    "Help": "Справка", "Home": "Главная", "Html Document": "Документ HTML", "Hyperlink": "Гиперссылка",
    "Ignore All": "Пропустить все", "Illustrations": "Иллюстрации", "Increase Font Size": "Увеличить размер шрифта",
    "Increase Font Size (Ctrl+Shift+>) ": "Увеличить размер шрифта (Ctrl+Shift+>) ", "Increase Indent": "Увеличить отступ",
    "Increase the zoom level": "Увеличить масштаб", "Indent from Left": "Отступ слева", "Indent from Right": "Отступ справа",
    "Indentation": "Отступ", "Indents": "Отступы", "Info": "Сведения", "Information": "Сведения", "Insert": "Вставка",
    "Insert Above": "Вставить сверху", "Insert Below": "Вставить снизу",
    "Insert Columns to the Left": "Вставить столбцы слева", "Insert Columns to the Right": "Вставить столбцы справа",
    "Insert Hyperlink": "Вставка гиперссылки", "Insert Left": "Вставить слева", "Insert Right": "Вставить справа",
    "Insert Rows Above": "Вставить строки сверху", "Insert Rows Below": "Вставить строки снизу",
    "Insert Table": "Вставка таблицы", "Insert Table...": "Вставить таблицу...", "Insert a Comment": "Вставить примечание",
    "Insert a Page Break (Ctrl+Return)": "Вставить разрыв страницы (Ctrl+Enter)",
    "Insert pictures from your computer or from other computers that you're connected to.": "Вставить рисунки с компьютера или из подключённых папок.",
    "It creates the new empty document.": "Создаёт новый пустой документ.", "Italic": "Курсив", "Italic (Ctrl+I)": "Курсив (Ctrl+I)",
    "Italicize your text.": "Выделить текст курсивом.", "Jump to the next comment.": "Перейти к следующему примечанию.",
    "Jump to the previous comment.": "Перейти к предыдущему примечанию.", "Justify": "По ширине", "Justify (Ctrl+J)": "По ширине (Ctrl+J)",
    "Layout": "Макет", "Left": "Слева", "Left-to-Right Text Direction": "Направление текста слева направо",
    "Left-to-right": "Слева направо", "Less": "Меньше",
    "Level 1": "Уровень 1", "Level 2": "Уровень 2", "Level 3": "Уровень 3", "Level 4": "Уровень 4", "Level 5": "Уровень 5",
    "Level 6": "Уровень 6", "Level 7": "Уровень 7", "Level 8": "Уровень 8", "Level 9": "Уровень 9",
    "Line Spacing": "Междустрочный интервал", "Linked (paragraph and character)": "Связанный (абзац и знак)", "Links": "Ссылки",
    "List Level": "Уровень списка", "List Library": "Библиотека списков", "Loading page": "Загрузка страницы",
    "Make your text a bit bigger.": "Немного увеличить текст.", "Make your text a bit smaller.": "Немного уменьшить текст.",
    "Make your text bold.": "Выделить текст полужирным.",
    "Make your text pop by highlighting it in a bright color.": "Выделить текст ярким цветом маркера.",
    "Mark multiple rows as header rows. If your table spans multiple pages the header rows will appear on every page for easy reference.":
        "Пометить строки как заголовок. Если таблица занимает несколько страниц, заголовок повторяется на каждой.",
    "Match case": "Учитывать регистр", "Measure in": "Единицы", "Merge": "Объединение", "Merge Cells": "Объединить ячейки",
    "Merge the selected cells into one cell.": "Объединить выделенные ячейки в одну.", "Modify": "Изменить", "Modify Style": "Изменение стиля",
    "More": "Больше", "Move your paragraph closer to the margin.": "Приблизить абзац к полю.",
    "Move your paragraph farther away from the margin.": "Отодвинуть абзац от поля.", "Multilevel List": "Многоуровневый список",
    "Multiple": "Множитель", "Multiple Pages": "Несколько страниц", "Name": "Имя", "New": "Создать", "New Comment": "Создать примечание",
    "New window": "Новое окно", "Next": "Далее", "Next Comment": "Следующее", "No Color": "Нет цвета",
    "No results found": "Ничего не найдено", "None": "Нет", "Number": "Номер",
    "Number Alignment: Left": "Выравнивание номера: по левому краю", "Number Alignment: Right": "Выравнивание номера: по правому краю",
    "Number Format": "Формат номера", "Number alignment: Left": "Выравнивание номера: по левому краю",
    "Number alignment: Right": "Выравнивание номера: по правому краю", "Number style for this level": "Вид нумерации для уровня",
    "Numbering": "Нумерация", "Numbering Library": "Библиотека нумерации", "Numbering and Bullets": "Нумерация и маркеры",
    "OK": "ОК", "Of": "из", "One Page": "Одна страница", "Online Help": "Справка в сети", "Open": "Открыть",
    "Open Hyperlink": "Открыть гиперссылку", "Options": "Параметры", "Page": "Страница", "Page Break": "Разрыв страницы",
    "Page Width": "По ширине страницы", "Page number in document.": "Номер страницы в документе.", "Pages": "Страницы",
    "Paragraph": "Абзац", "Paragraph...": "Абзац...", "Parent frame": "Родительская рамка",
    "Password-protect this document.": "Защитить документ паролем.", "Paste": "Вставить", "Paste (Ctrl+V)": "Вставить (Ctrl+V)",
    "Percent": "Проценты", "Pick a new font for your text.": "Выбрать шрифт для текста.", "Pictures": "Рисунки", "Pink": "Розовый",
    "Pixel": "Пиксели", "Preferred width": "Ширина", "Preview": "Образец", "Previous": "Назад", "Previous Comment": "Предыдущее",
    "Print": "Печать", "Print Layout": "Разметка страницы", "Proofing": "Правописание", "Properties": "Свойства",
    "Protect Document": "Защитить документ",
    "Put a copy of the selection on the clipboard so you can paste it somewhere else.": "Поместить копию выделенного фрагмента в буфер обмена.",
    "RESULTS": "РЕЗУЛЬТАТЫ", "ReadOnly": "Только чтение", "Red": "Красный", "Redo": "Повторить", "Redo (Ctrl+Y)": "Повторить (Ctrl+Y)",
    "Remove Hyperlink": "Удалить гиперссылку",
    "Remove the selection and put it on the Clipboard so you can paste it somewhere else.": "Удалить выделенный фрагмент и поместить его в буфер обмена.",
    "Repeat Header Rows": "Повторять заголовки", "Repeat as header row at the top of each page": "Повторять как заголовок на каждой странице",
    "Replace": "Заменить", "Replace (Ctrl+H)": "Заменить (Ctrl+H)", "Replace All": "Заменить все", "Replace with": "Заменить на",
    "Restart by": "Начать заново", "Restrict Editing": "Ограничить редактирование",
    "Restrict the content editing and formatting changes.": "Ограничить изменение содержимого и форматирования.",
    "Restricts editing to this document.": "Ограничивает редактирование документа.", "Result ": "Результат ", "Resume": "Продолжить",
    "Review": "Рецензирование", "Right": "Справа", "Right-to-Left Text Direction": "Направление текста справа налево",
    "Right-to-left": "Справа налево", "Row": "Строка", "Row height is": "Режим высоты", "Row size": "Высота строки",
    "Rows & Columns": "Строки и столбцы", "Same as the whole table": "Как у всей таблицы", "Same frame": "Та же рамка",
    "Save": "Сохранить", "Save (Ctrl+S)": "Сохранить (Ctrl+S)", "Save a copy as Word 97-2003": "Сохранить копию в формате Word 97-2003",
    "Save as": "Сохранить как", "Save the file as Html document": "Сохранить файл как документ HTML",
    "Save the file as Text document": "Сохранить файл как текстовый документ", "Save the file as Word document": "Сохранить файл как документ Word",
    "Save the file as Xaml document": "Сохранить файл как документ XAML", "ScreenTip Text": "Текст подсказки", "Search": "Поиск",
    "Search for more things": "Искать другое",
    "Search for text you'd like to change and replace it with something else.": "Найти текст и заменить его другим.",
    "Search paused": "Поиск приостановлен", "See all the comments alongside the document.": "Показать все примечания рядом с документом.",
    "See how your document would look as a webpage. This layout is also great if you have wide tables in your document.":
        "Показать документ в виде веб-страницы. Удобно, если в документе широкие таблицы.",
    "Select": "Выделить", "Select Cell": "Выделить ячейку", "Select Column": "Выделить столбец", "Select Row": "Выделить строку",
    "Select Table": "Выделить таблицу", "Select the current cell, row, column, or entire table.": "Выделить текущую ячейку, строку, столбец или всю таблицу.",
    "Set the text to read from left to right.": "Текст читается слева направо.", "Set the text to read from right to left.": "Текст читается справа налево.",
    "Setting": "Тип", "Shading": "Заливка", "Show Comments": "Показать примечания",
    "Show the Table Properties dialog to change advanced table properties, such as indentation.": "Открыть свойства таблицы, например отступы.",
    "Size": "Размер", "Space": "Пробел", "Spacing": "Интервал", "Special": "Особый", "Specify height": "Высота",
    "Spelling": "Орфография", "Spelling (F7)": "Орфография (F7)", "Spelling check complete. You're good to go!": "Проверка орфографии завершена.",
    "Start at": "Начать с", "Strikethrough": "Зачёркнутый", "Style": "Стиль", "Style based on": "Основан на стиле",
    "Style for following paragraph": "Стиль следующего абзаца", "Style type": "Тип стиля", "Styles": "Стили",
    "Subscript": "Подстрочный", "Subscript (Ctrl+=)": "Подстрочный (Ctrl+=)", "Superscript": "Надстрочный",
    "Superscript (Ctrl+Shift++)": "Надстрочный (Ctrl+Shift++)", "Syncfusion User Interface Help": "Справка по интерфейсу",
    "Tab": "Табуляция", "Table": "Таблица", "Table Options": "Параметры таблицы", "Table Properties": "Свойства таблицы",
    "Table Properties...": "Свойства таблицы...", "Table Tools": "Работа с таблицами", "Table direction": "Направление таблицы",
    "Tables": "Таблицы", "Target Frame": "Целевая рамка", "Teal": "Сине-зелёный", "Text Document": "Текстовый документ",
    "Text Highlight Color": "Цвет выделения текста", "Text indent at": "Отступ текста", "Text to Display": "Текст",
    "That's shows up a lot.": "Встречается часто.", "Title": "Название", "Top": "Сверху", "Turquoise": "Бирюзовый",
    "Type very small letter just above the line of text.": "Набрать маленькие знаки над строкой текста.",
    "Type very small letter just just above the line of text.": "Набрать маленькие знаки над строкой текста.",
    "Typos? Not on our watch. Let us check your spelling.": "Проверить орфографию документа.",
    "UG documents helps understanding the usage of our Syncfusion UI components.": "Руководство по использованию компонентов интерфейса.",
    "Underline": "Подчёркнутый", "Underline (Ctrl+U)": "Подчёркнутый (Ctrl+U)", "Underline your text.": "Подчеркнуть текст.",
    "Undo": "Отменить", "Undo (Ctrl+Z)": "Отменить (Ctrl+Z)", "Use regular expression": "Регулярные выражения",
    "Use search box for find text in the document.": "Используйте поле поиска, чтобы найти текст в документе.",
    "Vertical alignment": "Вертикальное выравнивание", "View": "Вид", "Views": "Режимы", "Violet": "Фиолетовый",
    "We couldn't find what you were looking for.": "Ничего не найдено.", "We couldn’t find the person you were looking for.": "Пользователь не найден.",
    "Web Layout": "Веб-документ", "Whole page": "Страница целиком", "Width": "Ширина",
    "Word 97 - 2003 Document": "Документ Word 97–2003", "Word Document": "Документ Word", "Xaml Document": "Документ XAML",
    "Yellow": "Жёлтый", "Zoom": "Масштаб", "Zoom In": "Увеличить", "Zoom Out": "Уменьшить",
    "Zoom the document so that the width of the page matches the width of the window.": "Масштаб по ширине страницы.",
    "Zoom the document so that you can see the entire page in window.": "Показать страницу целиком.",
    "Zoom the document so you can see multiple pages in the window.": "Показать несколько страниц в окне.",
    "Zoom your document to 100%.": "Масштаб 100%.",
}

IDENT = re.compile(r"^[A-Z][A-Za-z]*[a-z][A-Za-z]*$")  # LowLetter, BrightGreen, FarEast…
KEEP = {"AaBbCcDdEe", "100%", "Gray-25%", "Gray-50%"}


def build(src: Path) -> tuple[int, list[str]]:
    """Заменяет значения прямо в тексте, сохраняя остальную разметку resx без изменений."""
    text = src.read_text(encoding="utf-8")
    missing: list[str] = []
    count = 0

    def repl(m: re.Match) -> str:
        nonlocal count
        raw = html.unescape(m.group(2))
        if raw in RU and raw not in KEEP:
            count += 1
            return m.group(1) + xml_escape(RU[raw]) + m.group(3)
        if not (raw in KEEP or (IDENT.match(raw) and raw not in RU)):
            missing.append(raw)
        return m.group(0)

    # только <value> внутри <data>, заголовки resheader не трогаем
    out = re.sub(r'(<data [^>]*>\s*<value>)(.*?)(</value>)', repl, text, flags=re.S)
    src.with_name(src.stem + ".ru.resx").write_text(out, encoding="utf-8")
    return count, missing


if __name__ == "__main__":
    folder = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    for name in ("Syncfusion.SfRichTextRibbon.WPF.resx", "Syncfusion.SfRichTextBoxAdv.WPF.resx"):
        n, miss = build(folder / name)
        print(f"{name}: переведено {n}, без перевода {len(miss)}")
        for m in miss:
            print("   ", repr(m))
