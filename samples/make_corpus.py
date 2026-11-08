"""Корпус для приёмки AC-02: 30 обычных DOCX по категориям и 10 сложных (безопасное поведение).

    python samples/make_corpus.py samples/docx/corpus

Обычные: 6 текст и стили, 4 списки, 6 таблицы, 4 изображения, 4 колонтитулы и разметка, 6 смешанных.
Сложные: примечания, исправления, сноски, разделы, надписи, формулы, элементы управления, оглавление,
диаграмма, внедрённый HTML-фрагмент.
"""
import io
import random
import sys
import zipfile
from pathlib import Path

from docx import Document
from docx.enum.section import WD_ORIENT
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK, WD_COLOR_INDEX
from docx.oxml import OxmlElement, parse_xml
from docx.oxml.ns import nsdecls, qn
from docx.shared import Cm, Pt, RGBColor

OUT = Path(sys.argv[1] if len(sys.argv) > 1 else "samples/docx/corpus")
OUT.mkdir(parents=True, exist_ok=True)
random.seed(7)

RU = ("Редактор должен открывать реальные документы, сохранять оформление, таблицы и изображения, "
      "переносить текст между страницами и не терять данные при сохранении. ")
EN = "The quick brown fox jumps over the lazy dog while the editor keeps every paragraph intact. "


def png(w=320, h=200, color=(46, 117, 182)):
    from PIL import Image, ImageDraw
    img = Image.new("RGB", (w, h), color)
    d = ImageDraw.Draw(img)
    for i in range(0, w, 20):
        d.line([(i, 0), (w - i, h)], fill=(255, 255, 255), width=2)
    b = io.BytesIO()
    img.save(b, "PNG")
    b.seek(0)
    return b


