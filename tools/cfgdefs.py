import re,sys,json
def load(path):
    src=open(path).read()
    defs={}
    # split at each add
    parts=re.split(r'(?=\bdef\s*=\s*this->add(?:_nullable)?\(\s*"[a-z0-9_]+"\s*,)',src)
    for p in parts:
        m=re.match(r'def\s*=\s*this->add(?:_nullable)?\(\s*"([a-z0-9_]+)"\s*,\s*(co\w+)',p)
        if not m: continue
        k,t=m.group(1),m.group(2)
        ev=re.findall(r'enum_values\.(?:push_back|emplace_back)\(\s*"([^"]*)"',p)
        # set_enum<...>({ {"a", ...}, ...}) style (PrusaSlicer 2.6+)
        if not ev:
            mm=re.search(r'set_enum<[^>]*>\s*\(\s*\{(.*?)\}\s*\)\s*;',p,re.S)
            if mm: ev=re.findall(r'\{\s*"([^"]*)"',mm.group(1))
            mm=re.search(r'set_enum_values\s*\(\s*\{(.*?)\}\s*\)',p,re.S) if not ev else None
            if mm: ev=re.findall(r'"([^"]*)"',mm.group(1))
        defs[k]=(t,ev)
    for m in re.finditer(r'this->add\("([a-z0-9_]+)"\s*\+\s*axis\.name',src):
        for a in 'xyze': defs.setdefault(m.group(1)+a,('coFloats',[]))
    return defs
if __name__=='__main__':
    d=load(sys.argv[1]); print(len(d)); 
    for k in sys.argv[2:]: print(k,d.get(k))
