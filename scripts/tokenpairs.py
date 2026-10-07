#!/usr/bin/env python3
"""
Prove a token refactor changed no value: the half of the evidence pixels cannot give.

    python3 scripts/tokenpairs.py <base-ref>

Pairs every changed line in src/Lightbox.App/**/*.axaml between <base-ref> and the
working tree. Each `{StaticResource Token}` is replaced, on BOTH sides, with the
value Styles/Tokens.axaml gives it, and the two lines must then be equal. Every
token on a changed line must also be of the type its property takes: a Thickness
for a margin or padding, a CornerRadius for a radius, a number for a size.

Why it exists (docs/DESIGN-tokens.md): the gallery's --snapshot-app shows each
view in its default state, so a token inside a selected-bone section, a populated
effect stack or a provider field is never on screen. A static resource resolves
to the same value whether or not its element is visible, so a line whose only
change is token-for-value, of the right type, cannot change a pixel in any state.

History, kept because each was a hole: the first version resolved only the new
side and flagged 98 clean docker lines; the second compared text only, so a
Thickness token on a width would have passed (adversary review, windows step).
"""
import re
import subprocess
import sys

TOKENS = 'src/Lightbox.App/Styles/Tokens.axaml'

# What each property takes.
EXPECTS = {
    'Margin': 'Thickness', 'Padding': 'Thickness', 'CornerRadius': 'CornerRadius',
    'Width': 'x:Double', 'Height': 'x:Double', 'MinWidth': 'x:Double', 'MinHeight': 'x:Double',
    'MaxWidth': 'x:Double', 'MaxHeight': 'x:Double', 'FontSize': 'x:Double', 'Spacing': 'x:Double',
}
TOKEN_REF = re.compile(r'\{StaticResource (\w+)\}')
ATTR_TOKEN = re.compile(r'(?<![\w.:])(\w+)="\{StaticResource (\w+)\}"')
SETTER_TOKEN = re.compile(r'Property="(\w+)" Value="\{StaticResource (\w+)\}"')


def read_tokens():
    text = open(TOKENS, encoding='utf-8').read()
    values, types = {}, {}
    for kind, key, value in re.findall(r'<([\w:]+) x:Key="(\w+)">([^<]+)<', text):
        values[key] = value
        types[key] = kind
    return values, types


def resolve(line, values):
    return TOKEN_REF.sub(lambda m: values.get(m.group(1), m.group(0)), line)


def type_errors(line, types):
    errors = []
    for prop, token in ATTR_TOKEN.findall(line) + SETTER_TOKEN.findall(line):
        want, got = EXPECTS.get(prop), types.get(token)
        if want and got and want != got:
            errors.append(f'{prop} takes {want}, but {token} is {got}')
    return errors


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    values, types = read_tokens()
    diff = subprocess.run(
        ['git', 'diff', '-U0', sys.argv[1], '--', 'src/Lightbox.App/*.axaml', 'src/Lightbox.App/**/*.axaml',
         f':(exclude){TOKENS}'],
        capture_output=True, text=True, encoding='utf-8', check=True).stdout

    pairs = bad = 0
    removed, added, file = [], [], None

    def flush():
        nonlocal pairs, bad
        if len(removed) != len(added):
            bad += 1
            print(f'{file}: {len(removed)} line(s) removed but {len(added)} added, not a pure swap')
        else:
            for old, new in zip(removed, added):
                pairs += 1
                if resolve(new, values).strip() != resolve(old, values).strip():
                    bad += 1
                    print(f'{file}:\n  - {old.strip()}\n  + {new.strip()}')
                for error in type_errors(new, types):
                    bad += 1
                    print(f'{file}: {error}\n  + {new.strip()}')
        removed.clear()
        added.clear()

    for line in diff.splitlines():
        if line.startswith('+++ b/'):
            file = line[6:]
        elif line.startswith('@@'):
            flush()
        elif line.startswith('-') and not line.startswith('---'):
            removed.append(line[1:])
        elif line.startswith('+') and not line.startswith('+++'):
            added.append(line[1:])
    flush()
    print(f'{pairs} changed line(s) paired; {bad} not a same-value, same-type token swap')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
