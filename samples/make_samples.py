"""Генератор стартового эталонного набора для этапа 0 (часть из 40 файлов раздела 7 ТЗ).

Запуск: python make_samples.py <папка>
Нужны пакеты python-docx и pillow.
"""
import io
import sys
import zipfile
from pathlib import Path

from docx import Document
from docx.enum.section import WD_ORIENT
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK
from docx.oxml import OxmlElement, parse_xml
from docx.oxml.ns import nsdecls, qn
from docx.shared import Cm, Pt, RGBColor
from PIL import Image, ImageDraw

sys.stdout.reconfigure(encoding="utf-8")  # консоль Windows по умолчанию не UTF-8
OUT = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
OUT.mkdir(parents=True, exist_ok=True)

LOREM = ("Редактор должен открывать документ, сохранять оформление и переносить текст между "
         "страницами так же, как это делает привычный текстовый процессор. ")


def add_field(paragraph, code):
    """Поле со сложной разметкой fldChar/instrText (как пишет Word)."""
    for kind, text in (("begin", None), (None, code), ("separate", None), (None, "1"), ("end", None)):
        run = paragraph.add_run()
        if kind:
            el = OxmlElement("w:fldChar")
            el.set(qn("w:fldCharType"), kind)
            run._r.append(el)
        elif text == code:
            el = OxmlElement("w:instrText")
            el.set(qn("xml:space"), "preserve")
            el.text = f" {code} "
            run._r.append(el)
        else:
            run.text = text


def add_hyperlink(paragraph, url, text):
    part = paragraph.part
    r_id = part.relate_to(url, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
                          is_external=True)
    link = OxmlElement("w:hyperlink")
    link.set(qn("r:id"), r_id)
    run = OxmlElement("w:r")
    rpr = OxmlElement("w:rPr")
    style = OxmlElement("w:rStyle")
    style.set(qn("w:val"), "Hyperlink")
    rpr.append(style)
    run.append(rpr)
    t = OxmlElement("w:t")
    t.text = text
    run.append(t)
    link.append(run)
    paragraph._p.append(link)


def shade(cell, hex_fill):
    cell._tc.get_or_add_tcPr().append(parse_xml(f'<w:shd {nsdecls("w")} w:val="clear" w:color="auto" w:fill="{hex_fill}"/>'))


def header_footer(doc, title):
    sec = doc.sections[0]
    sec.different_first_page_header_footer = True
    sec.header.paragraphs[0].text = title
    sec.header.paragraphs[0].alignment = WD_ALIGN_PARAGRAPH.RIGHT
    fp = sec.footer.paragraphs[0]
    fp.alignment = WD_ALIGN_PARAGRAPH.CENTER
    fp.add_run("Страница ")
    add_field(fp, "PAGE")
    fp.add_run(" из ")
    add_field(fp, "NUMPAGES")
    sec.first_page_footer.paragraphs[0].text = "Титульная страница"


def png(color, label, w=480, h=300):
    img = Image.new("RGB", (w, h), color)
    d = ImageDraw.Draw(img)
    d.rectangle([10, 10, w - 10, h - 10], outline="white", width=6)
    d.text((30, 30), label, fill="white")
    buf = io.BytesIO()
    img.save(buf, "PNG")
    buf.seek(0)
    return buf


# 1. Стили, списки, многостраничность, колонтитулы
doc = Document()
header_footer(doc, "Эталон 01 — стили и списки")
doc.add_paragraph("Годовой отчёт", style="Title")
for ch in range(1, 6):
    doc.add_heading(f"Глава {ch}. Раздел документа", level=1)
    doc.add_paragraph(LOREM * 6)
    doc.add_heading(f"{ch}.1 Подраздел", level=2)
    for i in range(3):
        doc.add_paragraph(f"Пункт маркированного списка {i + 1}", style="List Bullet")
        doc.add_paragraph(f"Вложенный пункт {i + 1}.a", style="List Bullet 2")
    doc.add_heading(f"{ch}.1.1 Детали", level=3)
    for i in range(4):
        doc.add_paragraph(f"Нумерованный шаг {i + 1}", style="List Number")
    p = doc.add_paragraph()
    r = p.add_run("Жирный, ")
    r.bold = True
    r = p.add_run("курсив, ")
    r.italic = True
    r = p.add_run("подчёркнутый, ")
    r.underline = True
    r = p.add_run("цветной ")
    r.font.color.rgb = RGBColor(0xC0, 0x30, 0x30)
    r = p.add_run("и крупный текст.")
    r.font.size = Pt(16)
    p = doc.add_paragraph(LOREM * 4)
    p.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    p.paragraph_format.first_line_indent = Cm(1.25)
    p.paragraph_format.space_after = Pt(12)
    p.paragraph_format.line_spacing = 1.5
    if ch < 5:
        doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)
doc.save(OUT / "01-styles-lists-multipage.docx")

# 2. Таблицы
doc = Document()
header_footer(doc, "Эталон 02 — таблицы")
doc.add_heading("Таблицы", level=1)
t = doc.add_table(rows=4, cols=4)
t.style = "Table Grid"
t.alignment = WD_TABLE_ALIGNMENT.CENTER
for r in range(4):
    for c in range(4):
        t.cell(r, c).text = f"R{r + 1}C{c + 1}"