def jpeg(w=400, h=260):
    from PIL import Image
    img = Image.new("RGB", (w, h))
    px = img.load()
    for x in range(w):
        for y in range(h):
            px[x, y] = (x * 255 // w, y * 255 // h, 128)
    b = io.BytesIO()
    img.save(b, "JPEG", quality=85)
    b.seek(0)
    return b


def add_field(paragraph, code, shown="1"):
    run = paragraph.add_run()
    for kind in ("begin", None, "separate"):
        if kind:
            f = OxmlElement("w:fldChar"); f.set(qn("w:fldCharType"), kind); run._r.append(f)
        else:
            t = OxmlElement("w:instrText"); t.set(qn("xml:space"), "preserve"); t.text = f" {code} "; run._r.append(t)
    paragraph.add_run(shown)
    end = paragraph.add_run()
    f = OxmlElement("w:fldChar"); f.set(qn("w:fldCharType"), "end"); end._r.append(f)


def hyperlink(paragraph, url, text):
    part = paragraph.part
    rid = part.relate_to(url, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", is_external=True)
    h = OxmlElement("w:hyperlink"); h.set(qn("r:id"), rid)
    r = OxmlElement("w:r"); rpr = OxmlElement("w:rPr"); st = OxmlElement("w:rStyle"); st.set(qn("w:val"), "Hyperlink"); rpr.append(st); r.append(rpr)
    t = OxmlElement("w:t"); t.text = text; r.append(t); h.append(r)
    paragraph._p.append(h)


def shade(cell, fill):
    cell._tc.get_or_add_tcPr().append(parse_xml(f'<w:shd {nsdecls("w")} w:val="clear" w:color="auto" w:fill="{fill}"/>'))


def footer_numbers(doc, text="Страница "):
    p = doc.sections[0].footer.paragraphs[0]
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.add_run(text)
    add_field(p, "PAGE")
    p.add_run(" из ")
    add_field(p, "NUMPAGES")


def save(doc, name):
    doc.save(OUT / name)


# ── текст и стили (6) ──────────────────────────────────────────────────────
d = Document()
for i in range(12):
    d.add_paragraph((RU if i % 2 == 0 else EN) * 2)
save(d, "t01-plain-ru-en.docx")

d = Document()
p = d.add_paragraph()
for i, (font, size, color) in enumerate([("Arial", 10, "000000"), ("Times New Roman", 14, "C00000"), ("Calibri", 11, "1F4E79"), ("Courier New", 9, "385723"), ("Georgia", 18, "7030A0")]):
    r = p.add_run(f"{font} {size} пт. ")
    r.font.name = font; r.font.size = Pt(size); r.font.color.rgb = RGBColor.from_string(color)
d.add_paragraph(RU * 3)
save(d, "t02-fonts-sizes-colors.docx")

d = Document()
d.add_heading("Название документа", 0)
for lvl in (1, 2, 3):
    d.add_heading(f"Заголовок уровня {lvl}", lvl)
    d.add_paragraph(RU * 2)
save(d, "t03-headings-styles.docx")

d = Document()
for i in range(40):
    p = d.add_paragraph(RU * 4)
    p.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    p.paragraph_format.first_line_indent = Cm(1.25)
save(d, "t04-justified-multipage.docx")

d = Document()
for i in range(8):
    p = d.add_paragraph(f"Пункт {i + 1}\tзначение\tитог")
    r = p.add_run(); r.add_break(); p.add_run("Продолжение после переноса строки.")
save(d, "t05-tabs-breaks.docx")

d = Document()
p = d.add_paragraph()
p.add_run("Жирный ").bold = True
p.add_run("курсив ").italic = True
p.add_run("подчёркнутый ").underline = True
r = p.add_run("зачёркнутый "); r.font.strike = True
r = p.add_run("маркер "); r.font.highlight_color = WD_COLOR_INDEX.YELLOW
r = p.add_run("x"); p.add_run("2").font.superscript = True
p.add_run(" H"); p.add_run("2").font.subscript = True; p.add_run("O")
save(d, "t06-character-formatting.docx")

# ── списки (4) ─────────────────────────────────────────────────────────────
d = Document()
for i in range(5):
    d.add_paragraph(f"Маркер {i + 1}", style="List Bullet")
    d.add_paragraph(f"Вложенный {i + 1}", style="List Bullet 2")
save(d, "l01-bullets-levels.docx")

d = Document()
for i in range(6):
    d.add_paragraph(f"Шаг {i + 1}", style="List Number")
    if i % 2 == 0:
        d.add_paragraph("Подшаг", style="List Number 2")
save(d, "l02-numbered-levels.docx")

d = Document()
d.add_paragraph("Первый список:")
for i in range(3): d.add_paragraph(f"Пункт {i + 1}", style="List Number")
d.add_paragraph("Обычный абзац между списками. " + RU)
for i in range(3): d.add_paragraph(f"Маркер {i + 1}", style="List Bullet")
save(d, "l03-mixed-lists.docx")

d = Document()
t = d.add_table(rows=2, cols=2); t.style = "Table Grid"
c = t.cell(0, 0); c.paragraphs[0].text = "Список в ячейке:"
for i in range(3): c.add_paragraph(f"Пункт {i + 1}", style="List Bullet")
t.cell(1, 1).text = "Обычная ячейка"
save(d, "l04-list-in-table.docx")

# ── таблицы (6) ────────────────────────────────────────────────────────────
d = Document()
t = d.add_table(rows=5, cols=4); t.style = "Table Grid"
for r in range(5):
    for c in range(4): t.cell(r, c).text = f"Р{r + 1}С{c + 1}"
save(d, "tb01-simple-grid.docx")

d = Document()
t = d.add_table(rows=4, cols=4); t.style = "Table Grid"
t.cell(0, 0).merge(t.cell(0, 2)).text = "Объединено по горизонтали"
for r in range(1, 4):
    for c in range(4): t.cell(r, c).text = f"{r}.{c}"
save(d, "tb02-horizontal-merge.docx")

d = Document()
t = d.add_table(rows=5, cols=3); t.style = "Table Grid"
t.cell(0, 2).merge(t.cell(3, 2)).text = "Объединено по вертикали"
for r in range(5):
    for c in range(2): t.cell(r, c).text = f"{r}.{c}"
save(d, "tb03-vertical-merge.docx")

d = Document()
d.add_paragraph("Длинная таблица с повтором строки заголовка:")
t = d.add_table(rows=80, cols=3); t.style = "Table Grid"
hdr = t.rows[0]
hdr._tr.get_or_add_trPr().append(parse_xml(f'<w:tblHeader {nsdecls("w")}/>'))
for c, h in enumerate(["№", "Наименование", "Сумма"]): hdr.cells[c].text = h; shade(hdr.cells[c], "FFE699")
for r in range(1, 80):
    t.cell(r, 0).text = str(r); t.cell(r, 1).text = f"Позиция {r}"; t.cell(r, 2).text = f"{r * 125:,}".replace(",", " ")
save(d, "tb04-header-repeat-multipage.docx")

d = Document()
t = d.add_table(rows=2, cols=2); t.style = "Table Grid"
inner = t.cell(0, 0).add_table(rows=2, cols=2)
inner.style = "Table Grid"
for r in range(2):
    for c in range(2): inner.cell(r, c).text = f"вн {r}.{c}"
t.cell(1, 1).text = "Внешняя ячейка"
save(d, "tb05-nested-table.docx")

d = Document()
t = d.add_table(rows=3, cols=3); t.style = "Table Grid"; t.alignment = WD_TABLE_ALIGNMENT.CENTER
for c, w in enumerate([Cm(2), Cm(8), Cm(4)]):
    for r in range(3): t.cell(r, c).width = w
for r in range(3):
    for c in range(3):
        t.cell(r, c).text = f"{r}.{c}"
        if (r + c) % 2 == 0: shade(t.cell(r, c), "DDEBF7")
save(d, "tb06-widths-shading.docx")

# ── изображения (4) ────────────────────────────────────────────────────────
d = Document(); d.add_paragraph("Одно изображение PNG:"); d.add_picture(png(), width=Cm(8)); save(d, "im01-png.docx")
d = Document(); d.add_paragraph("Изображение JPEG:"); d.add_picture(jpeg(), width=Cm(10)); save(d, "im02-jpeg.docx")
d = Document()
p = d.add_paragraph("Текст ")
p.add_run().add_picture(png(120, 80, (200, 80, 60)), width=Cm(2))
p.add_run(" между картинками ")
p.add_run().add_picture(png(120, 80, (60, 160, 80)), width=Cm(2))
p.add_run(" и дальше. " + RU)
save(d, "im03-inline-multiple.docx")
d = Document()
d.sections[0].header.paragraphs[0].add_run().add_picture(png(200, 40), width=Cm(4))
t = d.add_table(rows=1, cols=2); t.style = "Table Grid"
t.cell(0, 0).paragraphs[0].add_run().add_picture(png(160, 100), width=Cm(4))
t.cell(0, 1).text = "Подпись к рисунку"
save(d, "im04-in-table-and-header.docx")

# ── колонтитулы и разметка (4) ─────────────────────────────────────────────
d = Document()
d.sections[0].header.paragraphs[0].text = "Верхний колонтитул"
footer_numbers(d)
for i in range(30): d.add_paragraph(RU * 2)
save(d, "hf01-header-footer-numbers.docx")

d = Document()
s = d.sections[0]; s.different_first_page_header_footer = True
s.first_page_header.paragraphs[0].text = "Титульная страница"
s.header.paragraphs[0].text = "Обычный колонтитул"
for i in range(30): d.add_paragraph(RU * 2)
save(d, "hf02-different-first-page.docx")

d = Document()
s = d.sections[0]; s.orientation = WD_ORIENT.LANDSCAPE; s.page_width, s.page_height = Cm(27.94), Cm(21.59)
d.add_paragraph("Альбомная страница Letter. " + RU * 3)
save(d, "hf03-landscape-letter.docx")

d = Document()
s = d.sections[0]; s.left_margin, s.right_margin, s.top_margin, s.bottom_margin = Cm(3), Cm(1.5), Cm(2), Cm(2)
for i in range(3):
    d.add_paragraph(f"Страница {i + 1}. " + RU * 2)
    if i < 2: d.add_paragraph().add_run().add_break(WD_BREAK.PAGE)
save(d, "hf04-margins-page-breaks.docx")

# ── смешанные (6) ──────────────────────────────────────────────────────────
for k in range(6):
    d = Document()
    d.add_heading(f"Отчёт {k + 1}", 0)
    d.sections[0].header.paragraphs[0].text = f"Отчёт {k + 1}"
    footer_numbers(d)
    for sec in range(3 + k % 2):
        d.add_heading(f"Раздел {sec + 1}", 1)
        d.add_paragraph(RU * (2 + k % 3))
        for i in range(3): d.add_paragraph(f"Пункт {i + 1}", style="List Bullet" if k % 2 else "List Number")
        if sec % 2 == 0:
            t = d.add_table(rows=4, cols=3); t.style = "Table Grid"
            for r in range(4):
                for c in range(3): t.cell(r, c).text = f"{sec}.{r}.{c}"
            if k % 3 == 0: t.cell(1, 0).merge(t.cell(2, 0))
        if sec == 1: d.add_picture(png(240, 140), width=Cm(6))
        p = d.add_paragraph("Ссылка: "); hyperlink(p, "https://example.com/" + str(k), "пример")
    save(d, f"mx0{k + 1}-report.docx")

# ── сложные (10): должны открываться безопасно, без потерь при сохранении ──
d = Document(); d.add_paragraph(RU)
save(d, "cx01-comments.docx")
# примечание добавляется в пакет вручную
def patch(name, edit):
    src = OUT / name
    data = {}
    with zipfile.ZipFile(src) as z:
        for i in z.infolist(): data[i.filename] = z.read(i.filename)
    edit(data)
    with zipfile.ZipFile(src, "w", zipfile.ZIP_DEFLATED) as z:
        for k, v in data.items(): z.writestr(k, v)

W = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"'
def add_comment(data):
    doc = data["word/document.xml"].decode()
    doc = doc.replace("<w:p>", '<w:p><w:commentRangeStart w:id="0"/>', 1).replace("</w:p>", '<w:commentRangeEnd w:id="0"/><w:r><w:commentReference w:id="0"/></w:r></w:p>', 1)
    data["word/document.xml"] = doc.encode()
    data["word/comments.xml"] = f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:comments {W}><w:comment w:id="0" w:author="Тест" w:date="2026-01-01T00:00:00Z"><w:p><w:r><w:t>Примечание</w:t></w:r></w:p></w:comment></w:comments>'.encode()
    ct = data["[Content_Types].xml"].decode().replace("</Types>", '<Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/></Types>')
    data["[Content_Types].xml"] = ct.encode()
    rels = data["word/_rels/document.xml.rels"].decode().replace("</Relationships>", '<Relationship Id="rIdC1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="comments.xml"/></Relationships>')
    data["word/_rels/document.xml.rels"] = rels.encode()
patch("cx01-comments.docx", add_comment)

d = Document(); p = d.add_paragraph("Текст с исправлениями: ")
p._p.append(parse_xml(f'<w:ins {nsdecls("w")} w:id="1" w:author="Тест" w:date="2026-01-01T00:00:00Z"><w:r><w:t>вставлено</w:t></w:r></w:ins>'))
p._p.append(parse_xml(f'<w:del {nsdecls("w")} w:id="2" w:author="Тест" w:date="2026-01-01T00:00:00Z"><w:r><w:delText> удалено</w:delText></w:r></w:del>'))
save(d, "cx02-tracked-changes.docx")

d = Document(); d.add_paragraph("Абзац со сноской" + "."); save(d, "cx03-footnotes.docx")
def add_footnote(data):
    doc = data["word/document.xml"].decode().replace("</w:p>", '<w:r><w:rPr><w:vertAlign w:val="superscript"/></w:rPr><w:footnoteReference w:id="1"/></w:r></w:p>', 1)
    data["word/document.xml"] = doc.encode()
    data["word/footnotes.xml"] = f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:footnotes {W}><w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote><w:footnote w:id="1"><w:p><w:r><w:t>Текст сноски</w:t></w:r></w:p></w:footnote></w:footnotes>'.encode()
    data["[Content_Types].xml"] = data["[Content_Types].xml"].decode().replace("</Types>", '<Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/></Types>').encode()
    data["word/_rels/document.xml.rels"] = data["word/_rels/document.xml.rels"].decode().replace("</Relationships>", '<Relationship Id="rIdF1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/></Relationships>').encode()
patch("cx03-footnotes.docx", add_footnote)

d = Document(); d.add_paragraph("Раздел 1 (книжный)")
s = d.add_section(); s.orientation = WD_ORIENT.LANDSCAPE; s.page_width, s.page_height = d.sections[0].page_height, d.sections[0].page_width
d.add_paragraph("Раздел 2 (альбомный)")
save(d, "cx04-multiple-sections.docx")

d = Document(); p = d.add_paragraph("Надпись: ")
p._p.append(parse_xml(f'<w:r {nsdecls("w")}><w:pict><v:shape xmlns:v="urn:schemas-microsoft-com:vml" style="width:150pt;height:50pt"><v:textbox><w:txbxContent><w:p><w:r><w:t>Текст в надписи</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></w:r>'))
save(d, "cx05-textbox.docx")

d = Document(); p = d.add_paragraph("Формула: ")
p._p.append(parse_xml('<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><m:r><m:t>a²+b²=c²</m:t></m:r></m:oMath>'))
save(d, "cx06-equation.docx")

d = Document(); body = d.element.body
body.insert(0, parse_xml(f'<w:sdt {nsdecls("w")}><w:sdtPr><w:alias w:val="Поле"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Элемент управления содержимым</w:t></w:r></w:p></w:sdtContent></w:sdt>'))
save(d, "cx07-content-control.docx")

d = Document(); p = d.add_paragraph(); add_field(p, 'TOC \\o "1-3" \\h', "Оглавление (обновите поле)")
d.add_heading("Глава", 1); d.add_paragraph(RU)
save(d, "cx08-toc-field.docx")

d = Document(); d.add_paragraph("Документ с диаграммой (часть chart).")
save(d, "cx09-chart-part.docx")
def add_chart(data):
    data["word/charts/chart1.xml"] = b'<?xml version="1.0" encoding="UTF-8"?><c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"><c:chart/></c:chartSpace>'
    data["[Content_Types].xml"] = data["[Content_Types].xml"].decode().replace("</Types>", '<Override PartName="/word/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/></Types>').encode()
    data["word/_rels/document.xml.rels"] = data["word/_rels/document.xml.rels"].decode().replace("</Relationships>", '<Relationship Id="rIdCh1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="charts/chart1.xml"/></Relationships>').encode()
patch("cx09-chart-part.docx", add_chart)

d = Document(); d.add_paragraph("Документ с внедрённым HTML-фрагментом (altChunk).")
save(d, "cx10-altchunk.docx")
def add_alt(data):
    data["word/afchunk.htm"] = "<html><body><p>Внедрённый фрагмент</p></body></html>".encode()
    doc = data["word/document.xml"].decode().replace("<w:sectPr", '<w:altChunk xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="rIdA1"/><w:sectPr', 1)
    data["word/document.xml"] = doc.encode()
    data["[Content_Types].xml"] = data["[Content_Types].xml"].decode().replace("</Types>", '<Default Extension="htm" ContentType="application/xhtml+xml"/></Types>').encode()
    data["word/_rels/document.xml.rels"] = data["word/_rels/document.xml.rels"].decode().replace("</Relationships>", '<Relationship Id="rIdA1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/aFChunk" Target="afchunk.htm"/></Relationships>').encode()
patch("cx10-altchunk.docx", add_alt)

# ── замер AC-06: 20 страниц, около 5 МБ ────────────────────────────────────
from PIL import Image
d = Document()
footer_numbers(d)
for pg in range(20):
    d.add_heading(f"Страница {pg + 1}", 1)
    for _ in range(4): d.add_paragraph(RU * 3)
    if pg % 5 == 0:
        img = Image.frombytes("RGB", (700, 520), random.randbytes(700 * 520 * 3))
        b = io.BytesIO(); img.save(b, "PNG"); b.seek(0)
        d.add_picture(b, width=Cm(12))
    if pg < 19: d.add_paragraph().add_run().add_break(WD_BREAK.PAGE)
save(d, "perf-20p-5mb.docx")

print("Корпус:", len(list(OUT.glob("*.docx"))), "файлов в", OUT)
