import sys,collections,io
sys.stdout=io.TextIOWrapper(sys.stdout.buffer,encoding='utf-8')
def load(p):
    d={}
    for l in open(p,encoding='utf-8'):
        l=l.rstrip('\n').split('|')
        if len(l)==4: d[tuple(l[:3])]=l[3]
    return d
a=load(sys.argv[1]); b=load(sys.argv[2])
def sec(t):
    h,m,s=map(int,t.split(':')); return h*3600+m*60+s
diffs=collections.Counter(); minute=collections.Counter(); big=[]
nullmis=[]
for k in a:
    va=a[k]; vb=b.get(k)
    if vb is None: print('missing',k); continue
    if va=='null' or vb=='null':
        if va!=vb: nullmis.append((k,va,vb))
        continue
    d=sec(vb)-sec(va)
    if d>43200: d-=86400
    if d<-43200: d+=86400
    diffs[(k[2],d)]+=1
    if va[:5]!=vb[:5]: minute[k[2]]+=1
    if abs(d)>2: big.append((k,va,vb,d))
byid=collections.defaultdict(collections.Counter)
for (i,d),n in diffs.items(): byid[i][d]+=n
print('total compared',sum(diffs.values()))
print('minute-display mismatches:',sum(minute.values()),dict(minute))
print('null mismatches',nullmis[:20],len(nullmis))
print('|diff|>2s:',len(big))
for x in big[:40]: print(x)
print('--- per id distribution of diffs (s)')
for i,c in byid.items(): print(i,dict(sorted(c.items())))
