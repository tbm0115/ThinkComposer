"""Independent verification of the outputs from test_large_model.ps1 (64-bit Python)."""
import gc, hashlib, json, zipfile
import prototype as p
import validate as v

def main():
    original=p.ROOT/'reference/mtconnect/MTConnect_Imported.tcom'
    with zipfile.ZipFile(original) as archive: source=json.loads(archive.read('Composition.json'))
    issues=[]; previous=None; results=[]
    for filename in ('MTConnect_Saved.tcom','MTConnect_Resaved.tcom'):
        path=p.REPO/'artifacts/sysml-large-model-build'/filename
        with zipfile.ZipFile(path) as archive:
            saved=json.loads(archive.read('Composition.json'))
            manifest=json.loads(archive.read('manifest.json'))
            for part in manifest['authoritativeParts']:
                digest=hashlib.sha256()
                with archive.open(part['partUri'].lstrip('/')) as stream:
                    while chunk:=stream.read(1024*1024): digest.update(chunk)
                assert digest.hexdigest()==part['sha256']
        issues.extend(v.compare_saved(source,saved))
        assert len(saved['ideas'])==len(source['ideas']) and len(saved['relationships'])==len(source['relationships'])
        views={view['id']:view for view in saved['views']}
        for view in source['views']:
            actual=views[view['id']]; assert actual['ownerIdeaId']==view['ownerIdeaId']
            visuals={visual['ideaId']:visual for visual in actual['visuals']}
            for visual in view['visuals']:
                for key in ('x','y','width','height'): assert abs(visuals[visual['ideaId']][key]-visual[key])<0.01
        signature={node['id']:hashlib.sha256(json.dumps(node,sort_keys=True,ensure_ascii=False).encode()).hexdigest()
                   for node in saved['ideas']+saved['relationships']}
        if previous is not None: assert signature==previous, 'Native entity state changed on resave.'
        previous=signature
        results.append(dict(file=filename,concepts=len(saved['ideas']),relationships=len(saved['relationships']),
                            views=len(saved['views']),visuals=sum(len(view['visuals']) for view in saved['views']),manifestHashesVerified=True))
        del saved; gc.collect()
    assert not issues, issues[:8]
    p.write_json(p.ROOT/'reports/full-model-save-preservation.json',dict(formatVersion=1,status='passed',
        inputSha256=hashlib.sha256(original.read_bytes()).hexdigest(),results=results,issues=issues,
        checks=['Stable identities, owners, relationship endpoints and ordered Details preserved',
                'All View identities, owners, symbols and geometry preserved',
                'All model entity snapshots identical across native save/reopen/resave']))
    print('PASS: full-model save/reopen/resave preserves model and View data.')

if __name__=='__main__': main()
