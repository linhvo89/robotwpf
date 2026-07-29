from pathlib import Path
from docx import Document
from docx.table import Table as DocxTable
from docx.text.paragraph import Paragraph
from docx.oxml.ns import qn
from reportlab.lib import colors
from reportlab.lib.colors import HexColor
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import LETTER
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate, PageTemplate, Frame, Paragraph as RLParagraph, Spacer,
    Table as RLTable, TableStyle, PageBreak, Image, KeepTogether
)

ROOT = Path(r"E:\Nittan\WpfCompanyApp_net58")
SRC = ROOT / "output" / "documents" / "Huong_dan_van_hanh_KBOT.docx"
OUT = ROOT / "output" / "pdf" / "Huong_dan_van_hanh_KBOT.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

pdfmetrics.registerFont(TTFont("Arial", r"C:\Windows\Fonts\arial.ttf"))
pdfmetrics.registerFont(TTFont("Arial-Bold", r"C:\Windows\Fonts\arialbd.ttf"))
pdfmetrics.registerFont(TTFont("Arial-Italic", r"C:\Windows\Fonts\ariali.ttf"))

BLUE = HexColor("#164A8A")
DARK = HexColor("#17324D")
LIGHT = HexColor("#E8EEF5")
PALE = HexColor("#F4F6F9")
GRAY = HexColor("#5B6570")

styles = getSampleStyleSheet()
body = ParagraphStyle("BodyKBOT", fontName="Arial", fontSize=9.4, leading=12,
                      textColor=colors.black, spaceAfter=5)
title = ParagraphStyle("TitleKBOT", fontName="Arial-Bold", fontSize=23, leading=28,
                       textColor=DARK, alignment=TA_CENTER, spaceAfter=8)
subtitle = ParagraphStyle("SubtitleKBOT", fontName="Arial", fontSize=12, leading=15,
                          textColor=GRAY, alignment=TA_CENTER, spaceAfter=9)
h1 = ParagraphStyle("H1KBOT", fontName="Arial-Bold", fontSize=14.5, leading=18,
                    textColor=BLUE, spaceBefore=12, spaceAfter=7, keepWithNext=True)
h2 = ParagraphStyle("H2KBOT", fontName="Arial-Bold", fontSize=11.5, leading=14,
                    textColor=BLUE, spaceBefore=9, spaceAfter=5, keepWithNext=True)
h3 = ParagraphStyle("H3KBOT", fontName="Arial-Bold", fontSize=10.3, leading=13,
                    textColor=DARK, spaceBefore=7, spaceAfter=4, keepWithNext=True)
bullet = ParagraphStyle("BulletKBOT", parent=body, leftIndent=24, firstLineIndent=-12,
                        bulletIndent=10, spaceAfter=3)
cell = ParagraphStyle("CellKBOT", fontName="Arial", fontSize=8.1, leading=10,
                      spaceAfter=0)
cell_head = ParagraphStyle("CellHeadKBOT", fontName="Arial-Bold", fontSize=8.2,
                           leading=10, textColor=DARK, alignment=TA_CENTER, spaceAfter=0)
caption = ParagraphStyle("CaptionKBOT", fontName="Arial-Italic", fontSize=8.2,
                         leading=10, alignment=TA_CENTER, textColor=GRAY, spaceAfter=5)

def esc(text):
    return (text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            .replace("\n", "<br/>"))

def iter_blocks(parent):
    parent_elm = parent.element.body
    for child in parent_elm.iterchildren():
        if child.tag == qn("w:p"):
            yield Paragraph(child, parent)
        elif child.tag == qn("w:tbl"):
            yield DocxTable(child, parent)

def para_text_with_format(p):
    chunks = []
    for run in p.runs:
        text = esc(run.text)
        if not text:
            continue
        if run.bold:
            text = f"<b>{text}</b>"
        if run.italic:
            text = f"<i>{text}</i>"
        chunks.append(text)
    return "".join(chunks) or esc(p.text)

def has_page_break(p):
    return bool(p._p.xpath('.//w:br[@w:type="page"]'))

