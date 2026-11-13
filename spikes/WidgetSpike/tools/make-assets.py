"""Generates placeholder PNG assets (needs Pillow). Usage: python tools/make-assets.py"""
import os
from PIL import Image, ImageDraw, ImageFont

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
assets = os.path.join(root, "Assets"); prov = os.path.join(root, "ProviderAssets")
os.makedirs(assets, exist_ok=True); os.makedirs(prov, exist_ok=True)
FONT = r"C:\Windows\Fonts\segoeui.ttf"
def font(sz):
    try: return ImageFont.truetype(FONT, sz)
    except Exception: return ImageFont.load_default()

BLUE = (27, 79, 138, 255)

def logo(size, path, text="O"):
    im = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, size[0]-1, size[1]-1], radius=min(size)//6, fill=BLUE)
    f = font(int(min(size)*0.6))
    bb = d.textbbox((0, 0), text, font=f)
    d.text(((size[0]-(bb[2]-bb[0]))/2 - bb[0], (size[1]-(bb[3]-bb[1]))/2 - bb[1]), text, font=f, fill="white")
    im.save(path)

logo((50, 50),   os.path.join(assets, "StoreLogo.png"))
logo((44, 44),   os.path.join(assets, "Square44x44Logo.png"))
logo((150, 150), os.path.join(assets, "Square150x150Logo.png"))
logo((310, 150), os.path.join(assets, "Wide310x150Logo.png"))
logo((88, 88),   os.path.join(assets, "Square44x44Logo.scale-200.png"))
logo((64, 64),   os.path.join(prov, "Icon.png"))

def screenshot(path, dark):
    # picker screenshot: 300x304, transparent rounded corners, medium-size widget look
    bg = (32, 32, 32, 255) if dark else (249, 249, 249, 255)
    fg = (255, 255, 255, 255) if dark else (20, 20, 20, 255)
    im = Image.new("RGBA", (300, 304), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, 299, 303], radius=12, fill=bg)
    d.text((24, 24), "Otzaria Widget Spike", font=font(20), fill=fg)
    d.text((24, 64), "םלוע םולש", font=font(28), fill=fg)   # visual-order Hebrew for demo
    d.text((24, 112), "Count: 0", font=font(16), fill=fg)
    d.rounded_rectangle([24, 240, 276, 276], radius=6, fill=BLUE)
    d.text((110, 246), "+1", font=font(20), fill="white")
    im.save(path)

screenshot(os.path.join(prov, "Screenshot.png"), False)
screenshot(os.path.join(prov, "Screenshot_dark.png"), True)
print("assets generated")
