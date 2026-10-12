"""Recenter shipped prototype views without regenerating or replacing model data.

Keep a content-addressed backup before replacing each package. Do not touch
arbitrary user files, Git-managed model projections, or application source.
"""
import difflib, hashlib, json, os, pathlib, tempfile, zipfile
import prototype as p

def repair(path,backups):
    raw=path.read_bytes(); before=hashlib.sha256(raw).hexdigest()
    with zipfile.ZipFile(path) as archive:
        infos=archive.infolist(); parts={i.filename:archive.read(i.filename) for i in infos}
    manifest=json.loads(parts['manifest.json'])
    if manifest.get('modelTracking') or manifest.get('tracking'): raise ValueError('Tracked model requires its normal shared save path: '+str(path))
    changes=[]
    for name in ('Composition.json','TemplateComposition.json'):
        if name not in parts: continue
        model=json.loads(parts[name]); moved=p.center_generated_views(model)
        if not moved: continue
        if any(v.get('connectors') or v.get('complements') for view in model.get('views',[]) for v in view.get('visuals',[])):
            raise ValueError('Package has authored connector/complement geometry; preserve it for explicit reconciliation: '+str(path))
        parts[name]=p.encoded(model); changes.extend(moved)
        for entry in manifest['authoritativeParts']:
            if entry['partUri'].lstrip('/')==name: entry.update(sha256=hashlib.sha256(parts[name]).hexdigest(),bytes=len(parts[name]))
    if not changes: return dict(package=str(path.relative_to(p.ROOT)),status='unchanged')
    backup=backups/(path.stem+'-'+before[:16]+path.suffix); backup.parent.mkdir(parents=True,exist_ok=True)
    if not backup.exists(): backup.write_bytes(raw)
    parts['manifest.json']=p.encoded(manifest)
    handle,tmp=tempfile.mkstemp(prefix=path.stem+'-',suffix='.tmp',dir=path.parent); os.close(handle)
    try:
        with zipfile.ZipFile(tmp,'w') as archive:
            for info in infos: archive.writestr(info,parts[info.filename])
        with zipfile.ZipFile(tmp) as archive: assert archive.testzip() is None
        if hashlib.sha256(path.read_bytes()).hexdigest()!=before: raise ValueError('Package changed while preparing repair: '+str(path))
        os.replace(tmp,path)
    finally:
        if os.path.exists(tmp): os.unlink(tmp)
    return dict(package=str(path.relative_to(p.ROOT)),status='recentered',beforeSha256=before,afterSha256=hashlib.sha256(path.read_bytes()).hexdigest(),backup=str(backup),views=changes)

def proposed_navigation_fix():
    relative='ThinkComposer/ApplicationProduct/Widgets/WidgetNavTree.xaml.cs'; path=p.REPO/relative
    original=path.read_text(encoding='utf-8-sig')
    old='''                            this.CurrentView.Presenter.BringIntoView(
                                  TargetIdea.VisualRepresentators.First(vrep => vrep.DisplayingView == this.CurrentView).MainSymbol.BaseArea);'''
    new='''                            // Model-only elements legitimately have no symbol in this view.
                            var CurrentRepresentation = TargetIdea.VisualRepresentators
                                .FirstOrDefault(vrep => vrep.DisplayingView == this.CurrentView);
                            if (CurrentRepresentation != null && CurrentRepresentation.MainSymbol != null)
                                this.CurrentView.Presenter.BringIntoView(CurrentRepresentation.MainSymbol.BaseArea);'''
    if new in original: return # Approved patch is already applied; retain the review diff.
    if original.count(old)!=1: raise ValueError('Navigation code changed; review before preparing patch.')
    patch=''.join(difflib.unified_diff(original.splitlines(True),original.replace(old,new).splitlines(True),fromfile='a/'+relative,tofile='b/'+relative))
    target=p.ROOT/'fixes/model-only-navigation.patch'; target.parent.mkdir(exist_ok=True); target.write_text(patch,encoding='utf-8')

def main():
    paths=sorted((p.ROOT/'domains').glob('*.tdom'))+sorted((p.ROOT/'examples').glob('*.tcom'))+sorted((p.ROOT/'reference/mtconnect').glob('*.tcom'))
    results=[repair(path,p.REPO/'artifacts/sysml-desktop-repair/backups') for path in paths]
    proposed_navigation_fix()
    guarded='var CurrentRepresentation = TargetIdea.VisualRepresentators' in (p.REPO/'ThinkComposer/ApplicationProduct/Widgets/WidgetNavTree.xaml.cs').read_text(encoding='utf-8-sig')
    p.write_json(p.ROOT/'reports/desktop-view-repair.json',dict(formatVersion=1,results=results,applicationSourceModifiedByRepair=False,
        applicationNavigationGuard='present in source; use a rebuilt executable' if guarded else 'pending; coordinate repair alone does not fix navigation'))
    print(json.dumps([dict(package=r['package'],status=r['status']) for r in results],indent=2))

if __name__=='__main__': main()
