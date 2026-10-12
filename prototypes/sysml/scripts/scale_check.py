"""Explicit, bounded native scale test; never closes an existing desktop application."""
import argparse, hashlib, pathlib, subprocess, time
import prototype as p

def main():
    args=argparse.ArgumentParser(); args.add_argument('--cli',required=True); args.add_argument('--timeout',type=int,default=180)
    a=args.parse_args(); cli=pathlib.Path(a.cli).resolve(); source=p.ROOT/'reference/mtconnect/MTConnect_Imported.tcom'
    work=p.REPO/'artifacts/sysml-scale'; work.mkdir(parents=True,exist_ok=True); log=work/'native.log'
    before=hashlib.sha256(source.read_bytes()).hexdigest(); start=time.monotonic()
    command=[str(cli),'composition','validate-json-persistence','--input',str(source),'--output-dir',str(work/'roundtrip')]
    try:
        with log.open('wb') as output:
            run=subprocess.run(command,stdout=output,stderr=subprocess.STDOUT,timeout=a.timeout)
        status='passed' if run.returncode==0 else 'failed'; exit_code=run.returncode
    except subprocess.TimeoutExpired: status='timeout'; exit_code=None
    report=dict(formatVersion=1,status=status,seconds=round(time.monotonic()-start,2),limitSeconds=a.timeout,exitCode=exit_code,
                command=command,inputSha256=before,inputUnchanged=before==hashlib.sha256(source.read_bytes()).hexdigest(),
                cliSha256=hashlib.sha256(cli.read_bytes()).hexdigest(),
                logTail=log.read_text(encoding='utf-8-sig',errors='replace').splitlines()[-12:],
                interpretation='Timeout is an incomplete test, not proof of corruption or a diagnosed cause. No full-model persistence success may be claimed without a passing run.')
    p.write_json(p.ROOT/'reports/full-model-scale.json',report); print(status,flush=True)
    return 0 if status=='passed' else 1

if __name__=='__main__': raise SystemExit(main())
