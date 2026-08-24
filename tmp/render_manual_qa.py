from pathlib import Path
import pypdfium2 as pdfium
from PIL import Image, ImageDraw

root = Path(r"E:\Nittan\WpfCompanyApp_net58")
pdf_path = root / "output" / "pdf" / "Huong_dan_van_hanh_KBOT_v2.1.2.pdf"
out = root / "tmp" / "manual_qa"
out.mkdir(parents=True, exist_ok=True)

pdf = pdfium.PdfDocument(str(pdf_path))
thumbs = []
for index in range(len(pdf)):
    page = pdf[index]
    image = page.render(scale=1.35).to_pil().convert("RGB")
    path = out / f"page-{index + 1}.png"
    image.save(path)
    thumb = image.copy()
    thumb.thumbnail((510, 660))
    canvas = Image.new("RGB", (540, 710), "white")
    canvas.paste(thumb, ((540 - thumb.width) // 2, 25))
    ImageDraw.Draw(canvas).text((18, 680), f"Trang {index + 1}", fill="black")
    thumbs.append(canvas)

for group_index in range(0, len(thumbs), 4):
    group = thumbs[group_index:group_index + 4]
    sheet = Image.new("RGB", (1080, 1420), "#d9d9d9")
    for j, thumb in enumerate(group):
        sheet.paste(thumb, ((j % 2) * 540, (j // 2) * 710))
    sheet.save(out / f"contact-{group_index // 4 + 1}.png")

print(f"pages={len(pdf)} contacts={(len(thumbs) + 3) // 4}")
