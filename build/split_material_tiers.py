"""Split the glass material tokens into three named tiers.

Tier 1 Solid  -> Themes/MaterialSolid.xaml   (opaque, also the low-tier fallback)
Tier 2 Tinted -> the theme's own values      (default; hand-off wants "readable tinted glass")
Tier 3 Clear  -> Themes/MaterialClear.xaml   (the most translucent values, moved out of the theme)

The theme keeps the tinted values so the default look needs no extra dictionary; the two extra
tiers override the same keys through DynamicResource. Idempotent.
"""
from pathlib import Path
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
THEME = ROOT / 'source' / 'HowToFishTrainer' / 'themes' / 'theme.xaml'
CLEAR = ROOT / 'source' / 'HowToFishTrainer' / 'themes' / 'MaterialClear.xaml'

# token -> (clear value, tinted value). The theme currently holds the clear value; retune to tinted.
TOKENS = {
    'GlassCanvasBrush': ('#EFF5F8|#E8F0F4|#E1EBF0', '#EDF3F7|#E5EDF2|#DDE7ED'),
    'GlassChromeBrush': ('#EAF6FAFC|#D6EDF4F8', '#F2FAFCFD|#E6F2F7FA'),
    'GlassNavBrush': ('#E4F3F8FB|#CFEAF2F7', '#EEF6FAFC|#DEF0F6F9'),
    'GlassNavCapsuleBrush': ('#F2FFFFFF|#DCEAF2F6', '#F7FFFFFF|#E8F2F7FA'),
    'GlassEdgeHBrush': ('#E0FFFFFF|#66A0B8C4', '#D9FFFFFF|#59A0B8C4'),
    'GlassEdgeVBrush': ('#7FB0C5D0|#5C9DB4C0', '#6BADC2CD|#4D9DB4C0'),
    'GlassEdgeChromeBrush': ('#BFFFFFFF|#40A6BCC7', '#CCFFFFFF|#4DA6BCC7'),
    'GlassHaloAccentBrush': ('#4A82A6B8', '#3D7FA3B4'),
    'GlassHaloWarmBrush': ('#33C6B9A6', '#29C6B9A6'),
    'GlassCardBrush': ('#F7FFFFFF|#F0F5F9FB', '#F7FFFFFF|#F0F5F9FB'),
}

CLEAR_HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<!--
  ===========================================================================
  AbyssalCrab Studio \u00b7 Themes/MaterialClear.xaml\uff08\u901a\u900f\u73bb\u7483\u6863\uff09
  ===========================================================================
  \u00b7 \u4e09\u6863\u6750\u8d28\u5f3a\u5ea6\u4e4b\u4e00\uff1a\u5b9e\u8272( MaterialSolid ) / \u7740\u8272\u73bb\u7483( MaterialTinted\uff0c\u9ed8\u8ba4 ) / \u901a\u900f( \u672c\u6587\u4ef6 )
    \u4ea4\u63a5\u6587\u6863 \u00a77.3 \u8981\u6c42\u201c\u9ed8\u8ba4\u504f\u53ef\u8bfb\u7684\u7740\u8272\u73bb\u7483\u201d\uff0c\u6240\u4ee5\u9ed8\u8ba4\u4e0d\u662f\u672c\u6863\uff1b
    \u672c\u6863\u662f\u900f\u660e\u5ea6\u6700\u9ad8\u3001\u73bb\u7483\u611f\u6700\u5f3a\u7684\u4e00\u6863\uff0c\u7531\u7528\u6237\u5728\u9876\u680f\u300c\u6750\u8d28\u300d\u83dc\u5355\u91cc\u9009\u62e9\u3002
  \u00b7 \u8fd9\u4e9b\u503c\u539f\u672c\u5c31\u662f\u4e3b\u9898\u91cc\u7684\u53d6\u503c\uff0c\u73b0\u5728\u628a\u5b83\u4eec\u79fb\u5230\u72ec\u7acb\u6863\uff0c
    \u4e3b\u9898\u81ea\u8eab\u6539\u4e3a\u9ed8\u8ba4\u7684\u7740\u8272\u73bb\u7483\u53d6\u503c\u3002\u503c\u672c\u8eab\u672a\u6539\u53d8\u3002
  \u00b7 \u53ea\u8986\u76d6\u6750\u8d28 token\uff0c\u989c\u8272\u8bc6\u522b\u8272/\u5706\u89d2/\u5b57\u53f7/\u95f4\u8ddd/\u52a8\u6548 token \u4e00\u5f8b\u4e0d\u52a8\u3002
  ===========================================================================
