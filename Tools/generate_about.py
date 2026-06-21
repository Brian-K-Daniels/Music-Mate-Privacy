"""Generate Resources/Raw/about.html and About.docx from current user guide content."""
from pathlib import Path
import html as html_module
import re
import zipfile
import xml.sax.saxutils as xml_escape

ROOT = Path(__file__).resolve().parents[1]
HTML_PATH = ROOT / "Resources" / "Raw" / "about.html"
DOCX_PATH = ROOT / "About.docx"
DOCX_FALLBACK = ROOT / "About_new.docx"

HTML = r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Music Mate — About and User Guide</title>
</head>
<body>

<h1>Music Mate</h1>
<p><em>About page and user guide</em></p>

<h2>Welcome</h2>
<p>Music Mate helps you learn to read music, play with better pitch accuracy, and develop rhythm and timing — using your real instrument and the microphone on your phone or tablet.</p>
<p>The app listens while you play and gives immediate feedback on the staff. As you practice, it tracks your progress and gradually increases the challenge through a level system designed for steady improvement.</p>
<p>Explore freely — you cannot break anything. Use <strong>Reset to defaults</strong> on the Settings page if you want to return to the starting settings.</p>

<h2>Quick Start</h2>
<ol>
<li>Open the menu (☰) and choose <strong>What to Play</strong>.</li>
<li>Pick your <strong>Instrument</strong> and <strong>Key</strong>.</li>
<li>Choose what to practice: a <strong>Scale</strong>, <strong>Arpeggio</strong>, <strong>Tune</strong>, <strong>Random</strong> pattern, or <strong>Tuner</strong>.</li>
<li>Go to <strong>Practice</strong> (the main staff page).</li>
<li>Tap the green <strong>GO</strong> button to start listening.</li>
<li>Play each note in order. Correct notes turn green on the staff.</li>
<li>When you finish, read your session result banner for your score and progress.</li>
</ol>
<p><strong>Tip:</strong> On long pages, drag to scroll. A quick flick moves faster through lists.</p>

<h2>Getting Around</h2>
<p>Tap the menu button (☰) at the top left to open these pages:</p>
<ul>
<li><strong>Practice</strong> — a simple start screen for choosing instrument and level (great for children).</li>
<li><strong>What to Play</strong> — choose instrument, key, and practice mode before you begin.</li>
<li><strong>Practice</strong> — the main practice screen with the musical staff, listening controls, and playback.</li>
<li><strong>Settings</strong> — tempo, note range, rhythm options, statistics collection, and mastery rules.</li>
<li><strong>Statistics</strong> — review note and session history.</li>
<li><strong>Advanced</strong> — level-up criteria, practice mix, and audio tuning (for teachers and experienced users).</li>
<li><strong>About</strong> — this guide, search, font size, and Premium purchase.</li>
</ul>
<p>The back arrow (←) on most pages returns you to <strong>Practice</strong>.</p>

<h2>What to Play</h2>
<p>Use this page to set up your session before practicing.</p>

<h3>Instrument and Key</h3>
<p>Choose the instrument you play. Music Mate remembers your choice. For transposing instruments (such as B♭ clarinet or E♭ alto sax), the staff shows music in the written key for your instrument. The concert key is shown in parentheses when helpful.</p>

<h3>Practice Modes</h3>
<p>Pick one mode from the four buttons:</p>
<ul>
<li><strong>Tunes</strong> — well-known pieces: <em>Mary Had a Little Lamb</em>, <em>Ode to Joy</em>, and <em>Minuet in G (simplified)</em>.</li>
<li><strong>Scales</strong> — major, minor, modes, pentatonic, blues, chromatic, and more.</li>
<li><strong>Arpeggios</strong> — chord patterns such as major triads and seventh chords. More arpeggios unlock as your level increases.</li>
<li><strong>Random / Tuner</strong> — <strong>Random</strong> generates a fresh exercise; <strong>Tuner</strong> shows the pitch of any note you play.</li>
</ul>

<h3>Repeat and Background Color</h3>
<p><strong>Repeat</strong> (or <strong>Repeat New</strong> / <strong>Repeat Same</strong> in Random mode) automatically starts a new session a few seconds after you finish.</p>
<p><strong>Background Color</strong> opens a color picker for the staff panel background. The same color is used on several pages.</p>

