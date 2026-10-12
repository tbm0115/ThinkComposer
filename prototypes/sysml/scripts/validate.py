"""Prototype checks and native CLI regression runner. This is NOT a SysML parser/exporter."""
import argparse, collections, copy, hashlib, json, pathlib, re, subprocess, time, zipfile
import xml.etree.ElementTree as ET
import prototype as p

ROOT=p.ROOT
REF_ATTRS={'type','general','specific','client','supplier','source','target','definingFeature','classifier','association','memberEnd','annotatedElement',p.XI+'idref'}

def load(path):
    with zipfile.ZipFile(path) as z:
        return json.loads(z.read('Domain.json')), json.loads(z.read('Composition.json')) if 'Composition.json' in z.namelist() else None

def check_model(c,d):
    issues=[]
    def issue(code,n,detail): issues.append(dict(code=code,id=n.get('id',''),detail=detail))
    nodes=c['ideas']+c.get('relationships',[]); byid={n['id']:n for n in nodes}; cid=c['composition']['id']
    definitions={x['techName'] for x in d['conceptDefinitions']+d['relationshipDefinitions']}
    seen=set()
    for n in nodes:
        if n['id'] in seen: issue('DUPLICATE_ID',n,'Native identity is duplicated')
        seen.add(n['id'])
        if n['definitionTechName'] not in definitions: issue('UNKNOWN_DEFINITION',n,n['definitionTechName'])
        owner=n.get('containerId',cid)
        if owner!=cid and owner not in byid: issue('INVALID_OWNER',n,owner)
        chain={n['id']}; at=owner
        while at in byid:
            if at in chain: issue('OWNERSHIP_CYCLE',n,at); break
            chain.add(at); at=byid[at].get('containerId',cid)
        for target in n.get('originIdeaIds',[])+n.get('targetIdeaIds',[]):
            if target not in byid: issue('BROKEN_REFERENCE',n,target)
        m=p.metadata(n)
        if m.get('UmlType','').endswith(':InventedMetaclass'): issue('UNSUPPORTED_CONSTRUCT',n,m['UmlType'])
        if m.get('ExpressionLanguage')=='prototype-integer-comparison' and not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*\s*(?:>=|<=|==|!=|>|<)\s*-?\d+',m.get('Expression','')):
            issue('MALFORMED_EXPRESSION',n,'Restricted comparison grammar only; no execution')
        for field in ('Lower','Upper'):
            if m.get(field) and not re.fullmatch(r'\d+|\*',m[field]): issue('INVALID_MULTIPLICITY',n,field)
    return issues

def check_package(path):
    issues=[]
    with zipfile.ZipFile(path) as z:
        manifest=json.loads(z.read('manifest.json'))
        for part in manifest['authoritativeParts']:
            raw=z.read(part['partUri'].lstrip('/'))
            if len(raw)!=part['bytes'] or hashlib.sha256(raw).hexdigest()!=part['sha256']: issues.append(dict(code='PART_HASH',detail=part['partUri']))
    d,c=load(path)
    if c: issues+=check_model(c,d)
    return issues

def expected_nodes(c,language):
    field={'SysML_v1_XMI':'XmlTag','SysML_v2_Text':'TextKeyword','KerML_Text':'KerMLKeyword'}[language]
    nodes=c['ideas']+c['relationships']; byid={n['id']:n for n in nodes}; expected=[]; omitted=[]; folded=[]
    for n in nodes:
        if n['definitionTechName']=='ModelDocument': continue
        m=p.metadata(n); at=n; included=True; depth=0
        if language!='SysML_v1_XMI' and n['definitionTechName'] in ('Typing','Generalization'):
            origins=[byid[x] for x in n.get('originIdeaIds',[]) if x in byid]
            targets=[byid[x] for x in n.get('targetIdeaIds',[]) if x in byid]
            if len(origins)==1 and len(targets)==1 and p.metadata(origins[0]).get(field) and p.metadata(targets[0]).get(field):
                folded.append(n['id']); continue
        while at['definitionTechName']!='ModelDocument':
            depth+=1
            if not p.metadata(at).get(field): included=False; break
            if at.get('containerId') not in byid: included=False; break
            at=byid[at['containerId']]
        if included and depth<=p.DEPTH: expected.append(n)
        else: omitted.append(dict(id=n['id'],sourceId=m.get('SourceId',''),definition=n['definitionTechName'],reason='No supported template mapping, omitted ancestor, or depth limit'))
    return expected,omitted,folded