def image_from_paragraph(p, doc):
    blips = p._p.xpath(".//a:blip")
    if not blips:
        return None
    rid = blips[0].get(qn("r:embed"))
    if not rid or rid not in doc.part.related_parts:
        return None
    part = doc.part.related_parts[rid]
    ext = part.content_type.split("/")[-1].replace("jpeg", "jpg")
    tmp = ROOT / "tmp" / f"pdf_embedded_{rid}.{ext}"
    tmp.write_bytes(part.blob)
    img = Image(str(tmp))
    iw, ih = img.imageWidth, img.imageHeight
    max_w, max_h = 4.6 * inch, 2.65 * inch
    scale = min(max_w / iw, max_h / ih)
    img.drawWidth = iw * scale
    img.drawHeight = ih * scale
    img.hAlign = "CENTER"
    return img

def table_widths(tbl, total=6.5*inch):
    vals = []
    for c in tbl.rows[0].cells:
        tcW = c._tc.get_or_add_tcPr().find(qn("w:tcW"))
        try:
            vals.append(int(tcW.get(qn("w:w"))))
        except Exception:
            vals.append(1)
    s = sum(vals) or len(vals)
    return [total * v / s for v in vals]

def header_footer(canvas, document):
    canvas.saveState()
    page = canvas.getPageNumber()
    canvas.setFont("Arial-Bold", 7.5)
    canvas.setFillColor(GRAY)
    canvas.drawRightString(7.65*inch, 10.62*inch, "KBOT  |  HƯỚNG DẪN VẬN HÀNH")
    canvas.setFont("Arial", 7.2)
    canvas.drawCentredString(4.25*inch, 0.34*inch,
                            f"NITTAN VIETNAM  •  Tài liệu nội bộ  |  Trang {page}")
    canvas.restoreState()

docx = Document(SRC)
story = []
for block in iter_blocks(docx):
    if isinstance(block, Paragraph):
        if has_page_break(block):
            story.append(PageBreak())
            continue
        img = image_from_paragraph(block, docx)
        if img:
            story.extend([img, Spacer(1, 5)])
            continue
        text = block.text.strip()
        if not text:
            story.append(Spacer(1, 4))
            continue
        style_name = block.style.name if block.style else ""
        if style_name == "Title":
            sty = title
        elif style_name == "Subtitle":
            sty = subtitle
        elif style_name == "Heading 1":
            sty = h1
        elif style_name == "Heading 2":
            sty = h2
        elif style_name == "Heading 3":
            sty = h3
        elif style_name.startswith("List Bullet"):
            story.append(RLParagraph("•", bullet, bulletText=""))
            story[-1] = RLParagraph(esc(text), bullet, bulletText="•")
            continue
        elif text.startswith("Hình "):
            sty = caption
        else:
            sty = body
        story.append(RLParagraph(para_text_with_format(block), sty))
    else:
        data = []
        for r_idx, row in enumerate(block.rows):
            vals = []
            for c in row.cells:
                txt = "<br/>".join(esc(p.text) for p in c.paragraphs if p.text.strip())
                vals.append(RLParagraph(txt or " ", cell_head if r_idx == 0 else cell))
            data.append(vals)
        widths = table_widths(block)
        t = RLTable(data, colWidths=widths, repeatRows=1, hAlign="CENTER")
        t.setStyle(TableStyle([
            ("BACKGROUND", (0,0), (-1,0), LIGHT),
            ("TEXTCOLOR", (0,0), (-1,0), DARK),
            ("FONTNAME", (0,0), (-1,0), "Arial-Bold"),
            ("FONTNAME", (0,1), (-1,-1), "Arial"),
            ("GRID", (0,0), (-1,-1), 0.45, HexColor("#AAB4BE")),
            ("VALIGN", (0,0), (-1,-1), "MIDDLE"),
            ("LEFTPADDING", (0,0), (-1,-1), 6),
            ("RIGHTPADDING", (0,0), (-1,-1), 6),
            ("TOPPADDING", (0,0), (-1,-1), 5),
            ("BOTTOMPADDING", (0,0), (-1,-1), 5),
        ]))
        story.extend([t, Spacer(1, 5)])

frame = Frame(0.82*inch, 0.62*inch, 6.86*inch, 9.76*inch,
              leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
template = PageTemplate(id="KBOT", frames=[frame], onPage=header_footer)
pdf = BaseDocTemplate(str(OUT), pagesize=LETTER, leftMargin=0.82*inch,
                      rightMargin=0.82*inch, topMargin=0.62*inch, bottomMargin=0.62*inch,
                      title="Hướng dẫn vận hành hệ thống KBOT",
                      author="Nittan Vietnam")
pdf.addPageTemplates([template])
pdf.build(story)
print(OUT)