<h2>Practice</h2>
<p>This is where you play. The title bar shows <strong>Practice</strong> and a status message with your current mode and progress.</p>

<h3>The Staff</h3>
<p>Music Mate displays music on one or two treble staves with a standard treble clef, key signature, time signature, and tempo marking. Notes are shown with correct rhythm — quarter notes, eighth notes, rests, and more as your level increases.</p>
<p>Feedback colors on noteheads:</p>
<ul>
<li><strong>Bright green (lime)</strong> — the note you should play now.</li>
<li><strong>Green</strong> — played correctly.</li>
<li><strong>Red</strong> — incorrect attempt (still pending).</li>
<li><strong>Black or dim</strong> — upcoming notes.</li>
</ul>

<h3>Listening and Playback</h3>
<p>Tap the green <strong>GO</strong> circle in the title bar to start listening. While listening, it changes to a red <strong>Stop</strong> button.</p>
<p>Tap the green <strong>Play</strong> button on the staff to hear the exercise at your <strong>Playback BPM</strong> setting. During playback the button turns red <strong>Stop</strong>.</p>

<h3>Child Level Controls</h3>
<p>When using child levels, a row of buttons lets you adjust level by −10, −5, −1, +1, +5, or +10. The status bar shows progress toward the next level (sessions completed, pitch accuracy, timing, and overall score).</p>

<h3>Tuner Mode</h3>
<p>In Tuner mode the app listens to any note and displays:</p>
<ul>
<li>the nearest note name and octave</li>
<li>how sharp or flat you are (in cents)</li>
<li>the frequency heard and the nearest standard frequency</li>
</ul>

<h2>Practice (Child Start Screen)</h2>
<p>The green <strong>Practice</strong> page is a simplified entry point. Pick your instrument, adjust your level with − and +, read the short description of what that level teaches, then tap <strong>▶ Start</strong> to go to Practice with those settings applied.</p>

<h2>Settings</h2>
<p>Settings control how Music Mate behaves during practice.</p>

<h3>Range and Tempo</h3>
<ul>
<li><strong>Note Range</strong> — shown automatically from your instrument and level (you do not set low and high notes manually).</li>
<li><strong>Mastered</strong> — choose <strong>% Correct</strong> or <strong>Streak</strong> to decide when Random mode stops testing notes you already know well.</li>
<li><strong>Playback BPM</strong> — speed of the Play button audition (30–200).</li>
<li><strong>Music BPM</strong> — written tempo on the staff and rhythm evaluation (30–200).</li>
</ul>

<h3>Mastery and Random Mode</h3>
<ul>
<li><strong>Accidental %</strong> — how often Random mode adds sharps or flats (Premium required above 0).</li>
<li><strong>Omit ≥ % correct</strong> or <strong>Streak</strong> — threshold for leaving out mastered notes in Random mode.</li>
<li><strong>Min correct count</strong> — minimum correct plays before a note can be omitted (Streak mode).</li>
<li><strong>Omit below MsAvg</strong> — slow notes stay in the pool even if accuracy is high (% Correct mode).</li>
</ul>

<h3>Statistics and Rhythm</h3>
<ul>
<li><strong>Auto start on appear</strong> — begin listening when Practice opens.</li>
<li><strong>Collect Note Statistics</strong> / <strong>Collect Session Statistics</strong> — turn tracking on or off.</li>
<li><strong>Attempts kept per note</strong> and <strong>Max Session DB (MB)</strong> — control database size.</li>
<li><strong>Time signature</strong> — 4/4, 3/4, or 2/4.</li>
<li><strong>Smallest note</strong> — quarter, eighth, or sixteenth.</li>
<li><strong>Rhythm mode</strong> — Simple or Mixed.</li>
<li><strong>Syncopation</strong> — None, Simple, or Full.</li>
<li><strong>Show note names</strong> — Current only, All notes, or Off.</li>
</ul>
<p><strong>Reset to defaults</strong> restores Settings, Advanced, and related options.</p>

<h2>Statistics</h2>
<p>Review your practice history. Choose a database:</p>
<ul>
<li><strong>Note</strong> — per-note correct/wrong counts, % correct, average response time (MsAvg), and streak.</li>
<li><strong>Session</strong> — one row per completed session: date, level, accuracy scores, key, scale, instrument, note range, and more.</li>
<li><strong>Child Results</strong> — stored results used for automatic level-up (cleared with Clear All).</li>
</ul>
<p>Tap a column heading to sort. Use <strong>Clear All</strong> to erase the selected database.</p>

