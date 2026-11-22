"""Contraste e hierarquia visual dos recursos usados pelas telas e controles do Pulso."""

from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


RES = Path(__file__).resolve().parents[1] / "res"


def palette(night):
    colors = {}
    directories = ["values", "values-night"] if night else ["values"]
    for directory in directories:
        for path in (RES / directory).glob("*.xml"):
            for color in ET.parse(path).getroot().findall("color"):
                colors[color.get("name")] = color.text.strip()
    return colors


def luminance(rgb):
    channels = [int(rgb[i:i+2], 16) / 255 for i in (1, 3, 5)]
    linear = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in channels]
    return sum(value * weight for value, weight in zip(linear, (0.2126, 0.7152, 0.0722)))


def contrast(first, second):
    low, high = sorted((luminance(first), luminance(second)))
    return (high + 0.05) / (low + 0.05)


class ThemeTest(unittest.TestCase):
    def test_light_and_dark_pages_have_distinct_backgrounds_and_cards(self):
        for night in (False, True):
            with self.subTest(night=night):
                colors = palette(night)
                for role in ("page_background", "card_surface"):
                    self.assertIn(role, colors)
                    light = luminance(colors[role])
                    if night:
                        self.assertLess(light, 0.07)
                    else:
                        self.assertGreater(light, 0.70)
                self.assertNotEqual(colors["page_background"], colors["card_surface"])

    def test_readability_on_each_control_and_background(self):
        foregrounds = {
            "page_background": ("text_primary", "text_muted", "accent"),
            "card_surface": ("text_primary", "text_muted", "accent", "error_text"),
            "accent_surface": ("accent",),
            "accent": ("on_accent",),
            "focus_surface": ("on_focus", "on_focus_secondary", "focus_action"),
            "focus_secondary_surface": ("on_focus",),
            "focus_action": ("on_focus_action",),
            "focus_choice": ("on_focus_choice",),
        }
        for night in (False, True):
            colors = palette(night)
            for background, roles in foregrounds.items():
                for role in roles:
                    with self.subTest(night=night, background=background, foreground=role):
                        self.assertIn(role, colors)
                        self.assertIn(background, colors)
                        self.assertGreaterEqual(contrast(colors[role], colors[background]), 4.5)


if __name__ == "__main__":
    unittest.main()
