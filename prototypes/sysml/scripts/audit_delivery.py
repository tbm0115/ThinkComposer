"""Check reusable artifacts, template registration, documentation links and build determinism."""
import hashlib, re
import prototype as p
import validate as v

def main():
    paths=sorted((p.ROOT/'domains').glob('*.tdom'))+sorted((p.ROOT/'examples').glob('*.tcom'))
    checks=[]
    before={str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    p.build()
    checks.append(dict(name='repeat-build-identical',passed=all(before[str(path)]==hashlib.sha256(path.read_bytes()).hexdigest() for path in paths)))
    for path in paths:
        issues=v.check_package(path); checks.append(dict(name=path.name,passed=not issues,issues=issues))
        d,_=v.load(path)
        owner=next(x for x in d['conceptDefinitions'] if x['techName']=='ModelDocument')
        for template in owner['outputTemplates']:
            expected=(p.ROOT/'templates'/(template['externalLanguageTechName']+'.liquid')).read_text(encoding='utf-8')
            checks.append(dict(name=path.name+':'+template['externalLanguageTechName'],passed=expected==template['templateText']))
    for path in [p.ROOT/'README.md']+list((p.ROOT/'docs').glob('*.md')):
        for target in re.findall(r'\[[^\]]*\]\(([^)]+)\)',path.read_text(encoding='utf-8')):
            if '://' in target or target.startswith('#'): continue
            checks.append(dict(name=str(path.relative_to(p.ROOT))+':'+target,passed=(path.parent/target.split('#')[0]).exists()))
    p.write_json(p.ROOT/'reports/delivery-audit.json',dict(formatVersion=1,checks=checks))
    failed=[x for x in checks if not x['passed']]; print('Delivery checks:',len(checks),'failures:',failed)
    return 1 if failed else 0

if __name__=='__main__': raise SystemExit(main())