def check_xmi(path):
    issues=[]; external=[]
    try: root=ET.parse(path).getroot()
    except ET.ParseError as e: return [dict(code='MALFORMED_XML',detail=str(e))],external,set()
    ids=[e.get(p.XI+'id') for e in root.iter() if e.get(p.XI+'id')]; found=set(ids)
    if not ids: issues.append(dict(code='EMPTY_EXPORT',detail='No model elements emitted'))
    for identity,count in collections.Counter(ids).items():
        if count>1: issues.append(dict(code='DUPLICATE_ID',id=identity))
    for e in root.iter():
        for key,value in e.attrib.items():
            if key=='href': external.append(value)
            elif key in REF_ATTRS or key.startswith('base_'):
                for target in value.split():
                    if target not in found: issues.append(dict(code='BROKEN_XMI_REFERENCE',id=e.get(p.XI+'id',''),attribute=key,target=target))
        if e.tag=='ownedAttribute' and e.get(p.XI+'type')!='uml:Property': issues.append(dict(code='INVALID_PROPERTY_MAPPING',id=e.get(p.XI+'id','')))
        if e.tag.startswith('{'+p.NS['sysml']+'}'):
            expected={'Block':'base_Class','Requirement':'base_Class','ConstraintBlock':'base_Class','ValueType':'base_DataType'}.get(e.tag.split('}')[-1])
            if expected and not e.get(expected): issues.append(dict(code='PROFILE_BASE_MISSING',id=e.get(p.XI+'id','')))
    return issues,external,found

def export_report(package,language,output):
    d,c=load(package); expected,omitted,folded=expected_nodes(c,language); issues=check_model(c,d); external=[]
    text=pathlib.Path(output).read_text(encoding='utf-8-sig')
    if 'Liquid error:' in text: issues.append(dict(code='TEMPLATE_RUNTIME_ERROR',detail='Renderer embedded an error into output despite CLI exit zero'))
    if 'PROTOTYPE_DEPTH_LIMIT' in text: issues.append(dict(code='DEPTH_LIMIT',detail='Traversal was truncated'))
    if language=='SysML_v1_XMI':
        more,external,found=check_xmi(output); issues+=more
        wanted={p.metadata(n).get('SourceId') or 'tc_'+n['id'] for n in expected}
    else:
        found=set(re.findall(r'// source-id: ([0-9a-f-]+)',text)); wanted={n['id'] for n in expected}
        # Identity coverage and delimiter sanity are not a substitute for the pinned parser.
        if text.count('{')!=text.count('}'): issues.append(dict(code='UNBALANCED_DELIMITERS'))
    if found!=wanted: issues.append(dict(code='CONTENT_COVERAGE_MISMATCH',missing=sorted(wanted-found),unexpected=sorted(found-wanted)))
    return dict(formatVersion=1,package=str(pathlib.Path(package).relative_to(ROOT)),language=language,
        outputSha256=hashlib.sha256(pathlib.Path(output).read_bytes()).hexdigest(),emittedElements=len(found),foldedRelationships=folded,
        omittedElements=omitted,issues=issues,externalReferences=external,completeExport=False,
        declaredStructuralChecks='failed' if issues else 'passed',
        languageSemanticValidation='unverified: external parser/profile-library validation unavailable',
        profileBindings='SysML URI matches supplied reference; external profile resolution unverified' if language=='SysML_v1_XMI' else 'not applicable',
        exclusions=['proprietary diagram encoding','execution/evaluation','full profile/metadata semantics'],
        warning='This is a partial prototype export, never a conformance certificate.')

def negative_tests():
    d,c=load(ROOT/'examples/SysML_v1_Survey.tcom'); results=[]
    mutations={
        'duplicate-id':('DUPLICATE_ID',lambda x:x['ideas'].append(copy.deepcopy(x['ideas'][1]))),
        'broken-reference':('BROKEN_REFERENCE',lambda x:x['relationships'][0].update(targetIdeaIds=['missing-target'])),
        'invalid-ownership':('INVALID_OWNER',lambda x:x['ideas'][1].update(containerId='missing-owner')),
        'ownership-cycle':('OWNERSHIP_CYCLE',lambda x:x['ideas'][1].update(containerId=x['ideas'][1]['id'])),
        'unsupported-construct':('UNSUPPORTED_CONSTRUCT',lambda x:p.metadata(x['ideas'][1]).update(UmlType='uml:InventedMetaclass')),
        'malformed-expression':('MALFORMED_EXPRESSION',lambda x:p.metadata(x['ideas'][1]).update(ExpressionLanguage='prototype-integer-comparison',Expression='value >= )')),
    }
    for name,(code,mutate) in mutations.items():
        bad=copy.deepcopy(c); mutate(bad); path=ROOT/'fixtures/negative'/(name+'.tcom'); p.package(path,d,bad)
        issues=check_model(bad,d); results.append(dict(name=name,expected=code,passed=code in [i['code'] for i in issues]))
    path=ROOT/'fixtures/negative/empty.xml'; path.write_text('<xmi:XMI xmlns:xmi="'+p.NS['xmi']+'"/>',encoding='utf-8')
    results.append(dict(name='empty-export',expected='EMPTY_EXPORT',passed=any(i['code']=='EMPTY_EXPORT' for i in check_xmi(path)[0])))
    p.write_json(ROOT/'reports/negative-tests.json',results)
    return results

