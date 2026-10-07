#!/usr/bin/env python3
"""
Prove a token refactor changed no value: the half of the evidence pixels cannot give.

    python3 scripts/tokenpairs.py <base-ref>

Pairs every changed line in src/Lightbox.App/**/*.axaml between <base-ref> and the
working tree, replaces each `{StaticResource Token}` on the new side with the value
Styles/Tokens.axaml gives it, and requires the result to equal the old line exactly.
A line that differs in anything but a token-for-its-value swap is reported.

Why it exists (docs/DESIGN-tokens.md): the gallery's --snapshot-app shows each
docker and page in its default state, so a token inside a selected-bone section, a
populated effect stack or a provider field is never on screen. A static resource
resolves to the same number whether or not its element is visible, so a line whose
only change is token-for-value cannot change a pixel in any state.
"""
import re
import subprocess
import sys

TOKENS = 'src/Lightbox.App/Styles/Tokens.axaml'


def token_values():
    text = open(TOKENS, encoding='utf-8').read()
    return dict(re.findall(r'x:Key="(\w+)">([^<]+)<', text))


def resolve(line, values):
    return re.sub(r'\{StaticResource (\w+)\}', lambda m: values.get(m.group(1), m.group(0)), line)


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    base = sys.argv[1]
    values = token_values()
    diff = subprocess.run(
        ['git', 'diff', '-U0', base, '--', 'src/Lightbox.App/*.axaml', 'src/Lightbox.App/**/*.axaml',
         f':(exclude){TOKENS}'],
        capture_output=True, text=True, encoding='utf-8', check=True).stdout

    pairs = bad = 0
    removed, added, file = [], [], None

    def flush():
        nonlocal pairs, bad
        if len(removed) != len(added):
            bad += 1
            print(f'{file}: {len(removed)} line(s) removed but {len(added)} added — not a pure swap')
        else:
            for old, new in zip(removed, added):
                pairs += 1
                # Both sides, the same way: a line that keeps an older token
                # unchanged must compare equal to itself. The first version
                # resolved only the new side and flagged 98 clean docker lines.
                if resolve(new, values).strip() != resolve(old, values).strip():
                    bad += 1
                    print(f'{file}:\n  - {old.strip()}\n  + {new.strip()}')
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
    print(f'{pairs} changed line(s) paired; {bad} not a token-for-its-value swap')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