a = t.cell(0, 0).merge(t.cell(0, 1))
a.text = "Объединено по горизонтали"
b = t.cell(1, 3).merge(t.cell(3, 3))
b.text = "Объединено по вертикали"
shade(t.cell(0, 2), "D9E2F3")
shade(t.cell(0, 3), "D9E2F3")

doc.add_paragraph()
doc.add_paragraph("Длинная таблица с повтором заголовка на каждой странице:")
long = doc.add_table(rows=1, cols=3)
long.style = "Table Grid"
hdr = long.rows[0]
for i, txt in enumerate(("№", "Наименование", "Сумма, ₽")):
    hdr.cells[i].text = txt
    shade(hdr.cells[i], "FFE699")
hdr._tr.get_or_add_trPr().append(parse_xml(f'<w:tblHeader {nsdecls("w")}/>'))
for i in range(1, 121):
    row = long.add_row().cells
    row[0].text = str(i)
    row[1].text = f"Позиция {i}"
    row[2].text = f"{i * 1250:,}".replace(",", " ")
doc.save(OUT / "02-tables.docx")

# 3. Изображения и ссылки, альбомная ориентация
doc = Document()
header_footer(doc, "Эталон 03 — изображения")
sec = doc.sections[0]
sec.orientation = WD_ORIENT.LANDSCAPE
sec.page_width, sec.page_height = sec.page_height, sec.page_width
doc.add_heading("Изображения в строке и гиперссылки", level=1)
for i, col in enumerate(("#2E75B6", "#548235", "#C55A11")):
    p = doc.add_paragraph(f"Рисунок {i + 1}: ")
    p.add_run().add_picture(png(col, f"Image {i + 1}"), width=Cm(8))
    doc.add_paragraph(LOREM * 2)
p = doc.add_paragraph("Ссылка: ")
add_hyperlink(p, "https://example.com/", "example.com")
p.add_run(" — открывается только по Ctrl+щелчку.")
doc.add_paragraph().add_run().add_break(WD_BREAK.PAGE)
doc.add_paragraph("Вторая страница после ручного разрыва.")
doc.save(OUT / "03-images-links.docx")

# 4. Неподдерживаемые в P0 конструкции: формула, исправления, элемент содержимого, пользовательский XML
doc = Document()
doc.add_heading("Неподдерживаемые конструкции", level=1)
p = doc.add_paragraph("Формула: ")
p._p.append(parse_xml(
    '<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">'
    '<m:r><m:t>E=mc</m:t></m:r><m:sSup><m:e><m:r><m:t>c</m:t></m:r></m:e><m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup>'
    '</m:oMath>'))
p = doc.add_paragraph("Исправление: ")
p._p.append(parse_xml(
    f'<w:ins {nsdecls("w")} w:id="1" w:author="Рецензент" w:date="2026-11-01T10:00:00Z">'
    '<w:r><w:t>вставленный текст</w:t></w:r></w:ins>'))
doc.element.body.insert(1, parse_xml(
    f'<w:sdt {nsdecls("w")}><w:sdtPr><w:alias w:val="Поле"/></w:sdtPr>'
    '<w:sdtContent><w:p><w:r><w:t>Элемент управления содержимым</w:t></w:r></w:p></w:sdtContent></w:sdt>'))
path4 = OUT / "04-unsupported.docx"
doc.save(path4)
# добавляем пользовательские свойства (docProps/custom.xml), как в корпоративных шаблонах;
# customXml уже есть в шаблоне python-docx
buf = io.BytesIO(path4.read_bytes())
with zipfile.ZipFile(buf) as zin, zipfile.ZipFile(path4, "w", zipfile.ZIP_DEFLATED) as zout:
    for item in zin.infolist():
        data = zin.read(item.filename)
        if item.filename == "[Content_Types].xml":
            data = data.replace(b"</Types>",
                b'<Override PartName="/docProps/custom.xml" '
                b'ContentType="application/vnd.openxmlformats-officedocument.custom-properties+xml"/></Types>')
        if item.filename == "_rels/.rels":
            data = data.replace(b"</Relationships>",
                b'<Relationship Id="rIdCustom" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" '
                b'Target="docProps/custom.xml"/></Relationships>')
        zout.writestr(item, data)
    zout.writestr("docProps/custom.xml",
        '<?xml version="1.0" encoding="UTF-8"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties" '
        'xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">'
        '<property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="Отдел"><vt:lpwstr>Продажи</vt:lpwstr></property></Properties>')

# 5. Повреждённые и опасные файлы
(OUT / "90-password-or-doc.docx").write_bytes(bytes([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]) + b"\0" * 504)
good = (OUT / "03-images-links.docx").read_bytes()
(OUT / "91-truncated.docx").write_bytes(good[: len(good) // 2])
with zipfile.ZipFile(OUT / "92-zip-slip.docx", "w") as z:
    z.writestr("[Content_Types].xml", '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>')
    z.writestr("../evil.txt", "x")
with zipfile.ZipFile(OUT / "93-not-docx.docx", "w") as z:
    z.writestr("hello.txt", "это обычный zip")

print("Готово:", ", ".join(sorted(p.name for p in OUT.glob("*.docx"))))
