"""Explicit empty-lobby fixture: native solver -> owner ack, rigid shells, frame gaps."""
import json,os,pathlib,statistics,subprocess,threading,time
bridge=pathlib.Path(os.environ['LOCALAPPDATA'])/'RoweRC/bridge'
state=(bridge/'ui-state.tsv').read_text(encoding='utf8')
if '\nrole\thost\n' not in state or '\ncontact\t1\n' not in state or '0 friend(s), 0 cars' not in state:
    raise RuntimeError('Requires an empty host test lobby with contact on')
out=pathlib.Path('evidence/collision-only-0.1.7');out.mkdir(exist_ok=True)
module=pathlib.Path('src/RoweRC/bin/Release/net9.0-windows/win-x64/RoweRC.dll')
frames={};stop=threading.Event();report=[]
def frame():
    options=[]
    for p in bridge.glob('local.[01].tsv'):
        try:
            fields=p.read_text().rstrip('\n').split('\t')
            if len(fields)==13 and fields[11]!='0':options.append(fields)
        except OSError:pass
    return max(options,key=lambda f:int(f[2])) if options else None
def monitor():
    while not stop.is_set():
        fields=frame()
        if fields:frames[int(fields[2])]=float(fields[11].split(',',1)[0])
        stop.wait(.008)
def wait(predicate,limit=12):
    until=time.monotonic()+limit
    while time.monotonic()<until:
        value=predicate()
        if value:return value
        time.sleep(.025)
    raise RuntimeError('Collision fixture timed out')
def command(action,arg='',response=None):
    serial=str(time.time_ns()//1000000)
    (bridge/'command.tsv').write_text(f'{serial}\t{action}\t{arg}\t{serial}\n')
    if response:return wait(lambda:(bridge/response).read_text().startswith(serial+'\t'))
original=(bridge.parent/'shell.txt').read_text().strip()
thread=threading.Thread(target=monitor,daemon=True);thread.start()
try:
    for shell in ('optional_shell','supra_rc'):
        command('shell',shell,'shell-result.txt')
        for index in range(3):
            command('reset')
            def ready():
                f=frame()
                if not f:return None
                p=f[11].split(',',9)
                return f if p[8]=='1' and sum(abs(float(v)) for v in p[2:5])<.02 else None
            before=wait(ready);time.sleep(.2)
            started=time.monotonic()
            subprocess.run(['dotnet',str(module),'--contact-smoke-test'],check=True)
            hit=json.loads((bridge/'contact-smoke.json').read_text())['Hit']['Sequence']
            after=wait(lambda:(f if int(f[11].split(',',9)[7])>=hit else None) if (f:=frame()) else None)
            elapsed=(time.monotonic()-started)*1000
            time.sleep(.3)
            report.append({'shell':shell,'contact':hit,'acknowledged':True,'commandToAckMs':round(elapsed,1),
                           'beforeSequence':int(before[2]),'afterSequence':int(after[2])})
            print(f'PASS: {shell} contact {index+1} acknowledged in {elapsed:.1f} ms',flush=True)
finally:
    command('reset');command('shell',original,'shell-result.txt')
    stop.set();thread.join()
times=[t for _,t in sorted(frames.items())];gaps=[(b-a)*1000 for a,b in zip(times,times[1:]) if b>=a]
result={'completed':len(report)==6,'contacts':report,'frames':len(frames),
        'maxObservedExportGapMs':round(max(gaps),2),'medianObservedExportGapMs':round(statistics.median(gaps),2),
        'scope':'Empty-lobby native solver and owner application; not a two-PC impact test.'}
(out/'native-contact-report.json').write_text(json.dumps(result,indent=2)+'\n')
assert result['completed'] and len(frames)>30 and max(gaps)<250,result
assert not (bridge/'contact-error.txt').read_text().strip()
print(f'PASS: {len(frames)} live frames; maximum observed export gap {max(gaps):.1f} ms; rigid-shell collision fixture',flush=True)
