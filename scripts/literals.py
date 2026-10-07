"""Seed or re-measure .claude/quality/ratchets/literals.json (TokenRatchetTests' budgets)."""
import glob, json, os, re

props = 'FontSize|Spacing|Margin|Padding|CornerRadius|Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight'
lit = re.compile(r'(?<![\w.:])(' + props + r')="-?[0-9][0-9,.\-]*"|Property="(' + props + r')"\s+Value="-?[0-9][0-9,.\-]*"')
out = {}
for f in sorted(glob.glob(os.path.join('src', 'Lightbox.App', '**', '*.axaml'), recursive=True)):
    f = f.replace(os.sep, '/')
    if '/bin/' in f or '/obj/' in f or f.endswith('/Tokens.axaml'):
        continue
    n = len(lit.findall(open(f, encoding='utf-8').read()))
    if n:
        out[f] = n
with open(os.path.join('.claude', 'quality', 'ratchets', 'literals.json'), 'w', encoding='utf-8', newline='\n') as fh:
    json.dump(out, fh, indent=2, sort_keys=True)
    fh.write('\n')
print(len(out), 'files', sum(out.values()), 'literals')
print(sorted(out.items(), key=lambda x: -x[1])[:6])