<h2>Advanced Settings</h2>
<p>These settings are mainly for parents, teachers, and experienced users.</p>

<h3>Practice Composition %</h3>
<p>Sliders for <strong>Tunes</strong>, <strong>Random</strong>, <strong>Scales</strong>, and <strong>Arpeggios</strong> (they always total 100%). These weights guide how child-level sessions mix practice types.</p>

<h3>Level Advancement Criteria</h3>
<p>Customize when a child advances to the next level:</p>
<ul>
<li><strong>Sessions Required to Advance</strong></li>
<li><strong>Minimum Pitch Accuracy (%)</strong></li>
<li><strong>Minimum Timing Consistency (%)</strong></li>
<li><strong>Minimum Overall Accuracy (%)</strong></li>
<li><strong>Minimum Notes Per Session</strong></li>
</ul>
<p>Sessions must be at the same level and on the same instrument. Very short sessions are excluded.</p>

<h3>Audio and Pitch</h3>
<ul>
<li><strong>Pitch Tolerance (cents)</strong> — how close your pitch must be to count as correct.</li>
<li><strong>RMS Threshold</strong> — helps ignore background noise.</li>
<li><strong>Cooldown (ms)</strong> — pause between accepting consecutive notes.</li>
<li><strong>Pitch Offset (cents)</strong> — fine-tune detection if your device reads slightly sharp or flat.</li>
</ul>

<h2>Instruments</h2>
<p>Music Mate supports concert-pitch and transposing instruments, including:</p>
<ul>
<li><strong>Concert Pitch (C)</strong>
<ul>
<li>Bassoon, Cello, Euphonium, Flute, Harpsichord, Marimba, Oboe, Organ, Piano, Recorder, Steel Pans, Timpani, Trombone, Tuba, Vibraphone, Viola, Violin, and Xylophone</li>
</ul>
</li>
<li>B♭ Clarinet, B♭ Trumpet, Soprano Saxophone, Tenor Saxophone</li>
<li>A Clarinet</li>
<li>F Horn</li>
<li>E♭ Alto Saxophone, E♭ Clarinet, Baritone Saxophone</li>
<li>Piccolo, Glockenspiel, Double Bass</li>
</ul>
<p>Each instrument has a practical note range used for automatic range selection at each level.</p>

<h2>Scales</h2>
<p>Scale mode shows the key signature and walks through the chosen scale on the staff — ascending and descending — so you practice reading, hearing, and playing in key. Available scales include Major, Harmonic Minor, Melodic Minor, Natural Minor, modes (Dorian, Phrygian, Lydian, Mixolydian, Locrian), pentatonic, blues, chromatic, and others.</p>

<h2>Arpeggios</h2>
<p>Arpeggio mode plays broken chord patterns (triads, seventh chords, and more). The arpeggio list grows as your child level increases. Choose an arpeggio rooted in the current key when possible.</p>

<h2>Random Tunes</h2>
<p>Random mode builds a fresh exercise from your current key, scale, note range, and accidental settings. It can focus on notes you still find difficult, using the mastery rules from Settings. Turn on <strong>Repeat New</strong> for a new pattern after each session, or <strong>Repeat Same</strong> to practice the same pattern again.</p>
<p><strong>Tip:</strong> Random mode works best when a scale is selected in What to Play.</p>

<h2>Tune Practice</h2>
<p>Choose a fixed piece under <strong>Tunes</strong> — <em>Mary Had a Little Lamb</em>, <em>Ode to Joy</em>, or <em>Minuet in G (simplified)</em>. The full tune is written on the staff with correct rhythms and bar lines.</p>

<h2>Playback and Tempo</h2>
<p><strong>Playback BPM</strong> (Settings) controls how fast the <strong>Play</strong> button auditions the exercise.</p>
<p><strong>Music BPM</strong> sets the tempo marking on the staff and is used for rhythm evaluation.</p>
<p>After a session, Music Mate may show a detected tempo based on how steadily you played.</p>

