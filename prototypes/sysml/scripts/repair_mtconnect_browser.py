"""Add selected, populated drill-down Views without changing imported model data."""
import copy, hashlib, json, os, tempfile, zipfile
import prototype as p

def main():
    path=p.ROOT/'reference/mtconnect/MTConnect_Imported.tcom'
    raw=path.read_bytes(); before=hashlib.sha256(raw).hexdigest()
    with zipfile.ZipFile(path) as archive:
        infos=archive.infolist(); parts={i.filename:archive.read(i.filename) for i in infos}
    original=json.loads(parts['Composition.json']); model=copy.deepcopy(original)
    added=p.add_mtconnect_browser_views(model)
    if not added: print('No missing browser views.'); return
    restored=copy.deepcopy(model); restored['views']=original['views']
    assert restored==original, 'Only View additions are authorized by this repair.'
    manifest=json.loads(parts['manifest.json'])
    if manifest.get('tracking') or manifest.get('modelTracking'): raise ValueError('Managed model: use its shared persistence path.')
    parts['Composition.json']=p.encoded(model)
    for entry in manifest['authoritativeParts']:
        if entry['partUri']=='/Composition.json':
            entry.update(sha256=hashlib.sha256(parts['Composition.json']).hexdigest(),bytes=len(parts['Composition.json']))
    parts['manifest.json']=p.encoded(manifest)
    backup=p.REPO/'artifacts/sysml-browser-repair/backups'/('MTConnect_Imported-'+before[:16]+'.tcom')
    backup.parent.mkdir(parents=True,exist_ok=True)
    if not backup.exists(): backup.write_bytes(raw)
    handle,tmp=tempfile.mkstemp(prefix='mtconnect-browser-',suffix='.tmp',dir=path.parent); os.close(handle)
    try:
        with zipfile.ZipFile(tmp,'w') as archive:
            for info in infos: archive.writestr(info,parts[info.filename])
        with zipfile.ZipFile(tmp) as archive: assert archive.testzip() is None
        if hashlib.sha256(path.read_bytes()).hexdigest()!=before: raise ValueError('File changed during repair.')
        os.replace(tmp,path)
    finally:
        if os.path.exists(tmp): os.unlink(tmp)
    result=dict(formatVersion=1,beforeSha256=before,afterSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                backup=str(backup),modelDataUnchanged=True,viewsAdded=added,
                scope='Five root entry points and their ownership paths; semantic members of four selected Blocks. Not source diagram reconstruction.')
    p.write_json(p.ROOT/'reports/mtconnect-browser-repair.json',result)
    print(json.dumps(result,indent=2))

if __name__=='__main__': main()