-->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
'''


def build_clear_stops(spread: str) -> str:
    return '\n'.join(
        f'    <GradientStop Color="{part}" Offset="{offset}" />'
        for part, offset in zip(spread.split('|'), ('0', '1') if len(spread.split('|')) == 2 else ('0', '0.55', '1')))


def main() -> None:
    theme = THEME.read_text(encoding='utf-8')
    changed = 0

    for token, (clear, tinted) in TOKENS.items():
        import re
        pattern = re.compile(r'(x:Key="' + token + r'"[^>]*>)(.*?)(</)', re.S)

        def replace(match: re.Match) -> str:
            nonlocal changed
            body = match.group(2)
            parts = clear.split('|')
            if len(parts) == 1:
                # single-stop radial brush
                new_body = body.replace(clear, tinted.split('|')[0])
            else:
                new_body = body
                for old, new in zip(parts, tinted.split('|')):
                    new_body = new_body.replace(old, new)
            if new_body != body:
                changed += 1
            return match.group(1) + new_body + match.group(3)

        theme = pattern.sub(replace, theme, count=1)

    THEME.write_text(theme, encoding='utf-8', newline='\n')
    print(f'theme retuned to the tinted default: {changed} token block(s) changed')

    # Build the clear dictionary from the captured clear values.
    sections = []
    for token, (clear, _) in TOKENS.items():
        if token in ('GlassHaloAccentBrush', 'GlassHaloWarmBrush'):
            sections.append(
                f'  <RadialGradientBrush x:Key="{token}" GradientOrigin="0.5,0.5" Center="0.5,0.5" '
                f'RadiusX="0.5" RadiusY="0.5">\n'
                f'    <GradientStop Color="{clear}" Offset="0" />\n'
                f'    <GradientStop Color="#00FFFFFF" Offset="1" />\n'
                f'  </RadialGradientBrush>')
            continue
        start, end = ('0,0', '0.35,1') if token == 'GlassCanvasBrush' else (
            ('0,0', '1,0') if token == 'GlassEdgeVBrush' else ('0,0', '0,1'))
        parts = clear.split('|')
        offsets = ('0', '1') if len(parts) == 2 else ('0', '0.55', '1')
        stops = '\n'.join(f'    <GradientStop Color="{p}" Offset="{o}" />' for p, o in zip(parts, offsets))
        sections.append(f'  <LinearGradientBrush x:Key="{token}" StartPoint="{start}" EndPoint="{end}">\n{stops}\n  </LinearGradientBrush>')

    shadows = (
        '  <DropShadowEffect x:Key="GlassShadowSoft" BlurRadius="12" ShadowDepth="2" Direction="270"\n'
        '                    Color="#3D5A6B78" Opacity="0.075" RenderingBias="Performance" />\n'
        '  <DropShadowEffect x:Key="GlassShadowRaised" BlurRadius="24" ShadowDepth="3" Direction="270"\n'
        '                    Color="#3D5A6B78" Opacity="0.12" RenderingBias="Performance" />\n'
        '  <DropShadowEffect x:Key="GlassShadowChrome" BlurRadius="18" ShadowDepth="2" Direction="270"\n'
        '                    Color="#3D5A6B78" Opacity="0.07" RenderingBias="Performance" />'
    )

    CLEAR.write_text(CLEAR_HEADER + '\n' + '\n\n'.join(sections) + '\n\n' + shadows + '\n</ResourceDictionary>\n',
                     encoding='utf-8', newline='\n')
    print(f'wrote {CLEAR.name} with {len(sections)} brush tokens + 3 shadows')


if __name__ == '__main__':
    main()
