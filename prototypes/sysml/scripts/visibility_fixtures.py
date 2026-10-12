"""Reproduce the hidden-namesake regression and a small MTConnect overview fixture."""
import copy, hashlib, json, zipfile
import prototype as p

def main():
    with zipfile.ZipFile(p.ROOT/'examples/SysML_v1_Starter.tcom') as archive:
        domain=json.loads(archive.read('Domain.json'))
    composition=p.composition('HiddenNamesake',domain)
    p.add_node(composition,'Model','Model',composition['ideas'][0]['id'],key='hidden-namesake')
    visible=p.ROOT/'fixtures/hidden-namesake-visible.tcom'
    p.package(visible,domain,composition)
    with zipfile.ZipFile(visible) as archive:
        infos=archive.infolist(); parts={i.filename:archive.read(i.filename) for i in infos}
    blocked=copy.deepcopy(composition)
    del blocked['views'][0]['visuals'][0]['visual']
    parts['Composition.json']=p.encoded(blocked)
    manifest=json.loads(parts['manifest.json'])
    for part in manifest['authoritativeParts']:
        if part['partUri']=='/Composition.json':
            part.update(sha256=hashlib.sha256(parts['Composition.json']).hexdigest(),bytes=len(parts['Composition.json']))
    parts['manifest.json']=p.encoded(manifest)
    with zipfile.ZipFile(p.ROOT/'fixtures/blocked/hidden-namesake.tcom','w') as archive:
        for info in infos: archive.writestr(info,parts[info.filename])

    with zipfile.ZipFile(p.ROOT/'reference/mtconnect/MTConnect_Imported.tcom') as archive:
        domain=json.loads(archive.read('Domain.json')); composition=json.loads(archive.read('Composition.json'))
    composition['views']=[v for v in composition['views'] if v['id']==composition['composition']['rootViewId']]
    nodes={n['id']:n for n in composition['ideas']}
    selected={v['ideaId'] for v in composition['views'][0]['visuals']}
    for name in {nodes[i]['techName'] for i in selected}:
        duplicate=next((n for n in composition['ideas'] if n['techName']==name and n['id'] not in selected),None)
        if duplicate: selected.add(duplicate['id'])
    for identity in list(selected):
        parent=nodes[identity].get('containerId')
        while parent in nodes and parent not in selected:
            selected.add(parent); parent=nodes[parent].get('containerId')
    composition['ideas']=[n for n in composition['ideas'] if n['id'] in selected]
    composition['relationships']=[]
    # Rendering fixture only: omitted semantic references are intentionally not resolved.
    p.package(p.ROOT/'fixtures/mtconnect-overview-visibility.tcom',domain,composition)

if __name__=='__main__': main()
