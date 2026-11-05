"""Reference card renderer: builds a card face from cards.json + card-layout.json + art + strips.
Proves the template works and documents the algorithm the WPF CardControl should follow.
Usage: python render_card.py <bundle_data_dir> <out_dir> [card ids...]
Fonts here are stand-ins (squeezed DejaVu Condensed Bold, Carlito Italic); the app uses Haettenschweiler / Calibri."""
import json, re, sys, os
from PIL import Image, ImageDraw, ImageFont

DATA, OUT = sys.argv[1], sys.argv[2]
IDS = sys.argv[3:]
L = json.load(open(os.path.join(DATA, "card-layout.json")))
CARDS = {c["id"]: c for c in json.load(open(os.path.join(DATA, "cards.json")))["cards"]}
DISPLAY_FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf"
FLAVOR_FONT = "/usr/share/fonts/truetype/crosextra/Carlito-Italic.ttf"
SQUEEZE = 0.74          # mimic Haettenschweiler's narrow width
RACES = L["races"]
RACE_WORDS = {v["display"]: k for k, v in RACES.items() if k not in ("Warzone",)}

def hexrgb(h): return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))

class Squeezed:
    """Draw text horizontally squeezed, so a regular condensed font approximates a compressed display face."""
    def __init__(self, path, size, squeeze=SQUEEZE):
        self.f = ImageFont.truetype(path, size); self.sq = squeeze; self.size = size
    def width(self, s): return int(self.f.getlength(s) * self.sq)
    def draw(self, img, xy, s, fill):
        w = int(self.f.getlength(s)) + 4; asc, desc = self.f.getmetrics()
        tmp = Image.new("RGBA", (w, asc + desc), (0, 0, 0, 0))
        ImageDraw.Draw(tmp).text((0, 0), s, font=self.f, fill=fill)
        tmp = tmp.resize((max(1, int(w * self.sq)), asc + desc), Image.LANCZOS)
        img.alpha_composite(tmp, (int(xy[0]), int(xy[1])))

def tokens(text, own_color):
    """Split rules text into (word, color) runs: race names in race colours, numbers in the card's colour."""
    names = sorted(RACE_WORDS, key=len, reverse=True)
    pat = re.compile("(" + "|".join(re.escape(n) for n in names) + r"|[+\-]?\d+|[+\-]X\b|\bX\b)")
    runs = []
    for part in pat.split(text):
        if not part: continue
        if part in RACE_WORDS: col = hexrgb(RACES[RACE_WORDS[part]]["textColor"])
        elif re.fullmatch(r"[+\-]?\d+|[+\-]?X", part): col = own_color
        else: col = (0, 0, 0)
        for w in re.split(r"(\s+)", part):
            if w: runs.append((w, col))
    return runs

def layout_runs(runs, font, width):
    lines, cur, cw = [], [], 0
    for w, col in runs:
        ww = font.width(w)
        if w.isspace():
            if cur: cur.append((w, col)); cw += ww
            continue
        if cw + ww > width and cur:
            while cur and cur[-1][0].isspace(): cur.pop()
            lines.append(cur); cur, cw = [], 0
        cur.append((w, col)); cw += ww
    if cur: lines.append(cur)
    return lines

def rounded_mask(size, radii):
    w, h = size; m = Image.new("L", (w * 4, h * 4), 0); d = ImageDraw.Draw(m)
    d.rectangle((0, 0, w * 4, h * 4), fill=255)
    r = radii["topRight"] * 4
    if r:
        d.rectangle((w * 4 - r, 0, w * 4, r), fill=0)
        d.pieslice((w * 4 - 2 * r, 0, w * 4, 2 * r), 270, 360, fill=255)
    return m.resize(size, Image.LANCZOS)