<h2>Levels</h2>
<p>Music Mate uses levels 1 through 100 for progressive skill development. Each band of ten levels introduces new ideas:</p>
<ul>
<li><strong>Beginner</strong> — more notes, C major, quarter notes</li>
<li><strong>New keys</strong> — more keys, longer note values</li>
<li><strong>Rests</strong> — rests and natural minor</li>
<li><strong>Minor and 16ths</strong> — minor scales and sixteenth notes</li>
<li><strong>Syncopation</strong> — simple off-beat rhythms</li>
<li><strong>Modes and blues</strong> — modes and blues sounds</li>
<li><strong>Full syncopation</strong> — more complex rhythms</li>
<li><strong>Mastery</strong> — wider range, speed, and chromatic notes</li>
</ul>
<p>At each level the app chooses an appropriate scale, key, note range, rhythm complexity, and exercise length. When your child meets the advancement criteria (see Advanced Settings), they move up automatically and see a celebration message. Levels never go down on their own, and each instrument tracks progress separately.</p>

<h2>Premium</h2>
<p>Music Mate has a free edition and a Premium edition.</p>

<h3>Free Edition</h3>
<ul>
<li>Keys: C, F, B♭, G, and D</li>
<li>Scales: Major and Harmonic Minor</li>
<li>Accidental % fixed at 0 in Random mode</li>
</ul>

<h3>Premium Edition</h3>
<p>Premium unlocks all keys and scales, accidental practice in Random mode, and other extended options. Premium is a one-time purchase that includes future app updates.</p>
<p>If you select a Premium-only key or scale while using the free edition, a message appears. An adult can choose to buy Premium or continue with free settings.</p>
<p>On the <strong>About</strong> page, tap <strong>Get Premium</strong> to purchase. When Premium is active you will see: <strong>⭐ You have all premium privileges</strong>.</p>

<h2>About This Page</h2>
<p>On the About page you can:</p>
<ul>
<li>read this guide</li>
<li>change <strong>Font</strong> size for easier reading</li>
<li>search the text with the 🔍 bar and tap <strong>Next</strong> to jump between matches</li>
<li>buy Premium or confirm your Premium status</li>
</ul>

<h2>Tips for Success</h2>
<ul>
<li>Start at a comfortable level and increase slowly.</li>
<li>Use <strong>Play</strong> to hear how an exercise should sound before you play it.</li>
<li>Watch for green noteheads — they confirm correct pitch.</li>
<li>Practice regularly; Statistics shows your improvement over time.</li>
<li>Keep background noise low for best pitch detection.</li>
</ul>

<h2>Things to Know</h2>
<p>Pitch detection works well in normal conditions, but no app is perfect. Very low or very high notes, wrong-octave attempts, or loud background noise can affect results. Music Mate is continually improving.</p>

<h2>Feedback and Support</h2>
<p>Questions, ideas, or problems? Email Brian_K_Daniels@yahoo.com.</p>
<p>When reporting a problem, please include your device, Android version, instrument, and what you were doing. If you are a child, ask a parent, teacher, or older student to help send the email.</p>

</body>
</html>
"""


def strip_tags(html: str) -> str:
    return html_module.unescape(re.sub(r"<[^>]+>", "", html))


def html_to_docx_paragraphs(html: str) -> list[tuple[str, str]]:
    """Return list of (style, text) where style is Title|Heading1|Heading2|Heading3|List|Normal."""
    body = re.search(r"<body>(.*)</body>", html, re.S | re.I)
    if not body:
        return []
    content = body.group(1)
    parts = re.split(r"(<h1[^>]*>.*?</h1>|<h2[^>]*>.*?</h2>|<h3[^>]*>.*?</h3>|<p>.*?</p>|<ul>.*?</ul>|<ol>.*?</ol>)", content, flags=re.S | re.I)
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
    return f"<w:p>{ppr}<w:r><w:t xml:space=\"preserve\">{esc}</w:t></w:r></w:p>"


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
    HTML_PATH.parent.mkdir(parents=True, exist_ok=True)
    HTML_PATH.write_text(HTML, encoding="utf-8")
    print(f"Wrote {HTML_PATH} ({HTML_PATH.stat().st_size} bytes)")

    paragraphs = html_to_docx_paragraphs(HTML)
    try:
        write_docx(DOCX_PATH, paragraphs)
        print(f"Wrote {DOCX_PATH} ({DOCX_PATH.stat().st_size} bytes)")
    except PermissionError:
        write_docx(DOCX_FALLBACK, paragraphs)
        print(f"About.docx is locked — wrote {DOCX_FALLBACK} ({DOCX_FALLBACK.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
