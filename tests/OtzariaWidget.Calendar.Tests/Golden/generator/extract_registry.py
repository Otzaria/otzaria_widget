import re, json, sys
src = open(r'C:\Users\user\Documents\otzaria_project\lib\tools\calendar\helpers\zmanim_helpers.dart', encoding='utf-8').read()
start = src.index('final List<ZmanDefinition> kZmanimRegistry = [')
body = src[start:]
parts = body.split('  ZmanDefinition(\n')[1:]
out = []
STR = r"'((?:[^'\\]|\\.)*)'"
def sfield(chunk, name):
    m = re.search(r'^\s{4}' + name + r': ' + STR + r',?\s*$', chunk, re.M)
    return m.group(1).replace("\\'", "'").replace('\\"', '"') if m else ''
for p in parts:
    cid = sfield(p, 'id')
    title = sfield(p, 'title'); sub = sfield(p, 'subtitle'); cat = sfield(p, 'category')
    pair = sfield(p, 'pairId'); pl = sfield(p, 'pairLabel')
    m = re.search(r"explanation:\s*'''(.*?)'''", p, re.S)
    if not m:
        m = re.search(r"explanation:\s*" + STR, p, re.S)
    expl = m.group(1) if m else ''
    first = expl.strip().split('\n')[0].strip()
    out.append(dict(id=cid, title=title, subtitle=sub, category=cat, description=first,
        defaultEnabled=bool(re.search(r'defaultEnabled:\s*true', p)),
        showHebrewDate=bool(re.search(r'showHebrewDate:\s*true', p)),
        pairId=pair or None, pairLabel=pl))
print(len(out), sum(1 for o in out if o['defaultEnabled']), file=sys.stderr)
bad = [o['id'] for o in out if not (o['id'] and o['title'] and o['category'] and o['description'])]
assert not bad, bad
json.dump(dict(source='otzaria lib/tools/calendar/helpers/zmanim_helpers.dart kZmanimRegistry', zmanim=out), open(sys.argv[1], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