def render(cid):
    c = CARDS[cid]
    race = c["races"][0] if c["races"] else "Neutral"
    own = hexrgb(RACES[race]["textColor"])
    W, H = L["canvas"]["width"], L["canvas"]["height"]
    img = Image.new("RGBA", (W, H), "white")
    # strip
    style = L["strip"]["warzoneStyle"] if c["kind"] == "Warzone" else L["strip"]["styleFromRace"][race]
    img.alpha_composite(Image.open(os.path.join(DATA, L["strip"]["image"].format(style=style))).convert("RGBA"), (0, 0))
    # art
    ax, ay, aw, ah = L["art"]["rect"]
    art = Image.open(os.path.join(DATA, L["art"]["image"].format(id=cid))).convert("RGBA")
    art.putalpha(rounded_mask(art.size, L["art"]["cornerRadii"]))
    img.alpha_composite(art, (ax, ay))
    # name: draw horizontally on a temp canvas, then rotate +90 (reads bottom->top)
    N = L["name"]; size = N["fontSize"]
    f = Squeezed(DISPLAY_FONT, size)
    while f.width(c["name"]) > N["maxLength"] and size > 40:
        size -= 2; f = Squeezed(DISPLAY_FONT, size)
    tw = f.width(c["name"]) + 20; th = int(size * 1.3)
    tmp = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    sx, sy = N["shadow"]["offsetX"], N["shadow"]["offsetY"]
    # shadow is down/right in card space; in the unrotated temp that is (+y -> card right ... ) so use (-sy, +sx) mapping
    f.draw(tmp, (6 + sy, 4 + sx), c["name"], hexrgb(N["shadow"]["color"]) + (255,))
    for dx in (-2, 0, 2):
        for dy in (-2, 0, 2):
            f.draw(tmp, (6 + dx, 4 + dy), c["name"], (0, 0, 0, 255))
    f.draw(tmp, (6, 4), c["name"], (255, 255, 255, 255))
    rot = tmp.rotate(90, expand=True)
    img.alpha_composite(rot, (N["centerX"] - rot.width // 2, N["centerY"] - rot.height // 2))
    # header
    Hd = L["header"]; hf = Squeezed(DISPLAY_FONT, Hd["fontSize"])
    grey = hexrgb(Hd["subtypeColor"])
    rdisp = RACES[race]["display"]
    if c["kind"] == "Unit":
        cls = "*Leader*" if c["unitClass"] == "Leader" else c["unitClass"]
        rows = [("Race:", [(rdisp, own)]), ("Rank:", [(str(c["baseRank"]), own), ("  (" + cls + ")", grey)])]
    elif c["kind"] == "Order":
        rows = [("Race:", [(rdisp, own)]), ("Type:", [("Order", own), ("  (" + c["orderType"] + ")", grey)])]
    else:
        rows = [("Type:", [("Warzone", (0, 0, 0))]), ("Race:", [(rdisp, own)])]
    for (label, parts), top in zip(rows, (Hd["line1Top"], Hd["line2Top"])):
        hf.draw(img, (Hd["labelX"], top - 10), label, (0, 0, 0, 255))
        x = Hd["valueX"]
        for s, col in parts:
            hf.draw(img, (x, top - 10), s, col + (255,)); x += hf.width(s)
    # ability text
    A = L["ability"]; F = L["flavor"]
    ff0 = ImageFont.truetype(FLAVOR_FONT, F["fontSize"])
    def flavor_lines(font):
        words, lines, cur = c["flavor"].split(), [], ""
        for w in words:
            t = (cur + " " + w).strip()
            if font.getlength(t) > F["width"] and cur: lines.append(cur); cur = w
            else: cur = t
        return lines + [cur] if cur else lines
    nflav = len(flavor_lines(ff0)) if c["flavor"] else 0
    size = A["fontSize"]
    while True:   # shrink ability text until text + gap + flavour fit above the bottom limit
        af = Squeezed(DISPLAY_FONT, size); lh = A["lineHeight"] * size / A["fontSize"]
        lines_ = layout_runs(tokens(c["text"], own), af, A["width"])
        need = A["top"] + len(lines_) * lh + (40 + nflav * F["lineHeight"] if nflav else 0)
        if need <= A["bottomLimit"] or size <= A["minFontSize"]: break
        size -= 2
    y = A["top"] - 8
    for line in lines_:
        x = A["x"]
        for w, col in line:
            af.draw(img, (x, y), w, col + (255,)); x += af.width(w)
        y += lh
    ability_bottom = y
    # flavour
    if c["flavor"]:
        ff = ff0; lines = flavor_lines(ff)
        top = max(ability_bottom + 40 if c["text"] else 780, 1010 - len(lines) * F["lineHeight"])
        d = ImageDraw.Draw(img)
        for i, ln in enumerate(lines):
            d.text((F["x"], top + i * F["lineHeight"]), ln, font=ff, fill=hexrgb(F["color"]))
    return img.convert("RGB")

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for cid in IDS or list(CARDS):
        render(cid).save(os.path.join(OUT, f"{cid}.png"))
