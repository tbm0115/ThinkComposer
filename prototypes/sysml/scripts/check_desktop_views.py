"""Check startup coordinates and data preservation, independently of fitted image export."""
import copy, json, zipfile
import prototype as p

def without_coordinates(model):
    model=copy.deepcopy(model)
    for view in model.get('views',[]):
        for visual in view.get('visuals',[]): visual.pop('x',None); visual.pop('y',None)
    return model

def intersects_startup(visual):
    # Conservative 800x500 viewport centered as View.LastScrollOffset initializes it.
    left,top=1440*11/2-400,1070*11/2-250
    x,y,w,h=(visual.get(k,0) for k in ('x','y','width','height'))
    return x+w>left and x<left+800 and y+h>top and y<top+500

def main():
    report=p.read_json(p.ROOT/'reports/desktop-view-repair.json'); checks=[]
    for entry in report['results']:
        if entry['status']!='recentered': continue
        with zipfile.ZipFile(entry['backup']) as old, zipfile.ZipFile(p.ROOT/entry['package']) as new:
            for name in ('Composition.json','TemplateComposition.json'):
                if name not in new.namelist(): continue
                before=json.loads(old.read(name)); after=json.loads(new.read(name))
                views=[v for v in after['views'] if v.get('visuals')]
                checks.append(dict(package=entry['package'],part=name,
                    modelAndNonCoordinateStatePreserved=without_coordinates(before)==without_coordinates(after),
                    visibleSymbolInDefaultViewport=all(any(intersects_startup(x) for x in v['visuals']) for v in views),
                    firstSymbolCenters=[dict(x=v['visuals'][0]['x']+v['visuals'][0]['width']/2,y=v['visuals'][0]['y']+v['visuals'][0]['height']/2) for v in views]))
    passed=all(x['modelAndNonCoordinateStatePreserved'] and x['visibleSymbolInDefaultViewport'] for x in checks)
    p.write_json(p.ROOT/'reports/desktop-viewport-check.json',dict(passed=passed,checks=checks,manualDesktopCheck='not performed',
        limitation='Coordinates use the current application defaults. This does not test navigation of model-only elements.'))
    print('Viewport/data preservation checks:',len(checks),'passed:',passed)
    return 0 if passed else 1

if __name__=='__main__': raise SystemExit(main())
