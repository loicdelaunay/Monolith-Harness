"""Build the native inline icon font from the vendored MIT Material Icon Theme SVGs.
Requires fonttools==4.60.1. No download occurs during application builds.
"""
from pathlib import Path
import copy
import xml.etree.ElementTree as ET
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.transformPen import TransformPen
from fontTools.svgLib.path import SVGPath
from fontTools.colorLib.builder import buildCOLR, buildCPAL

root = Path(__file__).resolve().parent.parent
icons = root / "src/MonolithHarness.Core.Desktop/Assets/FileIcons"
output = root / "src/MonolithHarness.App/Assets/Fonts/MaterialFileIcons.ttf"
output.parent.mkdir(parents=True, exist_ok=True)
fb = FontBuilder(1000, isTTF=True)
glyphs = {}; order = [".notdef"]; cmap = {}; layers = {}; palette = []
glyphs[".notdef"] = TTGlyphPen(None).glyph()
for index, path in enumerate(sorted(icons.glob("*.svg"))):
    svg = ET.parse(path).getroot()
    x, y, width, height = map(float, svg.attrib["viewBox"].split())
    base = path.stem; order.append(base); cmap[0xE100 + index] = base
    glyphs[base] = TTGlyphPen(None).glyph(); layers[base] = []
    for number, element in enumerate(svg.findall("{http://www.w3.org/2000/svg}path")):
        # Respect the SVG root fill: invisible view-box paths must not become black glyph layers.
        fill = element.attrib.get("fill", svg.attrib.get("fill", "#000000"))
        if fill == "none": continue
        name = f"{base}.{number}"; order.append(name)
        pen = TTGlyphPen(None)
        transformed = TransformPen(Cu2QuPen(pen, max_err=0.5), (1000/width, 0, 0, -1000/height, -x*1000/width, 850+y*1000/height))
        layer = ET.Element("{http://www.w3.org/2000/svg}svg")
        layer.append(copy.deepcopy(element))
        SVGPath.fromstring(ET.tostring(layer)).draw(transformed)
        glyphs[name] = pen.glyph()
        color = fill
        if color.startswith("url("):
            # COLR v0 uses solid fills; retain the gradient's first stop for native text.
            color = svg.find(".//{http://www.w3.org/2000/svg}stop").attrib["stop-color"]
        color = color.lstrip("#")
        if len(color) == 3: color = "".join(c*2 for c in color)
        rgba = tuple(int(color[i:i+2],16)/255 for i in (0,2,4)) + (float(element.attrib.get("opacity",1)),)
        if rgba not in palette: palette.append(rgba)
        layers[base].append((name, palette.index(rgba)))
fb.setupGlyphOrder(order); fb.setupCharacterMap(cmap); fb.setupGlyf(glyphs)
fb.setupHorizontalMetrics({name:(1000,0) for name in order})
fb.setupHorizontalHeader(ascent=850, descent=-150)
fb.setupNameTable({"familyName":"Monolith Material Files", "styleName":"Regular", "uniqueFontIdentifier":"MonolithMaterialFiles-1", "fullName":"Monolith Material Files", "psName":"MonolithMaterialFiles"})
fb.setupOS2(sTypoAscender=850,sTypoDescender=-150,usWinAscent=850,usWinDescent=150)
fb.setupPost(); fb.setupMaxp()
fb.font["COLR"] = buildCOLR(layers,version=0); fb.font["CPAL"] = buildCPAL([palette])
fb.font.recalcTimestamp = False; fb.font["head"].created = fb.font["head"].modified = 3863980800
fb.save(output)
print(f"Generated {len(cmap)} icons: {output.name}")
