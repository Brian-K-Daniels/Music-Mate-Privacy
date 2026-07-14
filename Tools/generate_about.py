"""Generate About.docx from Resources/Raw/about.html.

The shipped About WebView loads Resources/Raw/about.html. That file is the
single source of truth — edit the HTML, then run this script to refresh
About.docx. Do not re-introduce an embedded HTML string here, or regenerating
docs can revive obsolete copy.
"""
from pathlib import Path
import html as html_module
import re
import zipfile
import xml.sax.saxutils as xml_escape

ROOT = Path(__file__).resolve().parents[1]
HTML_PATH = ROOT / "Resources" / "Raw" / "about.html"
DOCX_PATH = ROOT / "About.docx"
DOCX_FALLBACK = ROOT / "About_new.docx"


def strip_tags(html: str) -> str:
    return html_module.unescape(re.sub(r"<[^>]+>", "", html))


def html_to_docx_paragraphs(html: str) -> list[tuple[str, str]]:
    """Return list of (style, text) where style is Title|Heading1|Heading2|Heading3|List|Normal."""
    body = re.search(r"<body>(.*)</body>", html, re.S | re.I)
    if not body:
        return []
    content = body.group(1)
    parts = re.split(
        r"(<h1[^>]*>.*?</h1>|<h2[^>]*>.*?</h2>|<h3[^>]*>.*?</h3>|<p>.*?</p>|<ul>.*?</ul>|<ol>.*?</ol>)",
        content,
        flags=re.S | re.I,
    )
    out: list[tuple[str, str]] = []
    for part in parts:
        part = part.strip()
        if not part:
            continue
        if part.lower().startswith("<h1"):
            out.append(("Title", strip_tags(part)))
        elif part.lower().startswith("<h2"):
            out.append(("Heading1", strip_tags(part)))
        elif part.lower().startswith("<h3"):
            out.append(("Heading2", strip_tags(part)))
        elif part.lower().startswith("<p"):
            out.append(("Normal", strip_tags(part)))
        elif part.lower().startswith("<ul") or part.lower().startswith("<ol"):
            for li in re.findall(r"<li>(.*?)</li>", part, re.S | re.I):
                out.append(("List", strip_tags(li)))
    return out


def w_p(text: str, style: str | None = None) -> str:
    esc = xml_escape.escape(text)
    esc = re.sub(r"\*\*(.+?)\*\*", r"\1", esc)  # no markdown in source
    ppr = f'<w:pPr><w:pStyle w:val="{style}"/></w:pPr>' if style else ""
    return f'<w:p>{ppr}<w:r><w:t xml:space="preserve">{esc}</w:t></w:r></w:p>'


def build_document_xml(paragraphs: list[tuple[str, str]]) -> str:
    style_map = {
        "Title": "Title",
        "Heading1": "Heading1",
        "Heading2": "Heading2",
        "Normal": "Normal",
        "List": "ListParagraph",
    }
    ps = []
    for style, text in paragraphs:
        ps.append(w_p(text, style_map.get(style)))
    body = "".join(ps)
    return f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    {body}
    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr>
  </w:body>
</w:document>"""


def write_docx(path: Path, paragraphs: list[tuple[str, str]]) -> None:
    document_xml = build_document_xml(paragraphs)
    content_types = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>"""
    rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>"""
    doc_rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>"""
    styles = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
  <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:after="240"/></w:pPr><w:rPr><w:b/><w:sz w:val="36"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr><w:rPr><w:b/><w:sz w:val="28"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:before="200" w:after="80"/></w:pPr><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>
  <w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="720"/></w:pPr></w:style>
</w:styles>"""

    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", content_types)
        z.writestr("_rels/.rels", rels)
        z.writestr("word/_rels/document.xml.rels", doc_rels)
        z.writestr("word/document.xml", document_xml.encode("utf-8"))
        z.writestr("word/styles.xml", styles)


def main() -> None:
    if not HTML_PATH.is_file():
        raise SystemExit(f"Missing guide source: {HTML_PATH}")
    html = HTML_PATH.read_text(encoding="utf-8")
    print(f"Read {HTML_PATH} ({HTML_PATH.stat().st_size} bytes)")

    paragraphs = html_to_docx_paragraphs(html)
    try:
        write_docx(DOCX_PATH, paragraphs)
        print(f"Wrote {DOCX_PATH} ({DOCX_PATH.stat().st_size} bytes)")
    except PermissionError:
        write_docx(DOCX_FALLBACK, paragraphs)
        print(f"About.docx is locked — wrote {DOCX_FALLBACK} ({DOCX_FALLBACK.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