def compare_saved(original,saved):
    issues=[]; actual={n['id']:n for n in saved['ideas']+saved['relationships']}
    actual_views={v['id']:v for v in saved.get('views',[])}
    for view in original.get('views',[]):
        actual_visuals={v['ideaId'] for v in actual_views.get(view['id'],{}).get('visuals',[])}
        for visual in view.get('visuals',[]):
            if visual['ideaId'] not in actual_visuals:
                issues.append(dict(code='LOST_VISUAL',viewId=view['id'],id=visual['ideaId']))
    for n in original['ideas']+original['relationships']:
        other=actual.get(n['id'])
        if other is None: issues.append(dict(code='LOST_ID',id=n['id'])); continue
        for field in ('containerId','originIdeaIds','targetIdeaIds'):
            if field in n and n[field]!=other.get(field): issues.append(dict(code='CHANGED_REFERENCE_OR_OWNER',id=n['id'],field=field))
        for detail in n.get('details',[]):
            found=next((x for x in other.get('details',[]) if x.get('designatorTechName')==detail['designatorTechName']),None)
            if found is None or len(found['records'])!=len(detail['records']): issues.append(dict(code='LOST_DETAIL',id=n['id'])); continue
            for i,record in enumerate(detail['records']):
                for k,v in record.items():
                    if found['records'][i].get(k)!=v: issues.append(dict(code='CHANGED_DETAIL_OR_ORDER',id=n['id'],field=k))
    return issues

def feasibility_fixtures():
    d,c=load(ROOT/'examples/SysML_v1_Survey.tcom')
    broken=copy.deepcopy(d)
    definition=next(x for x in broken['conceptDefinitions'] if x['techName']=='ModelDocument')
    definition['outputTemplates'][0]['templateText']='%%:FileName=Model.xml\n%%:TemplateRole=DocumentRoot\n<xmi:XMI xmlns:xmi="'+p.NS['xmi']+'">{% for child in CompositeIdeas %}{% assign m = child["ModelMetadata"].Records[0] %}{% if m.XmlTag != null and m.XmlTag != "" %}<element xmi:id="{{ child.GlobalId }}"/>{% endif %}{% endfor %}</xmi:XMI>'
    p.package(ROOT/'fixtures/blocked/chained-detail-access.tcom',broken,c)
    d,c=load(ROOT/'examples/SysML_v2_Survey.tcom'); bad=copy.deepcopy(d)
    definition=next(x for x in bad['conceptDefinitions'] if x['techName']=='ModelDocument')
    definition['outputTemplates'][0]['templateText']="%%:FileName=Model.sysml\n%%:TemplateRole=DocumentRoot\n{{ 'example */' | Replace: '*/', '* /' }}"
    p.package(ROOT/'fixtures/blocked/regex-replace-error.tcom',bad,c)
    c=p.composition('DepthLimit',d); owner=c['ideas'][0]
    for i in range(14): owner=p.add_node(c,'Package','Level'+str(i),owner['id'],{'TextKeyword':'package','KerMLKeyword':'package'})
    p.package(ROOT/'fixtures/blocked/depth-limit.tcom',d,c)

