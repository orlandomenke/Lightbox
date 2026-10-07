"""Seed or re-measure .claude/quality/ratchets/literals.json (TokenRatchetTests' budgets)."""
import glob, json, os, re

props = 'FontSize|Spacing|Margin|Padding|CornerRadius|Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight'
num = r"""["']-?[0-9][0-9,.\-]*["']"""
q = r"""["']"""
# The same pattern as TokenRatchetTests.Literal: either quote, spaces round the
# equals, a Setter's Value before or after its Property.
lit = re.compile(
    r'(?<![\w.:])(?:' + props + r')\s*=\s*' + num
    + r'|Property\s*=\s*' + q + '(?:' + props + ')' + q + r'\s+Value\s*=\s*' + num
    + r'|Value\s*=\s*' + num + r'\s+Property\s*=\s*' + q + '(?:' + props + ')' + q)
TOKEN_FILE = 'src/Lightbox.App/Styles/Tokens.axaml'
out = {}
for f in sorted(glob.glob(os.path.join('src', 'Lightbox.App', '**', '*.axaml'), recursive=True)):
    f = f.replace(os.sep, '/')
    if '/bin/' in f or '/obj/' in f or f == TOKEN_FILE:
        continue
    n = len(lit.findall(open(f, encoding='utf-8').read()))
    if n:
        out[f] = n
with open(os.path.join('.claude', 'quality', 'ratchets', 'literals.json'), 'w', encoding='utf-8', newline='\n') as fh:
    json.dump(out, fh, indent=2, sort_keys=True)
    fh.write('\n')
print(len(out), 'files', sum(out.values()), 'literals')
print(sorted(out.items(), key=lambda x: -x[1])[:6])
