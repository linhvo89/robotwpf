from pathlib import Path
from docx import Document
from docx.shared import Inches
from docx.oxml.ns import qn

root = Path(r"E:\Nittan\WpfCompanyApp_net58")
docx_path = root / "output" / "documents" / "Huong_dan_van_hanh_KBOT.docx"
output_path = root / "output" / "documents" / "Huong_dan_van_hanh_KBOT_anh_trang_van_hanh.docx"
image_path = Path(r"C:\Users\LinhVo\OneDrive\Desktop\anh\TrangVanhanh.PNG")

doc = Document(docx_path)

# Hình đầu tiên là logo trên bìa; hình thứ hai là ảnh màn hình vận hành.
if len(doc.inline_shapes) < 2:
    raise RuntimeError("Không tìm thấy ảnh màn hình vận hành trong tài liệu.")

shape = doc.inline_shapes[1]
blip = shape._inline.graphic.graphicData.pic.blipFill.blip
relationship_id = blip.get(qn("r:embed"))
image_part = doc.part.related_parts[relationship_id]
image_part._blob = image_path.read_bytes()

# Tận dụng gần hết chiều rộng trang, giữ đúng tỷ lệ ảnh 1213 x 707.
shape.width = Inches(6.3)
shape.height = Inches(6.3 * 707 / 1213)

doc.core_properties.comments = "Cập nhật ảnh màn hình vận hành ngày 11/08/2026"
doc.save(output_path)
print(output_path)