def native(cli,work):
    cli=pathlib.Path(cli).resolve(); work=pathlib.Path(work).resolve(); work.mkdir(parents=True,exist_ok=True)
    results=[]
    def run(name,args,timeout=90):
        start=time.monotonic(); log=work/(name+'.log')
        try:
            with log.open('wb') as f: proc=subprocess.run([str(cli)]+list(map(str,args)),stdout=f,stderr=subprocess.STDOUT,timeout=timeout)
            status='passed' if proc.returncode==0 else 'failed'; code=proc.returncode
        except subprocess.TimeoutExpired: status='timeout'; code=None
        r=dict(name=name,status=status,exitCode=code,seconds=round(time.monotonic()-start,2),command=[str(cli)]+list(map(str,args)))
        results.append(r); print(name+': '+status,flush=True); return r
    for path in sorted((ROOT/'domains').glob('*.tdom')):
        run(path.stem,['domain','validate-json-persistence','--input',path,'--output-dir',work/path.stem])
    for path in sorted((ROOT/'examples').glob('*.tcom')):
        result=run(path.stem,['composition','validate-json-persistence','--input',path,'--output-dir',work/path.stem])
        saved=work/path.stem/'composition-persistence-1.json'
        if result['status']=='passed' and saved.exists():
            issues=compare_saved(load(path)[1],p.read_json(saved))
            results.append(dict(name=path.stem+'-input-preserved',status='failed' if issues else 'passed',issues=issues))
    samples=list((ROOT/'examples').glob('*_Survey.tcom'))+list((ROOT/'reference/mtconnect').glob('*_Slice.tcom'))
    for path in sorted(samples):
        d,c=load(path)
        for lang in [l['techName'] for l in d['externalLanguages']]:
            output=ROOT/'exports'/path.stem/lang; output.mkdir(parents=True,exist_ok=True)
            args=['output','generate','--input',path,'--language',lang,'--output-dir',output]
            r=run(path.stem+'-'+lang,args)
            ext={'SysML_v1_XMI':'xml','SysML_v2_Text':'sysml','KerML_Text':'kerml'}[lang]; file=output/('Model.'+ext)
            if r['status']=='passed' and file.exists():
                before=file.read_bytes(); report=export_report(path,lang,file)
                run(path.stem+'-'+lang+'-repeat',args)
                report['repeatExportIdentical']=before==file.read_bytes()
                p.write_json(output/'coverage-validation.json',report)
                results.append(dict(name=path.stem+'-'+lang+'-coverage',status='passed' if not report['issues'] and report['repeatExportIdentical'] else 'failed'))
    # Native rename/export round trip: identity and typed relationships must survive.
    path=ROOT/'examples/SysML_v2_Survey.tcom'; d,c=load(path)
    before_ids={n['id'] for n in c['ideas']+c['relationships']}
    target=next(n for n in c['ideas'] if n['definitionTechName']=='PartDefinition')
    target.update(name='Renamed part definition',techName='RenamedPartDefinition')
    renamed=work/'Renamed.tcom'; p.package(renamed,d,c)
    run('rename-roundtrip',['composition','validate-json-persistence','--input',renamed,'--output-dir',work/'rename-roundtrip'])
    run('rename-export',['output','generate','--input',renamed,'--language','SysML_v2_Text','--output-dir',work/'rename-export'])
    generated=work/'rename-export/Model.sysml'
    results.append(dict(name='rename-preserves-identity-and-typing',status='passed' if before_ids=={n['id'] for n in c['ideas']+c['relationships']} and generated.exists() and 'part PartUsageExample : RenamedPartDefinition' in generated.read_text() else 'failed'))
    for key in ('SysML_v1','SysML_v2'):
        run(key+'-preview',['composition','export-image','--input',ROOT/'examples'/(key+'_Survey.tcom'),'--output',ROOT/'reports'/(key+'-browser.png'),'--width','1400','--height','700'])
    feasibility_fixtures()
    for name,lang,ext,code in [('chained-detail-access','SysML_v1_XMI','xml','EMPTY_EXPORT'),('regex-replace-error','SysML_v2_Text','sysml','TEMPLATE_RUNTIME_ERROR'),('depth-limit','SysML_v2_Text','sysml','DEPTH_LIMIT')]:
        path=ROOT/'fixtures/blocked'/(name+'.tcom'); dest=work/name
        r=run(name,['output','generate','--input',path,'--language',lang,'--output-dir',dest])
        if (dest/('Model.'+ext)).exists():
            report=export_report(path,lang,dest/('Model.'+ext)); p.write_json(ROOT/'reports'/(name+'.json'),report)
            results.append(dict(name=name+'-detected',status='passed' if code in [i['code'] for i in report['issues']] else 'failed'))
    p.write_json(ROOT/'reports/native-regressions.json',dict(formatVersion=1,cliSha256=hashlib.sha256(cli.read_bytes()).hexdigest(),results=results))
    return results

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('command',choices=['check','negative','native','export-report'])
    parser.add_argument('--input'); parser.add_argument('--language'); parser.add_argument('--output'); parser.add_argument('--cli'); parser.add_argument('--work-dir',default=str(p.REPO/'artifacts/sysml-validation'))
    a=parser.parse_args()
    if a.command=='check':
        issues=check_package(a.input); print(json.dumps(issues,indent=2)); return 1 if issues else 0
    if a.command=='negative': return 0 if all(r['passed'] for r in negative_tests()) else 1
    if a.command=='native': return 0 if all(r['status']=='passed' for r in native(a.cli,a.work_dir)) else 1
    if a.command=='export-report':
        report=export_report(a.input,a.language,a.output); p.write_json(pathlib.Path(a.output).with_suffix('.coverage.json'),report)
        return 1 if report['issues'] else 0

if __name__=='__main__': raise SystemExit(main())
