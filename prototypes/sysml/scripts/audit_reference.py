"""Audit complete source accounting without treating opaque preservation as modeling support."""
import argparse, collections, gzip, hashlib, json, pathlib
import xml.etree.ElementTree as ET
import prototype as p
import validate as v

def audit(folder):
    folder=pathlib.Path(folder); report=p.read_json(folder/'import-report.json')
    raw=(folder/'original/MTConnectSysMLModel.xml').read_bytes(); root=ET.fromstring(raw)
    with gzip.open(folder/'source-ledger.jsonl.gz','rt',encoding='utf-8') as f: rows=[json.loads(line) for line in f]
    d,c=v.load(folder/'MTConnect_Imported.tcom'); nodes={n['id']:n for n in c['ideas']+c['relationships']}
    issues=v.check_package(folder/'MTConnect_Imported.tcom')
    if hashlib.sha256(raw).hexdigest()!=report['sourceSha256']: issues.append(dict(code='SOURCE_HASH_CHANGED'))
    source=list(root.iter())
    if len(rows)!=len(source) or len(rows)!=report['totalXmlElements']: issues.append(dict(code='SOURCE_ACCOUNTING_COUNT'))
    if dict(collections.Counter(r['status'] for r in rows))!=report['statuses']: issues.append(dict(code='STATUS_ACCOUNTING_COUNT'))
    checked=0
    for e,row in zip(source,rows):
        if e.get(p.XI+'id','')!=row['sourceId']: issues.append(dict(code='LEDGER_ORDER_OR_ID',sourcePath=row['sourcePath']))
        if not row.get('targetId'): continue
        n=nodes.get(row['targetId'])
        if n is None: issues.append(dict(code='LEDGER_TARGET_MISSING',sourceId=row['sourceId'])); continue
        m=p.metadata(n)
        if m.get('SourceId','')!=row['sourceId']: issues.append(dict(code='SOURCE_ID_CHANGED',sourceId=row['sourceId']))
        if m.get('TextContent','')!=(e.text or ''): issues.append(dict(code='SOURCE_TEXT_CHANGED',sourceId=row['sourceId']))
        if 'name' in e.attrib and n['name']!=e.get('name'): issues.append(dict(code='SOURCE_NAME_CHANGED',sourceId=row['sourceId']))
        original={k:val for k,val in e.attrib.items() if k not in (p.XI+'id',p.XI+'type','name')}
        attrs=next((x['records'] for x in n.get('details',[]) if x['designatorTechName']=='Attributes'),[])
        retained={x['Key']:x['Value'] for x in attrs}
        reverse={uri:prefix for prefix,uri in p.read_json(p.ROOT/'sources/namespaces.json').items()}
        for key,value in original.items():
            if n['definitionTechName']=='Generalization' and key=='general':
                target=nodes[n['targetIdeaIds'][0]]
                if p.metadata(target).get('SourceId')!=value: issues.append(dict(code='GENERALIZATION_TARGET_CHANGED',sourceId=row['sourceId']))
                continue
            if key.startswith('{'):
                uri,local=key[1:].split('}',1); key=reverse.get(uri,'UNSUPPORTED_NAMESPACE')+':'+local
            if retained.get(key)!=value: issues.append(dict(code='SOURCE_ATTRIBUTE_CHANGED',sourceId=row['sourceId'],attribute=key))
        checked+=1
    result=dict(formatVersion=1,status='failed' if issues else 'passed',sourceSha256=report['sourceSha256'],sourceElements=len(source),ledgerRows=len(rows),nativeMappedObjectsChecked=checked,
                nativePackageStructuralIssues=issues,completeSemanticImport=False,
                limits=['Audit compares authored authoritative JSON, not full native desktop save/reopen.','Unsupported source subtrees remain in the unchanged original XML.','Language semantics and external references remain unverified.'])
    p.write_json(folder/'accounting-audit.json',result); print(json.dumps(result,indent=2)); return 1 if issues else 0

if __name__=='__main__':
    args=argparse.ArgumentParser(); args.add_argument('--folder',default=str(p.ROOT/'reference/mtconnect'))
    raise SystemExit(audit(args.parse_args().folder))
