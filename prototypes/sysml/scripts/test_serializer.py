"""Compare canonical JSON byte-for-byte against the pre-change application build."""
import hashlib, json, os, pathlib, subprocess, zipfile
import prototype as p

def main():
    builds=[p.REPO/'artifacts/sysml-navigation-build',p.REPO/'artifacts/sysml-large-model-build']
    work=p.REPO/'artifacts/sysml-serializer-regression'; work.mkdir(exist_ok=True)
    compiler=pathlib.Path(os.environ['WINDIR'])/'Microsoft.NET/Framework/v4.0.30319/csc.exe'
    for build in builds:
        subprocess.run([str(compiler),'/nologo','/target:exe','/platform:x86','/out:'+str(build/'SerializerRegression.exe'),
            '/r:'+str(build/'Instrumind.ThinkComposer.exe'),'/r:'+str(build/'Instrumind.Common.dll'),
            str(p.ROOT/'scripts/SerializerRegression.cs')],check=True)
    cases=[]
    for package in sorted((p.ROOT/'examples').glob('*.tcom')):
        with zipfile.ZipFile(package) as archive: document=json.loads(archive.read('Composition.json'))
        # Includes escaped/control characters, Unicode, empty collections and optional values.
        document['composition']['description']='Escapes: " \\ \n\r\t\b\f\u0000 Unicode: café Ω 😀 <&>'
        source=work/(package.stem+'.json'); p.write_json(source,document)
        outputs=[]
        for index,build in enumerate(builds):
            output=work/(package.stem+'.'+str(index)+'.json')
            subprocess.run([str(build/'SerializerRegression.exe'),str(source),str(output)],check=True,capture_output=True)
            outputs.append(output.read_bytes())
        assert outputs[0]==outputs[1], 'Canonical output changed: '+package.stem
        cases.append(dict(package=package.name,status='passed',sha256=hashlib.sha256(outputs[1]).hexdigest()))
    for number,text in enumerate(['[]','{"ideas":[','{"ideas":[{},]}','{"ideas":[],}',
                                 '{"composition":{"name":"unterminated}', '{"ideas":[]} {}',
                                 '{"ideas":[{"details":[}]}', '{"ideas":}', '{"ideas":[{"name":"bad\\q"}]}']):
        source=work/('malformed-'+str(number)+'.json'); source.write_text(text,encoding='utf-8')
        run=subprocess.run([str(builds[1]/'SerializerRegression.exe'),str(source),str(work/'rejected.json')],capture_output=True)
        assert run.returncode!=0, 'Malformed input was accepted: '+text
        cases.append(dict(case='malformed-'+str(number),status='passed'))
    p.write_json(p.ROOT/'reports/streaming-serializer-regression.json',dict(formatVersion=1,results=cases))
    print('PASS: 12 byte-identical canonical outputs and equivalent readers; 9 malformed inputs rejected.')

if __name__=='__main__': main()
