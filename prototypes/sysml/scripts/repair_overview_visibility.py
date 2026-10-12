"""Repair only missing per-view visibility controls, retaining all source model data."""
import hashlib, json, os, tempfile, zipfile
import prototype as p

def repair(path):
    raw=path.read_bytes(); before=hashlib.sha256(raw).hexdigest()
    with zipfile.ZipFile(path) as archive:
        infos=archive.infolist(); parts={i.filename:archive.read(i.filename) for i in infos}
    manifest=json.loads(parts['manifest.json'])
    if manifest.get('modelTracking') or manifest.get('tracking'):
        raise ValueError('Use the managed persistence path for registered packages: '+str(path))
    changes=[]
    for name in ('Composition.json','TemplateComposition.json'):
        if name not in parts: continue
        model=json.loads(parts[name]); controls=p.make_overview_visibility_explicit(model)
        if not controls: continue
        # Only the listed visual-control additions are permitted; prove reversibility.
        repaired=p.encoded(model)
        for view in model['views']:
            for visual in view.get('visuals',[]):
                if dict(viewId=view['id'],ideaId=visual['ideaId']) in controls: del visual['visual']
        assert model==json.loads(parts[name]), 'Unexpected model data change'
        parts[name]=repaired; changes.extend(controls)
        for entry in manifest['authoritativeParts']:
            if entry['partUri'].lstrip('/')==name:
                entry.update(sha256=hashlib.sha256(repaired).hexdigest(),bytes=len(repaired))
    if not changes: return dict(package=str(path.relative_to(p.ROOT)),status='unchanged')
    backup=p.REPO/'artifacts/sysml-visibility-repair/backups'/(path.stem+'-'+before[:16]+path.suffix)
    backup.parent.mkdir(parents=True,exist_ok=True)
    if not backup.exists(): backup.write_bytes(raw)
    parts['manifest.json']=p.encoded(manifest)
    handle,tmp=tempfile.mkstemp(prefix=path.stem+'-',suffix='.tmp',dir=path.parent); os.close(handle)
    try:
        with zipfile.ZipFile(tmp,'w') as archive:
            for info in infos: archive.writestr(info,parts[info.filename])
        with zipfile.ZipFile(tmp) as archive: assert archive.testzip() is None
        if hashlib.sha256(path.read_bytes()).hexdigest()!=before: raise ValueError('Concurrent file edit: '+str(path))
        os.replace(tmp,path)
    finally:
        if os.path.exists(tmp): os.unlink(tmp)
    return dict(package=str(path.relative_to(p.ROOT)),status='repaired',beforeSha256=before,
                afterSha256=hashlib.sha256(path.read_bytes()).hexdigest(),backup=str(backup),controls=changes,
                modelDataUnchanged=True)

def main():
    paths=sorted((p.ROOT/'domains').glob('*.tdom'))+sorted((p.ROOT/'examples').glob('*.tcom'))+sorted((p.ROOT/'reference/mtconnect').glob('*.tcom'))
    results=[repair(path) for path in paths]
    # A no-op rerun must not replace the original recovery/evidence record.
    if any(r['status']=='repaired' for r in results):
        p.write_json(p.ROOT/'reports/overview-visibility-repair.json',dict(formatVersion=1,results=results))
    print(json.dumps([dict(package=r['package'],status=r['status']) for r in results],indent=2))

if __name__=='__main__': main()
